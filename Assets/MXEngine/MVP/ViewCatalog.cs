using System.Collections.Generic;
using UnityEngine;

namespace MXEngine.MVP
{
    [CreateAssetMenu(fileName = "ViewCatalog", menuName = "MXEngine/View Catalog")]
    public class ViewCatalog : ScriptableObject
    {
        [SerializeField] private List<ViewEntry> entries;

        public ViewEntry Get(ViewId id)
        {
            return entries.Find(x => x.Id == id);
        }
    }
}