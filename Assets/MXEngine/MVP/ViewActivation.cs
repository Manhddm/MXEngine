using System;
using UnityEngine;

namespace MXEngine.MVP
{
    internal static class ViewActivation
    {
        // Unity does not call Awake under an inactive ancestor. Briefly activate on the
        // active destination, then hide before returning to the async initialization flow.
        // OnEnable can run here before Bind; no frame is yielded while unbound and active.
        internal static void PrepareForBinding(GameObject instance, Transform parent)
        {
            if (parent == null || !parent.gameObject.activeInHierarchy)
                throw new InvalidOperationException("View parent must be active in the hierarchy.");
            using var context = LifecycleContext.Enter();
            instance.SetActive(false);
            instance.transform.SetParent(parent, false);
            instance.SetActive(true);
            instance.SetActive(false);
        }
    }
}
