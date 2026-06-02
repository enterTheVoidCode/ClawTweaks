using System;
using System.Management;
using NLog;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Low-level access to the MSI Claw's ACPI-WMI platform interface (root\WMI,
    /// MSI_ACPI). Ported from the Handheld Companion fork's WMI helper — used for
    /// fan-table / fan-control / power-limit data blocks.
    ///
    /// Data-block protocol: every call sends a 32-byte package whose first byte is the
    /// data-block index; reads return a buffer whose first byte is a success flag (1 = ok)
    /// followed by the payload.
    /// </summary>
    internal static class MsiClawWmi
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public const string Scope = @"root\WMI";
        public const string Path  = @"MSI_ACPI.InstanceName='ACPI\PNP0C14\0_0'";

        /// <summary>Payload length per Get_AP data-block index (from HC).</summary>
        public static int GetAPLength(byte iDataBlockIndex)
        {
            switch (iDataBlockIndex)
            {
                case 0:  return 6;
                case 1:  return 3;
                case 2:  return 7;
                default: return 32;
            }
        }

        /// <summary>
        /// Invokes an MSI_ACPI method with a 32-byte input package and returns the raw
        /// output ManagementBaseObject (or null on failure).
        /// </summary>
        public static ManagementBaseObject Set(string scope, string path, string methodName, byte[] fullPackage)
        {
            try
            {
                var managementObject = new ManagementObject(scope, path, null);

                ManagementBaseObject inParams = null;
                ManagementBaseObject inParamsData = null;
                bool parametersAvailable = false;

                try
                {
                    inParams = managementObject.GetMethodParameters(methodName);
                    inParamsData = inParams["Data"] as ManagementBaseObject;
                    parametersAvailable = (inParams != null && inParamsData != null);
                }
                catch { }

                // Fallback: some firmware exposes the Data template only via Get_WMI.
                if (!parametersAvailable)
                {
                    try
                    {
                        inParams = managementObject.InvokeMethod("Get_WMI", null, null);
                        inParamsData = inParams?["Data"] as ManagementBaseObject;
                    }
                    catch { }
                }

                if (inParams == null || inParamsData == null)
                {
                    Logger.Warn($"MsiClawWmi.Set failed: method={methodName} (no Data parameter)");
                    return null;
                }

                inParamsData.SetPropertyValue("Bytes", fullPackage);
                inParams.SetPropertyValue("Data", inParamsData);

                return managementObject.InvokeMethod(methodName, inParams, null);
            }
            catch (Exception ex)
            {
                Logger.Warn($"MsiClawWmi.Set({methodName}) threw: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads a data block. Returns the payload (without the leading success flag);
        /// <paramref name="readSuccess"/> is true only when the firmware flagged the read ok.
        /// </summary>
        public static byte[] Get(string scope, string path, string methodName, byte iDataBlockIndex, int length, out bool readSuccess)
        {
            readSuccess = false;
            byte[] resultData = new byte[length];

            byte[] fullPackage = new byte[32];
            fullPackage[0] = iDataBlockIndex;

            ManagementBaseObject outParams = Set(scope, path, methodName, fullPackage);
            if (outParams == null)
                return resultData;

            var dataOut = outParams["Data"] as ManagementBaseObject;
            if (dataOut == null)
                return resultData;

            byte[] outBytes = dataOut["Bytes"] as byte[];
            if (outBytes == null || outBytes.Length < 1)
                return resultData;

            byte flag = outBytes[0];
            readSuccess = (flag == 1);

            int dataLength = outBytes.Length - 1;
            resultData = new byte[dataLength];
            Array.Copy(outBytes, 1, resultData, 0, dataLength);
            return resultData;
        }
    }
}
