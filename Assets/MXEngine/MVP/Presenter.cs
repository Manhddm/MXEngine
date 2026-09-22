using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace MXEngine.MVP
{
    public class Presentr<TView, TState> : IDisposable
        where TView : View<TState>
        where TState : ViewState, new()
    {
        protected TView View { get; }
        protected TState State { get; private set; }
        private bool _initialized;
        private bool _disposed;

        protected Presentr(TView view)
        {
            View = view;
        }

        public async UniTask InitializeAsync()
        {
            if (_initialized)
            {
                throw new InvalidOperationException("Presenter is already initialized.");
            }

            State = new TState();
            await View.BindAsync(State);
            await OnInitializeAsync(State);
            _initialized = true;
        }

        protected virtual UniTask OnInitializeAsync(TState state)
        {
            return UniTask.CompletedTask;
        }

        protected virtual void OnDispose()
        {
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            OnDispose();

            State?.Dispose();

            _disposed = true;
        }
    }
}