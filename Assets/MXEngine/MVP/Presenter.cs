using System;
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
        private UniTaskCompletionSource _initializationCompletion;
        private UniTaskCompletionSource _disposeCompletion;

        protected Presentr(TView view)
        {
            View = view;
        }

        public async UniTask InitializeAsync()
        {
            ThrowIfDisposed();
            if (_initialized || _initializationCompletion != null)
            {
                throw new InvalidOperationException("Presenter is already initialized.");
            }

            _initializationCompletion = new UniTaskCompletionSource();
            try
            {
                try
                {
                    var state = new TState();
                    State = state;
                    await View.BindAsync(state);
                    ThrowIfDisposed();
                    await OnInitializeAsync(state);
                    ThrowIfDisposed();
                    _initialized = true;
                }
                finally
                {
                    _initializationCompletion.TrySetResult();
                }
            }
            catch
            {
                // A failed initialization is terminal; release partially bound state.
                await DisposeAsync();
                throw;
            }
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
            DisposeAsync().Forget();
        }

        // Await this before releasing the View through IViewLoader.
        public UniTask DisposeAsync()
        {
            if (_disposeCompletion != null)
                return _disposeCompletion.Task;

            _disposed = true;
            _disposeCompletion = new UniTaskCompletionSource();
            DisposeCoreAsync().Forget();
            return _disposeCompletion.Task;
        }

        private async UniTask DisposeCoreAsync()
        {
            try
            {
                // Let an in-flight hook finish before disposing the state it uses.
                if (_initializationCompletion != null)
                    await _initializationCompletion.Task;

                try
                {
                    if (View != null)
                        await View.UnbindAsync();
                }
                finally
                {
                    try
                    {
                        OnDispose();
                    }
                    finally
                    {
                        var state = State;
                        State = null;
                        state?.Dispose();
                    }
                }

                _disposeCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                _disposeCompletion.TrySetException(exception);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}
