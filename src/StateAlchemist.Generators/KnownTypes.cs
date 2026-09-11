using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace StateAlchemist.Generators;

/// <summary>The runtime and BCL types the front-end recognises, resolved once per compilation.</summary>
internal sealed class KnownTypes(Compilation compilation)
{
    public INamedTypeSymbol? Machine { get; } = compilation.GetTypeByMetadataName("StateAlchemist.MachineAttribute");

    public INamedTypeSymbol? Include { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IncludeAttribute");

    public INamedTypeSymbol? Module { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ModuleAttribute");

    public INamedTypeSymbol? Transition { get; } = compilation.GetTypeByMetadataName("StateAlchemist.TransitionAttribute");

    public INamedTypeSymbol? Decision { get; } = compilation.GetTypeByMetadataName("StateAlchemist.DecisionAttribute");

    public INamedTypeSymbol? On { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnAttribute");

    public INamedTypeSymbol? OnRange { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnRangeAttribute");

    public INamedTypeSymbol? OnAny { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnAnyAttribute");

    public INamedTypeSymbol? OnEvent { get; } = compilation.GetTypeByMetadataName("StateAlchemist.OnEventAttribute");

    public INamedTypeSymbol? Run { get; } = compilation.GetTypeByMetadataName("StateAlchemist.RunAttribute");

    public INamedTypeSymbol? To { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ToAttribute");

    public INamedTypeSymbol? Exited { get; } = compilation.GetTypeByMetadataName("StateAlchemist.ExitedAttribute");

    public INamedTypeSymbol? Entered { get; } = compilation.GetTypeByMetadataName("StateAlchemist.EnteredAttribute");

    public INamedTypeSymbol? Initial { get; } = compilation.GetTypeByMetadataName("StateAlchemist.InitialAttribute");

    public INamedTypeSymbol? RootState { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IRootState");

    public INamedTypeSymbol? State { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IState`1");

    public INamedTypeSymbol? Event { get; } = compilation.GetTypeByMetadataName("StateAlchemist.IEvent");

    public INamedTypeSymbol? DecisionFailed { get; } = compilation.GetTypeByMetadataName("StateAlchemist.DecisionFailed");

    public INamedTypeSymbol? TransitionInfo { get; } = compilation.GetTypeByMetadataName("StateAlchemist.TransitionInfo`1");

    public INamedTypeSymbol? ValueTask { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");

    public INamedTypeSymbol? ValueTaskOfT { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

    public INamedTypeSymbol? Task { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");

    public INamedTypeSymbol? TaskOfT { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");

    public INamedTypeSymbol? ReadOnlySpan { get; } = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");

    public INamedTypeSymbol? ReadOnlyMemory { get; } = compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1");

    public INamedTypeSymbol? CancellationToken { get; } = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
}

/// <summary>Compares by reference: the model's records compare their lists by reference anyway, and two equal methods are still two methods.</summary>
internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly ReferenceComparer<T> Instance = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
