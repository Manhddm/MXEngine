using System;
using System.Collections;
using System.Threading.Tasks;
using MXEngine.MVP;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;

namespace MXEngine.Samples.Tests
{
    public sealed class AddressablesIntegrationTests
    {
        // SettingsModal.prefab is registered as an Addressable by NavigationDemoBuilder.
        private const string SettingsGuid = "69fb8a5adaa34094ab7ebe04630a2b22";

        [UnityTest]
        public IEnumerator SameAddressableCanHaveThreeLiveInstances()
        {
            var root = new GameObject("Addressables integration root", typeof(RectTransform));
            var loader = new AddressableViewLoader();
            var reference = new AssetReferenceGameObject(SettingsGuid);
            var pending = VerifyAsync(loader, reference, root.transform);
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
            AssetReferenceGameObject reference, Transform parent)
        {
            NavigationDemoView first = null, second = null, third = null;
            try
            {
                first = await loader.LoadAsync<NavigationDemoView>(reference, parent);
                second = await loader.LoadAsync<NavigationDemoView>(reference, parent);
                third = await loader.LoadAsync<NavigationDemoView>(reference, parent);
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
