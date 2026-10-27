extern alias generator;

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Core;
using InternalEventAnalyzer = generator::StateAlchemist.Generators.InternalEventAnalyzer;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;

namespace StateAlchemist.Generators.Tests.Analysis;

/// <summary><c>SALCH0602</c>: an <c>[Internal]</c> event fired from outside, through the generic overload, the only one that compiles.</summary>
public class InternalEventAnalyzerTests
{
    private const string Machine = """
        using System.Threading.Tasks;
        using StateAlchemist;
        namespace App;
        public struct Root : IRootState { }
        [Initial] public struct Leaf : IState<Root> { }
        [Internal] public struct Tick : IEvent { }
        public struct Tock : IEvent { }
        [Module] public static class TickModule
        {
            [Transition(From = typeof(Leaf)), OnEvent(typeof(Tick))]
            public static void Ticked(ref Leaf self) { }

            [Transition(From = typeof(Leaf)), OnEvent(typeof(Tock))]
            public static void Tocked(ref Leaf self) { }
        }
        [Machine(Root = typeof(Root), Value = typeof(byte))]
        [Include(typeof(TickModule))]
        public sealed partial class Clock { }
        """;

    [Test]
    public async Task FiringAnInternalEventIsSALCH0602()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go(IMachine<byte> machine)
                {
                    await new Clock().FireAsync(new Tick());
                    await machine.FireAsync(new Tick());
                }
            }
            """);
        await Assert.That(reported).IsEquivalentTo(new[] { "SALCH0602", "SALCH0602" });
    }

    [Test]
    public async Task FiringAnOrdinaryEventIsFine()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go()
                {
                    await new Clock().FireAsync(new Tock());
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    public async Task AMachineHasNoOverloadForAnInternalEvent()
    {
        var compilation = TestCompilation.Create(Machine + """

            public static class Use
            {
                public static void Go() => new Clock().Fire(new Tick());
            }
            """);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        var errors = generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToList();
        await Assert.That(errors).Contains("CS1503");
    }

    private static async Task<ImmutableArray<string>> Analyze(string use)
    {
        var compilation = TestCompilation.Create(Machine + "\n" + use);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        var errors = generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS")).ToList();
        await Assert.That(errors).IsEmpty().Because("the source itself must compile:\n" + string.Join("\n", errors));

        var diagnostics = await generated
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new InternalEventAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var crashed = diagnostics.Where(d => d.Id == "AD0001").Select(d => d.GetMessage()).ToList();
        await Assert.That(crashed).IsEmpty().Because("the analyzer must not throw:\n" + string.Join("\n", crashed));
        return diagnostics.Select(d => d.Id).ToImmutableArray();
    }
}
