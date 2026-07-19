using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shared.Enums;

namespace ClawTweaksSetup.Core
{
    /// <summary>
    /// Client for the Helper's second Named Pipe ("ClawTweaksCenter", see
    /// XboxGamingBarHelper/Program.cs InitializeConnection). Speaks the exact same line-delimited-JSON
    /// wire protocol the widget uses on its own pipe ({"RequestId":0,"Command":1,"Function":N,
    /// "Content":"..."}), so property writes go through the Helper's existing generic property
    /// dispatch (properties.HandlePipeMessage) — no Helper logic is duplicated here.
    ///
    /// Deliberately does NOT reference Shared.IPC.PipeMessage: that type's ToValueSet()/FromValueSet()
    /// pull in Windows.Foundation.Collections.ValueSet (WinRT), which Shared.csproj's own comment warns
    /// must never flow into a modern SDK-style consumer like this one (CoreClrInitFailure at startup).
    /// Only the plain Function/Command enums are used here — those are safe, same as DeviceInfo/
    /// DeviceType already are elsewhere in this project.
    /// </summary>
    public sealed class HelperPipeClient : IDisposable
    {
        private const string PipeName = "ClawTweaksCenter";

        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private CancellationTokenSource _cts;
        private Task _readLoop;

        private readonly object _valuesLock = new object();
        private readonly Dictionary<Function, string> _lastKnownValues = new Dictionary<Function, string>();

        public bool IsConnected => _pipe?.IsConnected == true;

        /// <summary>Raised whenever the helper pushes a property value (our own writes echo back too).</summary>
        public event Action<Function, string> PropertyUpdated;

        /// <summary>Connects with retries — the helper may still be starting up. Returns false on timeout.</summary>
        public async Task<bool> ConnectAsync(TimeSpan timeout, Action<string> log = null)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipe.ConnectAsync(1500).ConfigureAwait(false);

                    _reader = new StreamReader(_pipe, Encoding.UTF8);
                    _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = false };

                    _cts = new CancellationTokenSource();
                    _readLoop = Task.Run(() => ReadLoop(_cts.Token));

                    log?.Invoke("Connected to helper.");
                    return true;
                }
                catch (Exception)
                {
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = null;
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }

            log?.Invoke("Timed out waiting for the helper's pipe.");
            return false;
        }

        private void ReadLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _pipe.IsConnected)
                {
                    string line = _reader.ReadLine();
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (TryParseFunctionContent(line, out var function, out var content))
                    {
                        lock (_valuesLock) { _lastKnownValues[function] = content; }
                        PropertyUpdated?.Invoke(function, content);
                    }
                }
            }
            catch (IOException) { /* normal on disconnect */ }
            catch (ObjectDisposedException) { /* normal on dispose */ }
        }

        /// <summary>Same hand-rolled extraction the widget's own PipeClient.cs uses — no JSON library.</summary>
        private static bool TryParseFunctionContent(string json, out Function function, out string content)
        {
            function = Function.None;
            content = null;

            var funcMatch = Regex.Match(json, "\"Function\"\\s*:\\s*(-?\\d+)");
            if (!funcMatch.Success) return false;
            if (!int.TryParse(funcMatch.Groups[1].Value, out int funcInt)) return false;
            function = (Function)funcInt;

            var contentMatch = Regex.Match(json, "\"Content\"\\s*:\\s*\"([^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"");
            if (contentMatch.Success) content = UnescapeJson(contentMatch.Groups[1].Value);
            return true;
        }

        private static string UnescapeJson(string s) =>
            s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");

        /// <summary>Fire-and-forget property write — RequestId stays 0 (async push, no correlated ack
        /// expected back). The helper's own value-change broadcast is what confirms it landed.</summary>
        public bool SetProperty(Function function, object value)
        {
            if (!IsConnected) return false;
            string content = value is bool b ? (b ? "true" : "false") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            string json = $"{{\"RequestId\":0,\"Command\":{(int)Command.Set},\"Function\":{(int)function},\"Content\":\"{content}\"}}";

            try
            {
                lock (_writeLock)
                {
                    _writer.WriteLine(json);
                    _writer.Flush();
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool TryGetLastKnownValue(Function function, out string content)
        {
            lock (_valuesLock) return _lastKnownValues.TryGetValue(function, out content);
        }

        /// <summary>
        /// Asks the helper to push its current state for the properties onboarding cares about (see
        /// Program.PipeHandlers.cs PushCenterStatusSnapshot — replies only on this pipe, never the
        /// widget's). Fire-and-forget; caller awaits the resulting PropertyUpdated pushes separately.
        /// </summary>
        public bool RequestStatusRefresh()
        {
            if (!IsConnected) return false;
            try
            {
                lock (_writeLock)
                {
                    _writer.WriteLine("{\"RequestId\":0,\"Command\":0,\"Function\":0,\"CenterRequestStatus\":true}");
                    _writer.Flush();
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sets a property, then waits (via the PropertyUpdated push, not polling a request/response)
        /// until the helper confirms the expected value — or times out. Used for onboarding steps that
        /// must not proceed until the previous one is actually done (e.g. Center M fully disabled).
        /// </summary>
        public async Task<bool> SetAndWaitForConfirmationAsync(Function function, object value, string expectedContent, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            void Handler(Function f, string c)
            {
                if (f == function && string.Equals(c, expectedContent, StringComparison.OrdinalIgnoreCase))
                    tcs.TrySetResult(true);
            }

            PropertyUpdated += Handler;
            try
            {
                // Already at the target value? Nothing to wait for.
                if (TryGetLastKnownValue(function, out var current) &&
                    string.Equals(current, expectedContent, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!SetProperty(function, value)) return false;

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
                return completed == tcs.Task && tcs.Task.Result;
            }
            finally
            {
                PropertyUpdated -= Handler;
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _pipe?.Dispose(); } catch { }
        }
    }
}
