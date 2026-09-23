using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        public async UniTask<T> LoadAsync<T>(AssetReferenceGameObject reference, Transform parent) where T : Component
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));

            // Keep an active prefab outside the visible hierarchy until the Presenter binds it.
            var stage = new GameObject("MXEngine View Stage", typeof(RectTransform));
            stage.SetActive(false);
            stage.transform.SetParent(parent, false);
            AsyncOperationHandle<GameObject> handle = default;
            try
            {
                // handle = reference.InstantiateAsync(stage.transform);
               handle = Addressables.InstantiateAsync(reference, stage.transform); 
                var gObject = await handle.Task;
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new InvalidOperationException($"Failed to load view from {reference.AssetGUID}");

                if (gObject == null || !gObject.TryGetComponent<T>(out var view))
                    throw new InvalidOperationException($"View {reference.AssetGUID} is missing {typeof(T).Name}.");

                gObject.SetActive(false);
                gObject.transform.SetParent(parent, false);
                _instances.Add(view, handle);
                return view;
            }
            catch
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                throw;
            }
            finally
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(stage);
                else
                    UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        public UniTask ReleaseAsync(Component view)
        {
            if (ReferenceEquals(view, null))
                return UniTask.CompletedTask;

            if (!_instances.TryGetValue(view, out var handle))
                throw new InvalidOperationException("View was not created by this loader or was already released.");

            _instances.Remove(view);
            if (handle.IsValid())
                Addressables.ReleaseInstance(handle);

            return UniTask.CompletedTask;
        }

        private sealed class ComponentReferenceComparer : IEqualityComparer<Component>
        {
            public bool Equals(Component x, Component y) => ReferenceEquals(x, y);
            public int GetHashCode(Component obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
