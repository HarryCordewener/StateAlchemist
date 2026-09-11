extern alias generator;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StateAlchemist.Reference;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Agreement;

/// <summary>
/// The generated machine and the reference interpreter agree on random machines (spec §10): for any tree and any
/// batch of triggers, the same state, the same data, the same actions in the same order, and the same plans.
/// </summary>
public class RandomMachineTests
{
    private const int Machines = 60;
    private const int Triggers = 60;

    [Test]
    public async Task GeneratedAndInterpretedMachinesAgreeOnRandomTrees()
    {
        var checkedMachines = 0;
        var withRuns = 0;
        var seed = 0;
        while (checkedMachines < Machines)
        {
            seed++;
            var source = RandomMachineSource.Generate(seed);
            if (Compile(source) is not { } assembly)
            {
                continue; // an invalid random machine — a conflict, an unreachable initial child — is skipped, not tested
            }

            checkedMachines++;
            withRuns += source.Contains(", Run]") ? 1 : 0;
            var disagreement = await Compare(assembly, seed);
            await Assert.That(disagreement).IsEqualTo("").Because($"seed {seed}:\n{source}");
        }

        await Assert.That(seed).IsLessThan(Machines * 4).Because("most random machines should be valid");
        await Assert.That(withRuns).IsGreaterThanOrEqualTo(Machines / 6).Because("runs must be among what agrees");
    }

    /// <summary>Compiles <paramref name="source"/> with the generator, or returns <see langword="null"/> if the machine has errors.</summary>
    private static Assembly? Compile(string source)
    {
        var compilation = TestCompilation.Create(source);
        CSharpGeneratorDriver.Create(new MachineGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        using var image = new MemoryStream();
        var emitted = output.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("The generated code does not compile:\n" + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        image.Position = 0;
        return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(image);
    }

    private static async Task<string> Compare(Assembly assembly, int seed)
    {
        var machineType = assembly.GetType($"{RandomMachineSource.Namespace}.RandomMachine")!;
        var logType = assembly.GetType($"{RandomMachineSource.Namespace}.Log")!;
        var stateTypes = assembly.GetTypes().Where(t => t.IsValueType && !t.IsNested && !t.IsEnum && t.Namespace == RandomMachineSource.Namespace).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        var module = assembly.GetType($"{RandomMachineSource.Namespace}.RandomModule")!;

        var generatedLog = Activator.CreateInstance(logType)!;
        var interpretedLog = Activator.CreateInstance(logType)!;
        var generated = (IMachine<byte>)Activator.CreateInstance(machineType, generatedLog)!;
        var reflected = ReflectionModelBuilder.Build(new MachineSpec("RandomMachine", assembly.GetType($"{RandomMachineSource.Namespace}.S0"), typeof(byte), [module], logType));
        var interpreted = ReferenceMachine<byte>.Create(reflected, interpretedLog);

        await generated.StartAsync();
        await interpreted.StartAsync();
        var random = new Random(seed * 7919);
        for (var step = 0; step <= Triggers; step++)
        {
            if (Describe(generated, generatedLog, stateTypes) is var g && Describe(interpreted, interpretedLog, stateTypes) is var i && g != i)
            {
                return $"after {step} triggers\n generated:   {g}\n interpreted: {i}";
            }

            // A batch of one to four values, so runs form; the plan is compared for its first value.
            var batch = Enumerable.Range(0, random.Next(1, 5)).Select(_ => (byte)random.Next(8)).ToArray();
            if (generated.Plan(batch[0]).Transition != interpreted.Plan(batch[0]).Transition)
            {
                return $"step {step}: the plans for {batch[0]} differ: {generated.Plan(batch[0]).Transition} and {interpreted.Plan(batch[0]).Transition}";
            }

            await generated.FireAsync(batch);
            await interpreted.FireAsync(batch);
        }

        return "";
    }

    /// <summary>The active leaf, every active state's value, and the log — everything the two machines must agree on.</summary>
    private static string Describe(IMachine<byte> machine, object log, List<Type> stateTypes)
    {
        var values = stateTypes.Select(t =>
        {
            var arguments = new object?[] { null };
            var active = (bool)typeof(IMachine<byte>).GetMethod(nameof(IMachine<byte>.TryGetState))!.MakeGenericMethod(t).Invoke(machine, arguments)!;
            return active ? $"{t.Name}={t.GetField("Value")!.GetValue(arguments[0])}" : null;
        }).OfType<string>();
        var entries = (List<string>)log.GetType().GetField("Entries")!.GetValue(log)!;
        return $"{machine.StateType.Name} [{string.Join(" ", values)}] log: {string.Join(" | ", entries)}";
    }
}
