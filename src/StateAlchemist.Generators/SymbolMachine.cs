using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

/// <summary>A machine's model, and the symbols the emitter needs to write code against it.</summary>
/// <param name="Machine">The <c>[Machine]</c> class.</param>
/// <param name="Model">The model.</param>
/// <param name="StateTypes">The state structs, by state index.</param>
/// <param name="Methods">The symbol behind each method of the model.</param>
/// <param name="Events">Every event type the machine names, by metadata name.</param>
/// <param name="Value">The value type, if declared.</param>
/// <param name="Context">The context type, if declared.</param>
/// <param name="Config">The configuration type, if declared.</param>
/// <param name="Locations">The source location behind each span the model records.</param>
/// <param name="MachineLocation">Where the <c>[Machine]</c> attribute is: diagnostics with no location of their own go here.</param>
/// <param name="Modules">The included modules' metadata names, in include order: actions from different modules with equal <c>Order</c> run in this order.</param>
internal sealed record SymbolMachine(
    INamedTypeSymbol Machine,
    MachineModel Model,
    IReadOnlyList<INamedTypeSymbol> StateTypes,
    IReadOnlyDictionary<MethodModel, IMethodSymbol> Methods,
    IReadOnlyDictionary<string, INamedTypeSymbol> Events,
    ITypeSymbol? Value,
    ITypeSymbol? Context,
    ITypeSymbol? Config,
    IReadOnlyDictionary<SourceSpan, Location> Locations,
    SourceSpan MachineLocation,
    IReadOnlyList<string> Modules);
