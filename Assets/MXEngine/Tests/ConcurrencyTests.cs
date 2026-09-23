using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MXEngine.Tests
{
    public sealed class ConcurrencyTests : NavigationTestFixture
    {
        [UnityTest] public IEnumerator ExternalRequestQueuesWhileInitializeAwaits() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            var opening = Screen(configure: p => p.InitializeHook = async _ => await signal.Task).AsTask();
            var external = OpenModal().AsTask();
            Assert.IsFalse(opening.IsCompleted);
            Assert.IsFalse(external.IsCompleted, "External request must wait, not fail reentrancy.");
            Assert.AreEqual(1, Loader.Created.Count);
            signal.TrySetResult();
            await opening;
            await external;
            Assert.AreEqual(1, Navigation.ModalCount);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator OverlayCanOpenWhileScreenIsInitializing() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            var opening = Screen(configure: p => p.InitializeHook = async _ => await signal.Task).AsTask();
            var overlay = await OpenOverlay();
            Assert.IsFalse(opening.IsCompleted);
            Assert.IsTrue(overlay.TestView.gameObject.activeSelf);
            signal.TrySetResult();
            await opening;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator NestedRequestBeforeAwaitFailsFast() => Run(async () =>
        {
            await Fails<InvalidOperationException>(() => Screen(configure: p =>
                p.InitializeHook = async _ => await OpenModal()).AsTask());
            Assert.AreEqual(0, Navigation.ModalCount);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator NestedRequestAfterUniTaskAwaitFailsFast() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            var opening = Screen(configure: p => p.InitializeHook = async _ =>
            { await signal.Task; await OpenModal(); }).AsTask();
            var external = OpenModal().AsTask();
            Assert.IsFalse(external.IsCompleted);
            signal.TrySetResult();
            await Fails<InvalidOperationException>(() => opening);
            await external;
            Assert.AreEqual(1, Navigation.ModalCount);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator NestedBindAfterAwaitFailsFast() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            Loader.Configure = v => v.BindHook = async _ => { await signal.Task; await OpenModal(); };
            var opening = Screen().AsTask();
            signal.TrySetResult();
            await Fails<InvalidOperationException>(() => opening);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator NestedUnbindAfterAwaitFailsFast() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            var a = await Screen(A);
            a.TestView.UnbindHook = async () => { await signal.Task; await OpenModal(); };
            var replacing = Screen(B, stack: false).AsTask();
            signal.TrySetResult();
            await replacing;
            Assert.AreEqual(1, Diagnostics.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator NestedShutdownFailsFast() => Run(async () =>
        {
            await Fails<InvalidOperationException>(() => Screen(configure: p =>
                p.InitializeHook = async _ => await Navigation.ShutdownAsync()).AsTask());
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator NestedDisposeNavigationFailsFast() => Run(async () =>
        {
            await Screen(A, p => p.DisposeHook = () => Navigation.CloseModalAsync().GetAwaiter().GetResult());
            await Screen(B, stack: false);
            Assert.AreEqual(1, Diagnostics.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator FailedHookDoesNotPoisonLaterRequestContext() => Run(async () =>
        {
            await Fails<InvalidOperationException>(() => Screen(configure: p => p.FailInitialize = true).AsTask());
            await OpenModal();
            Assert.AreEqual(1, Navigation.ModalCount);
            await AssertEmpty();
        });
    }
}
