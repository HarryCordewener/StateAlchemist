extern alias generator;

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Reference.Tests.FrontEnd;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Generation;

/// <summary>What the generator reports, where, and when it does not run again.</summary>
public class DiagnosticTests
{
    private static GeneratorDriver Driver(LanguageVersion language = LanguageVersion.Latest, bool track = false) => CSharpGeneratorDriver.Create(
        [new MachineGenerator().AsSourceGenerator()],
        parseOptions: new CSharpParseOptions(language),
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, track));

    [Test]
    public async Task AMachineWithErrorsIsReportedAndGetsNoCode()
    {
        var app = TestCompilation.Machine("Bad", typeof(Connected), typeof(byte), typeof(TelnetContext),
            [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule), typeof(RivalGmcpModule)]);
        var compilation = TestCompilation.Create(app, BadDeclarationsSource());

        var result = Driver().RunGenerators(compilation).GetRunResult();

        await Assert.That(result.GeneratedTrees.Length).IsEqualTo(0);
        await Assert.That(string.Join(",", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id))).IsEqualTo("SALCH0101");
    }

    [Test]
    public async Task AProblemInTheApplicationsOwnSourceIsReportedWhereItIsWritten()
    {
        var app = TestCompilation.Machine("Bad", typeof(Connected), typeof(byte), typeof(TelnetContext),
            [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule), typeof(ExitingWriteModule)]);
        var compilation = TestCompilation.Create(app, BadDeclarationsSource());

        var diagnostic = Driver().RunGenerators(compilation).GetRunResult().Diagnostics.Single(d => d.Id == "SALCH0201");
        var at = diagnostic.Location.GetLineSpan();

        await Assert.That(at.Path).IsEqualTo("App1.cs");
        await Assert.That(compilation.SyntaxTrees.ElementAt(1).GetText().Lines[at.StartLinePosition.Line].ToString().Trim())
            .IsEqualTo("public static void Leave(ref SubNegotiation parent)");
        await Assert.That(diagnostic.Descriptor.HelpLinkUri).EndsWith("diagnostics.md#salch0201");
    }

    [Test]
    public async Task AProblemInAReferencedModuleIsReportedOnTheMachine()
    {
        var app = TestCompilation.Machine("Bad", typeof(Root), typeof(byte), typeof(RecordingContext), [typeof(RecorderModule), typeof(RecorderExtras)]);
        var compilation = TestCompilation.Create(app);

        var diagnostic = Driver().RunGenerators(compilation).GetRunResult().Diagnostics.First(d => d.Id == "SALCH0301");

        var at = diagnostic.Location.GetLineSpan();

        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(compilation.SyntaxTrees.Single(t => t.FilePath == at.Path).GetText().Lines[at.StartLinePosition.Line].ToString())
            .StartsWith("[global::StateAlchemist.Machine(");
    }

    [Test]
    public async Task AnUnrelatedEditDoesNotRegenerate()
    {
        var app = TestCompilation.Machine("M", typeof(GuardRoot), typeof(byte), typeof(RecordingContext), [typeof(GuardModule)]);
        var compilation = TestCompilation.Create(app);
        var driver = Driver(track: true).RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Unrelated { }", new CSharpParseOptions(LanguageVersion.Latest), path: "Other.cs"));
        var result = driver.RunGenerators(edited).GetRunResult().Results.Single();

        var reasons = result.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(run => run.Outputs).Select(o => o.Reason).Distinct().ToList();
        await Assert.That(reasons).DoesNotContain(IncrementalStepRunReason.Modified);
        await Assert.That(reasons).DoesNotContain(IncrementalStepRunReason.New);
    }

    /// <summary>
    /// An attribute written with a property it does not have arrives with no constructor arguments at all. The
    /// compiler reports that typo itself; the generator's part is to keep writing the machine, because a generator
    /// that throws produces nothing and buries the one real error under a CS0246 for every state.
    /// </summary>
    [Test]
    public async Task AnAttributeWrittenWithTheWrongArgumentDoesNotStopTheGenerator()
    {
        var source = """
            namespace Typo
            {
                public struct R : global::StateAlchemist.IRootState { }

                [global::StateAlchemist.Initial]
                public struct A : global::StateAlchemist.IState<R> { }

                [global::StateAlchemist.Module]
                public static class M
                {
                    [global::StateAlchemist.Transition(From = typeof(R)), global::StateAlchemist.OnAny]
                    public static void Ignore() { }

                    [global::StateAlchemist.Exited(Of = typeof(A))]
                    public static void Left() { }
                }

                [global::StateAlchemist.Machine(Root = typeof(R), Value = typeof(byte))]
                [global::StateAlchemist.Include(typeof(M))]
                public sealed partial class Machine { }
            }
            """;

        var result = Driver().RunGenerators(TestCompilation.Create(source)).GetRunResult();

        await Assert.That(result.Diagnostics.Where(d => d.Id == "CS8785").Select(d => d.GetMessage())).IsEmpty();
        await Assert.That(result.GeneratedTrees.Length).IsEqualTo(1);
    }

    private static string BadDeclarationsSource([CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "StateAlchemist.Reference.Tests", "FrontEnd", "BadDeclarations.cs"));
}
