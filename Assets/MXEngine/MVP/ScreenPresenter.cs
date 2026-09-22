namespace MXEngine.MVP
{
    public abstract class ScreenPresenter<TView, TState> : Presentr<TView, TState>
        where TView : View<TState>
        where TState : ViewState, new()
    {
        protected ScreenPresenter(TView view) : base(view)
        {
        }
    }
}