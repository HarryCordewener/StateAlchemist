extern alias generator;

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Core;
using DeclarationAnalyzer = generator::StateAlchemist.Generators.DeclarationAnalyzer;

namespace StateAlchemist.Generators.Tests.Analysis;

/// <summary>
/// The analyzer reports a declaring library's own problems where the module is written — with no machine anywhere in
/// the compilation, which is the situation the generator cannot help with (spec §8).
/// </summary>
public class DeclarationAnalyzerTests
{
    private const string States = """
        using System;
        using System.Threading.Tasks;
        using StateAlchemist;
        namespace Library;
        public struct Root : IRootState { }
        [Initial] public struct Leaf : IState<Root> { }
        """;

    [Test]
    public async Task AModuleWithNothingWrongReportsNothing()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class Fine
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static void Stay(ref Leaf self) { }

                [Transition(From = typeof(Leaf)), On(2)]
                public static class Class
                {
                    public static bool Guard(in Leaf from) => true;
                    public static void Transform(ref Leaf self) { }
                    public static ValueTask CompletedAsync() => default;
                }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    public async Task AStateThatIsNotAPublicStructIsSALCH0001()
    {
        var reported = await Analyze("""
            using StateAlchemist;
            namespace Library;
            public struct Root : IRootState { }
            public class NotAStruct : IState<Root> { }
            [Module] public static class Module
            {
                [Transition(From = typeof(NotAStruct)), On(1)]
                public static void Stay(NotAStruct self) { }
            }
            """);
        await Assert.That(reported).Contains("SALCH0001");
    }

    [Test]
    public async Task AMemberThatIsNotPublicStaticIsSALCH0002()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                internal static void Stay(ref Leaf self) { }
            }
            """);
        await Assert.That(reported).Contains("SALCH0002");
    }

    [Test]
    public async Task ATransitionWithNoTriggerIsSALCH0106()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf))]
                public static void Stay(ref Leaf self) { }
            }
            """);
        await Assert.That(reported).Contains("SALCH0106");
    }

    [Test]
    public async Task AMethodThatIsNotAPhaseIsSALCH0206()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Class
                {
                    public static void Transform(ref Leaf self) { }
                    public static void Finish() { }
                }
            }
            """);
        await Assert.That(reported).Contains("SALCH0206");
    }

    [Test]
    public async Task APhaseWhoseSuffixDisagreesWithItsReturnTypeIsSALCH0207()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Class
                {
                    public static void Transform(ref Leaf self) { }
                    public static void CompletedAsync() { }
                }
            }
            """);
        await Assert.That(reported).Contains("SALCH0207");
    }

    [Test]
    public async Task ADecisionWithBothDecideMethodsIsSALCH0208()
    {
        var reported = await Analyze($$"""
            {{States}}
            public readonly record struct Yes;
            [Module] public static class Module
            {
                [Decision(From = typeof(Leaf)), On(1)]
                public static class Ask
                {
                    public static Yes Decide() => default;
                    public static ValueTask<Yes> DecideAsync() => default;
                    public static void Complete(Yes outcome) { }
                }
            }
            """);
        await Assert.That(reported).Contains("SALCH0208");
    }

    /// <summary>D24: a class-form transition that does nothing yet says so, and offers the phases it could declare.</summary>
    [Test]
    public async Task AClassFormTransitionWithNoPhasesIsSALCH0901And0902()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Class
                {
                }
            }
            """);
        await Assert.That(reported).IsEquivalentTo(new[] { "SALCH0901", "SALCH0902" });
    }

    [Test]
    public async Task AClassFormTransitionMissingSomePhasesIsSALCH0902Alone()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Class
                {
                    public static void Transform(ref Leaf self) { }
                }
            }
            """);
        await Assert.That(reported).IsEquivalentTo(new[] { "SALCH0902" });
    }

    /// <summary>
    /// What the analyzer must not do: report what only a machine can know. A leaf with unhandled values
    /// (`SALCH0501`), a conflict, an unreachable state — all of those depend on which modules an application
    /// includes, and are the generator's to report.
    /// </summary>
    [Test]
    public async Task ProblemsThatNeedAMachineAreNotReportedHere()
    {
        var reported = await Analyze($$"""
            {{States}}
            [Module] public static class First
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static void Stay(ref Leaf self) { }
            }
            [Module] public static class Second
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static void Also(ref Leaf self) { }
            }
            """);
        await Assert.That(reported).IsEmpty();
    }

    /// <summary>The ids reported for <paramref name="source"/>, in order.</summary>
    private static async Task<ImmutableArray<string>> Analyze(string source)
    {
        var compilation = TestCompilation.Create(source);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS")).ToList();
        await Assert.That(errors).IsEmpty().Because("the source itself must compile:\n" + string.Join("\n", errors));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DeclarationAnalyzer());
        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return diagnostics.Select(d => d.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToImmutableArray();
    }
}
