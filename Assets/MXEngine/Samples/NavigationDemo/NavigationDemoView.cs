using Cysharp.Threading.Tasks;
using MXEngine.MVP;

namespace MXEngine.Samples
{
    public sealed class NavigationDemoView : View<NavigationDemoState>
    {
        protected override UniTask OnBindAsync(NavigationDemoState state)
        {
            return UniTask.CompletedTask;
        }
    }
}
