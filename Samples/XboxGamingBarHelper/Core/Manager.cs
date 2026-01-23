using System;
using NLog;

namespace XboxGamingBarHelper.Core
{
    internal abstract class Manager : IManager
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private bool disposed = false;

        protected Manager()
        {
        }

        public virtual void Update()
        {
            // Reserved.
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources in derived classes
                }
                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
