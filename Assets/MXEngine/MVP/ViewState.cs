using System;

namespace MXEngine.MVP
{
    public class ViewState : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;

            OnDispose();
            _disposed = true;
        }

        protected virtual void OnDispose()
        {
        }
    }
}