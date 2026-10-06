using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Samples.Radio;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;
using static StateAlchemist.Samples.Telnet.TelnetBytes;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// The code the guides show running a machine, run. Each snippet here is quoted by a page in <c>docs/</c>, so the
/// page cannot show a call that does not compile or a result the machine does not produce.
/// </summary>
public class DocumentationExampleTests
{
    /// <summary>Quoted by the getting-started guide's "Run it".</summary>
    [Test]
    public async Task GettingStartedRunsTheTelnetMachine()
    {
        // begin-snippet: sample-getting-started-run
        var context = new TelnetContext();
        await using var telnet = new SampleTelnet(context);
        await telnet.StartAsync();                       // runs [Entered] actions on the initial path

        await telnet.FireAsync(new byte[] { Iac, Will, GmcpOption });
        // context.Sent now holds IAC DO GMCP, and telnet.TryGetConnected(out var root) shows root.GmcpEnabled == true.
        // end-snippet

        await Assert.That(context.Sent.Select(s => string.Join(",", s))).IsEquivalentTo(new[] { "255,253,201" });
        await Assert.That(telnet.TryGetConnected(out var root) && root.GmcpEnabled).IsTrue();
    }

    /// <summary>Quoted by the lifecycle page.</summary>
    [Test]
    public async Task TheLifecycleRunsEnteredAndExitedActions()
    {
        var context = new TelnetContext();
        var bytes = new byte[] { (byte)'h', (byte)'i' };

        // begin-snippet: sample-lifecycle
        await using var telnet = new SampleTelnet(context);   // not started: nothing has run
        await telnet.StartAsync();                            // [Entered] actions on the initial path, root first
        await telnet.FireAsync(bytes);                        // running
        await telnet.StopAsync();                             // [Exited] actions from the leaf to the root
        // end-snippet

        await Assert.That(telnet.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(context.Log).IsEquivalentTo(new[] { "ready" });
    }

    // begin-snippet: sample-test-transform
    [Test]
    public async Task FinishReadsTheWindowSize()
    {
        var escaping = new NawsEscaping { Captured = new Naws { Bytes = [0, 80, 0, 24], Index = 4 } };
        var root = new Connected();

        NawsModule.Finish.Transform(in escaping, ref root);

        await Assert.That((root.Width, root.Height)).IsEqualTo((80, 24));
    }
    // end-snippet

    /// <summary>Quoted by the testing guide's "The pure layer".</summary>
    [Test]
    public async Task PlanSaysWhatATriggerWouldDo()
    {
        await using var telnet = new SampleTelnet(new TelnetContext());
        await telnet.StartAsync();

        // begin-snippet: sample-test-plan
        var plan = telnet.Plan(TelnetBytes.Iac);
        await Assert.That(plan.Transition).IsEqualTo("TelnetCore.BeginCommand");
        await Assert.That(plan.Target).IsEqualTo(typeof(AwaitingVerb));
        await Assert.That(string.Join(",", plan.Exiting.Select(t => t.Name))).IsEqualTo("Idle");
        // end-snippet
    }

    // begin-snippet: sample-test-machine
    [Test]
    public async Task GmcpIsAcceptedAndEverythingElseRefused()
    {
        var context = new TelnetContext();
        IMachine<byte> telnet = new SampleTelnet(context);
        await telnet.StartAsync();

        await telnet.FireAsync(new byte[] { Iac, Will, GmcpOption, Iac, Will, 42 });

        await Assert.That(string.Join(" | ", context.Sent.Select(s => string.Join(",", s))))
            .IsEqualTo("255,253,201 | 255,254,42");
        await Assert.That(telnet.TryGetState(out Connected root) && root.GmcpEnabled).IsTrue();
    }
    // end-snippet

    /// <summary>Quoted by the states page's "Going back with history".</summary>
    [Test]
    public async Task TheRadioComesBackOnTheBandItWasPlaying()
    {
        // begin-snippet: sample-radio-run
        await using var radio = new CarRadio();
        await radio.StartAsync();

        await radio.FireAsync(Knob.Power);   // on: FM, Playing's [Initial] child
        await radio.FireAsync(Knob.Band);    // AM
        await radio.FireAsync(Knob.Power);   // off
        await radio.FireAsync(Knob.Power);   // on again, by history: AM
        // radio.State == CarRadio.StateId.Am
        // end-snippet

        await Assert.That(radio.State).IsEqualTo(CarRadio.StateId.Am);
        await radio.FireAsync(Knob.Reset);
        await Assert.That(radio.State).IsEqualTo(CarRadio.StateId.Fm);
    }
}
