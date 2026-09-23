using UnityEngine.AddressableAssets;

namespace MXEngine.MVP
{
    [System.Serializable]
    public class ViewEntry
    {
        public int Id;
        public AssetReferenceGameObject Reference;
        public ViewLayer Layer;
    }

    public enum ViewLayer
    {
        Screen,
        Modal,
        Overlay,
    }
}
