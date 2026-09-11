using System;
using System.Reflection;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Tests.Api;

public class DeclarationAttributeTests
{
    [Test]
    [Arguments(typeof(MachineAttribute), AttributeTargets.Class, false)]
    [Arguments(typeof(IncludeAttribute), AttributeTargets.Class, true)]
    [Arguments(typeof(ModuleAttribute), AttributeTargets.Class, false)]
    public async Task AttributesApplyWhereTheSpecSays(Type attribute, AttributeTargets targets, bool allowMultiple)
    {
        var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>()!;
        await Assert.That(usage.ValidOn).IsEqualTo(targets);
        await Assert.That(usage.AllowMultiple).IsEqualTo(allowMultiple);
        await Assert.That(usage.Inherited).IsFalse();
    }

    [Test]
    public async Task MachineDefaultsAreCheckedPermissiveAndIgnore()
    {
        var machine = new MachineAttribute();
        await Assert.That(machine.Concurrency).IsEqualTo(Concurrency.Checked);
        await Assert.That(machine.Purity).IsEqualTo(Purity.Permissive);
        await Assert.That(machine.Unhandled).IsEqualTo(Unhandled.Ignore);
        await Assert.That(machine.InboxCapacity).IsEqualTo(0);
    }
}
