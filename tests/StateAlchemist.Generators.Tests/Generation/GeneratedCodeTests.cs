extern alias generator;

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Contracts.Machines.Runs;
using StateAlchemist.Reference.Tests.FrontEnd;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Generation;

/// <summary>What the generator writes: code that compiles as C# 7.3, without a single warning.</summary>
public class GeneratedCodeTests
{
    private static GeneratorDriver Driver(LanguageVersion language = LanguageVersion.Latest, bool track = false) => CSharpGeneratorDriver.Create(
        [new MachineGenerator().AsSourceGenerator()],
        parseOptions: new CSharpParseOptions(language),
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, track));

    private static string Problems(Compilation compilation) => string.Join("\n", compilation.GetDiagnostics()
        .Where(d => d.Severity >= DiagnosticSeverity.Warning && !d.Id.StartsWith("SALCH", StringComparison.Ordinal))
        .Select(d => d.ToString()));

    [Test]
    [Arguments("Telnet")]
    [Arguments("Recorder")]
    [Arguments("Guards")]
    [Arguments("FailuresWithHooks")]
    [Arguments("Deciding")]
    [Arguments("DecidingSerialized")]
    [Arguments("RecorderSerialized")]
    [Arguments("Runs")]
    public async Task TheGeneratedCodeIsCSharp73AndCompilesWithoutAWarning(string machine)
    {
        var source = machine switch
        {
            "Telnet" => TestCompilation.Machine("M", typeof(Connected), typeof(byte), typeof(TelnetContext), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule)], body: " { }"),
            "Recorder" => TestCompilation.Machine("M", typeof(Root), typeof(byte), typeof(RecordingContext), [typeof(RecorderModule), typeof(RecorderExtras)], body: " { }"),
            "Guards" => TestCompilation.Machine("M", typeof(GuardRoot), typeof(byte), typeof(RecordingContext), [typeof(GuardModule)], body: " { }"),
            "Deciding" => TestCompilation.Machine("M", typeof(DecideRoot), typeof(byte), typeof(RecordingContext), [typeof(DecidingModule)], body: " { }"),
            "DecidingSerialized" => TestCompilation.Machine("M", typeof(DecideRoot), typeof(byte), typeof(RecordingContext), [typeof(DecidingModule)],
                ", Concurrency = global::StateAlchemist.Concurrency.Serialized", " { }"),
            "RecorderSerialized" => TestCompilation.Machine("M", typeof(Root), typeof(byte), typeof(RecordingContext), [typeof(RecorderModule), typeof(RecorderExtras)],
                ", Concurrency = global::StateAlchemist.Concurrency.Serialized", " { }"),
            "Runs" => TestCompilation.Machine("M", typeof(RunRoot), typeof(byte), typeof(RecordingContext), [typeof(RunModule)], body: " { }"),
            _ => TestCompilation.Machine("M", typeof(FailRoot), typeof(byte), typeof(RecordingContext), [typeof(FailureModule)], body: """
                 {
                     partial void OnGuardException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnTransformException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnExitedException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnEnteredException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                     partial void OnCompletedException(global::System.Exception e, in global::StateAlchemist.TransitionInfo<byte> t, ref global::StateAlchemist.ExceptionResolution r) { }
                 }
                 """),
        };
        var compilation = TestCompilation.Create(LanguageVersion.CSharp7_3, source);

        Driver(LanguageVersion.CSharp7_3).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        await Assert.That(output.SyntaxTrees.Count()).IsEqualTo(2);
        await Assert.That(Problems(output)).IsEqualTo("");
    }
}
