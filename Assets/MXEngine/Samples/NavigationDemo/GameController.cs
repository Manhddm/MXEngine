using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MXEngine.Samples
{
    public class GameController : MonoBehaviour
    {
        public static GameController Instance { get; private set; }
        [SerializeField] private Canvas canvas;
        [SerializeField] private UIRoot uiRoot;
        private NavigationService _navigation;
        public NavigationService Navigation => _navigation;
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // This sample's canvas and UIRoot belong to the scene too.
            // Persisting only the controller would retain destroyed UI roots.
            _navigation = new NavigationService(new AddressableViewLoader(), uiRoot);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            _navigation?.ShutdownAsync().Forget(Debug.LogException);
        }
    }
}
