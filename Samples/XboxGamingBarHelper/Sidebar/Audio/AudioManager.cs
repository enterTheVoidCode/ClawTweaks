using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NLog;

namespace XboxGamingBarHelper.Sidebar.Audio
{
    internal struct AudioDevice
    {
        internal string Id;
        internal string FriendlyName;
    }

    internal class AudioManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private bool _disposed;

        private IMMDeviceEnumerator _enumerator;

        internal AudioManager()
        {
            try
            {
                var type = Type.GetTypeFromCLSID(CLSID.MMDeviceEnumerator);
                var obj = Activator.CreateInstance(type);
                _enumerator = (IMMDeviceEnumerator)obj;
            }
            catch (Exception ex)
            {
                Logger.Error($"AudioManager: Failed to create device enumerator: {ex.Message}");
                _enumerator = null;
            }
        }

        internal int GetVolume()
        {
            try
            {
                if (_enumerator == null) return 50;
                _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
                if (device == null) return 50;

                device.Activate(IID.IAudioEndpointVolume, 1 /* CLSCTX_ALL */, IntPtr.Zero, out var iface);
                var vol = (IAudioEndpointVolume)iface;
                vol.GetMasterVolumeLevelScalar(out float level);

                Marshal.ReleaseComObject(vol);
                Marshal.ReleaseComObject(device);

                return (int)Math.Round(level * 100);
            }
            catch (Exception ex)
            {
                Logger.Error($"AudioManager: GetVolume failed: {ex.Message}");
                return 50;
            }
        }

        internal void SetVolume(int vol)
        {
            try
            {
                if (_enumerator == null) return;
                _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
                if (device == null) return;

                device.Activate(IID.IAudioEndpointVolume, 1, IntPtr.Zero, out var iface);
                var endpoint = (IAudioEndpointVolume)iface;
                float level = Math.Max(0f, Math.Min(1f, vol / 100f));
                var ctx = Guid.Empty;
                endpoint.SetMasterVolumeLevelScalar(level, ref ctx);

                Marshal.ReleaseComObject(endpoint);
                Marshal.ReleaseComObject(device);
            }
            catch (Exception ex)
            {
                Logger.Error($"AudioManager: SetVolume failed: {ex.Message}");
            }
        }

        internal List<AudioDevice> GetRenderDevices()
        {
            var devices = new List<AudioDevice>();
            try
            {
                if (_enumerator == null) return devices;
                _enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE.ACTIVE, out var collection);
                if (collection == null) return devices;

                collection.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    collection.Item(i, out var device);
                    if (device == null) continue;

                    device.GetId(out string id);
                    string name = GetDeviceFriendlyName(device);
                    devices.Add(new AudioDevice { Id = id, FriendlyName = name ?? id });
                    Marshal.ReleaseComObject(device);
                }
                Marshal.ReleaseComObject(collection);
            }
            catch (Exception ex)
            {
                Logger.Error($"AudioManager: GetRenderDevices failed: {ex.Message}");
            }
            return devices;
        }

        internal AudioDevice? GetDefaultDevice()
        {
            try
            {
                if (_enumerator == null) return null;
                _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
                if (device == null) return null;

                device.GetId(out string id);
                string name = GetDeviceFriendlyName(device);
                Marshal.ReleaseComObject(device);
                return new AudioDevice { Id = id, FriendlyName = name ?? id };
            }
            catch (Exception ex)
            {
                Logger.Error($"AudioManager: GetDefaultDevice failed: {ex.Message}");
                return null;
            }
        }

        internal void SetDefaultDevice(string deviceId)
        {
            try
            {
                var type = Type.GetTypeFromCLSID(IID.CPolicyConfigClient);
                var obj = Activator.CreateInstance(type);
                var policy = (IPolicyConfig)obj;

                policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
                policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);

                Marshal.ReleaseComObject(policy);
                Logger.Info($"AudioManager: Default device set to {deviceId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"AudioManager: SetDefaultDevice failed: {ex.Message}");
            }
        }

        private static string GetDeviceFriendlyName(IMMDevice device)
        {
            try
            {
                device.OpenPropertyStore(STGM.READ, out var store);
                if (store == null) return null;

                var key = PROPERTYKEY.PKEY_Device_FriendlyName;
                store.GetValue(ref key, out var value);
                string name = value.GetString();
                Marshal.ReleaseComObject(store);
                return name;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_enumerator != null)
            {
                try { Marshal.ReleaseComObject(_enumerator); } catch { }
                _enumerator = null;
            }
        }
    }
}
