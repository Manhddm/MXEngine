using UnityEngine;

namespace MXEngine.Samples
{
    public sealed class NavigationDemoView : MonoBehaviour
    {
        [SerializeField] private GameObject content;

        private void OnEnable()
        {
            if (content != null) content.SetActive(true);
        }
    }
}
