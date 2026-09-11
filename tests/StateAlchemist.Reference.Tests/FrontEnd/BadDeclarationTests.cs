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
            "SALCH0206: 'MisnamedModule.Refuse.Transfrom' is not a phase; a class-form transition may declare Guard, Transform, Decide, DecideAsync, Complete, Completed and CompletedAsync");
    }

    [Test]
    public async Task ATransitionWithoutATriggerIsSalch0106()
    {
        await Assert.That(Problems(typeof(NoTriggerModule))).IsEqualTo("SALCH0106: 'NoTriggerModule.Nothing' has no trigger");
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
}
