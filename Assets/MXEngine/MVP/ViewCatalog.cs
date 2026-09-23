using System;
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

            ViewEntry found = null;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.Id == id)
                {
                    if (found != null)
                        throw new InvalidOperationException($"View {id} is registered more than once in {name}.");
                    found = entry;
                }
            }

            return found;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (entries == null)
                return;

            var seen = new HashSet<ViewId>();
            foreach (var entry in entries)
            {
                if (entry != null && !seen.Add(entry.Id))
                    Debug.LogError($"View {entry.Id} is registered more than once in {name}.", this);
            }
        }
#endif
    }
}
