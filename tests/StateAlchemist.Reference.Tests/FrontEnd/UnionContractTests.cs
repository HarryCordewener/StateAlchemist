using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using StateAlchemist.Samples.Door;
using TUnit.Core;

namespace StateAlchemist.Reference.Tests.FrontEnd;

/// <summary>
/// Below net11.0 the samples declare <c>UnionAttribute</c> and <c>IUnion</c> themselves; on net11.0 the compiler binds the
/// runtime's. The same assertions run on every target framework, so they hold only while the two shapes agree.
/// </summary>
public class UnionContractTests
{
    private static Type UnionAttribute =>
        typeof(Answer).GetCustomAttributes(inherit: false).Select(a => a.GetType()).Single(t => t.FullName == "System.Runtime.CompilerServices.UnionAttribute");

    private static Type IUnion => typeof(Answer).GetInterface("System.Runtime.CompilerServices.IUnion")!;

    [Test]
    public async Task TheUnionTypesComeFromTheRuntimeOnNet11AndFromThePolyfillBelow()
    {
#if NET11_0_OR_GREATER
        var expected = typeof(object).Assembly;
#else
        var expected = typeof(Answer).Assembly;
#endif
        await Assert.That(UnionAttribute.Assembly).IsEqualTo(expected);
        await Assert.That(IUnion.Assembly).IsEqualTo(expected);
    }

    [Test]
    public async Task UnionAttributeIsASealedAttributeOnClassesAndStructsOnly()
    {
        var usage = UnionAttribute.GetCustomAttribute<AttributeUsageAttribute>()!;

        await Assert.That(UnionAttribute.IsSealed).IsTrue();
        await Assert.That(UnionAttribute.BaseType).IsEqualTo(typeof(Attribute));
        await Assert.That(usage.ValidOn).IsEqualTo(AttributeTargets.Class | AttributeTargets.Struct);
        await Assert.That(usage.AllowMultiple).IsFalse();
        await Assert.That(usage.Inherited).IsFalse();
    }

    [Test]
    public async Task IUnionHasOnlyAValueProperty()
    {
        var members = IUnion.GetMembers().Where(m => m.MemberType != MemberTypes.Method).Select(m => m.ToString());
        var methods = IUnion.GetMethods().Select(m => m.ToString());

        await Assert.That(string.Join(", ", members)).IsEqualTo("System.Object Value");
        await Assert.That(string.Join(", ", methods)).IsEqualTo("System.Object get_Value()");
    }
}
