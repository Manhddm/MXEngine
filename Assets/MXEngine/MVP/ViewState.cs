using System;

namespace MXEngine.MVP
{
    public class ViewState : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            OnDispose();
        }

        protected virtual void OnDispose()
        {
        }
    }
}
