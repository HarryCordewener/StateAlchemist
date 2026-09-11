using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using StateAlchemist.Model;

namespace StateAlchemist.CodeFixes;

/// <summary>
/// D24: "add a phase" on a class-form transition. <c>SALCH0901</c> says a transition declares nothing yet;
/// <c>SALCH0902</c> is hidden and shows nothing — it exists so the fixes are offered on any transition that could
/// declare more. Each fix writes the phase with the signature the transition's own states give it, so the author
/// never has to remember which parameter is <c>ref</c> and which is <c>in</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddPhaseCodeFix)), Shared]
public sealed class AddPhaseCodeFix : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticCatalog.NoPhases.Id, DiagnosticCatalog.PhaseCanBeAdded.Id);

    /// <inheritdoc/>
    public override FixAllProvider? GetFixAllProvider() => null;

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
            if (!diagnostic.Properties.TryGetValue("Phases", out var phases) || string.IsNullOrEmpty(phases))
            {
                continue; // SALCH0901 without the hidden diagnostic's data: the fixes come from SALCH0902
            }

            if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ClassDeclarationSyntax>() is not { } declaration)
            {
                continue;
            }

            diagnostic.Properties.TryGetValue("From", out var from);
            diagnostic.Properties.TryGetValue("To", out var to);
            foreach (var phase in phases!.Split(','))
            {
                var name = phase;
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Add '{name}'",
                        _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(declaration, With(declaration, name, from, to)))),
                        equivalenceKey: nameof(AddPhaseCodeFix) + name),
                    diagnostic);
            }
        }
    }

    private static ClassDeclarationSyntax With(ClassDeclarationSyntax declaration, string phase, string? from, string? to)
    {
        var source = string.IsNullOrEmpty(from) ? "global::System.Object" : from!;
        var target = string.IsNullOrEmpty(to) ? null : to;
        var method = phase switch
        {
            "Guard" => $"public static bool Guard(in {source} from) => true;",
            "Transform" when target is null => $"public static void Transform(ref {source} self)\n{{\n}}",
            "Transform" => $"public static void Transform(in {source} from, ref {target} to)\n{{\n}}",
            "CompletedAsync" => "public static global::System.Threading.Tasks.ValueTask CompletedAsync() => default;",
            _ => $"public static void {phase}()\n{{\n}}",
        };

        var added = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(method)!;
        return declaration.AddMembers(added.WithAdditionalAnnotations(Formatter.Annotation));
    }
}
