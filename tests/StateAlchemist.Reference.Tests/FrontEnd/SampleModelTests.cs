using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Model;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Reference.Tests.FrontEnd;

/// <summary>The samples are the documentation's machine; the front-end must model them exactly and find nothing wrong.</summary>
public class SampleModelTests
{
    private static readonly ReflectedMachine Telnet = ReflectionModelBuilder.FromMachine(typeof(SampleTelnet));

    private static TransitionModel Transition(string name) => Telnet.Model.Transitions.Single(t => t.Name == name);

    [Test]
    public async Task TheSampleValidatesWithoutAnyDiagnostic()
    {
        await Assert.That(string.Join("\n", Telnet.Validate())).IsEqualTo("");
    }

    [Test]
    public async Task EveryStateIsFoundRootFirst()
    {
        await Assert.That(string.Join(",", Telnet.Model.States.Select(s => s.Name)))
            .IsEqualTo("Connected,AwaitingOption,AwaitingVerb,Command,Idle,Naws,NawsEscaping,SubNegotiation,Willing");
        await Assert.That(Telnet.StateTypes[0]).IsEqualTo(typeof(Connected));
    }

    [Test]
    public async Task ParentsAndInitialChildrenComeFromTheMarkers()
    {
        var naws = Telnet.Model.States.Single(s => s.Name == "Naws");
        await Assert.That(Telnet.Model.States[naws.Parent].Name).IsEqualTo("SubNegotiation");
        await Assert.That(naws.HasReset).IsTrue();
        await Assert.That(Telnet.Model.States.Single(s => s.Name == "Idle").IsInitial).IsTrue();
    }

    [Test]
    public async Task EachFormAndTriggerBecomesATransition()
    {
        await Assert.That(Transition("TelnetCore.Text").Kind).IsEqualTo(MoveKind.Stay);
        await Assert.That(Transition("TelnetCore.Text").Trigger).IsEqualTo(TriggerModel.Any);
        await Assert.That(Transition("GmcpModule.Accept").Trigger).IsEqualTo(TriggerModel.Value(201));
        await Assert.That(Transition("GmcpModule.Accept").Completed.Single().Name).IsEqualTo("CompletedAsync");
        await Assert.That(Transition("NawsModule.Finish").IsGuarded).IsTrue();
        await Assert.That(Transition("TelnetCore.Recover").Trigger).IsEqualTo(TriggerModel.Event(typeof(Error).FullName!));
    }

    [Test]
    public async Task ParametersAreClassifiedAndPassedAsDeclared()
    {
        var begin = Transition("NawsModule.Begin").Transform!;
        await Assert.That(string.Join(", ", begin.Parameters.Select(p => $"{p.Passing} {p.Kind} {p.TypeName.Split('.').Last()}")))
            .IsEqualTo("In State AwaitingOption, Ref State SubNegotiation, Ref State Naws");

        var refuse = Transition("TelnetCore.Refuse").Completed.Single();
        await Assert.That(string.Join(", ", refuse.Parameters.Select(p => p.Kind))).IsEqualTo("Context, Value");
    }

    [Test]
    public async Task StateActionsAreFound()
    {
        var ready = Telnet.Model.StateActions.Single();
        await Assert.That(ready.Phase).IsEqualTo(ActionPhase.Entered);
        await Assert.That(Telnet.Model.States[ready.State].Name).IsEqualTo("Idle");
        await Assert.That(Telnet.MethodOf(ready.Method).Name).IsEqualTo("Ready");
    }
}
