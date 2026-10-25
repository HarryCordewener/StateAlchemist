using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>Test paths from a machine's definition: every transition and outcome covered, shortest first.</summary>
public class MachinePathTests
{
    // begin-snippet: machine-paths
    private static string Text(MachineDefinition definition) =>
        string.Join("\n---\n", MachinePaths.Covering(definition).Select(p => p.ToString().TrimEnd().ReplaceLineEndings("\n")));
    // end-snippet

    [Test]
    public async Task EveryTransitionOfTheFinalsMachineIsCovered()
    {
        await Assert.That(Text(FinalsMachine.Definition)).IsEqualTo(
            """
            Idle --[7]--> Idle: FinalModule.Ping
            ---
            Idle --[9]--> LoopA: FinalModule.ToLoop
            LoopA --[always]--> LoopB: FinalModule.AToB *
            LoopB --[always]--> LoopA: FinalModule.BToA *
            ---
            Idle --[1]--> Working: FinalModule.Begin
            Working --[2]--> Finished: FinalModule.Finish
            Finished --[done]--> Review: FinalModule.JobDone
            Review --[3]--> Gate: FinalModule.Pass
            Gate --[4]--> Gate: FinalModule.Spin
            ---
            Idle --[1]--> Working: FinalModule.Begin
            Working --[2]--> Finished: FinalModule.Finish
            Finished --[done]--> Review: FinalModule.JobDone
            Review --[3]--> Gate: FinalModule.Pass
            Gate --[always]--> Closed: FinalModule.Through *
            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task EveryTransitionOfThePermitsMachineIsCovered()
    {
        await Assert.That(Text(PermitsMachine.Definition)).IsEqualTo(
            """
            Open --[100]--> Open: PermitModule.Hundred
            ---
            Open --[101]--> Open: PermitModule.HundredAndOne
            ---
            Open --[20]--> Open: PermitModule.Even *
            ---
            Open --[5]--> Open: PermitModule.Ranged *
            ---
            Open --[event Knock]--> Open: PermitModule.Answer *
            ---
            Open --[event Poke]--> Open: PermitModule.Poked *
            ---
            Open --[event Wave]--> Open: PermitModule.Waved
            ---
            Open --[1]--> Latched: PermitModule.Close
            Latched --[2]--> Open: PermitModule.Reopen
            """.ReplaceLineEndings("\n"));
    }
}
