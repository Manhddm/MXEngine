using System;
using System.Threading;

namespace MXEngine
{
    // Standard async lifecycle hooks flow ExecutionContext; UniTask's builder does not.
    internal static class LifecycleContext
    {
        private static readonly AsyncLocal<int> Depth = new();
        internal static Scope Enter() => new Scope(Depth.Value);
        internal static void ThrowIfActive()
        {
            if (Depth.Value != 0)
                throw new InvalidOperationException("Navigation cannot be requested from a UI lifecycle callback.");
        }
        internal readonly struct Scope : IDisposable
        {
            private readonly int _previous;
            internal Scope(int previous) { _previous = previous; Depth.Value = previous + 1; }
            public void Dispose() => Depth.Value = _previous;
        }
    }
}
