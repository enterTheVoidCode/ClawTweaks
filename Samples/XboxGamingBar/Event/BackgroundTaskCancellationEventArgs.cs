using System;
using Windows.ApplicationModel.Background;

namespace XboxGamingBar.Event
{
    internal class BackgroundTaskCancellationEventArgs : EventArgs
    {
        protected BackgroundTaskCancellationReason reason;
        public BackgroundTaskCancellationReason Reason
        {
            get { return reason; }
        }

        public BackgroundTaskCancellationEventArgs(BackgroundTaskCancellationReason reason)
        {
            this.reason = reason;
        }
    }
}
