using System;
using UnityEngine;

namespace MXEngine
{
    internal static class ViewActivation
    {
        internal static void Prepare(GameObject instance, Transform parent)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (parent == null || !parent.gameObject.activeInHierarchy)
                throw new InvalidOperationException("View parent must be active in the hierarchy.");
            instance.SetActive(false);
            instance.transform.SetParent(parent, false);
        }
    }
}
