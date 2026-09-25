using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MXEngine.MVP
{
    public class AddressableViewLoader : IViewLoader
    {
        private readonly Dictionary<Component, AsyncOperationHandle<GameObject>> _instances =
            new(new ComponentReferenceComparer());

        private readonly List<AbandonedLoad> _abandoned = new();
        public int OwnedInstanceCount => _instances.Count;
        public int PendingCleanupCount => _abandoned.Count;

        public UniTask<T> LoadAsync<T>(string key, Transform parent,
            CancellationToken cancellationToken = default) where T : Component =>
            LoadCoreAsync<T>(key, parent, cancellationToken).AsUniTask();

        private async Task<T> LoadCoreAsync<T>(string key, Transform parent,
            CancellationToken cancellationToken) where T : Component
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("View key is required.", nameof(key));
            if (parent == null || !parent.gameObject.activeInHierarchy)
                throw new InvalidOperationException("View parent must be active in the hierarchy.");
            var stage = new GameObject("MXEngine View Stage", typeof(RectTransform));
            stage.SetActive(false);
            stage.transform.SetParent(parent, false);
            AsyncOperationHandle<GameObject> handle = default;
            try
            {
                handle = Addressables.InstantiateAsync(key, stage.transform);
                // Stop awaiting on cancellation without releasing a still-running handle.
                // Task's await captures Unity's synchronization context for cleanup.
                var instance = await AwaitHandleAsync(handle, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ??
                          new InvalidOperationException($"Failed to load {key}.");
                if (instance == null || !instance.TryGetComponent<T>(out var view))
                    throw new InvalidOperationException($"View {key} is missing {typeof(T).Name}.");
                ViewActivation.PrepareForBinding(instance, parent);
                cancellationToken.ThrowIfCancellationRequested();
                _instances.Add(view, handle);
                DestroyStage(stage);
                return view;
            }
            catch
            {
                // A canceled operation still owns its inactive stage and eventual result.
                // Keep ownership even if deferred release throws; RetryPendingCleanup can retry.
                var abandoned = new AbandonedLoad(handle, stage);
                _abandoned.Add(abandoned);
                if (handle.IsValid() && !handle.IsDone)
                    handle.Completed += _ => CleanupAbandoned(abandoned);
                else
                    CleanupAbandoned(abandoned);
                throw;
            }
        }

        private static async Task<GameObject> AwaitHandleAsync(AsyncOperationHandle<GameObject> handle,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return await handle.Task;

            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
            {
                await Task.WhenAny(handle.Task, cancellation.Task);
                cancellationToken.ThrowIfCancellationRequested();
                return await handle.Task;
            }
        }

        public UniTask ReleaseAsync(Component view)
        {
            if (ReferenceEquals(view, null)) return UniTask.CompletedTask;
            if (!_instances.TryGetValue(view, out var handle))
                throw new InvalidOperationException("View was not created by this loader or was already released.");
            if (!handle.IsValid())
                throw new InvalidOperationException("Owned Addressables handle was released outside this loader.");
            if (!Addressables.ReleaseInstance(handle))
                throw new InvalidOperationException("Addressables did not release the owned instance.");
            _instances.Remove(view);
            return UniTask.CompletedTask;
        }

        public void RetryPendingCleanup()
        {
            for (var i = _abandoned.Count - 1; i >= 0; i--)
            {
                var pending = _abandoned[i];
                if (!pending.Handle.IsValid() || pending.Handle.IsDone)
                    CleanupAbandoned(pending);
            }
        }

        private void CleanupAbandoned(AbandonedLoad pending)
        {
            try
            {
                var handle = pending.Handle;
                if (handle.IsValid())
                {
                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                    {
                        if (!Addressables.ReleaseInstance(handle))
                            throw new InvalidOperationException("Addressables did not release an abandoned instance.");
                    }
                    else Addressables.Release(handle);
                }

                DestroyStage(pending.Stage);
                _abandoned.Remove(pending);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
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
                Handle = handle;
                Stage = stage;
            }
        }

        private sealed class ComponentReferenceComparer : IEqualityComparer<Component>
        {
            public bool Equals(Component x, Component y) => ReferenceEquals(x, y);
            public int GetHashCode(Component obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
