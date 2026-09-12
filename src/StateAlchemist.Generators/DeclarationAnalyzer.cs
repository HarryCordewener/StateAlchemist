using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using StateAlchemist.Model;
using RoslynDescriptor = Microsoft.CodeAnalysis.DiagnosticDescriptor;

namespace StateAlchemist.Generators;

/// <summary>
/// Reports a declaring library's own problems where its modules are written (spec §8). Roslyn cannot see a
/// referenced assembly's non-public members, so a module compiled into a library has to be checked while that
/// library compiles; the generator, running in the application, would never see them.
/// <para>
/// Only what a module's own declarations can be wrong about is reported here: a state that is not a public struct,
/// a member that is not public static, a phase whose name or signature is wrong. Anything that depends on how
/// declarations fit together needs a machine, and stays the generator's.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeclarationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostics reported in the declaring library, plus D24's code-fix carriers.</summary>
    private static readonly ImmutableArray<RoslynDescriptor> Reported = DiagnosticCatalog.All
        .Where(d => d.ReportedIn == "declaring library")
        .Select(d => Descriptors.For(d.Id))
        .ToImmutableArray();

    /// <inheritdoc/>
    public override ImmutableArray<RoslynDescriptor> SupportedDiagnostics => Reported;

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilation =>
        {
            var known = new KnownTypes(compilation.Compilation);
            if (known.Module is null)
            {
                return; // the assembly does not reference StateAlchemist: nothing here is a module
            }

            compilation.RegisterSymbolAction(symbol => Analyze(symbol, known), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, KnownTypes known)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.Module)))
        {
            return;
        }

        Phases(context, type, known);

        var built = SymbolModelBuilder.BuildModule(type, context.Compilation);
        var scoped = DiagnosticCatalog.All.Where(d => d.ReportedIn == "declaring library").Select(d => d.Id).ToImmutableHashSet();
        var found = built.Model.FrontEndDiagnostics.Concat(ShapeValidator.Validate(built.Model)).Where(d => scoped.Contains(d.Id));
        foreach (var diagnostic in found)
        {
            var location = built.Locations.TryGetValue(diagnostic.Location, out var at) ? at : type.Locations.FirstOrDefault();
            context.ReportDiagnostic(DiagnosticInfo.From(diagnostic, location).ToDiagnostic());
        }
    }

    /// <summary>
    /// D24: what each transition in <paramref name="module"/> could still declare. A class-form transition with no
    /// phase at all is <c>SALCH0901</c>, an information message — it does nothing yet. Every transition carries
    /// <c>SALCH0902</c>, hidden, which shows nothing and exists to put the "add a phase" fixes on the declaration.
    /// </summary>
    private static void Phases(SymbolAnalysisContext context, INamedTypeSymbol module, KnownTypes known)
    {
        foreach (var nested in module.GetTypeMembers())
        {
            var isDecision = nested.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.Decision));
            if (!isDecision && !nested.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.Transition)))
            {
                continue;
            }

            var declared = nested.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).Select(m => m.Name).ToImmutableHashSet();
            var possible = isDecision ? DecisionPhases : TransitionPhases;
            var missing = possible.Where(phase => !declared.Contains(phase) && !declared.Contains(Other(phase))).ToList();
            var location = nested.Locations.FirstOrDefault() ?? Location.None;
            if (declared.IsEmpty)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.For(DiagnosticCatalog.NoPhases.Id), location, nested.Name));
            }

            if (missing.Count == 0)
            {
                continue;
            }

            // What the code fix needs to write a phase's signature: which phases are missing, and the states the
            // transition names. It cannot read the model — it runs in the IDE, over one document.
            var attribute = nested.GetAttributes().First(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, isDecision ? known.Decision : known.Transition));
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add("Phases", string.Join(",", missing))
                .Add("From", TypeName(attribute, "From"))
                .Add("To", TypeName(attribute, "To"));
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.For(DiagnosticCatalog.PhaseCanBeAdded.Id), location, properties, nested.Name, string.Join(", ", missing)));
        }
    }

    /// <summary>The named type argument of an attribute, fully qualified, or the empty string.</summary>
    private static string TypeName(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is INamedTypeSymbol type
            ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : string.Empty;

    /// <summary>A phase's other spelling: declaring one means the other is not missing.</summary>
    private static string Other(string phase) => phase switch
    {
        "Completed" => "CompletedAsync",
        "Decide" => "DecideAsync",
        _ => phase,
    };

    private static readonly string[] TransitionPhases = { "Guard", "Transform", "Completed" };

    // A decision's Decide and Complete are not offered: their outcome type is the author's choice, and a fix
    // cannot invent a union. SALCH0901 still says a decision declares nothing, which is what needs saying.
    private static readonly string[] DecisionPhases = { "Guard", "Completed" };
}
