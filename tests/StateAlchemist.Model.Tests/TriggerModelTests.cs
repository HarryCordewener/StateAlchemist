using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class TriggerModelTests
{
    [Test]
    public async Task OverlapFollowsWhatEachKindMatches()
    {
        await Assert.That(TriggerModel.Value(5).Overlaps(TriggerModel.Value(5))).IsTrue();
        await Assert.That(TriggerModel.Value(5).Overlaps(TriggerModel.Value(6))).IsFalse();
        await Assert.That(TriggerModel.Range(1, 10).Overlaps(TriggerModel.Range(10, 20))).IsTrue();
        await Assert.That(TriggerModel.Range(1, 9).Overlaps(TriggerModel.Range(10, 20))).IsFalse();
        await Assert.That(TriggerModel.Any.Overlaps(TriggerModel.Value(3))).IsTrue();
        await Assert.That(TriggerModel.Any.Overlaps(TriggerModel.Event("T.Error"))).IsFalse();
        await Assert.That(TriggerModel.Event("T.Error").Overlaps(TriggerModel.Event("T.Error"))).IsTrue();
    }

    [Test]
    public async Task ItPrintsLikeTheRuntimeDefinition()
    {
        await Assert.That(TriggerModel.Value(31).ToString()).IsEqualTo("31");
        await Assert.That(TriggerModel.Range(32, 126).ToString()).IsEqualTo("32..126");
        await Assert.That(TriggerModel.Any.ToString()).IsEqualTo("any");
        await Assert.That(TriggerModel.Event("T.Error").ToString()).IsEqualTo("event Error");
    }
}
