using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using StateAlchemist.Model;
using RoslynDescriptor = Microsoft.CodeAnalysis.DiagnosticDescriptor;

namespace StateAlchemist.Generators;

/// <summary>
/// <c>SALCH0602</c>: firing an <c>[Internal]</c> event from outside the machine. A machine writes no overload for one,
/// so the call that compiles is the generic <c>FireAsync&lt;TEvent&gt;</c> of <c>IMachine</c>, which would throw.
/// Reported at the call, where the mistake is.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InternalEventAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<RoslynDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.For(DiagnosticCatalog.InternalEventFired.Id));

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var known = new KnownTypes(start.Compilation);
            if (known.Internal is not { } internalAttribute || known.Machine is null)
            {
                return;
            }

            start.RegisterOperationAction(operation => Analyze((IInvocationOperation)operation.Operation, operation, internalAttribute), OperationKind.Invocation);
        });
    }

    private static void Analyze(IInvocationOperation call, OperationAnalysisContext context, INamedTypeSymbol internalAttribute)
    {
        var method = call.TargetMethod;
        if (method.Name is not ("FireAsync" or "Fire") || method.TypeArguments.Length != 1 || !IsMachine(method.ContainingType))
        {
            return;
        }

        var fired = method.TypeArguments[0];
        if (fired.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, internalAttribute)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.For(DiagnosticCatalog.InternalEventFired.Id), call.Syntax.GetLocation(), fired.Name));
        }
    }

    /// <summary>A machine's own type, or one of the interfaces it implements.</summary>
    private static bool IsMachine(INamedTypeSymbol? type) =>
        type is not null && (IsMachineInterface(type) || type.AllInterfaces.Any(IsMachineInterface));

    private static bool IsMachineInterface(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == "StateAlchemist" && type.MetadataName == "IMachine`1";
}
