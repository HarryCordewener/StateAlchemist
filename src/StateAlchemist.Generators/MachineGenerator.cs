using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

/// <summary>
/// Writes each <c>[Machine]</c> class. Whole-program: the modules it includes may come from any referenced assembly,
/// and the generated code calls their methods directly. Every problem the analysis finds is a compiler diagnostic;
/// a machine with errors gets no code, so there is nothing to half-work.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MachineGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var machines = context.SyntaxProvider.ForAttributeWithMetadataName(
            "StateAlchemist.MachineAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributed, cancellation) => Generate((INamedTypeSymbol)attributed.TargetSymbol, attributed.SemanticModel.Compilation, cancellation));

        context.RegisterSourceOutput(machines, static (output, machine) =>
        {
            foreach (var diagnostic in machine.Diagnostics)
            {
                output.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (machine.Source is not null)
            {
                output.AddSource(machine.HintName, machine.Source);
            }
        });
    }

    internal static GeneratedMachine Generate(INamedTypeSymbol machine, Compilation compilation, CancellationToken cancellation)
    {
        var built = SymbolModelBuilder.Build(machine, compilation);
        cancellation.ThrowIfCancellationRequested();
        var diagnostics = ModelValidator.Validate(built.Model);
        var fallback = built.Locations.TryGetValue(built.MachineLocation, out var at) ? at : machine.Locations.FirstOrDefault();
        var infos = diagnostics
            .Select(d => DiagnosticInfo.From(d, built.Locations.TryGetValue(d.Location, out var location) ? location : fallback))
            .ToArray();
        var hint = machine.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty) + ".g.cs";
        var source = diagnostics.Any(d => d.Severity == Severity.Error) ? null : MachineEmitter.Emit(built);
        return new GeneratedMachine(hint, source, new EquatableArray<DiagnosticInfo>(infos));
    }
}
