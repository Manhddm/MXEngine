using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MXEngine.Tests
{
    public sealed class PresenterTests : NavigationTestFixture
    {
        [UnityTest] public IEnumerator InitializeAndDisposeAreOrdered() => Run(async () =>
        {
            var presenter = await Screen();
            var state = presenter.TestState;
            Assert.AreSame(state, presenter.TestView.State);
            Assert.AreEqual(1, presenter.TestView.BindCount);
            await presenter.DisposeAsync();
            Assert.IsNull(presenter.TestView.State);
            Assert.IsNull(presenter.TestState);
            Assert.IsTrue(state.Disposed);
            Assert.AreEqual(1, presenter.TestView.UnbindCount);
            await presenter.DisposeAsync();
            Assert.AreEqual(1, presenter.Disposals);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator DoubleInitializeIsRejected() => Run(async () =>
        {
            var presenter = await Screen();
            await Fails<InvalidOperationException>(() => presenter.InitializeAsync().AsTask());
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator DisposeBeforeInitializeIsTerminal() => Run(async () =>
        {
            TestScreen captured = null;
            await Fails<ObjectDisposedException>(() => Screen(configure: p =>
            { captured = p; p.DisposeAsync().GetAwaiter().GetResult(); }).AsTask());
            Assert.AreEqual(1, captured.Disposals);
            Assert.AreEqual(0, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator DisposeWaitsForInFlightHook() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            TestScreen captured = null;
            var opening = Screen(configure: p => { captured = p; p.InitializeHook = async _ => await signal.Task; }).AsTask();
            var state = captured.TestState;
            var disposal = captured.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(state.Disposed);
            signal.TrySetResult();
            await Fails<OperationCanceledException>(() => opening);
            await disposal;
            Assert.IsTrue(state.Disposed);
            Assert.AreEqual(1, captured.Disposals);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator ConcurrentInitializeIsRejected() => Run(async () =>
        {
            var signal = new UniTaskCompletionSource();
            TestScreen captured = null;
            var opening = Screen(configure: p => { captured = p; p.InitializeHook = async _ => await signal.Task; }).AsTask();
            await Fails<InvalidOperationException>(() => captured.InitializeAsync().AsTask());
            signal.TrySetResult();
            await opening;
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator BindFailureUnbindsPartialState() => Run(async () =>
        {
            TestState state = null;
            Loader.Configure = view => view.BindHook = _ =>
            { state = view.State; throw new InvalidOperationException("bind"); };
            await Fails<InvalidOperationException>(() => Screen().AsTask());
            Assert.IsTrue(state.Disposed);
            Assert.AreEqual(0, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator InitializeFailureDisposesState() => Run(async () =>
        {
            TestScreen captured = null;
            await Fails<InvalidOperationException>(() => Screen(configure: p => { captured = p; p.FailInitialize = true; }).AsTask());
            Assert.AreEqual(1, captured.Disposals);
            Assert.IsNull(captured.TestState);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator UnbindFailureStillDisposesPresenterAndState() => Run(async () =>
        {
            var p = await Screen();
            var state = p.TestState;
            p.TestView.FailUnbind = true;
            await Fails<InvalidOperationException>(() => p.DisposeAsync().AsTask());
            Assert.AreEqual(1, p.Disposals);
            Assert.IsTrue(state.Disposed);
            Assert.IsNull(p.TestView.State);
            // Navigation reports the cached disposal failure, but still releases the view.
            await Fails<AggregateException>(() => Navigation.ShutdownAsync().AsTask());
            Assert.AreEqual(0, Loader.Owned.Count);
        });

        [UnityTest] public IEnumerator DisposeFailureStillDisposesState() => Run(async () =>
        {
            var p = await Screen();
            var state = p.TestState;
            p.FailDispose = true;
            await Fails<InvalidOperationException>(() => p.DisposeAsync().AsTask());
            Assert.IsTrue(state.Disposed);
            await Fails<AggregateException>(() => Navigation.ShutdownAsync().AsTask());
            Assert.AreEqual(0, Loader.Owned.Count);
        });

        [UnityTest] public IEnumerator AllCleanupFailuresArePreserved() => Run(async () =>
        {
            var p = await Screen();
            p.TestView.FailUnbind = true;
            p.FailDispose = true;
            p.TestState.FailDispose = true;
            var error = await Fails<AggregateException>(() => p.DisposeAsync().AsTask());
            Assert.AreEqual(3, error.Flatten().InnerExceptions.Count);
            await Fails<AggregateException>(() => Navigation.ShutdownAsync().AsTask());
            Assert.AreEqual(0, Loader.Owned.Count);
        });

        [UnityTest] public IEnumerator InitializeAndCleanupFailuresArePreserved() => Run(async () =>
        {
            var error = await Fails<AggregateException>(() => Screen(configure: p =>
            { p.FailInitialize = true; p.FailDispose = true; }).AsTask());
            var messages = string.Join("|", error.Flatten().InnerExceptions);
            StringAssert.Contains("initialize", messages);
            StringAssert.Contains("presenter-dispose", messages);
            Assert.AreEqual(0, Loader.Owned.Count);
            await AssertEmpty();
        });

        [UnityTest] public IEnumerator AwakeRunsBeforeBindForInactiveView() => Run(async () =>
        {
            Loader.Configure = v => v.RequireAwake = true;
            var p = await Screen();
            Assert.AreEqual(1, p.TestView.BindCount);
            await AssertEmpty();
        });
    }
}
