using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MXEngine.MVP
{
    public abstract class View<TState> : MonoBehaviour
        where TState : ViewState
    {
        public TState State { get; private set; }
        public UniTask BindAsync(TState state, CancellationToken cancellationToken = default) =>
            BindCoreAsync(state, cancellationToken).AsUniTask();

        private async Task BindCoreAsync(TState state, CancellationToken cancellationToken)
        {
            using var context = LifecycleContext.Enter();
            cancellationToken.ThrowIfCancellationRequested();
            if (State != null)
                throw new InvalidOperationException("View is already bound.");

            State = state ?? throw new ArgumentNullException(nameof(state));
            await OnBindAsync(state, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        
        public UniTask UnbindAsync() => UnbindCoreAsync().AsUniTask();

        private async Task UnbindCoreAsync()
        {
            using var context = LifecycleContext.Enter();
            if (State == null)
                return;

            try
            {
                await OnUnbindAsync(State);
            }
            finally
            {
                State = null;
            }
        }
        protected abstract Task OnBindAsync(TState state, CancellationToken cancellationToken);
        protected virtual Task OnUnbindAsync(TState state)
        {
            return Task.CompletedTask;
        }
        
    }
}
