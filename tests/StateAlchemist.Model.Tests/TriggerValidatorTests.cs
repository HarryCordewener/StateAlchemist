using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class TriggerValidatorTests
{
    [Test]
    public async Task AValueOutsideTheValueTypeIsSalch0104()
    {
        var model = new TestModel();
        var root = model.Root();
        model.Add("TooBig", root, -1, TriggerModel.Value(300));
        model.Add("Straddles", root, -1, TriggerModel.Range(250, 260));
        await Assert.That(TriggerValidator.Validate(model.Build()).Describe()).IsEqualTo(
            "SALCH0104: 'TooBig' fires on 300, which is outside the value type 'System.Byte'\n" +
            "SALCH0104: 'Straddles' fires on 250..260, which is outside the value type 'System.Byte'");
    }

    [Test]
    public async Task AnUnsupportedValueTypeIsSalch0105()
    {
        var model = new MachineModel("M", new MachineOptions("System.Int64", new ValueDomain(long.MinValue, long.MaxValue), ValueTypeSupported: false), [], []);
        await Assert.That(TriggerValidator.Validate(model).Describe())
            .IsEqualTo("SALCH0105: The value type 'System.Int64' is not supported: use an integral type or an enum of 16 bits or fewer");
    }
}
