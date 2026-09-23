using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace MXEngine.MVP
{
    public class Presenter<TView, TState> : IDisposable
        where TView : View<TState>
        where TState : ViewState, new()
    {
        protected TView View { get; }
        protected TState State { get; private set; }
        private bool _disposed;
        private TaskCompletionSource<bool> _initializationCompletion;
        private TaskCompletionSource<bool> _disposeCompletion;
        private CancellationTokenSource _initializationCancellation;

        protected Presenter(TView view)
        {
            View = view != null ? view : throw new ArgumentNullException(nameof(view));
        }

        public UniTask InitializeAsync(CancellationToken cancellationToken = default) =>
            InitializeCoreAsync(cancellationToken).AsUniTask();

        private async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (_initializationCompletion != null)
                throw new InvalidOperationException("Presenter is already initialized.");
            _initializationCompletion = new TaskCompletionSource<bool>();
            _initializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var context = LifecycleContext.Enter();
            try
            {
                try
                {
                    var token = _initializationCancellation.Token;
                    token.ThrowIfCancellationRequested();
                    State = new TState();
                    await View.BindAsync(State, token);
                    token.ThrowIfCancellationRequested();
                    ThrowIfDisposed();
                    await OnInitializeAsync(State, token);
                    token.ThrowIfCancellationRequested();
                    ThrowIfDisposed();
                }
                finally
                {
                    var source = _initializationCancellation;
                    _initializationCancellation = null;
                    // Do not release a View while an initialization hook still uses it.
                    _initializationCompletion.TrySetResult(true);
                    source.Dispose();
                }
            }
            catch (Exception initializationException)
            {
                try { await DisposeAsync(); }
                catch (Exception cleanupException)
                {
                    throw new AggregateException("Presenter initialization and cleanup both failed.",
                        initializationException, cleanupException);
                }
                throw;
            }
        }

        // Task preserves context even when this hook awaits a UniTask.
        protected virtual Task OnInitializeAsync(TState state, CancellationToken cancellationToken) => Task.CompletedTask;
        protected virtual void OnDispose() { }
        public void Dispose() => DisposeAsync().Forget();

        public UniTask DisposeAsync()
        {
            if (_disposeCompletion != null)
                return _disposeCompletion.Task.AsUniTask();
            _disposed = true;
            _disposeCompletion = new TaskCompletionSource<bool>();
            Exception cancellationError = null;
            try { _initializationCancellation?.Cancel(); }
            catch (Exception exception) { cancellationError = exception; }
            CompleteDisposalAsync(cancellationError).AsUniTask().Forget();
            return _disposeCompletion.Task.AsUniTask();
        }

        private async Task CompleteDisposalAsync(Exception cancellationError)
        {
            using var context = LifecycleContext.Enter();
            List<Exception> errors = null;
            if (cancellationError != null)
                (errors ??= new List<Exception>()).Add(cancellationError);
            try
            {
                if (_initializationCompletion != null)
                    await _initializationCompletion.Task;
                try { if (View != null) await View.UnbindAsync(); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
                try { OnDispose(); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
                var state = State;
                State = null;
                try { state?.Dispose(); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
                if (errors != null)
                    throw errors.Count == 1 ? errors[0] : new AggregateException("Presenter cleanup failed.", errors);
                _disposeCompletion.TrySetResult(true);
            }
            catch (Exception exception) { _disposeCompletion.TrySetException(exception); }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }
    }
}
