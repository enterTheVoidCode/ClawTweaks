using System;

namespace XboxGamingBarHelper.Windows
{
    public struct ProcessWindow
    {
        public int ProcessId { get; }
        public IntPtr WindowHandle { get; }
        public string Title { get; }
        public string ProcessName { get; }
        public string Path { get; }
        public bool IsForeground { get; }

        public ProcessWindow(int processId, IntPtr windowHandle, string title, string processName, string path, bool isForeground)
        {
            ProcessId = processId;
            WindowHandle = windowHandle;
            Title = title;
            ProcessName = processName;
            Path = path;
            IsForeground = isForeground;
        }
    }
}
