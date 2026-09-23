using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MXEngine.MVP
{
    public abstract class View<TState> : MonoBehaviour
        where TState : ViewState
    {
        public TState State { get; private set; }
        public async UniTask BindAsync(TState state)
        {
            State = state;
            await OnBindAsync(state);
        }
        
        public async UniTask UnbindAsync()
        {
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
        protected abstract UniTask OnBindAsync(TState state);
        protected virtual UniTask OnUnbindAsync(TState state)
        {
            return UniTask.CompletedTask;
        }
        
    }
}
