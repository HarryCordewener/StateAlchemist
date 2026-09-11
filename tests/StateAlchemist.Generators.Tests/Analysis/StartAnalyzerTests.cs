extern alias generator;

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;
using StartAnalyzer = generator::StateAlchemist.Generators.StartAnalyzer;

namespace StateAlchemist.Generators.Tests.Analysis;

/// <summary>
/// <c>SALCH0801</c>: a machine fired before it is started, where one method's control flow shows it. What the
/// analyzer must not do is guess: a machine it cannot follow is the runtime check's business.
/// </summary>
public class StartAnalyzerTests
{
    private const string Machine = """
        using System.Threading.Tasks;
        using StateAlchemist;
        namespace App;
        public struct Root : IRootState { }
        [Initial] public struct Leaf : IState<Root> { }
        [Module] public static class Module
        {
            [Transition(From = typeof(Leaf)), OnAny]
            public static void Stay(ref Leaf self) { }
        }
        [Machine(Root = typeof(Root), Value = typeof(byte))]
        [Include(typeof(Module))]
        public sealed partial class Small { }
        """;

    [Test]
    public async Task FiringWithoutStartingIsReported()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go()
                {
                    var machine = new Small();
                    await machine.FireAsync((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEquivalentTo(new[] { "SALCH0801" });
    }

    [Test]
    public async Task StartingFirstIsNotReported()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go()
                {
                    var machine = new Small();
                    await machine.StartAsync();
                    await machine.FireAsync((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    /// <summary>"On every path" means what it says: started in one branch only is still a warning.</summary>
    [Test]
    public async Task StartingOnOnlyOneBranchIsReported()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go(bool yes)
                {
                    var machine = new Small();
                    if (yes)
                    {
                        await machine.StartAsync();
                    }

                    await machine.FireAsync((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEquivalentTo(new[] { "SALCH0801" });
    }

    [Test]
    public async Task StartingOnEveryBranchIsNotReported()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go(bool yes)
                {
                    var machine = new Small();
                    if (yes)
                    {
                        await machine.StartAsync();
                    }
                    else
                    {
                        await machine.StartAsync();
                    }

                    await machine.FireAsync((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    /// <summary>A loop that starts the machine on its first turn still fires it started on every later one.</summary>
    [Test]
    public async Task StartingBeforeALoopThatFiresIsNotReported()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go(byte[] values)
                {
                    var machine = new Small();
                    await machine.StartAsync();
                    foreach (var value in values)
                    {
                        await machine.FireAsync(value);
                    }
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    public async Task FiringInALoopWithoutStartingIsReported()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go(byte[] values)
                {
                    var machine = new Small();
                    foreach (var value in values)
                    {
                        await machine.FireAsync(value);
                    }
                }
            }
            """);
        await Assert.That(reported).IsEquivalentTo(new[] { "SALCH0801" });
    }

    /// <summary>A machine that arrives from elsewhere is not this method's to judge.</summary>
    [Test]
    public async Task AMachineThisMethodDidNotConstructIsNotReported()
    {
        var reported = await Analyze("""
            public static class Use
            {
                public static async Task Go(Small machine)
                {
                    await machine.FireAsync((byte)1);
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
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
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new StartAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var crashed = diagnostics.Where(d => d.Id == "AD0001").Select(d => d.GetMessage()).ToList();
        await Assert.That(crashed).IsEmpty().Because("the analyzer must not throw:\n" + string.Join("\n", crashed));
        return diagnostics.Select(d => d.Id).ToImmutableArray();
    }
}
