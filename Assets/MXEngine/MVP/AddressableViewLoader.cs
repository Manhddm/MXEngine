using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MXEngine.MVP
{
    public class AddressableViewLoader : IViewLoader
    {
        public async UniTask<T> LoadAsync<T>(AssetReferenceGameObject reference, Transform parent) where T : Component
        {
            var handle = reference.InstantiateAsync(parent);
            try
            {
                var gObject = await handle.Task;
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new System.InvalidOperationException($"Failed to load view from {reference.AssetGUID}");

                if (gObject == null || !gObject.TryGetComponent<T>(out var view))
                    throw new System.InvalidOperationException($"View {reference.AssetGUID} is missing {typeof(T).Name}.");

                return view;
            }
            catch
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                throw;
            }
        }

        public UniTask ReleaseAsync(Component view)
        {
            if (view != null)
                Addressables.ReleaseInstance(view.gameObject);

            return UniTask.CompletedTask;
        }
    }
}
