using System;
using Cysharp.Threading.Tasks;
using MXEngine.MVP;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        // This legacy demo intentionally reuses NavigationDemoView on several prefabs,
        // so it supplies explicit addresses. New views normally omit address and use
        // the View type name automatically.
        private const string LobbyAddress = "NavigationDemo/Lobby";
        private const string GameplayAddress = "NavigationDemo/Gameplay";
        private const string SettingsAddress = "NavigationDemo/Settings";
        private const string LoadingAddress = "NavigationDemo/Loading";

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
            _navigation = GameController.Instance.Navigation;
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

        private void OnGameplay()
        {
            SceneManager.LoadScene("Gameplay", LoadSceneMode.Additive);
            ExecuteAsync(DemoAction.Gameplay).Forget();
        }

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
            var cancellationToken = this.GetCancellationTokenOnDestroy();
            try
            {
                switch (action)
                {
                    case DemoAction.Lobby:
                        await _navigation.ShowScreenAsync<NavigationDemoScreenPresenter, NavigationDemoView,
                            NavigationDemoState>(
                            view => new NavigationDemoScreenPresenter(view),
                            cancellationToken: cancellationToken,
                            address: LobbyAddress);
                        break;

                    case DemoAction.Gameplay:
                        await _navigation.ShowScreenAsync<NavigationDemoScreenPresenter, NavigationDemoView,
                            NavigationDemoState>(
                            view => new NavigationDemoScreenPresenter(view),
                            cancellationToken: cancellationToken,
                            address: GameplayAddress);
                        break;

                    case DemoAction.Settings:
                        await _navigation.ShowModalAsync<NavigationDemoModalPresenter, NavigationDemoView,
                            NavigationDemoState>(
                            view => new NavigationDemoModalPresenter(view),
                            cancellationToken: cancellationToken,
                            address: SettingsAddress);
                        break;

                    case DemoAction.Back:
                        await _navigation.BackToPreviousScreenAsync(cancellationToken);
                        break;

                    case DemoAction.CloseModal:
                        await _navigation.CloseModalAsync(cancellationToken);
                        break;

                    case DemoAction.CloseAllModals:
                        await _navigation.CloseAllModalsAsync(cancellationToken);
                        break;

                    case DemoAction.Loading:
                        if (_loadingVisible)
                            await _navigation.HideOverlayAsync(LoadingAddress, cancellationToken);
                        else
                            await _navigation.ShowOverlayAsync<RectTransform>(
                                cancellationToken,
                                LoadingAddress);
                        _loadingVisible = !_loadingVisible;
                        break;
                }

                if (statusText != null)
                    statusText.text =
                        $"Screens: {_navigation.ScreenCount}    Modals: {_navigation.ModalCount}    Loading: {_loadingVisible}";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Scene teardown cancels this demo's request. GameController owns NavigationService.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (statusText != null)
                    statusText.text = $"Navigation error: {exception.Message}";
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
