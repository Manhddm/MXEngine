using System;
using MXEngine.MVP;
using NUnit.Framework;

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

    }
}
