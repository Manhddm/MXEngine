using System;
using System.Collections.Generic;
using System.Reflection;
using MXEngine.MVP;
using NUnit.Framework;
using UnityEngine;

namespace MXEngine.Tests
{
    public sealed class CoreContractTests
    {
        [Test]
        public void ViewStateDisposesOnlyOnceWhenHookThrows()
        {
            var state = new TestState { FailDispose = true };
            Assert.Throws<InvalidOperationException>(() => state.Dispose());
            Assert.DoesNotThrow(() => state.Dispose());
        }

        [Test]
        public void DuplicateCatalogIdIsRejected()
        {
            var catalog = ScriptableObject.CreateInstance<ViewCatalog>();
            try
            {
                typeof(ViewCatalog).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(catalog, new List<ViewEntry>
                    {
                        new ViewEntry { Id = 7, Layer = ViewLayer.Screen },
                        new ViewEntry { Id = 7, Layer = ViewLayer.Modal }
                    });
                Assert.Throws<InvalidOperationException>(() => catalog.Get(7));
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }
    }
}
