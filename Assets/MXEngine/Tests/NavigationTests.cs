using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MXEngine.Tests
{
    public sealed class NavigationTests : NavigationTestFixture
    {
        [UnityTest] public IEnumerator ScreenStackAndBack() => Run(async () =>
        {
            var first = await Screen(A);
            var second = await Screen(B);
            Assert.IsFalse(first.gameObject.activeSelf);
            Assert.IsTrue(second.gameObject.activeSelf);
            Assert.IsTrue(await Navigation.BackToPreviousScreenAsync());
            Assert.IsTrue(first.gameObject.activeSelf);
            Assert.IsFalse(await Navigation.BackToPreviousScreenAsync());
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ReopeningExistingScreenReordersWithoutInstantiate() => Run(async () =>
        {
            var first = await Screen(A);
            await Screen(B);
            await Screen(C);
            Assert.AreSame(first, await Screen(A));
            Assert.AreEqual(3, Loader.Created.Count);
            Assert.AreEqual(3, Navigation.ScreenCount);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ReplaceReleasesHistory() => Run(async () =>
        {
            await Screen(A);
            await Screen(B);
            var replacement = await Screen(C, false);
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.IsTrue(replacement.gameObject.activeSelf);
            Assert.AreEqual(1, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ModalStackAllowsDuplicateKeys() => Run(async () =>
        {
            var first = await OpenModal();
            var second = await OpenModal();
            Assert.AreNotSame(first, second);
            Assert.IsTrue(await Navigation.CloseModalAsync());
            Assert.IsTrue(await Navigation.CloseModalAsync());
            Assert.IsFalse(await Navigation.CloseModalAsync());
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator OverlayKeyIsUnique() => Run(async () =>
        {
            await OpenOverlay();
            await Fails<InvalidOperationException>(() => OpenOverlay());
            Assert.IsTrue(await Navigation.HideOverlayAsync(Overlay));
            Assert.IsFalse(await Navigation.HideOverlayAsync(Overlay));
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator FailedLoadPreservesCurrentScreen() => Run(async () =>
        {
            var first = await Screen(A);
            Loader.FailLoad = true;
            await Fails<InvalidOperationException>(() => Screen(B));
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.IsTrue(first.gameObject.activeSelf);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CancellationDoesNotCommitOrLeak() => Run(async () =>
        {
            Loader.LoadHook = token => UniTask.Delay(Timeout.Infinite, cancellationToken: token);
            using (var cts = new CancellationTokenSource())
            {
                UniTask request = Screen(A, token: cts.Token);
                cts.Cancel();
                await Fails<OperationCanceledException>(() => request);
            }
            Assert.AreEqual(0, Navigation.ScreenCount);
            Assert.AreEqual(0, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CleanupFailureCanBeRetried() => Run(async () =>
        {
            var first = await Screen(A);
            Loader.FailRelease = true;
            var replacement = await Screen(B, false);
            Assert.IsTrue(replacement.gameObject.activeSelf);
            Assert.AreEqual(1, Navigation.PendingReleaseCount);
            Loader.FailRelease = false;
            await Navigation.RetryPendingReleasesAsync();
            Assert.AreEqual(0, Navigation.PendingReleaseCount);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ConcurrentRequestsAreSerialized() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            Loader.LoadHook = async _ => await signal.Task;
            UniTask first = Screen(A);
            UniTask second = Screen(B);
            Assert.AreEqual(UniTaskStatus.Pending, first.Status);
            signal.TrySetResult();
            await first;
            await second;
            Assert.AreEqual(2, Navigation.ScreenCount);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator BackRestoresPreviousBeforeSlowReleaseCompletes() => Run(async () =>
        {
            var first = await Screen(A);
            var second = await Screen(B);
            var release = new UniTaskCompletionSource();
            Loader.ReleaseHook = _ => release.Task;
            UniTask<bool> back = Navigation.BackToPreviousScreenAsync();
            Assert.AreEqual(UniTaskStatus.Pending, back.Status);
            Assert.IsTrue(first.gameObject.activeSelf);
            Assert.IsFalse(second.gameObject.activeSelf);
            Assert.AreEqual(1, Navigation.ScreenCount);
            release.TrySetResult();
            Assert.IsTrue(await back);
            Loader.ReleaseHook = null;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CancelledBackRestoresCurrentAndHistory() => Run(async () =>
        {
            var first = await Screen(A);
            var second = await Screen(B);
            using (var cts = new CancellationTokenSource())
            {
                first.EnableHook = cts.Cancel;
                await Fails<OperationCanceledException>(() => Navigation.BackToPreviousScreenAsync(cts.Token));
                first.EnableHook = null;
            }
            Assert.AreEqual(2, Navigation.ScreenCount);
            Assert.AreEqual(0, Loader.ReleaseCalls);
            Assert.IsFalse(first.gameObject.activeSelf);
            Assert.IsTrue(second.gameObject.activeSelf);
            Assert.IsTrue(await Navigation.BackToPreviousScreenAsync());
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator BackToDestroyedScreenPreservesCurrent() => Run(async () =>
        {
            var first = await Screen(A);
            var second = await Screen(B);
            UnityEngine.Object.DestroyImmediate(first.gameObject);
            await Fails<InvalidOperationException>(() => Navigation.BackToPreviousScreenAsync());
            Assert.AreEqual(2, Navigation.ScreenCount);
            Assert.IsTrue(second.gameObject.activeSelf);
            Assert.AreEqual(0, Loader.ReleaseCalls);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ShutdownStillCleansUpWhenCancellationCallbackThrows() => Run(async () =>
        {
            await Screen(A);
            var blocker = new UniTaskCompletionSource();
            Loader.LoadHook = async token =>
            {
                UniTask wait = blocker.Task.AttachExternalCancellation(token);
                using (token.Register(() => throw new InvalidOperationException("cancellation callback")))
                    await wait;
            };
            UniTask opening = Screen(B);
            UniTask shutdown = Navigation.ShutdownAsync();
            await Fails<OperationCanceledException>(() => opening);
            await Fails<AggregateException>(() => shutdown);
            // The same completed failure is returned to later shutdown callers.
            await Fails<AggregateException>(() => Navigation.ShutdownAsync());
            Assert.AreEqual(0, Navigation.ScreenCount);
            Assert.AreEqual(0, Loader.Owned.Count);
            Assert.IsFalse(Navigation.IsInTransition);
            blocker.TrySetResult();
        });

        [UnityTest] public IEnumerator DestroyedLoadResultStillReleasesOwnership() => Run(async () =>
        {
            Loader.CreatedHook = view => UnityEngine.Object.DestroyImmediate(view.gameObject);
            await Fails<InvalidOperationException>(() => Screen());
            Assert.AreEqual(1, Loader.ReleaseCalls);
            Assert.AreEqual(0, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator DestroyCallbackCannotReenterNavigation() => Run(async () =>
        {
            var modal = await OpenModal();
            Exception rejected = null;
            modal.DestroyHook = () =>
            {
                try { Navigation.ShutdownAsync(); }
                catch (Exception exception) { rejected = exception; }
            };
            Assert.IsTrue(await Navigation.CloseModalAsync());
            Assert.IsInstanceOf<InvalidOperationException>(rejected);
            // The rejected shutdown must not poison the service.
            await Screen();
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CancelDuringActivationHidesNewScreenBeforeRestoringOld() => Run(async () =>
        {
            var first = await Screen();
            TestUi second = null;
            bool overlapped = false;
            first.EnableHook = () => overlapped = second != null && second.gameObject.activeSelf;
            using (var cts = new CancellationTokenSource())
            {
                Loader.CreatedHook = view =>
                {
                    second = (TestUi)view;
                    second.EnableHook = cts.Cancel;
                };
                await Fails<OperationCanceledException>(() => Screen(B, token: cts.Token));
            }
            Assert.IsFalse(overlapped);
            Assert.IsTrue(first.gameObject.activeSelf);
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.AreEqual(1, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator OverlayCanOpenWhileScreenLoadIsPending() => Run(async () =>
        {
            var load = new UniTaskCompletionSource();
            Loader.LoadHook = _ => load.Task;
            UniTask screen = Screen();
            Loader.LoadHook = null;
            var overlay = await OpenOverlay();
            Assert.IsTrue(overlay.gameObject.activeSelf);
            Assert.AreEqual(UniTaskStatus.Pending, screen.Status);
            load.TrySetResult();
            await screen;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ShutdownRejectsQueuedAndFutureNavigation() => Run(async () =>
        {
            var load = new UniTaskCompletionSource();
            Loader.LoadHook = token => load.Task.AttachExternalCancellation(token);
            UniTask first = Screen(A);
            UniTask queued = Screen(B);
            UniTask shutdown = Navigation.ShutdownAsync();
            await Fails<OperationCanceledException>(() => first);
            await Fails<OperationCanceledException>(() => queued);
            await shutdown;
            await Fails<ObjectDisposedException>(() => Screen(C));
            load.TrySetResult();
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator FailedShutdownReleasesRemainRetryable() => Run(async () =>
        {
            await Screen();
            await OpenModal();
            await OpenOverlay();
            Loader.FailRelease = true;
            await Fails<AggregateException>(() => Navigation.ShutdownAsync());
            Assert.AreEqual(3, Navigation.PendingReleaseCount);
            Assert.AreEqual(0, Navigation.ScreenCount + Navigation.ModalCount + Navigation.OverlayCount);
            Loader.FailRelease = false;
            await Navigation.RetryPendingReleasesAsync();
            Assert.AreEqual(0, Navigation.PendingReleaseCount);
            Assert.AreEqual(0, Loader.Owned.Count);
        });
    }
}
