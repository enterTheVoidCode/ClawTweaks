using System;
using XboxGamingBarHelper.ControllerEmulation.Viiper;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Experimental VIIPER backend for the MSI Claw. ClawButtonMonitor stays the single
    /// source of truth for all input processing (DInput read, re-mappings, gyro→stick,
    /// deadzones). At the very point we normally call <c>_vigem.SubmitXboxState(...)</c> we
    /// additionally (or instead) push the same assembled XInput state to a VIIPER virtual
    /// controller via <see cref="ViiperService"/> + <see cref="ViiperWireFormat"/>.
    ///
    /// The virtual device type (Xbox 360, DualShock 4, DualSense Edge, Xbox Elite 2 /
    /// Steam, Switch Pro) follows the user's <c>ViiperDeviceType</c> selection. The Claw has
    /// no Legion back-paddles / touchpad and folds gyro into the stick, so it always passes
    /// <see cref="ViiperWireFormat.Extras.None"/>.
    ///
    /// The ViGEm integration is untouched: when VIIPER owns the pad, ViGEm is suppressed
    /// (<see cref="SetSuppressVigem"/>) so <c>_vigem</c> is null and its submit calls no-op.
    /// </summary>
    internal partial class ClawButtonMonitor
    {
        private const uint ViiperBusId = 1;

        // USBIP attach can transiently fail (device not yet present) and succeed on retry —
        // mirrors the usbip-win2 UI re-attach behaviour. Absorb it programmatically.
        private const int ViiperAttachMaxAttempts = 4;
        private const int ViiperAttachRetryDelayMs = 350;

        private ViiperService _viiperService;
        private uint _viiperDeviceId;
        private volatile bool _viiperEnabled;   // requested before Start()
        private volatile bool _viiperMounted;   // device actually created

        // Selected virtual device type (+ optional VID/PID for Steam sub-devices). Set before
        // Start() via SetViiperDeviceType; changed at runtime via SwitchViiperDeviceType.
        private volatile string _viiperDeviceType = "xbox360";
        private ushort _viiperVid;
        private ushort _viiperPid;
        private readonly object _viiperSwapLock = new object();

        /// <summary>
        /// Request the VIIPER backend (true) or the legacy ViGEm path (false). Must be set
        /// before <see cref="Start"/>. Mirrors <see cref="SetSuppressVigem"/>.
        /// </summary>
        public void SetViiperEnabled(bool enabled) => _viiperEnabled = enabled;

        /// <summary>
        /// Selects the VIIPER virtual device type (+ optional VID/PID for Steam sub-devices).
        /// Applied at mount time; call <see cref="SwitchViiperDeviceType"/> to change it live.
        /// </summary>
        public void SetViiperDeviceType(string deviceType, ushort vid = 0, ushort pid = 0)
        {
            _viiperDeviceType = string.IsNullOrEmpty(deviceType) ? "xbox360" : deviceType;
            _viiperVid = vid;
            _viiperPid = pid;
        }

        /// <summary>True once the VIIPER USBIP device is mounted.</summary>
        public bool ViiperMounted => _viiperMounted;

        /// <summary>The libviiper type tag of the currently mounted device (e.g. "dualsenseedge"), or null when not mounted.</summary>
        public string ViiperActiveDeviceType => _viiperMounted ? _viiperDeviceType : null;

        /// <summary>
        /// Brings up the VIIPER USBIP server + bus and adds the selected device. Idempotent.
        /// Mirrors <c>EnsureViGEm()</c>: returns false on any failure (so Start() can abort
        /// and the robust stop path restores the physical controller instead of hiding it
        /// with no replacement).
        /// </summary>
        private bool EnsureViiper()
        {
            if (_viiperMounted) return true;

            // Boot-timing: measure the USB/IP bring-up (Initialize + CreateBus + attach incl. retry).
            // The claim is VIIPER mounts much faster than ViGEm — the log should show this as a small
            // slice next to the ~4 s Claw-DInput open cost that dominates boot.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string type = _viiperDeviceType;
            try
            {
                if (_viiperService == null)
                    _viiperService = new ViiperService();

                if (!_viiperService.Initialize())
                {
                    Logger.Error("ClawButtonMonitor: VIIPER init failed (usbip-win2 missing or libviiper error)");
                    return false;
                }

                if (!_viiperService.CreateBus(ViiperBusId))
                {
                    Logger.Error("ClawButtonMonitor: VIIPER CreateBus failed");
                    return false;
                }

                // The USBIP attach (bundled inside AddDevice) can transiently fail with an
                // "IP device not present" style error — the same hiccup seen in the usbip-win2
                // UI, where the very next attempt succeeds. Retry a few times so programmatic
                // mounting is as robust as a manual re-attach.
                ViiperAddDeviceResult add = default;
                for (int attempt = 1; attempt <= ViiperAttachMaxAttempts; attempt++)
                {
                    add = _viiperService.AddDevice(ViiperBusId, type, _viiperVid, _viiperPid);
                    if (add.Success) break;
                    if (attempt < ViiperAttachMaxAttempts)
                    {
                        Logger.Warn($"ClawButtonMonitor: VIIPER AddDevice({type}) attempt {attempt}/{ViiperAttachMaxAttempts} failed (transient USBIP attach?) — retrying in {ViiperAttachRetryDelayMs} ms");
                        System.Threading.Thread.Sleep(ViiperAttachRetryDelayMs);
                    }
                }
                if (!add.Success)
                {
                    Logger.Error($"ClawButtonMonitor: VIIPER AddDevice({type}) failed after {ViiperAttachMaxAttempts} attempts");
                    try { _viiperService.RemoveBus(ViiperBusId); } catch { }
                    return false;
                }

                _viiperDeviceId = add.DeviceId;
                _viiperMounted = true;
                // Forward game force-feedback (rumble) from the VIIPER device to the physical Claw,
                // reusing the exact same OnRumbleReceived → WriteRumble path as the ViGEm backend.
                _viiperService.FeedbackReceived += OnViiperFeedbackReceived;
                Logger.Info($"ClawButtonMonitor: VIIPER {type} device mounted in {sw.ElapsedMilliseconds}ms (bus={ViiperBusId}, dev={_viiperDeviceId}, vid=0x{_viiperVid:X4}, pid=0x{_viiperPid:X4}, rumble forwarding enabled)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ClawButtonMonitor: EnsureViiper threw: {ex.Message}");
                TeardownViiper();
                return false;
            }
        }

        /// <summary>
        /// Hot-swaps the running VIIPER device to a new type (+ optional Steam VID/PID) without
        /// tearing down the bus. No-op if VIIPER isn't mounted or the target is unchanged. The
        /// poll loop is gated via <see cref="_viiperMounted"/> for the duration of the swap.
        /// </summary>
        public void SwitchViiperDeviceType(string deviceType, ushort vid = 0, ushort pid = 0)
        {
            string newType = string.IsNullOrEmpty(deviceType) ? "xbox360" : deviceType;
            lock (_viiperSwapLock)
            {
                _viiperVid = vid;
                _viiperPid = pid;
                if (!_viiperMounted || _viiperService == null)
                {
                    // Not mounted yet — just remember the selection for the next mount.
                    _viiperDeviceType = newType;
                    return;
                }
                if (newType == _viiperDeviceType)
                    return;

                Logger.Info($"ClawButtonMonitor: VIIPER hot-swap {_viiperDeviceType} -> {newType} (vid=0x{vid:X4}, pid=0x{pid:X4})");
                _viiperMounted = false; // gate the poll loop while the device is gone
                try
                {
                    var res = _viiperService.SwitchDeviceType(ViiperBusId, _viiperDeviceId, newType, vid, pid);
                    if (res.Success)
                    {
                        _viiperDeviceId = res.DeviceId;
                        _viiperDeviceType = newType;
                        _viiperMounted = true;
                        Logger.Info($"ClawButtonMonitor: VIIPER now presenting {newType} (dev={_viiperDeviceId})");
                    }
                    else
                    {
                        Logger.Error($"ClawButtonMonitor: VIIPER hot-swap to {newType} failed; device is gone until re-enable");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"ClawButtonMonitor: SwitchViiperDeviceType threw: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Pushes the assembled XInput state to the VIIPER device, translated to the current
        /// device's wire format. Called from the poll thread right next to the ViGEm submit —
        /// same variables, no extra processing. No-op until the device is mounted.
        /// </summary>
        private void SubmitViiperState(ushort buttons, byte leftTrigger, byte rightTrigger,
                                       short thumbLX, short thumbLY, short thumbRX, short thumbRY)
        {
            if (!_viiperMounted) return;
            var svc = _viiperService;
            if (svc == null) return;

            var gp = new ViiperXInputGamepad
            {
                Buttons = buttons,
                LeftTrigger = leftTrigger,
                RightTrigger = rightTrigger,
                ThumbLX = thumbLX,
                ThumbLY = thumbLY,
                ThumbRX = thumbRX,
                ThumbRY = thumbRY,
            };
            // Claw has no Legion aux/touchpad and folds gyro into the stick → no Extras.
            byte[] report = ViiperWireFormat.BuildForDeviceType(_viiperDeviceType, gp, ViiperWireFormat.Extras.None);
            try { svc.SetInput(ViiperBusId, _viiperDeviceId, report); }
            catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: VIIPER SetInput threw: {ex.Message}"); }
        }

        /// <summary>
        /// VIIPER force-feedback callback (rumble/LED). Fires on a native thread. For the Xbox
        /// formats the report is data[0]=large motor, data[1]=small motor (DS4/DSE swap them, but
        /// the small difference isn't worth a per-type branch for the Claw's single rumble motor
        /// pair). Routed into the shared OnRumbleReceived → WriteRumble path so the physical Claw
        /// vibrates exactly like on the ViGEm backend.
        /// </summary>
        private void OnViiperFeedbackReceived(uint busId, uint deviceId, byte[] data)
        {
            if (!_viiperMounted) return;
            if (busId != ViiperBusId || deviceId != _viiperDeviceId) return;
            if (data == null || data.Length < 2) return;
            try { OnRumbleReceived(data[0], data[1]); }
            catch (Exception ex) { Logger.Debug($"ClawButtonMonitor: VIIPER feedback handler failed: {ex.Message}"); }
        }

        /// <summary>Removes the VIIPER device + bus and disposes the service. Safe to call repeatedly.</summary>
        private void TeardownViiper()
        {
            var svc = _viiperService;
            if (svc == null)
            {
                _viiperMounted = false;
                return;
            }

            try { svc.FeedbackReceived -= OnViiperFeedbackReceived; } catch { }

            try
            {
                if (_viiperMounted)
                {
                    try { svc.RemoveDevice(ViiperBusId, _viiperDeviceId); } catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: VIIPER RemoveDevice threw: {ex.Message}"); }
                    try { svc.RemoveBus(ViiperBusId); } catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: VIIPER RemoveBus threw: {ex.Message}"); }
                }
                try { svc.Dispose(); } catch (Exception ex) { Logger.Warn($"ClawButtonMonitor: VIIPER Dispose threw: {ex.Message}"); }
            }
            finally
            {
                _viiperService = null;
                _viiperDeviceId = 0;
                _viiperMounted = false;
                Logger.Info("ClawButtonMonitor: VIIPER torn down");
            }
        }
    }
}
