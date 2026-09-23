using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MXEngine.Tests
{
    public sealed class CancellationTests : NavigationTestFixture
    {
        [UnityTest] public IEnumerator CanceledBeforeNavigationDoesNotLoad() => Run(async () =>
        {
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Fails<OperationCanceledException>(() => Screen(token: cts.Token).AsTask());
            Assert.AreEqual(0, Loader.Created.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CanceledGateWaitDoesNotLoadOrCorruptSemaphore() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            var opening = Screen(configure: p => p.InitializeHook = async _ => await signal.Task).AsTask();
            using var cts = new CancellationTokenSource();
            var modal = OpenModal(cts.Token).AsTask();
            cts.Cancel();
            await Fails<OperationCanceledException>(() => modal);
            Assert.AreEqual(1, Loader.Created.Count);
            signal.TrySetResult(); await opening;
            await OpenModal();
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CancellationReachesLoader() => Run(async () =>
        {
            Loader.LoadHook = token => Task.Delay(Timeout.Infinite, token);
            using var cts = new CancellationTokenSource();
            var opening = Screen(token: cts.Token).AsTask();
            cts.Cancel();
            await Fails<OperationCanceledException>(() => opening);
            Assert.AreEqual(0, Loader.Owned.Count);
            Assert.AreEqual(0, Navigation.ScreenCount);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CancellationReachesPresenterAndCleansState() => Run(async () =>
        {
            TestScreen captured = null;
            using var cts = new CancellationTokenSource();
            var opening = Screen(configure: p =>
            { captured = p; p.InitializeHook = token => Task.Delay(Timeout.Infinite, token); }, token: cts.Token).AsTask();
            var state = captured.TestState;
            cts.Cancel();
            await Fails<OperationCanceledException>(() => opening);
            Assert.IsTrue(state.Disposed);
            Assert.AreEqual(1, captured.Disposals);
            Assert.AreEqual(0, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CancellationReachesBind() => Run(async () =>
        {
            Loader.Configure = v => v.BindHook = token => Task.Delay(Timeout.Infinite, token);
            using var cts = new CancellationTokenSource();
            var opening = Screen(token: cts.Token).AsTask();
            cts.Cancel();
            await Fails<OperationCanceledException>(() => opening);
            Assert.AreEqual(0, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CancelAfterCreateBeforeCommitRestoresOldScreen() => Run(async () =>
        {
            var a = await Screen(A);
            using var cts = new CancellationTokenSource();
            Loader.Configure = v => v.AfterBoundEnable = cts.Cancel;
            await Fails<OperationCanceledException>(() => Screen(B, token: cts.Token).AsTask());
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.AreEqual(1, Loader.Owned.Count);
            Assert.IsTrue(a.TestView.gameObject.activeSelf);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ShutdownCancelsInFlightAndQueuedRequests() => Run(async () =>
        {
            var opening = Screen(configure: p => p.InitializeHook = token => Task.Delay(Timeout.Infinite, token)).AsTask();
            var modal = OpenModal().AsTask();
            var closing = Navigation.ShutdownAsync().AsTask();
            await Fails<OperationCanceledException>(() => opening);
            await Fails<OperationCanceledException>(() => modal);
            await closing;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CallerCancellationDuringCommittedTeardownDoesNotAbortCleanup() => Run(async () =>
        {
            using var cts = new CancellationTokenSource();
            await Screen(A, p => p.DisposeHook = cts.Cancel);
            var b = await Screen(B, token: cts.Token, stack: false);
            Assert.IsTrue(cts.IsCancellationRequested);
            Assert.IsTrue(b.TestView.gameObject.activeSelf);
            Assert.AreEqual(1, Loader.Owned.Count);
            await AssertEmpty();
        });
    }
}
