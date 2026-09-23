using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace MXEngine.MVP
{
    public interface IViewLoader
    {
        UniTask<T> LoadAsync<T>(
            AssetReferenceGameObject reference,
            Transform parent, CancellationToken cancellationToken = default)
            where T : Component;

        UniTask ReleaseAsync(Component view);
    }
}
