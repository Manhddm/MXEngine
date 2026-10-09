using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MXEngine
{
    public interface IViewLoader
    {
        UniTask<T> LoadAsync<T>(string key, Transform parent,
            CancellationToken cancellationToken = default) where T : Component;

        UniTask ReleaseAsync(Component view);
    }
}
