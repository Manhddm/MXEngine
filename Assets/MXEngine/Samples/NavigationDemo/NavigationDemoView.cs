using System.Threading;
using System.Threading.Tasks;
using MXEngine.MVP;

namespace MXEngine.Samples
{
    public sealed class NavigationDemoView : View<NavigationDemoState>
    {
        protected override Task OnBindAsync(NavigationDemoState state, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
