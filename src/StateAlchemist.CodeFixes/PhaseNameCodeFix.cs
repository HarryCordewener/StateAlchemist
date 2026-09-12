using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using StateAlchemist.Model;

namespace StateAlchemist.CodeFixes;

/// <summary>
/// <c>SALCH0206</c> and <c>SALCH0207</c>: a phase method whose name is not a phase, or whose <c>Async</c> suffix
/// disagrees with what it returns. Both are fixed by renaming, so the fix offers the phases that fit the return
/// type and renames the method through the solution.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PhaseNameCodeFix)), Shared]
public sealed class PhaseNameCodeFix : CodeFixProvider
{
    private static readonly string[] Synchronous = { "Guard", "Transform", "Decide", "Complete", "Completed" };
    private static readonly string[] Asynchronous = { "DecideAsync", "CompletedAsync" };

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticCatalog.UnknownPhase.Id, DiagnosticCatalog.AsyncSuffix.Id);

    /// <inheritdoc/>
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method
                || model.GetDeclaredSymbol(method) is not { } symbol)
            {
                continue;
            }

            var returnsTask = method.ReturnType is GenericNameSyntax { Identifier.ValueText: "ValueTask" or "Task" } or IdentifierNameSyntax { Identifier.ValueText: "ValueTask" or "Task" };
            var candidates = (returnsTask ? Asynchronous : Synchronous)
                .Where(name => !string.Equals(name, symbol.Name, StringComparison.Ordinal))
                .OrderBy(name => Distance(name, symbol.Name))
                .ToList();

            foreach (var name in candidates)
            {
                var chosen = name;
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Rename to '{chosen}'",
                        cancellation => Renamer.RenameSymbolAsync(context.Document.Project.Solution, symbol, new SymbolRenameOptions(), chosen, cancellation),
                        equivalenceKey: nameof(PhaseNameCodeFix) + chosen),
                    diagnostic);
            }
        }
    }

    /// <summary>How far <paramref name="candidate"/> is from what was written, so the likeliest rename comes first.</summary>
    private static int Distance(string candidate, string written)
    {
        var shared = 0;
        while (shared < candidate.Length && shared < written.Length && char.ToLowerInvariant(candidate[shared]) == char.ToLowerInvariant(written[shared]))
        {
            shared++;
        }

        return written.Length + candidate.Length - (2 * shared);
    }
}
