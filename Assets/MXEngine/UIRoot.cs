using UnityEngine;

namespace MXEngine
{
    public class UIRoot : MonoBehaviour
    {
        [field:SerializeField]
        public Transform SheetRoot { get; private set; }
        [field:SerializeField]
        public Transform ScreenRoot { get; private set; }
        [field:SerializeField]
        public Transform ModalRoot { get; private set; }
        [field:SerializeField]
        public Transform OverlayRoot { get; private set; }
    }
}