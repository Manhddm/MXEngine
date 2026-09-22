using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace MXEngine.MVP
{
    public class AddressableViewLoader : IViewLoader
    {
        public async UniTask<T> LoadAsync<T>(AssetReferenceGameObject reference, Transform parent) where T : Component
        {
            var handle = reference.InstantiateAsync(parent);
            var gObject = await handle.Task;
            if (!gObject.TryGetComponent<T>(out var view))
            {
                Addressables.ReleaseInstance(gObject);
                throw new System.InvalidOperationException($"Failed to load view from {reference.AssetGUID}");
            }
            return view;
        }

        public UniTask ReleaseAsync(Component view)
        {
            if (view != null)
                Addressables.ReleaseInstance(view.gameObject);

            return UniTask.CompletedTask;
        }
    }
}