extern alias generator;

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;
using SyncFireAnalyzer = generator::StateAlchemist.Generators.SyncFireAnalyzer;

namespace StateAlchemist.Generators.Tests.Analysis;

/// <summary>
/// <c>SALCH0601</c>: firing synchronously a machine that can suspend. The machine with only synchronous actions may
/// be fired either way, and that is the point of the diagnostic — it separates the two.
/// </summary>
public class SyncFireAnalyzerTests
{
    private const string Sync = """
        using System.Threading.Tasks;
        using StateAlchemist;
        namespace App;
        public struct Root : IRootState { }
        [Initial] public struct Leaf : IState<Root> { }
        [Module] public static class SyncModule
        {
            [Transition(From = typeof(Leaf)), OnAny]
            public static void Stay(ref Leaf self) { }
        }
        [Machine(Root = typeof(Root), Value = typeof(byte))]
        [Include(typeof(SyncModule))]
        public sealed partial class Synchronous { }
        """;

    private const string Async = """
        using System.Threading.Tasks;
        using StateAlchemist;
        namespace App;
        public struct ARoot : IRootState { }
        [Initial] public struct ALeaf : IState<ARoot> { }
        [Module] public static class AsyncModule
        {
            [Transition(From = typeof(ALeaf)), OnAny]
            public static class Stay
            {
                public static void Transform(ref ALeaf self) { }
                public static ValueTask CompletedAsync() => default;
            }
        }
        [Machine(Root = typeof(ARoot), Value = typeof(byte))]
        [Include(typeof(AsyncModule))]
        public sealed partial class Suspending { }
        """;

    [Test]
    public async Task FiringASynchronousMachineSynchronouslyIsFine()
    {
        var reported = await Analyze(Sync, """
            public static class Use
            {
                public static void Go()
                {
                    var machine = new Synchronous();
                    machine.Fire((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    public async Task FiringAMachineThatCanSuspendSynchronouslyIsSALCH0601()
    {
        var reported = await Analyze(Async, """
            public static class Use
            {
                public static void Go()
                {
                    var machine = new Suspending();
                    machine.Fire((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEquivalentTo(new[] { "SALCH0601" });
    }

    [Test]
    public async Task AwaitingTheSameMachineIsFine()
    {
        var reported = await Analyze(Async, """
            public static class Use
            {
                public static async Task Go()
                {
                    var machine = new Suspending();
                    await machine.FireAsync((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    private static async Task<ImmutableArray<string>> Analyze(string machine, string use)
    {
        var compilation = TestCompilation.Create(machine + "\n" + use);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        var errors = generated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS")).ToList();
        await Assert.That(errors).IsEmpty().Because("the source itself must compile:\n" + string.Join("\n", errors));

        var diagnostics = await generated
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SyncFireAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var crashed = diagnostics.Where(d => d.Id == "AD0001").Select(d => d.GetMessage()).ToList();
        await Assert.That(crashed).IsEmpty().Because("the analyzer must not throw:\n" + string.Join("\n", crashed));
        return diagnostics.Select(d => d.Id).ToImmutableArray();
    }
}
