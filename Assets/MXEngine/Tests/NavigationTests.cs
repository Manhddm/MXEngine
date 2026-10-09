using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
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
    }
}
