using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace XboxGamingBarHelper.IPC
{
    /// <summary>
    /// Named Pipe server for IPC with the widget.
    /// Uses proper ACLs to allow UWP apps to connect (even when helper is elevated).
    /// </summary>
    public class NamedPipeServer : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Pipe name - simple name without LOCAL\ prefix
        /// The ACLs handle UWP access permissions
        /// </summary>
        public const string PipeName = "GoTweaksHelper";

        /// <summary>
        /// Full pipe path for display/debugging
        /// </summary>
        public static readonly string FullPipePath = $"\\\\.\\pipe\\{PipeName}";

        private NamedPipeServerStream _pipeServer;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listenerTask;
        private bool _isDisposed;
        private bool _isConnected;

        /// <summary>
        /// Event raised when a message is received from the widget
        /// </summary>
        public event EventHandler<PipeMessageEventArgs> MessageReceived;

        /// <summary>
        /// Event raised when the widget connects
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// Event raised when the widget disconnects
        /// </summary>
        public event EventHandler Disconnected;

        /// <summary>
        /// Whether a client is currently connected
        /// </summary>
        public bool IsConnected => _isConnected && _pipeServer?.IsConnected == true;

        /// <summary>
        /// Starts the pipe server and begins listening for connections
        /// </summary>
        public void Start()
        {
            if (_listenerTask != null)
            {
                Logger.Warn("Pipe server already started");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoop(_cancellationTokenSource.Token));
            Logger.Info($"Named pipe server started: {FullPipePath}");
        }

        /// <summary>
        /// Stops the pipe server
        /// </summary>
        public void Stop()
        {
            Logger.Info("Stopping pipe server...");
            _cancellationTokenSource?.Cancel();

            try
            {
                // Force disconnect to unblock WaitForConnection
                _pipeServer?.Dispose();
            }
            catch { }

            try
            {
                _listenerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            _listenerTask = null;
            Logger.Info("Pipe server stopped");
        }

        /// <summary>
        /// Sends a message to the connected widget
        /// </summary>
        public bool SendMessage(string message)
        {
            if (!IsConnected)
            {
                Logger.Debug("Cannot send message - not connected");
                return false;
            }

            try
            {
                lock (_writeLock)
                {
                    _writer?.WriteLine(message);
                    _writer?.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending message: {ex.Message}");
                HandleDisconnection();
                return false;
            }
        }

        /// <summary>
        /// Main listen loop - accepts connections and reads messages
        /// </summary>
        private void ListenLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Create pipe with security that allows UWP apps
                    CreatePipeWithSecurity();

                    Logger.Info("Waiting for widget connection...");
                    _pipeServer.WaitForConnection();

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    _isConnected = true;
                    _reader = new StreamReader(_pipeServer, Encoding.UTF8);
                    _writer = new StreamWriter(_pipeServer, Encoding.UTF8) { AutoFlush = false };

                    Logger.Info("Widget connected via named pipe");
                    Connected?.Invoke(this, EventArgs.Empty);

                    // Read messages until disconnection
                    ReadMessages(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    Logger.Debug($"Pipe IO error (likely disconnect): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Pipe server error: {ex.Message}");
                }
                finally
                {
                    HandleDisconnection();
                }

                // Brief delay before accepting new connection
                if (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Creates the named pipe server with security that allows UWP apps to connect
        /// </summary>
        private void CreatePipeWithSecurity()
        {
            // Create security that allows:
            // 1. Current user (full control)
            // 2. ALL APPLICATION PACKAGES (S-1-15-2-1) - allows UWP apps to connect
            var pipeSecurity = new PipeSecurity();

            // Allow current user full control
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                WindowsIdentity.GetCurrent().User,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            // Allow ALL APPLICATION PACKAGES (this is the key for UWP access)
            // SID S-1-15-2-1 = ALL_APPLICATION_PACKAGES
            var allAppPackagesSid = new SecurityIdentifier("S-1-15-2-1");
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                allAppPackagesSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            // Also allow ALL RESTRICTED APPLICATION PACKAGES for extra compatibility
            // SID S-1-15-2-2 = ALL_RESTRICTED_APPLICATION_PACKAGES
            try
            {
                var allRestrictedAppPackagesSid = new SecurityIdentifier("S-1-15-2-2");
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    allRestrictedAppPackagesSid,
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));
            }
            catch
            {
                // S-1-15-2-2 may not exist on older Windows versions
            }

            // Use the NamedPipeServerStream constructor with PipeSecurity (available in .NET Framework)
            _pipeServer = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1, // maxNumberOfServerInstances
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                4096, // inBufferSize
                4096, // outBufferSize
                pipeSecurity);
        }

        /// <summary>
        /// Reads messages from the connected client
        /// </summary>
        private void ReadMessages(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _pipeServer.IsConnected)
                {
                    var line = _reader.ReadLine();
                    if (line == null)
                    {
                        // Client disconnected
                        Logger.Info("Widget disconnected (end of stream)");
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Logger.Debug($"Received: {line.Substring(0, Math.Min(100, line.Length))}...");
                        MessageReceived?.Invoke(this, new PipeMessageEventArgs(line));
                    }
                }
            }
            catch (IOException)
            {
                // Normal disconnection
            }
        }

        /// <summary>
        /// Handles client disconnection
        /// </summary>
        private void HandleDisconnection()
        {
            if (!_isConnected) return;

            _isConnected = false;
            Logger.Info("Widget disconnected");

            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _pipeServer?.Dispose(); } catch { }

            _reader = null;
            _writer = null;
            _pipeServer = null;

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Event args for pipe messages
    /// </summary>
    public class PipeMessageEventArgs : EventArgs
    {
        public string Message { get; }

        public PipeMessageEventArgs(string message)
        {
            Message = message;
        }
    }
}
