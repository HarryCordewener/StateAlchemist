extern alias generator;
extern alias codefixes;

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TUnit.Core;
using AddPhaseCodeFix = codefixes::StateAlchemist.CodeFixes.AddPhaseCodeFix;
using DeclarationAnalyzer = generator::StateAlchemist.Generators.DeclarationAnalyzer;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;
using PhaseNameCodeFix = codefixes::StateAlchemist.CodeFixes.PhaseNameCodeFix;
using RefToInCodeFix = codefixes::StateAlchemist.CodeFixes.RefToInCodeFix;

namespace StateAlchemist.Generators.Tests.Analysis;

/// <summary>
/// The code fixes spec §8 promises. Each is applied to source that has the diagnostic, and the result is compiled
/// again: a fix that writes code the compiler rejects would be worse than no fix.
/// </summary>
public class CodeFixTests
{
    private const string States = """
        using System;
        using System.Threading.Tasks;
        using StateAlchemist;
        namespace Library;
        public struct Root : IRootState { }
        [Initial] public struct Leaf : IState<Root> { }
        public struct Other : IState<Root> { }
        """;

    [Test]
    public async Task RefOnAnExitingStateBecomesIn()
    {
        var fixedSource = await Fix(
            new RefToInCodeFix(),
            $$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf), To = typeof(Other)), On(1)]
                public static void Move(ref Leaf from, ref Other to) { }
            }
            [Machine(Root = typeof(Root), Value = typeof(byte))]
            [Include(typeof(Module))]
            public sealed partial class Machine { }
            """,
            "SALCH0201");
        await Assert.That(fixedSource).Contains("public static void Move(in Leaf from, ref Other to)");
    }

    [Test]
    public async Task APhaseWithTheWrongSuffixIsRenamed()
    {
        var fixedSource = await Fix(
            new PhaseNameCodeFix(),
            $$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Stay
                {
                    public static void Transform(ref Leaf self) { }
                    public static void CompletedAsync() { }
                }
            }
            """,
            "SALCH0207");
        await Assert.That(fixedSource).Contains("public static void Completed()");
    }

    [Test]
    public async Task AMethodThatIsNotAPhaseIsRenamedToOne()
    {
        var fixedSource = await Fix(
            new PhaseNameCodeFix(),
            $$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Stay
                {
                    public static void Transform(ref Leaf self) { }
                    public static void Complet() { }
                }
            }
            """,
            "SALCH0206");
        await Assert.That(fixedSource).Contains("public static void Complete()");
    }

    [Test]
    public async Task AStayGetsATransformWithTheRightSignature()
    {
        var fixedSource = await Fix(
            new AddPhaseCodeFix(),
            $$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Stay
                {
                }
            }
            """,
            "SALCH0902",
            "AddPhaseCodeFixTransform");
        await Assert.That(fixedSource).Contains("public static void Transform(ref global::Library.Leaf self)");
    }

    [Test]
    public async Task AMoveGetsATransformThatTakesBothStates()
    {
        var fixedSource = await Fix(
            new AddPhaseCodeFix(),
            $$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf), To = typeof(Other)), On(1)]
                public static class Move
                {
                }
            }
            """,
            "SALCH0902",
            "AddPhaseCodeFixTransform");
        await Assert.That(fixedSource).Contains("public static void Transform(in global::Library.Leaf from, ref global::Library.Other to)");
    }

    [Test]
    public async Task AGuardIsAddedReturningTrue()
    {
        var fixedSource = await Fix(
            new AddPhaseCodeFix(),
            $$"""
            {{States}}
            [Module] public static class Module
            {
                [Transition(From = typeof(Leaf)), On(1)]
                public static class Stay
                {
                    public static void Transform(ref Leaf self) { }
                }
            }
            """,
            "SALCH0902",
            "AddPhaseCodeFixGuard");
        await Assert.That(fixedSource).Contains("public static bool Guard(in global::Library.Leaf from) => true;");
    }

    /// <summary>
    /// Applies the first fix <paramref name="provider"/> offers for the diagnostic <paramref name="id"/> — or the one
    /// with <paramref name="equivalenceKey"/> — and returns the document's text, having checked it still compiles.
    /// </summary>
    private static async Task<string> Fix(CodeFixProvider provider, string source, string id, string? equivalenceKey = null)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace
            .AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "App", "App", LanguageNames.CSharp))
            .WithMetadataReferences(TestCompilation.References)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));
        var document = project.AddDocument("App.cs", SourceText.From(source));

        var diagnostics = await Diagnostics(document).ConfigureAwait(false);
        var diagnostic = diagnostics.FirstOrDefault(d => d.Id == id);
        await Assert.That(diagnostic).IsNotNull().Because($"{id} must be reported, not {string.Join(", ", diagnostics.Select(d => d.Id).Distinct())}");

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic!, (action, _) => actions.Add(action), CancellationToken.None)).ConfigureAwait(false);
        await Assert.That(actions).IsNotEmpty();

        var chosen = equivalenceKey is null ? actions[0] : actions.Single(a => a.EquivalenceKey == equivalenceKey);
        var operations = await chosen.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        var text = (await changed.GetTextAsync().ConfigureAwait(false)).ToString();

        var recompiled = TestCompilation.Create(text);
        var errors = recompiled.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal)).ToList();
        await Assert.That(errors).IsEmpty().Because("what the fix wrote does not compile:\n" + string.Join("\n", errors));
        return text;
    }

    /// <summary>Everything StateAlchemist reports for a document: the analyzer's diagnostics, and the generator's.</summary>
    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = (CSharpCompilation)(await document.Project.GetCompilationAsync().ConfigureAwait(false))!;
        var analyzed = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DeclarationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var generated);
        return analyzed.AddRange(generated);
    }
}
