using System;
using Cysharp.Threading.Tasks;
using MXEngine.MVP;
using UnityEngine;
using UnityEngine.UI;

namespace MXEngine.Samples
{
    public sealed class NavigationDemoState : ViewState
    {
    }

    public sealed class NavigationDemoScreenPresenter : ScreenPresenter<NavigationDemoView, NavigationDemoState>
    {
        public NavigationDemoScreenPresenter(NavigationDemoView view) : base(view)
        {
        }
    }

    public sealed class NavigationDemoModalPresenter : ModalPresenter<NavigationDemoView, NavigationDemoState>
    {
        public NavigationDemoModalPresenter(NavigationDemoView view) : base(view)
        {
        }
    }

    public sealed class NavigationDemo : MonoBehaviour
    {
        [SerializeField] private UIRoot uiRoot;
        [SerializeField] private ViewCatalog catalog;
        [SerializeField] private Button lobbyButton;
        [SerializeField] private Button gameplayButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button closeModalButton;
        [SerializeField] private Button closeAllModalsButton;
        [SerializeField] private Button loadingButton;
        [SerializeField] private Text statusText;

        private NavigationService _navigation;
        private bool _busy;
        private bool _loadingVisible;

        private enum DemoAction
        {
            Lobby,
            Gameplay,
            Settings,
            Back,
            CloseModal,
            CloseAllModals,
            Loading
        }

        private void Awake()
        {
            _navigation = new NavigationService(new AddressableViewLoader(), catalog, uiRoot);
            lobbyButton.onClick.AddListener(OnLobby);
            gameplayButton.onClick.AddListener(OnGameplay);
            settingsButton.onClick.AddListener(OnSettings);
            backButton.onClick.AddListener(OnBack);
            closeModalButton.onClick.AddListener(OnCloseModal);
            closeAllModalsButton.onClick.AddListener(OnCloseAllModals);
            loadingButton.onClick.AddListener(OnLoading);
        }

        private void Start()
        {
            ExecuteAsync(DemoAction.Lobby).Forget();
        }

        private void OnDestroy()
        {
            lobbyButton.onClick.RemoveListener(OnLobby);
            gameplayButton.onClick.RemoveListener(OnGameplay);
            settingsButton.onClick.RemoveListener(OnSettings);
            backButton.onClick.RemoveListener(OnBack);
            closeModalButton.onClick.RemoveListener(OnCloseModal);
            closeAllModalsButton.onClick.RemoveListener(OnCloseAllModals);
            loadingButton.onClick.RemoveListener(OnLoading);
        }

        private void OnLobby() => ExecuteAsync(DemoAction.Lobby).Forget();
        private void OnGameplay() => ExecuteAsync(DemoAction.Gameplay).Forget();
        private void OnSettings() => ExecuteAsync(DemoAction.Settings).Forget();
        private void OnBack() => ExecuteAsync(DemoAction.Back).Forget();
        private void OnCloseModal() => ExecuteAsync(DemoAction.CloseModal).Forget();
        private void OnCloseAllModals() => ExecuteAsync(DemoAction.CloseAllModals).Forget();
        private void OnLoading() => ExecuteAsync(DemoAction.Loading).Forget();

        private async UniTaskVoid ExecuteAsync(DemoAction action)
        {
            if (_busy)
                return;

            _busy = true;
            try
            {
                switch (action)
                {
                    case DemoAction.Lobby:
                        await _navigation.ShowScreenAsync<NavigationDemoScreenPresenter, NavigationDemoView,
                            NavigationDemoState>(ViewId.Lobby, view => new NavigationDemoScreenPresenter(view));
                        break;
                    case DemoAction.Gameplay:
                        await _navigation.ShowScreenAsync<NavigationDemoScreenPresenter, NavigationDemoView,
                            NavigationDemoState>(ViewId.Gameplay, view => new NavigationDemoScreenPresenter(view));
                        break;
                    case DemoAction.Settings:
                        await _navigation.ShowModalAsync<NavigationDemoModalPresenter, NavigationDemoView,
                            NavigationDemoState>(ViewId.Settings, view => new NavigationDemoModalPresenter(view));
                        break;
                    case DemoAction.Back:
                        await _navigation.BackToPreviousScreenAsync();
                        break;
                    case DemoAction.CloseModal:
                        await _navigation.CloseModalAsync();
                        break;
                    case DemoAction.CloseAllModals:
                        await _navigation.CloseAllModalsAsync();
                        break;
                    case DemoAction.Loading:
                        if (_loadingVisible)
                            await _navigation.HideOverlayAsync(ViewId.Loading);
                        else
                            await _navigation.ShowOverlayAsync<RectTransform>(ViewId.Loading);
                        _loadingVisible = !_loadingVisible;
                        break;
                }

                statusText.text = $"Screens: {_navigation.ScreenCount}    Modals: {_navigation.ModalCount}    Loading: {_loadingVisible}";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                statusText.text = $"Navigation error: {exception.Message}";
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
