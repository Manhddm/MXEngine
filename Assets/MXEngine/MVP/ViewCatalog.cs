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
            if (entries == null)
                return null;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.Id == id)
                    return entry;
            }

            return null;
        }
    }
}
