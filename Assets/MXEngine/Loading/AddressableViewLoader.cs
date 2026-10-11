using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MXEngine
{
    /// <summary>Loads and releases UI prefab instances through Addressables.</summary>
    public sealed class AddressableViewLoader : IViewLoader
    {
        private readonly Dictionary<Component, AsyncOperationHandle<GameObject>> _instances =
            new Dictionary<Component, AsyncOperationHandle<GameObject>>(new ComponentReferenceComparer());
        private readonly List<AbandonedLoad> _abandoned = new List<AbandonedLoad>();

        public int OwnedInstanceCount { get { return _instances.Count; } }
        public int PendingCleanupCount { get { return _abandoned.Count; } }

        public UniTask<T> LoadAsync<T>(string key, Transform parent,
            CancellationToken cancellationToken = default) where T : Component =>
            LoadCoreAsync<T>(key, parent, cancellationToken);

        private async UniTask<T> LoadCoreAsync<T>(string key, Transform parent,
            CancellationToken cancellationToken) where T : Component
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("UI key is required.", nameof(key));
            if (parent == null || !parent.gameObject.activeInHierarchy)
                throw new InvalidOperationException("View parent must be active in the hierarchy.");

            var stage = new GameObject("MXEngine View Stage", typeof(RectTransform));
            stage.SetActive(false);
            stage.transform.SetParent(parent, false);
            AsyncOperationHandle<GameObject> handle = default(AsyncOperationHandle<GameObject>);
            try
            {
                handle = Addressables.InstantiateAsync(key, stage.transform);
                GameObject instance = await AwaitHandleAsync(handle, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new InvalidOperationException("Failed to load " + key + ".");
                T view;
                if (instance == null || !instance.TryGetComponent(out view))
                    throw new InvalidOperationException("UI " + key + " is missing " + typeof(T).Name + ".");
                ViewActivation.Prepare(instance, parent);
                cancellationToken.ThrowIfCancellationRequested();
                _instances.Add(view, handle);
                DestroyStage(stage);
                return view;
            }
            catch
            {
                var abandoned = new AbandonedLoad(handle, stage);
                _abandoned.Add(abandoned);
                if (handle.IsValid() && !handle.IsDone)
                    handle.Completed += _ => CleanupAbandoned(abandoned);
                else
                    CleanupAbandoned(abandoned);
                throw;
            }
        }

        private static async UniTask<GameObject> AwaitHandleAsync(
            AsyncOperationHandle<GameObject> handle, CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: cancellationToken);
            return handle.Result;
        }

        public UniTask ReleaseAsync(Component view)
        {
            if (ReferenceEquals(view, null)) return UniTask.CompletedTask;
            AsyncOperationHandle<GameObject> handle;
            if (!_instances.TryGetValue(view, out handle))
                throw new InvalidOperationException("UI was not created by this loader or was already released.");
            if (!handle.IsValid()) throw new InvalidOperationException("Owned Addressables handle is invalid.");
            using (LifecycleContext.Enter())
                if (!Addressables.ReleaseInstance(handle))
                    throw new InvalidOperationException("Addressables did not release the owned instance.");
            _instances.Remove(view);
            return UniTask.CompletedTask;
        }

        public void RetryPendingCleanup()
        {
            for (var index = _abandoned.Count - 1; index >= 0; index--)
            {
                AbandonedLoad pending = _abandoned[index];
                if (!pending.Handle.IsValid() || pending.Handle.IsDone) CleanupAbandoned(pending);
            }
        }

        private void CleanupAbandoned(AbandonedLoad pending)
        {
            try
            {
                if (pending.Handle.IsValid())
                {
                    if (pending.Handle.Status == AsyncOperationStatus.Succeeded && pending.Handle.Result != null)
                    {
                        using (LifecycleContext.Enter())
                            if (!Addressables.ReleaseInstance(pending.Handle))
                                throw new InvalidOperationException("Addressables did not release an abandoned instance.");
                    }
                    else Addressables.Release(pending.Handle);
                }
                DestroyStage(pending.Stage);
                _abandoned.Remove(pending);
            }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private static void DestroyStage(GameObject stage)
        {
            if (stage == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(stage);
            else UnityEngine.Object.DestroyImmediate(stage);
        }

        private sealed class AbandonedLoad
        {
            internal readonly AsyncOperationHandle<GameObject> Handle;
            internal readonly GameObject Stage;
            internal AbandonedLoad(AsyncOperationHandle<GameObject> handle, GameObject stage)
            {
                Handle = handle; Stage = stage;
            }
        }

        private sealed class ComponentReferenceComparer : IEqualityComparer<Component>
        {
            public bool Equals(Component x, Component y) { return ReferenceEquals(x, y); }
            public int GetHashCode(Component obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
