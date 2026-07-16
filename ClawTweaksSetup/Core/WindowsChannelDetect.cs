using Microsoft.Win32;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Detects whether this device is enrolled in the Windows Insider Program (Dev/Beta/Release
    /// Preview/Canary channel) — the install routine (Add-AppxPackage sideloading, cert trust,
    /// HidHide/usbip driver installs) is known not to work reliably there, so the Center warns
    /// instead of failing silently partway through.
    ///
    /// WindowsSelfHost\Applicability\BranchName is set only on Insider-enrolled machines (empty/
    /// absent on Retail Windows) — the standard signal Insider-aware tools check. UI\Selection\
    /// UIBranch carries the friendly channel name ("Dev Channel", "Beta Channel", "Release Preview
    /// Channel") shown in Settings; BranchName itself is a raw internal codename, so UIBranch is
    /// preferred for display when present.
    /// </summary>
    public static class WindowsChannelDetect
    {
        public sealed class Result
        {
            public bool IsInsider;
            public string ChannelName;
        }

        public static Result Detect()
        {
            try
            {
                using var applicability = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\WindowsSelfHost\Applicability");
                string branchName = applicability?.GetValue("BranchName") as string;
                if (string.IsNullOrEmpty(branchName))
                    return new Result { IsInsider = false };

                string friendly = null;
                try
                {
                    using var selection = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\WindowsSelfHost\UI\Selection");
                    friendly = selection?.GetValue("UIBranch") as string;
                }
                catch { /* fall back to the raw branch codename below */ }

                return new Result { IsInsider = true, ChannelName = string.IsNullOrEmpty(friendly) ? branchName : friendly };
            }
            catch
            {
                return new Result { IsInsider = false };
            }
        }
    }
}
