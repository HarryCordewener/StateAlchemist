extern alias generator;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Model;
using StateAlchemist.Reference;
using StateAlchemist.Reference.Tests.FrontEnd;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;
using Generated = generator::StateAlchemist.Model;
using SymbolModelBuilder = generator::StateAlchemist.Generators.SymbolModelBuilder;

namespace StateAlchemist.Generators.Tests.FrontEnd;

/// <summary>
/// The generator and the reference interpreter must see the same machine: for every contract machine and the sample,
/// the model built from Roslyn symbols is the model built by reflection, to the last parameter.
/// </summary>
public class FrontEndAgreementTests
{
    private static readonly Dictionary<string, MachineShape> ShapesByName = new[]
    {
        Shapes.Recorder, Shapes.Guards, Shapes.GuardsThatThrow, Shapes.Failures, Shapes.RecorderSerialized,
        Shapes.Deciding, Shapes.Runs, Shapes.RunsSerialized, Shapes.Telnet,
    }.ToDictionary(s => s.Name);

    /// <summary>The Roslyn front-end's model of <paramref name="machine"/>, as text.</summary>
    private static string Roslyn(string source, string machine)
    {
        var compilation = TestCompilation.Create(source);
        return Generated.ModelText.Of(SymbolModelBuilder.Build(compilation.GetTypeByMetadataName(machine)!, compilation).Model);
    }

    private static string Options(MachineShape shape) =>
        (shape.Concurrency == Concurrency.Checked ? "" : $", Concurrency = global::StateAlchemist.Concurrency.{shape.Concurrency}") +
        (shape.Unhandled == Unhandled.Ignore ? "" : $", Unhandled = global::StateAlchemist.Unhandled.{shape.Unhandled}");

    [Test]
    [Arguments("Recorder")]
    [Arguments("Guards")]
    [Arguments("GuardsThatThrow")]
    [Arguments("Failures")]
    [Arguments("RecorderSerialized")]
    [Arguments("Deciding")]
    [Arguments("Runs")]
    [Arguments("SampleTelnet")]
    public async Task EveryContractMachineIsModelledAlike(string name)
    {
        var shape = ShapesByName[name];
        var roslyn = Roslyn(TestCompilation.Machine(shape.Name, shape.Root, typeof(byte), shape.Context, [.. shape.Modules], Options(shape)), shape.Name);
        var reflected = ReflectionModelBuilder.Build(new MachineSpec(shape.Name, shape.Root, typeof(byte), shape.Modules, shape.Context, null, shape.Concurrency, 0, shape.Purity, shape.Unhandled)).Model;
        await Assert.That(roslyn).IsEqualTo(ModelText.Of(reflected));
    }

    [Test]
    public async Task TheSampleDeclarationIsModelledAlikeFromMetadata()
    {
        var compilation = TestCompilation.Create("class Unused;");
        var roslyn = SymbolModelBuilder.Build(compilation.GetTypeByMetadataName(typeof(SampleTelnet).FullName!)!, compilation).Model;
        await Assert.That(Generated.ModelText.Of(roslyn)).IsEqualTo(ModelText.Of(ReflectionModelBuilder.FromMachine(typeof(SampleTelnet)).Model));
    }

    /// <summary>Bad declarations compiled as the application's own source, so Roslyn sees even their non-public members.</summary>
    [Test]
    [Arguments(typeof(HiddenModule))]
    [Arguments(typeof(ExitingWriteModule))]
    [Arguments(typeof(MisnamedModule))]
    [Arguments(typeof(NoTriggerModule))]
    [Arguments(typeof(RivalGmcpModule))]
    [Arguments(typeof(NotAModule))]
    public async Task EveryBadDeclarationIsDiagnosedAlike(Type module)
    {
        Type[] modules = [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule), module];
        var app = TestCompilation.Machine("Bad", typeof(Connected), typeof(byte), typeof(TelnetContext), modules);
        var compilation = TestCompilation.Create(app, BadDeclarationsSource());
        var roslyn = SymbolModelBuilder.Build(compilation.GetTypeByMetadataName("Bad")!, compilation).Model;
        var reflected = ReflectionModelBuilder.Build(new MachineSpec("Bad", typeof(Connected), typeof(byte), modules, typeof(TelnetContext))).Model;
        var roslynProblems = string.Join("\n", Generated.ModelValidator.Validate(roslyn)
            .Where(d => d.Severity == Generated.Severity.Error).Select(d => $"{d.Id}: {d.Message}"));
        var reflectedProblems = string.Join("\n", ModelValidator.Validate(reflected)
            .Where(d => d.Severity == Severity.Error).Select(d => $"{d.Id}: {d.Message}"));

        await Assert.That(roslynProblems).IsEqualTo(reflectedProblems);
        await Assert.That(roslynProblems).IsNotEqualTo("");
    }

    private static string BadDeclarationsSource([CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "StateAlchemist.Reference.Tests", "FrontEnd", "BadDeclarations.cs"));
}
