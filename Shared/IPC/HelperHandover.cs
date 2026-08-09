using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Shared.IPC
{
    /// <summary>
    /// The requesting side of the helper handover, shared so that nobody grows their own version of it.
    ///
    /// There used to be three policies for "get the old helper out of the way": Install.ps1 killed it
    /// in a poll loop, the helper killed it in KillOtherHelperInstances, and Center waited five seconds
    /// after installing and then killed whatever was left. Same goal, three behaviours, three places to
    /// forget. This class is the single requester; the receiver lives in the helper's main loop.
    ///
    /// WHY ASK AT ALL: Process.Kill is TerminateProcess. No managed cleanup runs, so the outgoing helper
    /// hands nothing over - it stops mid-write, while MSI WMI/EC, the HidHide/ViGEm mounts and the
    /// single-instance mutex each tolerate exactly one owner.
    ///
    /// THE ACK IS NOT PERMISSION TO START. It only means "heard you, I'm going". What makes it safe to
    /// touch the hardware is the target actually being gone, so <see cref="TryOrderlyShutdown"/> waits
    /// for the process exit too and reports false for every other outcome. Callers keep their hard kill
    /// as the fallback - a build older than this protocol never acks.
    ///
    /// SERIALISATION IS DELIBERATELY HAND-ROLLED. Shared targets netstandard2.0 and carries only NLog
    /// and Microsoft.Win32.Registry; pulling System.Text.Json in here would flow a dependency into
    /// every consumer, which this project has already been burned by (see the Windows.winmd note in
    /// Shared.csproj). The payload is five flat fields, the writers are this class and Install.ps1, and
    /// the reader in the helper uses a real JSON parser - so the format stays plain JSON with no nested
    /// objects and no escaped quotes.
    /// </summary>
    public static class HelperHandover
    {
        public const string RequestFileName = "shutdown-request.json";
        public const string AckFileName = "shutdown-ack.json";

        /// <summary>Requests older than this are garbage, not instructions (boot-loop guard).</summary>
        public const int MaxRequestAgeMs = 60000;

        public const int DefaultAckTimeoutMs = 3000;
        public const int DefaultExitTimeoutMs = 6000;

        /// <summary>
        /// LocalCache\Local of the package - the folder both helpers already log into, which is why it
        /// is the right place: writable from both integrity levels and it survives the package swap.
        /// </summary>
        public static string ResolveFolder(string packageFamilyName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", packageFamilyName, "LocalCache", "Local");
        }

        public static string RequestPath(string folder) => Path.Combine(folder, RequestFileName);
        public static string AckPath(string folder) => Path.Combine(folder, AckFileName);

        /// <summary>
        /// Asks <paramref name="target"/> to shut down and waits for both signals - the ack, then the
        /// real exit. Returns true only if the process is gone; anything else means the caller should
        /// fall back to its hard kill.
        /// </summary>
        public static bool TryOrderlyShutdown(string folder, Process target, string reason,
                                              Action<string> log = null,
                                              int ackTimeoutMs = DefaultAckTimeoutMs,
                                              int exitTimeoutMs = DefaultExitTimeoutMs)
        {
            if (string.IsNullOrEmpty(folder) || target == null) return false;

            int targetPid;
            try { targetPid = target.Id; }
            catch { return false; }

            string token = Guid.NewGuid().ToString("N");
            string requestPath = RequestPath(folder);
            string ackPath = AckPath(folder);

            try
            {
                Directory.CreateDirectory(folder);
                TryDelete(ackPath, log);   // never mistake an older ack for ours

                int requesterPid;
                try { requesterPid = Process.GetCurrentProcess().Id; }
                catch { requesterPid = 0; }

                WriteAtomic(requestPath, BuildRequestJson(targetPid, token, reason, requesterPid));
                log?.Invoke($"Handover: asked PID={targetPid} to shut down (reason '{reason}', token={Shorten(token)})");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Handover: could not place request for PID={targetPid}: {ex.Message}");
                return false;
            }

            if (!WaitForAck(ackPath, token, targetPid, ackTimeoutMs))
            {
                // Withdraw our own request so a later helper cannot stumble over it.
                TryDelete(requestPath, log);
                log?.Invoke($"Handover: no ack from PID={targetPid} within {ackTimeoutMs}ms - caller should fall back");
                return false;
            }

            bool exited;
            try { exited = target.WaitForExit(exitTimeoutMs); }
            catch (Exception ex)
            {
                log?.Invoke($"Handover: could not wait for PID={targetPid}: {ex.Message}");
                exited = false;
            }

            TryDelete(ackPath, log);

            log?.Invoke(exited
                ? $"Handover: PID={targetPid} acknowledged and exited - no kill needed"
                : $"Handover: PID={targetPid} acknowledged but was alive after {exitTimeoutMs}ms - caller should fall back");

            return exited;
        }

        public static string BuildRequestJson(int targetPid, string token, string reason, int requesterPid)
        {
            // Field names must match the helper's ShutdownRequest exactly - its reader is
            // case-sensitive by default.
            return "{\n" +
                   "  \"TargetPid\": " + targetPid.ToString(CultureInfo.InvariantCulture) + ",\n" +
                   "  \"Token\": \"" + Sanitise(token) + "\",\n" +
                   "  \"RequestedAtUtc\": \"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\",\n" +
                   "  \"Reason\": \"" + Sanitise(reason) + "\",\n" +
                   "  \"RequesterPid\": " + requesterPid.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "}\n";
        }

        private static bool WaitForAck(string ackPath, string token, int targetPid, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    if (File.Exists(ackPath))
                    {
                        string text = File.ReadAllText(ackPath);
                        if (ReadString(text, "Token") == token &&
                            ReadInt(text, "HelperPid") == targetPid)
                            return true;
                    }
                }
                catch { /* half-written or locked - try again next turn */ }

                try { System.Threading.Thread.Sleep(100); } catch { }
            }
            return false;
        }

        /// <summary>Reads "Name": "value" out of a flat JSON object. Null when absent.</summary>
        public static string ReadString(string json, string name)
        {
            int v = ValueStart(json, name);
            if (v < 0) return null;
            int q = json.IndexOf('"', v);
            if (q < 0) return null;
            int end = json.IndexOf('"', q + 1);
            return end < 0 ? null : json.Substring(q + 1, end - q - 1);
        }

        /// <summary>Reads "Name": 123 out of a flat JSON object. int.MinValue when absent/unparsable.</summary>
        public static int ReadInt(string json, string name)
        {
            int v = ValueStart(json, name);
            if (v < 0) return int.MinValue;
            int i = v;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-')) i++;
            return i > start && int.TryParse(json.Substring(start, i - start),
                                             NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : int.MinValue;
        }

        /// <summary>Index just past the colon following "Name". -1 when the field is not present.</summary>
        private static int ValueStart(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            int k = json.IndexOf("\"" + name + "\"", StringComparison.Ordinal);
            if (k < 0) return -1;
            int colon = json.IndexOf(':', k);
            return colon < 0 ? -1 : colon + 1;
        }

        /// <summary>
        /// Write via temp file + move so a reader can never see a half-written request. Install.ps1
        /// does the same; it is part of the contract, not an implementation detail.
        /// </summary>
        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void TryDelete(string path, Action<string> log)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { log?.Invoke($"Handover: could not delete {Path.GetFileName(path)}: {ex.Message}"); }
        }

        /// <summary>Keeps the hand-rolled format honest: no quotes, no backslashes, no newlines.</summary>
        private static string Sanitise(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "/").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
        }

        private static string Shorten(string token)
            => string.IsNullOrEmpty(token) ? "(none)" : token.Substring(0, Math.Min(8, token.Length));
    }
}
