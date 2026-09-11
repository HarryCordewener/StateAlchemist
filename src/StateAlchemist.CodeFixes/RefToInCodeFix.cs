using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StateAlchemist.Model;

namespace StateAlchemist.CodeFixes;

/// <summary>
/// <c>SALCH0201</c>: a transition takes a state it exits by <c>ref</c>. Writing to it would be writing to data that
/// is about to be cleared, so the fix is the only one that can be right — take it as <c>in</c>, and read it.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RefToInCodeFix)), Shared]
public sealed class RefToInCodeFix : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticCatalog.RefOnExitingState.Id);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ParameterSyntax>() is not { } parameter)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Take it as 'in'",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(parameter, AsIn(parameter)))),
                    equivalenceKey: nameof(RefToInCodeFix)),
                diagnostic);
        }
    }

    private static ParameterSyntax AsIn(ParameterSyntax parameter)
    {
        var modifiers = parameter.Modifiers.Where(m => !m.IsKind(SyntaxKind.RefKeyword) && !m.IsKind(SyntaxKind.OutKeyword)).ToList();
        modifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.InKeyword).WithTrailingTrivia(SyntaxFactory.Space));
        return parameter.WithModifiers(SyntaxFactory.TokenList(modifiers));
    }
}
