using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace MXEngine.MVP
{
    public interface IViewLoader
    {
        UniTask<T> LoadAsync<T>(
            string key,
            Transform parent, CancellationToken cancellationToken = default)
            where T : Component;

        UniTask ReleaseAsync(Component view);
    }
}
