using System;
using System.Threading;
using System.Threading.Tasks;
using MXEngine.MVP;
using NUnit.Framework;
using UnityEngine.UI;

namespace MXEngine.Tests
{
    public sealed class TestState : ViewState
    {
        public bool Disposed;
        public bool FailDispose;
        protected override void OnDispose()
        {
            Disposed = true;
            if (FailDispose) throw new InvalidOperationException("state-dispose");
        }
    }

    public sealed class TestView : View<TestState>
    {
        private Button _button;
        public bool RequireAwake; public Action AfterBoundEnable;
        public bool FailBind;
        public bool FailUnbind;
        public int BindCount;
        public int UnbindCount;
        public Func<CancellationToken, Task> BindHook;
        public Func<Task> UnbindHook;
        private void OnEnable() { if (State != null) AfterBoundEnable?.Invoke(); }
        private void Awake() => _button = GetComponent<Button>();
        protected override async Task OnBindAsync(TestState state, CancellationToken cancellationToken)
        {
            BindCount++;
            if (RequireAwake) Assert.IsNotNull(_button, "Awake must run before Bind.");
            if (FailBind) throw new InvalidOperationException("bind");
            if (BindHook != null) await BindHook(cancellationToken);
        }
        protected override async Task OnUnbindAsync(TestState state)
        {
            UnbindCount++;
            if (UnbindHook != null) await UnbindHook();
            if (FailUnbind) throw new InvalidOperationException("unbind");
        }
    }
}

