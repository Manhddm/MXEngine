namespace MXEngine.MVP
{
    public abstract class ModalPresenter<TView, TState> : Presentr<TView, TState>
        where TView : View<TState>
        where TState : ViewState, new()
    {
        protected ModalPresenter(TView view) : base(view)
        {
        }
    }
}