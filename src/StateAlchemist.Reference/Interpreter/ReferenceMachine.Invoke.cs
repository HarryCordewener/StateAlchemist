using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

// Invoked machines ([Invoke]). A transition stops the children of the states it exits at its commit, before the
// Exited actions, and starts the children of the states it enters after its actions, before OnTransitioned. A child
// that completes submits an input, as a timer does; once every child of a state has, the state's done transitions are
// tried. With Forward, a trigger the parent leaves unhandled goes to each running forwarding child, in declaration
// order, instead of being reported. MachineEmitter.Invoke.cs states the same rules.
public sealed partial class ReferenceMachine<TValue>
{
    private Child?[]? _children;
    private int[]? _childGenerations;

    private Child?[] Children => _children ??= new Child?[_model.Invokes.Count];

    private IEnumerable<int> InvokesOf(int state) => Enumerable.Range(0, _model.Invokes.Count).Where(k => _model.Invokes[k].State == state);

    private async ValueTask StartChildrenAsync(IEnumerable<int> entering)
    {
        _childGenerations ??= new int[_model.Invokes.Count];
        foreach (var state in entering)
        {
            foreach (var k in InvokesOf(state))
            {
                var method = _model.Invokes[k].Method;
                var arguments = method.Parameters.Select(p => p.Kind switch
                {
                    ParameterKind.State => _slots[p.State],
                    ParameterKind.Context => _context,
                    ParameterKind.Config => _config,
                    ParameterKind.CancellationToken => (object)_lifetime.Token,
                    _ => null,
                }).ToArray();
                var machine = Invoke(method, arguments) ?? throw new InvalidOperationException($"[Invoke] '{method.FullName}' returned null.");
                var child = new Child(machine, ++_childGenerations[k]);
                Children[k] = child;
                await child.StartAsync();
                _ = child.Completion.ContinueWith(_ => ChildEnded(k, child), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private async ValueTask StopChildrenAsync(IEnumerable<int> exiting)
    {
        if (_children is null)
        {
            return;
        }

        foreach (var state in exiting)
        {
            foreach (var k in InvokesOf(state).Reverse())
            {
                await StopChildAsync(k);
            }
        }
    }

    private async ValueTask StopAllChildrenAsync()
    {
        for (var k = (_children?.Length ?? 0) - 1; k >= 0; k--)
        {
            await StopChildAsync(k);
        }
    }

    private async ValueTask StopChildAsync(int k)
    {
        if (Children[k] is { } child)
        {
            Children[k] = null;
            await child.StopAsync();
        }
    }

    /// <summary>A child ended. A completion, while it is still the current child, is submitted; a paused parent keeps it.</summary>
    private void ChildEnded(int k, Child child)
    {
        if (child.Status != MachineStatus.Completed)
        {
            return;
        }

        Work work;
        lock (_sync)
        {
            if (Status is not (MachineStatus.Running or MachineStatus.Paused) || !ReferenceEquals(Children[k], child))
            {
                return;
            }

            work = Work.ForTimer(new ChildDone(k, child));
            work.ArrivedWhilePending = _pending is not null;
            _inbox.Add(work);
        }

        _ = Task.Run(async () =>
        {
            await PumpAsync();
            try
            {
                await work.Done!.Task;
            }
            catch (Exception)
            {
                // A completion has no caller to throw to.
            }
        });
    }

    private async ValueTask RunChildDoneAsync(ChildDone done)
    {
        if (!ReferenceEquals(Children[done.Invoke], done.Child))
        {
            return;
        }

        done.Child.Done = true;
        var state = _model.Invokes[done.Invoke].State;
        if (InvokesOf(state).Any(k => Children[k] is not { Done: true }))
        {
            return;
        }

        var none = new Trigger(default, false, default, null);
        if (Choose(_resolver.ForDone(state), none, hooks: true) is { } chosen)
        {
            await ExecuteAsync(chosen, chosen.Transform, chosen.Completed, PlanNow(chosen), (TransitionKind)chosen.Kind, none, outcome: null);
        }
    }

    private void PauseChildren()
    {
        foreach (var child in _children ?? [])
        {
            if (child is { Status: MachineStatus.Running })
            {
                child.Pause();
            }
        }
    }

    private void ResumeChildren()
    {
        foreach (var child in _children ?? [])
        {
            if (child is { Status: MachineStatus.Paused })
            {
                child.Resume();
            }
        }
    }

    /// <summary>Hands an unhandled trigger to the running forwarding children. False when there were none.</summary>
    private async ValueTask<bool> ForwardAsync(Trigger trigger)
    {
        var forwarded = false;
        for (var k = 0; k < (_children?.Length ?? 0); k++)
        {
            if (!_model.Invokes[k].Forward || Children[k] is not { Status: MachineStatus.Running } child)
            {
                continue;
            }

            if (trigger.HasValue)
            {
                if (child.Value == typeof(TValue))
                {
                    forwarded = true;
                    await child.FireValueAsync(trigger.Value);
                }
            }
            else if (trigger.Event is { } e && trigger.Timer is null)
            {
                forwarded = true;
                await child.FireEventAsync(e);
            }
        }

        return forwarded;
    }

    /// <summary>A child's completion, as the parent's inbox carries it.</summary>
    private sealed record ChildDone(int Invoke, Child Child);

    /// <summary>A child machine, called through the <c>IStatechart&lt;TValue&gt;</c> it implements, whatever its value type.</summary>
    private sealed class Child
    {
        private readonly object _machine;
        private readonly Type _machineInterface;
        private readonly Type _statechartInterface;

        public Child(object machine, int generation)
        {
            _machine = machine;
            Generation = generation;
            Value = ReflectionModelBuilder.ChildValueTypeOf(machine.GetType())!;
            _machineInterface = typeof(IMachine<>).MakeGenericType(Value);
            _statechartInterface = typeof(IStatechart<>).MakeGenericType(Value);
        }

        public int Generation { get; }

        public Type Value { get; }

        public bool Done { get; set; }

        public MachineStatus Status => (MachineStatus)_machineInterface.GetProperty(nameof(IMachine<int>.Status))!.GetValue(_machine)!;

        public Task Completion => (Task)_statechartInterface.GetProperty(nameof(IStatechart<int>.Completion))!.GetValue(_machine)!;

        public ValueTask StartAsync() => (ValueTask)Call(_machineInterface.GetMethod(nameof(IMachine<int>.StartAsync), Type.EmptyTypes)!);

        public ValueTask StopAsync() => (ValueTask)Call(_machineInterface.GetMethod(nameof(IMachine<int>.StopAsync), Type.EmptyTypes)!);

        public void Pause() => Call(_statechartInterface.GetMethod(nameof(IStatechart<int>.Pause))!);

        public void Resume() => Call(_statechartInterface.GetMethod(nameof(IStatechart<int>.Resume))!);

        public ValueTask FireValueAsync(object value) => (ValueTask)Call(_machineInterface.GetMethod(nameof(IMachine<int>.FireAsync), [Value])!, value);

        public ValueTask FireEventAsync(object e) => (ValueTask)Call(
            _machineInterface.GetMethods().Single(m => m.Name == nameof(IMachine<int>.FireAsync) && m.IsGenericMethodDefinition).MakeGenericMethod(e.GetType()), e);

        private object Call(MethodInfo method, params object?[] arguments) =>
            method.Invoke(_machine, BindingFlags.DoNotWrapExceptions, null, arguments, null)!;
    }
}
