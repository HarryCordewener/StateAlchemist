extern alias generator;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Performance;

/// <summary>
/// The two size targets of spec §9, on a machine as big as TNC's: generating it takes under a second, an unrelated
/// edit regenerates nothing, and constructing the machine it writes costs one allocation and well under 10 µs.
/// </summary>
public class TncSizedMachineTests
{
    private static readonly string Source = TncSizedMachineSource.Generate();


    [Test]
    public async Task GeneratingItTakesLessThanASecond()
    {
        // Warm up the generator on a small machine first: the target is generation, not the JIT.
        Run(TestCompilation.Create("[global::StateAlchemist.Machine(Root = typeof(global::StateAlchemist.Samples.Performance.Root), Value = typeof(byte))] public sealed partial class Small;"), out _);

        var compilation = TestCompilation.Create(Source);
        var watch = Stopwatch.StartNew();
        var driver = Run(compilation, out var generated);
        watch.Stop();

        await Assert.That(generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();
        // Spec §9 asks for under a second, which is what a developer machine does (0.4 s when this was written).
        // The budget here is five, because CI runs three frameworks at once on shared hardware: what a test on that
        // hardware can honestly catch is a regression of an order of magnitude, not a factor of two.
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5)).Because($"a TNC-sized machine generates quickly, not in {watch.ElapsedMilliseconds} ms");

        // An edit somewhere else in the program: the transform's result is unchanged, so nothing is written again.
        var edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Elsewhere { public sealed class Unrelated { public int Value; } }", (CSharpParseOptions)compilation.SyntaxTrees.First().Options));
        var incremental = Stopwatch.StartNew();
        var results = driver.RunGenerators(edited).GetRunResult().Results.Single();
        incremental.Stop();

        var outputs = results.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(step => step.Outputs).ToList();
        await Assert.That(outputs).IsNotEmpty();
        await Assert.That(outputs.All(output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue()
            .Because("an unrelated edit must not regenerate the machine");
        await Assert.That(incremental.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ConstructingItCostsOneObjectAndLittleTime()
    {
        var assembly = Compile();
        var type = assembly.GetType($"{TncSizedMachineSource.Namespace}.TncSizedMachine")!;
        var definition = (global::StateAlchemist.MachineDefinition)type.GetProperty("Definition", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        await Assert.That(definition.States).Count().IsEqualTo(TncSizedMachineSource.States);
        await Assert.That(definition.Transitions).Count().IsEqualTo(TncSizedMachineSource.Transitions);
        var log = Activator.CreateInstance(assembly.GetType($"{TncSizedMachineSource.Namespace}.Log")!)!;
        var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.CreateDelegate<Func<object, object>>();

        for (var i = 0; i < 100; i++)
        {
            _ = create(log);
            _ = RuntimeHelpers.GetUninitializedObject(type);
        }

        var empty = Allocated(() => RuntimeHelpers.GetUninitializedObject(type));
        var constructed = Allocated(() => create(log));
        await Assert.That(constructed).IsEqualTo(empty).Because("construction allocates the machine and nothing else");

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            _ = create(log);
        }

        watch.Stop();
        var each = watch.Elapsed.TotalMicroseconds / 1000;
        await Assert.That(each).IsLessThan(10).Because($"spec §9: construction takes under 10 µs, not {each:F3} µs");
    }

    private static long Allocated(Func<object> action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var kept = action();
        var after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(kept);
        return after - before;
    }

    private static GeneratorDriver Run(CSharpCompilation compilation, out Compilation generated) =>
        CSharpGeneratorDriver.Create(
                [new MachineGenerator().AsSourceGenerator()],
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                optionsProvider: null,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true))
            .RunGeneratorsAndUpdateCompilation(compilation, out generated, out _);

    private static Assembly Compile()
    {
        var compilation = TestCompilation.Create(LanguageVersion.Latest, TestCompilation.Net8Symbols, Source);
        Run(compilation, out var generated);
        using var image = new MemoryStream();
        var emitted = generated.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("The generated code does not compile:\n" + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        image.Position = 0;
        return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(image);
    }
}
