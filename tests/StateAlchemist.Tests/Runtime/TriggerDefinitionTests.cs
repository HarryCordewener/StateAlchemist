using System;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Tests.Runtime;

public class TriggerDefinitionTests
{
    private readonly struct Error : IEvent { }

    [Test]
    public async Task EachKindMatchesWhatItSays()
    {
        await Assert.That(TriggerDefinition.ForValue(31).Matches(31)).IsTrue();
        await Assert.That(TriggerDefinition.ForValue(31).Matches(32)).IsFalse();
        await Assert.That(TriggerDefinition.ForRange(0x20, 0x7E).Matches(0x41)).IsTrue();
        await Assert.That(TriggerDefinition.ForRange(0x20, 0x7E).Matches(0x7F)).IsFalse();
        await Assert.That(TriggerDefinition.ForAny().Matches(255)).IsTrue();
        await Assert.That(TriggerDefinition.ForEvent(typeof(Error)).Matches(0)).IsFalse();
    }

    [Test]
    public async Task AnEmptyRangeIsRejected()
    {
        await Assert.That(() => TriggerDefinition.ForRange(5, 4)).Throws<ArgumentException>();
    }

    [Test]
    public async Task EveryKindPrintsReadably()
    {
        await Assert.That(TriggerDefinition.ForValue(31).ToString()).IsEqualTo("31");
        await Assert.That(TriggerDefinition.ForRange(32, 126).ToString()).IsEqualTo("32..126");
        await Assert.That(TriggerDefinition.ForAny().ToString()).IsEqualTo("any");
        await Assert.That(TriggerDefinition.ForEvent(typeof(Error)).ToString()).IsEqualTo("event Error");
    }

    [Test]
    public async Task EqualTriggersAreEqual()
    {
        await Assert.That(TriggerDefinition.ForRange(1, 2)).IsEqualTo(TriggerDefinition.ForRange(1, 2));
        await Assert.That(TriggerDefinition.ForValue(1)).IsNotEqualTo(TriggerDefinition.ForRange(1, 1));
    }
}
