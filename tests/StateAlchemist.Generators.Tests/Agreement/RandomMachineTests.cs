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
        var withHistory = 0;
        var recalls = 0;
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
            withHistory += source.Contains("History = ") ? 1 : 0;
            var (disagreement, recalled) = await Compare(assembly, seed);
            await Assert.That(disagreement).IsEqualTo("").Because($"seed {seed}:\n{source}");
            recalls += recalled;
        }

        await Assert.That(seed).IsLessThan(Machines * 4).Because("most random machines should be valid");
        await Assert.That(withRuns).IsGreaterThanOrEqualTo(Machines / 6).Because("runs must be among what agrees");
        await Assert.That(withHistory).IsGreaterThanOrEqualTo(Machines / 6).Because("moves by history must be among what agrees");
        await Assert.That(recalls).IsGreaterThanOrEqualTo(Machines / 6).Because("moves by history must recall something other than the initial path");
    }

    /// <summary>Compiles <paramref name="source"/> with the generator, or returns <see langword="null"/> if the machine has errors.</summary>
    private static Assembly? Compile(string source)
    {
        var compilation = TestCompilation.Create(LanguageVersion.Latest, TestCompilation.Net8Symbols, source);
        CSharpGeneratorDriver.Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
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

    /// <summary>Where the machines first disagree, or empty; and how many plans a move by history made for a leaf other than its target's initial one.</summary>
    private static async Task<(string Disagreement, int Recalls)> Compare(Assembly assembly, int seed)
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
        var recalls = 0;
        var random = new Random(seed * 7919);
        for (var step = 0; step <= Triggers; step++)
        {
            if (Describe(generated, generatedLog, stateTypes) is var g && Describe(interpreted, interpretedLog, stateTypes) is var i && g != i)
            {
                return ($"after {step} triggers\n generated:   {g}\n interpreted: {i}", recalls);
            }

            // A batch of one to four values, so runs form; the plan is compared for its first value.
            var batch = Enumerable.Range(0, random.Next(1, 5)).Select(_ => (byte)random.Next(8)).ToArray();
            var plan = generated.Plan(batch[0]);
            if (Show(plan) is var gp && Show(interpreted.Plan(batch[0])) is var ip && gp != ip)
            {
                return ($"step {step}: the plans for {batch[0]} differ: {gp} and {ip}", recalls);
            }

            recalls += Recalls(generated.Definition, plan) ? 1 : 0;

            await generated.FireAsync(batch);
            await interpreted.FireAsync(batch);
        }

        return ("", recalls);
    }

    /// <summary>Whether <paramref name="plan"/> is a move by history, from outside its target, that ends somewhere other than the target's initial leaf.</summary>
    private static bool Recalls(MachineDefinition definition, TransitionPlan plan)
    {
        var transition = definition.Transitions.FirstOrDefault(t => t.Name == plan.Transition && t.History != History.None);
        if (transition is null || plan.Target is null)
        {
            return false;
        }

        var initial = transition.Target;
        while (definition.States.FirstOrDefault(s => s.Parent == initial && s.IsInitial) is { } child)
        {
            initial = child.Index;
        }

        var inside = definition.States.First(s => s.Type == plan.Leaf).Index;
        while (inside >= 0 && inside != transition.Target)
        {
            inside = definition.States[inside].Parent;
        }

        return inside < 0 && definition.States[initial].Type != plan.Target;
    }

    private static string Show(TransitionPlan plan) =>
        $"{plan.Transition} to {plan.Target?.Name} entering [{string.Join(",", plan.Entering.Select(t => t.Name))}] refused [{string.Join(",", plan.Refused)}]";

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
