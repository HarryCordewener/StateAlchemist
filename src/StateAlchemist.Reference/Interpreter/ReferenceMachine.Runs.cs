using System;
using System.Linq;
using System.Reflection;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

// Runs (spec §6.7). The stop set is not precomputed here: a value belongs to the run exactly when resolution from the
// active leaf would try the run first for it — which is the stop set's definition (StopSets), applied value by value.
public sealed partial class ReferenceMachine<TValue>
{
    private delegate void RunTransform<TState>(ref TState self, ReadOnlySpan<TValue> run);

    private delegate void RunTransformWithConfig<TState, TConfig>(ref TState self, ReadOnlySpan<TValue> run, in TConfig config);

    private delegate void RunTransformWithContext<TState, TContext>(ref TState self, ReadOnlySpan<TValue> run, TContext context);

    private delegate void RunTransformWithBoth<TState, TConfig, TContext>(ref TState self, ReadOnlySpan<TValue> run, in TConfig config, TContext context);

    /// <summary>
    /// The trigger at <paramref name="index"/>: if the leaf would give that value to a run transition, the run reaches
    /// up to the first value it would not give the same run; otherwise it is the one value.
    /// </summary>
    private Trigger NextRun(ReadOnlyMemory<TValue> values, int index)
    {
        var span = values.Span;
        var first = _resolver.ForValue(_leaf, ToInt64(span[index]));
        var end = index + 1;
        if (first.Count > 0 && first[0].IsRun)
        {
            while (end < span.Length && _resolver.ForValue(_leaf, ToInt64(span[end])) is { Count: > 0 } next && next[0].Index == first[0].Index)
            {
                end++;
            }
        }

        return Trigger.OfRun(values.Slice(index, end - index));
    }

    /// <summary>
    /// Calls a run transform. A <c>ReadOnlySpan&lt;TValue&gt;</c> cannot be boxed, so reflection cannot pass it: the
    /// method is bound to a delegate of its exact shape — <c>(ref TState, ReadOnlySpan&lt;TValue&gt;)</c>, optionally
    /// followed by <c>in TConfig</c> and the context — and the state written back into <paramref name="arguments"/>.
    /// </summary>
    private void InvokeRun(MethodModel method, object?[] arguments, ReadOnlyMemory<TValue> run)
    {
        var info = _machine.MethodOf(method);
        var types = info.GetParameters().Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType).ToArray();
        var extras = method.Parameters.Skip(2).Select(p => p.Kind).ToArray();
        var (helper, generic) = extras switch
        {
            [] => (nameof(CallRun), new[] { types[0] }),
            [ParameterKind.Config] => (nameof(CallRunWithConfig), new[] { types[0], types[2] }),
            [ParameterKind.Context] => (nameof(CallRunWithContext), new[] { types[0], types[2] }),
            [ParameterKind.Config, ParameterKind.Context] => (nameof(CallRunWithBoth), new[] { types[0], types[2], types[3] }),
            _ => throw new NotSupportedException($"'{method.FullName}' is not a run transform (SALCH0701)."),
        };
        arguments[0] = typeof(ReferenceMachine<TValue>).GetMethod(helper, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(generic)
            .Invoke(null, BindingFlags.DoNotWrapExceptions, null, [info, arguments[0], run, arguments.Skip(2).ToArray()], null);
    }

    private static object? CallRun<TState>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        ((RunTransform<TState>)method.CreateDelegate(typeof(RunTransform<TState>)))(ref state, run.Span);
        return state;
    }

    private static object? CallRunWithConfig<TState, TConfig>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        var config = (TConfig)extras[0]!;
        ((RunTransformWithConfig<TState, TConfig>)method.CreateDelegate(typeof(RunTransformWithConfig<TState, TConfig>)))(ref state, run.Span, in config);
        return state;
    }

    private static object? CallRunWithContext<TState, TContext>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        ((RunTransformWithContext<TState, TContext>)method.CreateDelegate(typeof(RunTransformWithContext<TState, TContext>)))(ref state, run.Span, (TContext)extras[0]!);
        return state;
    }

    private static object? CallRunWithBoth<TState, TConfig, TContext>(MethodInfo method, object? self, ReadOnlyMemory<TValue> run, object?[] extras)
    {
        var state = (TState)self!;
        var config = (TConfig)extras[0]!;
        ((RunTransformWithBoth<TState, TConfig, TContext>)method.CreateDelegate(typeof(RunTransformWithBoth<TState, TConfig, TContext>)))(ref state, run.Span, in config, (TContext)extras[1]!);
        return state;
    }
}
