using System;
using System.Collections;
using System.Threading.Tasks;
using MXEngine.MVP;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MXEngine.Samples.Tests
{
    public sealed class AddressablesIntegrationTests
    {
        // SettingsModal.prefab is registered as an Addressable by NavigationDemoBuilder.

        [UnityTest]
        public IEnumerator SameAddressableCanHaveThreeLiveInstances()
        {
            var root = new GameObject("Addressables integration root", typeof(RectTransform));
            var loader = new AddressableViewLoader();
            var pending = VerifyAsync(loader, GameViewKey.Settings, root.transform);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            try
            {
                while (!pending.IsCompleted)
                {
                    Assert.Less(DateTime.UtcNow, deadline, "Addressable load timed out.");
                    yield return null;
                }
                pending.GetAwaiter().GetResult();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static async Task VerifyAsync(AddressableViewLoader loader,
            string key, Transform parent)
        {
            NavigationDemoView first = null, second = null, third = null;
            try
            {
                first = await loader.LoadAsync<NavigationDemoView>(key, parent);
                second = await loader.LoadAsync<NavigationDemoView>(key, parent);
                third = await loader.LoadAsync<NavigationDemoView>(key, parent);
                Assert.AreNotSame(first, second);
                Assert.AreNotSame(second, third);
                Assert.AreEqual(3, loader.OwnedInstanceCount);
            }
            finally
            {
                if (first != null) await loader.ReleaseAsync(first);
                if (second != null) await loader.ReleaseAsync(second);
                if (third != null) await loader.ReleaseAsync(third);
            }
            Assert.AreEqual(0, loader.OwnedInstanceCount);
            Assert.AreEqual(0, loader.PendingCleanupCount);
        }
    }
}
