using System;
using System.Reflection;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Tests.Api;

public class StateMarkerTests
{
    private struct Root : IRootState { }

    private struct Child : IState<Root> { }

    [Test]
    public async Task AChildNamesItsParentThroughTheMarker()
    {
        var marker = typeof(Child).GetInterface("IState`1")!;
        await Assert.That(marker.GetGenericArguments()[0]).IsEqualTo(typeof(Root));
    }

    [Test]
    public async Task InitialMarksStructsOnlyOnce()
    {
        var usage = typeof(InitialAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        await Assert.That(usage.ValidOn).IsEqualTo(AttributeTargets.Struct);
        await Assert.That(usage.AllowMultiple).IsFalse();
        await Assert.That(usage.Inherited).IsFalse();
    }
}
