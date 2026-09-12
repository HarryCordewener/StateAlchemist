using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using StateAlchemist.Model;
using RoslynDescriptor = Microsoft.CodeAnalysis.DiagnosticDescriptor;

namespace StateAlchemist.Generators;

/// <summary>
/// <c>SALCH0601</c>: the synchronous <c>Fire</c> on a machine that can suspend. Every machine has <c>Fire</c>,
/// because the generator cannot know while emitting one machine whether a caller means to use it. On a machine
/// with an async action or decision the call would block the calling thread until the action came back, and
/// deadlock under a synchronization context, so it is reported at the call.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncFireAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<RoslynDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.For(DiagnosticCatalog.SyncFireOnAsyncMachine.Id));

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var known = new KnownTypes(start.Compilation);
            if (known.Machine is null)
            {
                return;
            }

            // One machine is analysed once, however many calls it has: building its model is not free.
            var suspends = new ConcurrentDictionary<string, bool>();
            start.RegisterOperationAction(
                operation => Analyze((IInvocationOperation)operation.Operation, operation, known.Machine, suspends),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(IInvocationOperation call, OperationAnalysisContext context, INamedTypeSymbol machineAttribute, ConcurrentDictionary<string, bool> suspends)
    {
        if (call.TargetMethod.Name != "Fire"
            || call.TargetMethod.ContainingType is not { } type
            || !type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, machineAttribute)))
        {
            return;
        }

        var key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!suspends.TryGetValue(key, out var canSuspend))
        {
            canSuspend = CanSuspend(SymbolModelBuilder.Build(type, context.Compilation).Model);
            suspends[key] = canSuspend;
        }

        if (canSuspend)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.For(DiagnosticCatalog.SyncFireOnAsyncMachine.Id),
                call.Syntax.GetLocation(),
                type.Name));
        }
    }

    /// <summary>Whether anything in the machine can suspend: an async action, or an async decision.</summary>
    private static bool CanSuspend(MachineModel model) =>
        model.AllMethods.Any(method => method.IsAsync) || model.Transitions.Any(t => t.Decision?.DecideAsync is not null);
}
