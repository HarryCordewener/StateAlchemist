using System;
using System.Reflection;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Tests.Api;

public class TransitionAttributeTests
{
    [Test]
    [Arguments(typeof(TransitionAttribute), AttributeTargets.Method | AttributeTargets.Class, false)]
    [Arguments(typeof(DecisionAttribute), AttributeTargets.Class, false)]
    [Arguments(typeof(OnAttribute), AttributeTargets.Method | AttributeTargets.Class, true)]
    [Arguments(typeof(OnRangeAttribute), AttributeTargets.Method | AttributeTargets.Class, true)]
    [Arguments(typeof(OnAnyAttribute), AttributeTargets.Method | AttributeTargets.Class, false)]
    [Arguments(typeof(OnEventAttribute), AttributeTargets.Method | AttributeTargets.Class, false)]
    [Arguments(typeof(RunAttribute), AttributeTargets.Method | AttributeTargets.Class, false)]
    [Arguments(typeof(ToAttribute), AttributeTargets.Method, false)]
    [Arguments(typeof(ExitedAttribute), AttributeTargets.Method, true)]
    [Arguments(typeof(EnteredAttribute), AttributeTargets.Method, true)]
    public async Task AttributesApplyWhereTheSpecSays(Type attribute, AttributeTargets targets, bool allowMultiple)
    {
        var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>()!;
        await Assert.That(usage.ValidOn).IsEqualTo(targets);
        await Assert.That(usage.AllowMultiple).IsEqualTo(allowMultiple);
        await Assert.That(usage.Inherited).IsFalse();
    }

    [Test]
    public async Task PhaseNamesListImperativeBeforePastTense()
    {
        await Assert.That(string.Join(",", PhaseNames.All))
            .IsEqualTo("Guard,Transform,Decide,DecideAsync,Complete,Completed,CompletedAsync");
    }

    [Test]
    public async Task ADecisionHandlesNoEventsByDefault()
    {
        await Assert.That(new DecisionAttribute().Handle).IsEmpty();
    }

    [Test]
    public async Task TriggerAttributesKeepTheirValues()
    {
        await Assert.That(new OnAttribute((byte)31).Value).IsEqualTo((object)(byte)31);
        var range = new OnRangeAttribute(0x20, 0x7E);
        await Assert.That(range.From).IsEqualTo((object)0x20);
        await Assert.That(range.To).IsEqualTo((object)0x7E);
    }
}
