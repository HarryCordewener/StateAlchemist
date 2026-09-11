using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using StateAlchemist.Model;
using RoslynDescriptor = Microsoft.CodeAnalysis.DiagnosticDescriptor;

namespace StateAlchemist.Generators;

/// <summary>
/// <c>SALCH0801</c>: firing a machine this method constructed and has not started on every path to the call. A
/// machine that crosses methods, fields or dependency injection is left to the runtime check — this analyzer only
/// claims what one method's control flow proves.
/// <para>
/// The analysis is the textbook one: walk the method's control-flow graph forwards, carrying the set of machines
/// started on *every* path into each block, and report a fire on a machine that set does not contain. Intersection
/// at joins is what makes "on every path" true rather than hopeful, and a loop is iterated to a fixed point.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StartAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<RoslynDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.For(DiagnosticCatalog.FiredBeforeStarted.Id));

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationBlockAction(Analyze);
    }

    private static void Analyze(OperationBlockAnalysisContext context)
    {
        var machine = new KnownTypes(context.Compilation).Machine;
        if (machine is null)
        {
            return;
        }

        foreach (var block in context.OperationBlocks)
        {
            if (block is not IBlockOperation body)
            {
                continue;
            }

            // The machines this method makes itself: a local assigned a `new` of a [Machine] class.
            var constructed = body.Descendants().OfType<IObjectCreationOperation>()
                .Where(creation => creation.Type is INamedTypeSymbol type && type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, machine)))
                .Select(creation => LocalOf(creation))
                .OfType<ILocalSymbol>()
                .Aggregate(ImmutableHashSet<ISymbol>.Empty.WithComparer(SymbolEqualityComparer.Default), (set, local) => set.Add(local));
            if (constructed.IsEmpty)
            {
                continue;
            }

            // The block's own parent is the method body operation, so the graph is asked for, not built here.
            Report(context, context.GetControlFlowGraph(body), constructed);
        }
    }

    /// <summary>The local a creation is assigned to, if it is assigned to one at all.</summary>
    private static ILocalSymbol? LocalOf(IObjectCreationOperation creation) => creation.Parent switch
    {
        IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } => declarator.Symbol,
        ISimpleAssignmentOperation { Target: ILocalReferenceOperation local } => local.Local,
        IConversionOperation conversion => conversion.Parent switch
        {
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } => declarator.Symbol,
            ISimpleAssignmentOperation { Target: ILocalReferenceOperation local } => local.Local,
            _ => null,
        },
        _ => null,
    };

    private static void Report(OperationBlockAnalysisContext context, ControlFlowGraph graph, ImmutableHashSet<ISymbol> constructed)
    {
        // Into each block: the machines started on every path that reaches it. Unvisited blocks start as "all of
        // them", so a first intersection is the predecessor's own set and a loop converges downwards.
        var into = new Dictionary<int, ImmutableHashSet<ISymbol>>();
        var queue = new Queue<BasicBlock>();
        into[graph.Blocks[0].Ordinal] = ImmutableHashSet<ISymbol>.Empty.WithComparer(SymbolEqualityComparer.Default);
        queue.Enqueue(graph.Blocks[0]);
        while (queue.Count > 0)
        {
            var block = queue.Dequeue();
            var started = Started(into[block.Ordinal], block, constructed);
            foreach (var next in Successors(block))
            {
                var merged = into.TryGetValue(next.Ordinal, out var existing) ? existing.Intersect(started) : started;
                if (into.TryGetValue(next.Ordinal, out var before) && before.SetEquals(merged))
                {
                    continue;
                }

                into[next.Ordinal] = merged;
                queue.Enqueue(next);
            }
        }

        foreach (var block in graph.Blocks)
        {
            if (!into.TryGetValue(block.Ordinal, out var started))
            {
                continue; // unreachable code: the compiler has its own opinion about that
            }

            foreach (var operation in block.Operations.Concat(new[] { block.BranchValue }).Where(o => o is not null))
            {
                foreach (var fired in operation!.DescendantsAndSelf().OfType<IInvocationOperation>())
                {
                    if (Target(fired) is not { } local || !constructed.Contains(local))
                    {
                        continue;
                    }

                    if (fired.TargetMethod.Name == "StartAsync")
                    {
                        started = started.Add(local);
                        continue;
                    }

                    if (fired.TargetMethod.Name is "FireAsync" or "Fire" && !started.Contains(local))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.For(DiagnosticCatalog.FiredBeforeStarted.Id),
                            fired.Syntax.GetLocation(),
                            local.Name));
                    }
                }
            }
        }
    }

    /// <summary>The set leaving <paramref name="block"/>: what came in, plus whatever it starts.</summary>
    private static ImmutableHashSet<ISymbol> Started(ImmutableHashSet<ISymbol> into, BasicBlock block, ImmutableHashSet<ISymbol> constructed)
    {
        var started = into;
        foreach (var operation in block.Operations.Concat(new[] { block.BranchValue }).Where(o => o is not null))
        {
            foreach (var call in operation!.DescendantsAndSelf().OfType<IInvocationOperation>())
            {
                if (call.TargetMethod.Name == "StartAsync" && Target(call) is { } local && constructed.Contains(local))
                {
                    started = started.Add(local);
                }
            }
        }

        return started;
    }

    private static IEnumerable<BasicBlock> Successors(BasicBlock block)
    {
        if (block.ConditionalSuccessor?.Destination is { } conditional)
        {
            yield return conditional;
        }

        if (block.FallThroughSuccessor?.Destination is { } fallThrough)
        {
            yield return fallThrough;
        }
    }

    /// <summary>The local a call is made on, through whatever the compiler wrapped it in.</summary>
    private static ILocalSymbol? Target(IInvocationOperation call) => Unwrap(call.Instance) is ILocalReferenceOperation local ? local.Local : null;

    private static IOperation? Unwrap(IOperation? operation) => operation switch
    {
        IConversionOperation conversion => Unwrap(conversion.Operand),
        _ => operation,
    };
}
