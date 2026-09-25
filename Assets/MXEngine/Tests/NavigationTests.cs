using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MXEngine.Tests
{
    public sealed class NavigationTests : NavigationTestFixture
    {
        [UnityTest] public IEnumerator PushAndBackRestorePreviousScreen() => Run(async () =>
        {
            var a = await Screen(A);
            var b = await Screen(B);
            Assert.IsFalse(a.TestView.gameObject.activeSelf);
            Assert.IsTrue(b.TestView.gameObject.activeSelf);
            Assert.IsTrue(await Navigation.BackToPreviousScreenAsync());
            Assert.IsTrue(a.TestView.gameObject.activeSelf);
            Assert.AreEqual(1, b.Disposals);
            Assert.IsFalse(await Navigation.BackToPreviousScreenAsync());
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ReopenBuriedScreenPreservesHistory() => Run(async () =>
        {
            var a = await Screen(A);
            var b = await Screen(B);
            var c = await Screen(C);
            Assert.AreSame(a, await Screen(A));
            Assert.AreEqual(3, Navigation.ScreenCount);
            Assert.AreEqual(3, Loader.Created.Count);
            Assert.IsFalse(c.TestView.gameObject.activeSelf);
            Assert.IsTrue(await Navigation.BackToPreviousScreenAsync());
            Assert.IsTrue(c.TestView.gameObject.activeSelf);
            await Navigation.BackToPreviousScreenAsync();
            Assert.IsTrue(b.TestView.gameObject.activeSelf);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator SameTopScreenIsReused() => Run(async () =>
        {
            var a = await Screen();
            Assert.AreEqual(A, Loader.LastKey);
            Assert.AreSame(a, await Screen());
            Assert.AreEqual(1, Loader.Created.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ReplaceReleasesAllHistory() => Run(async () =>
        {
            var a = await Screen(A);
            var b = await Screen(B);
            var c = await Screen(C, stack: false);
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.AreEqual(1, a.Disposals);
            Assert.AreEqual(1, b.Disposals);
            Assert.IsTrue(c.TestView.gameObject.activeSelf);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ReplaceCleanupFailureReturnsCommittedPresenter() => Run(async () =>
        {
            await Screen(A, p => p.FailDispose = true);
            var b = await Screen(B, stack: false);
            Assert.IsTrue(b.TestView.gameObject.activeSelf);
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.AreEqual(1, Diagnostics.Count);
            Assert.AreEqual(1, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ReleaseFailureKeepsOwnershipForRetry() => Run(async () =>
        {
            var a = await Screen(A);
            Loader.FailRelease = true;
            var b = await Screen(B, stack: false);
            Assert.AreEqual(1, Diagnostics.Count);
            Assert.AreEqual(1, Navigation.PendingReleaseCount);
            Assert.AreEqual(2, Loader.Owned.Count);
            Assert.IsFalse(a.TestView.gameObject.activeSelf);
            Loader.FailRelease = false;
            await Navigation.RetryPendingReleasesAsync();
            Assert.AreEqual(0, Navigation.PendingReleaseCount);
            Assert.AreEqual(1, a.Disposals);
            Assert.IsTrue(b.TestView.gameObject.activeSelf);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator RepeatedModalCreatesDistinctInstances() => Run(async () =>
        {
            var first = await OpenModal();
            var second = await OpenModal();
            var third = await OpenModal();
            Assert.AreNotSame(first, second);
            Assert.AreNotSame(second, third);
            Assert.AreEqual(3, Loader.Owned.Count);
            Assert.IsTrue(await Navigation.CloseModalAsync());
            Assert.AreEqual(1, third.Disposals);
            await Navigation.CloseAllModalsAsync();
            Assert.AreEqual(1, first.Disposals);
            Assert.AreEqual(1, second.Disposals);
            Assert.IsFalse(await Navigation.CloseModalAsync());
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CloseAllContinuesAfterReleaseFailure() => Run(async () =>
        {
            await OpenModal(); await OpenModal(); await OpenModal();
            Loader.FailRelease = true;
            await Navigation.CloseAllModalsAsync();
            Assert.AreEqual(0, Navigation.ModalCount);
            Assert.AreEqual(3, Navigation.PendingReleaseCount);
            Assert.AreEqual(3, Loader.ReleaseCalls);
            Assert.AreEqual(1, Diagnostics.Count);
            Loader.FailRelease = false;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator OverlayDuplicateAndHide() => Run(async () =>
        {
            var overlay = await OpenOverlay();
            await Fails<InvalidOperationException>(() => OpenOverlay().AsTask());
            Assert.AreEqual(1, Navigation.OverlayCount);
            Assert.IsTrue(await Navigation.HideOverlayAsync(Overlay));
            Assert.AreEqual(1, overlay.Disposals);
            Assert.IsFalse(await Navigation.HideOverlayAsync(Overlay));
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CloseAndHideReportPostCommitReleaseErrors() => Run(async () =>
        {
            await OpenModal();
            await OpenOverlay();
            Loader.FailRelease = true;
            Assert.IsTrue(await Navigation.CloseModalAsync());
            Assert.IsTrue(await Navigation.HideOverlayAsync(Overlay));
            Assert.AreEqual(0, Navigation.ModalCount);
            Assert.AreEqual(0, Navigation.OverlayCount);
            Assert.AreEqual(2, Navigation.PendingReleaseCount);
            Assert.AreEqual(2, Diagnostics.Count);
            Loader.FailRelease = false;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ComponentOnlyOverlayIsVisible() => Run(async () =>
        {
            var view = await Navigation.ShowOverlayAsync<TestView>(Overlay);
            Assert.IsTrue(view.gameObject.activeSelf);
            await Navigation.HideOverlayAsync(Overlay);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator FailedLoadPreservesPreviousScreen() => Run(async () =>
        {
            var a = await Screen(A);
            Loader.FailLoad = true;
            await Fails<InvalidOperationException>(() => Screen(B).AsTask());
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.IsTrue(a.TestView.gameObject.activeSelf);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator FailedInitializePreservesPreviousScreen() => Run(async () =>
        {
            var a = await Screen(A);
            await Fails<InvalidOperationException>(() => Screen(B, p => p.FailInitialize = true).AsTask());
            Assert.AreEqual(1, Navigation.ScreenCount);
            Assert.AreEqual(1, Loader.Owned.Count);
            Assert.IsTrue(a.TestView.gameObject.activeSelf);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator CreationAndReleaseFailureRemainRecoverable() => Run(async () =>
        {
            Loader.FailRelease = true;
            await Fails<AggregateException>(() => Screen(configure: p => p.FailInitialize = true).AsTask());
            Assert.AreEqual(0, Navigation.ScreenCount);
            Assert.AreEqual(1, Navigation.PendingReleaseCount);
            Loader.FailRelease = false;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ShutdownFailureCanRetryWithoutDoubleDispose() => Run(async () =>
        {
            var a = await Screen(); await OpenModal(); await OpenOverlay();
            Loader.FailRelease = true;
            await Fails<AggregateException>(() => Navigation.ShutdownAsync().AsTask());
            Assert.AreEqual(0, Navigation.ScreenCount);
            Assert.AreEqual(0, Navigation.ModalCount);
            Assert.AreEqual(0, Navigation.OverlayCount);
            Assert.AreEqual(3, Navigation.PendingReleaseCount);
            Loader.FailRelease = false;
            await Navigation.RetryPendingReleasesAsync();
            Assert.AreEqual(0, Loader.Owned.Count);
            Assert.AreEqual(1, a.Disposals);
            await Fails<ObjectDisposedException>(() => Screen().AsTask());
        });
    }
}
