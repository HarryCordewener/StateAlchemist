using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class HierarchyValidatorTests
{
    [Test]
    public async Task AValidTreeHasNoProblems()
    {
        var model = new TestModel();
        var root = model.Root();
        model.State("A", root, initial: true);
        model.State("B", root);
        await Assert.That(HierarchyValidator.Validate(model.Build().States).Ids()).IsEqualTo("");
    }

    [Test]
    public async Task ASecondRootIsReported()
    {
        var model = new TestModel();
        model.Root("One");
        model.Root("Two");
        await Assert.That(HierarchyValidator.Validate(model.Build().States).Describe())
            .IsEqualTo("SALCH0003: State 'Two' is a second root; 'One' is already the root");
    }

    [Test]
    public async Task AParentChainThatLoopsIsReported()
    {
        var model = new TestModel();
        var root = model.Root();
        var a = model.State("A", root, initial: true);
        model.Replace(a, new StateModel(a, "T.A", 2, true, false, false, true, 1, SourceSpan.None));
        model.State("B", a);
        await Assert.That(HierarchyValidator.Validate(model.Build().States).Describe())
            .IsEqualTo("SALCH0003: State 'A' has a parent chain that loops\nSALCH0003: State 'B' has a parent chain that loops");
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task AParentNeedsExactlyOneInitialChild(int initialChildren)
    {
        var model = new TestModel();
        var root = model.Root();
        model.State("A", root, initial: initialChildren >= 1);
        model.State("B", root, initial: initialChildren >= 2);
        await Assert.That(HierarchyValidator.Validate(model.Build().States).Describe())
            .IsEqualTo($"SALCH0004: State 'Root' has {initialChildren} [Initial] children; it needs exactly one");
    }
}
