namespace MXEngine.MVP
{
    public abstract class ModalPresenter<TView, TState> : Presenter<TView, TState>
        where TView : View<TState>
        where TState : ViewState, new()
    {
        protected ModalPresenter(TView view) : base(view)
        {
        }
    }
}