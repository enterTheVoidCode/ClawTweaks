using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Trusts the ClawTweaks signing certificate so the (self-signed) MSIX can be sideloaded.
    /// Mirrors Install.ps1: the .cer goes into LocalMachine\TrustedPeople — deliberately NOT the Root
    /// CA store (adding a root CA is an AV/EDR red flag and is unnecessary for sideloading).
    /// </summary>
    public static class CertInstaller
    {
        /// <summary>
        /// The ClawTweaks signing cert's Subject — verified identical across versions (same pfx signs
        /// every build), so this can answer "is our cert already trusted" without needing a local .cer
        /// to hash first. Used by the Center menu to decide msix-only vs. full-zip before downloading.
        /// </summary>
        private const string KnownSubject = "CN=ClawTweaks Dev, O=MSIClaw";

        /// <summary>True if a cert with the known ClawTweaks Subject is already in LocalMachine\TrustedPeople.</summary>
        public static bool IsKnownCertAlreadyTrusted()
        {
            try
            {
                using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates
                    .Cast<X509Certificate2>()
                    .Any(c => string.Equals(c.Subject, KnownSubject, StringComparison.Ordinal));
            }
            catch { return false; }
        }

        /// <summary>Finds the signing .cer shipped next to <see cref="SetupContext.AssetRoot"/> (and its package subfolder).</summary>
        public static string FindSiblingCer()
        {
            try
            {
                string dir = SetupContext.AssetRoot;
                foreach (var d in new[] { dir, Path.Combine(dir, "Package") })
                {
                    if (!Directory.Exists(d)) continue;
                    var cer = Directory.GetFiles(d, "*.cer", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (cer != null) return cer;
                }
            }
            catch { }
            return null;
        }

        public static string ThumbprintOf(string cerPath)
        {
            try
            {
                using var c = X509CertificateLoader.LoadCertificateFromFile(cerPath);
                return c.Thumbprint;
            }
            catch { return null; }
        }

        public static string SubjectOf(string cerPath)
        {
            try
            {
                using var c = X509CertificateLoader.LoadCertificateFromFile(cerPath);
                return c.Subject;
            }
            catch { return null; }
        }

        /// <summary>True if a cert with this thumbprint is already in LocalMachine\TrustedPeople.</summary>
        public static bool IsTrusted(string thumbprint)
        {
            if (string.IsNullOrEmpty(thumbprint)) return false;
            try
            {
                using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates
                    .Cast<X509Certificate2>()
                    .Any(c => string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>Imports the .cer into LocalMachine\TrustedPeople. Requires elevation (we have it).</summary>
        public static bool Install(string cerPath)
        {
            try
            {
                using var cert = X509CertificateLoader.LoadCertificateFromFile(cerPath);
                using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                store.Add(cert);
                store.Close();
                return true;
            }
            catch { return false; }
        }
    }
}
