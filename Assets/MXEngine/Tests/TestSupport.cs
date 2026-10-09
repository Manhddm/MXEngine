using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace MXEngine.Tests
{
    public sealed class TestUi : MonoBehaviour
    {
        public int Enables;
        public int Disables;
        private void OnEnable() { Enables++; }
        private void OnDisable() { Disables++; }
    }

    internal sealed class FakeViewLoader : IViewLoader
    {
        internal readonly List<Component> Created = new List<Component>();
        internal readonly HashSet<Component> Owned = new HashSet<Component>();
        internal bool FailLoad;
        internal bool FailRelease;
        internal int ReleaseCalls;
        internal Func<CancellationToken, UniTask> LoadHook;
        public async UniTask<T> LoadAsync<T>(string key, Transform parent,
            CancellationToken cancellationToken = default) where T : Component
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailLoad) throw new InvalidOperationException("load");
            if (LoadHook != null) await LoadHook(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var objectInstance = new GameObject(key, typeof(RectTransform));
            objectInstance.SetActive(false);
            objectInstance.transform.SetParent(parent, false);
            var result = objectInstance.AddComponent<T>();
            Created.Add(result);
            Owned.Add(result);
            return result;
        }

        public UniTask ReleaseAsync(Component view)
        {
            ReleaseCalls++;
            if (FailRelease) throw new InvalidOperationException("release");
            Assert.IsTrue(Owned.Remove(view));
            if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
            return UniTask.CompletedTask;
        }
    }

    public abstract class NavigationTestFixture
    {
        protected const string A = "A", B = "B", C = "C", Modal = "Modal", Overlay = "Overlay";
        protected FakeViewLoader Loader;
        protected NavigationService Navigation;
        private GameObject _root;
        protected readonly List<Exception> Diagnostics = new List<Exception>();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("UI Root");
            var uiRoot = _root.AddComponent<UIRoot>();
            foreach (string name in new[] { "ScreenRoot", "ModalRoot", "OverlayRoot" })
            {
                var child = new GameObject(name).transform;
                child.SetParent(_root.transform);
                typeof(UIRoot).GetField("<" + name + ">k__BackingField",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(uiRoot, child);
            }
            Loader = new FakeViewLoader();
            Navigation = new NavigationService(Loader, uiRoot);
            Navigation.CleanupFailed += Diagnostics.Add;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        protected UniTask<TestUi> Screen(string key = A, bool stack = true,
            CancellationToken token = default) =>
            Navigation.ShowScreenAsync<TestUi>(key, stack, token);

        protected UniTask<TestUi> OpenModal(CancellationToken token = default) =>
            Navigation.ShowModalAsync<TestUi>(Modal, token);

        protected UniTask<TestUi> OpenOverlay(CancellationToken token = default) =>
            Navigation.ShowOverlayAsync<TestUi>(Overlay, token);

        protected async UniTask AssertEmpty()
        {
            await Navigation.ShutdownAsync();
            Assert.AreEqual(0, Navigation.ScreenCount);
            Assert.AreEqual(0, Navigation.ModalCount);
            Assert.AreEqual(0, Navigation.OverlayCount);
            Assert.AreEqual(0, Navigation.PendingReleaseCount);
            Assert.AreEqual(0, Loader.Owned.Count);
        }

        protected static async UniTask<T> Fails<T>(Func<UniTask> operation) where T : Exception
        {
            try { await operation(); }
            catch (T exception) { return exception; }
            Assert.Fail("Expected " + typeof(T).Name);
            return null;
        }

        protected static IEnumerator Run(Func<UniTask> test)
        {
            UniTask pending = test();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!pending.IsCompleted)
            {
                Assert.Less(DateTime.UtcNow, deadline, "Async test timed out.");
                yield return null;
            }
            pending.GetAwaiter().GetResult();
        }
    }
}
