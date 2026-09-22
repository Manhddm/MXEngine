using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace MXEngine.MVP
{
    public interface IViewLoader
    {
        UniTask<T> LoadAsync<T>(
            AssetReferenceGameObject reference,
            Transform parent)
            where T : Component;

        UniTask ReleaseAsync(Component view);
    }
}