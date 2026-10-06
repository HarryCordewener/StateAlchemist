using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Reference.Tests.FrontEnd;

/// <summary>Each deliberately bad declaration produces the diagnostic the reference documents, with the documented message.</summary>
public class BadDeclarationTests
{
    private static string Problems(Type module, Type? value = null) =>
        string.Join("\n", ReflectionModelBuilder.Build(new MachineSpec("Bad", typeof(Connected), value ?? typeof(byte), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule), module], typeof(TelnetContext)))
            .Validate()
            .Where(d => d.Severity == StateAlchemist.Model.Severity.Error)
            .Select(d => $"{d.Id}: {d.Message}"));

    [Test]
    public async Task ANonPublicTransitionIsSalch0002()
    {
        await Assert.That(Problems(typeof(HiddenModule))).IsEqualTo("SALCH0002: 'HiddenModule.Hidden' must be public and static");
    }

    [Test]
    public async Task WritingAStateBeingLeftIsSalch0201()
    {
        await Assert.That(Problems(typeof(ExitingWriteModule)))
            .IsEqualTo("SALCH0201: Parameter 'parent' of 'ExitingWriteModule.Leave' takes 'SubNegotiation' by ref, but the transition exits it; take it as in");
    }

    [Test]
    public async Task AMisnamedPhaseAndAWrongSuffixAreReported()
    {
        await Assert.That(Problems(typeof(MisnamedModule))).IsEqualTo(
            "SALCH0207: 'MisnamedModule.Refuse.Completed' returns a task, so it must be named 'CompletedAsync'\n" +
            "SALCH0206: 'MisnamedModule.Refuse.Transfrom' is not a phase; a class-form transition may declare Guard, Transform, Delay, Decide, DecideAsync, Complete, Completed and CompletedAsync");
    }

    [Test]
    public async Task ATransitionWithoutATriggerIsSalch0106()
    {
        await Assert.That(Problems(typeof(NoTriggerModule))).IsEqualTo("SALCH0106: 'NoTriggerModule.Nothing' has no trigger");
    }

    [Test]
    public async Task AJoinWithAGuardIsSalch0106()
    {
        await Assert.That(Problems(typeof(GuardedJoinModule))).IsEqualTo("SALCH0106: 'GuardedJoinModule.Answer' is a join, which cannot have a Guard");
    }

    [Test]
    public async Task AJoinMixedWithAnotherTriggerIsSalch0106()
    {
        await Assert.That(Problems(typeof(MixedJoinModule))).IsEqualTo("SALCH0106: 'MixedJoinModule.Answer' mixes [OnAll] with other triggers");
    }

    [Test]
    public async Task AJoinOfOneEventIsSalch0106()
    {
        await Assert.That(Problems(typeof(LonelyJoinModule))).IsEqualTo("SALCH0106: 'LonelyJoinModule.Answer' has an [OnAll] with fewer than two events: use [OnEvent]");
    }

    [Test]
    public async Task AJoinTakesOnlyItsOwnEvents()
    {
        await Assert.That(Problems(typeof(UnlistedJoinEventModule))).IsEqualTo(
            "SALCH0204: Parameter 'error' of 'UnlistedJoinEventModule.Answer' cannot be bound: this join waits for Knock, Ring, not 'StateAlchemist.Samples.Telnet.Error'");
    }

    [Test]
    public async Task TwoModulesClaimingTheSameOptionConflict()
    {
        await Assert.That(Problems(typeof(RivalGmcpModule)))
            .IsEqualTo("SALCH0101: 'GmcpModule.Accept' and 'RivalGmcpModule.AcceptToo' both handle 201 in state 'Willing' without a guard");
    }

    [Test]
    public async Task IncludingSomethingThatIsNotAModuleIsSalch0107()
    {
        await Assert.That(Problems(typeof(NotAModule))).IsEqualTo("SALCH0107: Machine 'Bad' includes 'NotAModule', which is not a [Module]");
    }

    [Test]
    public async Task AWideValueTypeIsSalch0105()
    {
        await Assert.That(Problems(typeof(TelnetCore), typeof(int)))
            .IsEqualTo("SALCH0105: The value type 'System.Int32' is not supported: use an integral type or an enum of 16 bits or fewer");
    }

    [Test]
    [Arguments(typeof(TimerBothUnitsModule), "SALCH0106: 'TimerBothUnitsModule.Both' sets both Milliseconds and Seconds on [After]; set one")]
    [Arguments(typeof(TimerNoDelayModule), "SALCH0106: 'TimerNoDelayModule.Never' has an [After] with no delay: set Milliseconds or Seconds, or declare a Delay method")]
    [Arguments(typeof(TimerTwoDelaysModule), "SALCH0106: 'TimerTwoDelaysModule.Twice' sets an [After] delay and declares a Delay method; use one")]
    [Arguments(typeof(TimerMixedModule), "SALCH0106: 'TimerMixedModule.Mixed' mixes [After] with another trigger")]
    [Arguments(typeof(TimerDecisionModule), "SALCH0106: 'TimerDecisionModule.Wait' is a decision, which cannot fire on [After]")]
    [Arguments(typeof(StrayDelayModule), "SALCH0106: 'StrayDelayModule.Stray' declares a Delay method but does not fire on [After]")]
    [Arguments(typeof(TimerNegativeModule), "SALCH0106: 'TimerNegativeModule.Back' has a negative [After] delay")]
    [Arguments(typeof(TimerTooLongModule), "SALCH0106: 'TimerTooLongModule.Forever' has an [After] delay longer than 4294967294 milliseconds")]
    [Arguments(typeof(DelayReadsLeftStateModule), "SALCH0202: Parameter 'other' of 'DelayReadsLeftStateModule.Late.Delay' names 'Naws', which is not active when 'Idle' is entered\n" +
        "SALCH0204: Parameter 'self' of 'DelayReadsLeftStateModule.Late.Delay' cannot be bound: a Delay reads state: take it as in")]
    public async Task ABadTimerIsReported(Type module, string expected)
    {
        await Assert.That(Problems(module)).IsEqualTo(expected);
    }
}
