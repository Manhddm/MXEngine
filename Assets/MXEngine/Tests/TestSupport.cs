using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MXEngine.MVP;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MXEngine.Tests
{
    public sealed class TestScreen : ScreenPresenter<TestView, TestState>
    {
        public TestView TestView => View;
        public TestState TestState => State;
        public int Disposals;
        public bool FailInitialize;
        public bool FailDispose;
        public Func<CancellationToken, Task> InitializeHook;
        public Action DisposeHook;
        public TestScreen(TestView view) : base(view) { }
        protected override async Task OnInitializeAsync(TestState state, CancellationToken token)
        {
            if (FailInitialize) throw new InvalidOperationException("initialize");
            if (InitializeHook != null) await InitializeHook(token);
        }
        protected override void OnDispose()
        {
            Disposals++;
            DisposeHook?.Invoke();
            if (FailDispose) throw new InvalidOperationException("presenter-dispose");
        }
    }

    public sealed class TestModal : ModalPresenter<TestView, TestState>
    {
        public int Disposals;
        public TestModal(TestView view) : base(view) { }
        protected override void OnDispose() => Disposals++;
    }

    internal sealed class FakeViewLoader : IViewLoader
    {
        internal readonly HashSet<Component> Owned = new();
        internal readonly List<TestView> Created = new();
        internal readonly List<string> Addresses = new();
        internal bool FailLoad;
        internal bool FailRelease;
        internal int ReleaseCalls;
        internal Func<CancellationToken, Task> LoadHook;
        internal Action<TestView> Configure;

        public async UniTask<T> LoadAsync<T>(string address, Transform parent,
            CancellationToken token = default) where T : Component
        {
            token.ThrowIfCancellationRequested();
            if (FailLoad) throw new InvalidOperationException("load");
            if (LoadHook != null) await LoadHook(token);
            token.ThrowIfCancellationRequested();

            Addresses.Add(address);
            var instance = new GameObject("Test View", typeof(RectTransform));
            instance.SetActive(false);
            instance.AddComponent<Button>();
            var view = instance.AddComponent<TestView>();
            ViewActivation.PrepareForBinding(instance, parent);
            Configure?.Invoke(view);
            Created.Add(view);
            var component = instance.GetComponent<T>();
            Owned.Add(component);
            return component;
        }

        public UniTask ReleaseAsync(Component view)
        {
            ReleaseCalls++;
            if (FailRelease) throw new InvalidOperationException("release");
            Assert.IsTrue(Owned.Remove(view), "Loader must retain ownership until successful release.");
            UnityEngine.Object.DestroyImmediate(view.gameObject);
            return UniTask.CompletedTask;
        }
    }

    public abstract class NavigationTestFixture
    {
        protected const int A = 0, B = 1, C = 2;
        internal FakeViewLoader Loader;
        protected NavigationService Navigation;
        private GameObject _root;
        protected readonly List<Exception> Diagnostics = new();

        [SetUp]
        public void SetUp()
        {
            Diagnostics.Clear();
            _root = new GameObject("Test UI Root");
            var ui = _root.AddComponent<UIRoot>();
            foreach (var name in new[] { "ScreenRoot", "ModalRoot", "OverlayRoot" })
            {
                var child = new GameObject(name).transform;
                child.SetParent(_root.transform);
                typeof(UIRoot).GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(ui, child);
            }

            Loader = new FakeViewLoader();
            Navigation = new NavigationService(Loader, ui);
            Navigation.CleanupFailed += Diagnostics.Add;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var view in Loader.Created)
                if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
            UnityEngine.Object.DestroyImmediate(_root);
        }

        protected UniTask<TestScreen> Screen(int id = A, Action<TestScreen> configure = null,
            CancellationToken token = default, bool stack = true) =>
            Navigation.ShowScreenAsync<TestScreen, TestView, TestState>(
                v => new TestScreen(v),
                stack,
                configure,
                token,
                $"Test/Screen/{id}");

        protected UniTask<TestModal> OpenModal(CancellationToken token = default) =>
            Navigation.ShowModalAsync<TestModal, TestView, TestState>(
                v => new TestModal(v),
                cancellationToken: token,
                address: "Test/Modal");

        protected UniTask<TestScreen> OpenOverlay() =>
            Navigation.ShowOverlayAsync<TestScreen, TestView, TestState>(
                v => new TestScreen(v),
                address: "Test/Overlay");

        protected async Task AssertEmpty()
        {
            await Navigation.ShutdownAsync();
            Assert.AreEqual(0, Navigation.ScreenCount);
            Assert.AreEqual(0, Navigation.ModalCount);
            Assert.AreEqual(0, Navigation.OverlayCount);
            Assert.AreEqual(0, Navigation.PendingReleaseCount);
            Assert.AreEqual(0, Loader.Owned.Count);
        }

        protected static async Task<T> Fails<T>(Func<Task> operation) where T : Exception
        {
            try { await operation(); }
            catch (T exception) { return exception; }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

        protected static IEnumerator Run(Func<Task> test)
        {
            var pending = test();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!pending.IsCompleted)
            {
                Assert.Less(DateTime.UtcNow, deadline, "Async test timed out (possible deadlock).");
                yield return null;
            }
            pending.GetAwaiter().GetResult();
        }
    }
}
