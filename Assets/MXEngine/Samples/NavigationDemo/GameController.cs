using MXEngine.MVP;
using UnityEngine;

namespace MXEngine.Samples
{
    public class GameController : MonoBehaviour
    {
        public static GameController Instance { get; private set; }
        [SerializeField] private Canvas canvas;
        [SerializeField] private UIRoot uiRoot;
        private NavigationService _navigation;
        private Canvas Canvas => canvas;
        public NavigationService Navigation => _navigation;
        private void Awake()
        {
            if (Instance == null) Instance = this;
            DontDestroyOnLoad(gameObject);
            _navigation = new NavigationService(new AddressableViewLoader(), uiRoot);
        }
    }
}
