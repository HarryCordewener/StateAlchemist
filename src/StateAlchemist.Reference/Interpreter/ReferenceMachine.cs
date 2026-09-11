using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>
/// Runs a machine by the book: the model's own <see cref="Resolver"/> picks the transition, <see cref="PathPlanner"/>
/// plans it, and every phase is invoked by reflection in the order spec §6.2 gives. Slow and allocation-heavy on
/// purpose — it exists to be obviously right, so the generated machine can be checked against it.
/// </summary>
/// <remarks>
/// One difference is known and deliberately untested. A <c>Transform</c> that edits a staying state by <c>ref</c> and
/// then throws leaves that edit in place in a generated machine (spec §6.9: not rolled back). Reflection copies
/// <c>ref</c> arguments back only when the method returns, so here the edit is lost. The contract suite never asserts
/// what a half-run transform left behind.
/// </remarks>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public sealed partial class ReferenceMachine<TValue> : IMachine<TValue>
    where TValue : struct
{
    private readonly ReflectedMachine _machine;
    private readonly MachineModel _model;
    private readonly Hierarchy _hierarchy;
    private readonly Resolver _resolver;
    private readonly object?[] _slots;
    private readonly object? _context;
    private readonly object? _config;
    private readonly ReferenceHooks<TValue> _hooks;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lazy<MachineDefinition> _definition;
    private int _leaf;

    private ReferenceMachine(ReflectedMachine machine, object? context, object? config, ReferenceHooks<TValue>? hooks)
    {
        _machine = machine;
        _model = machine.Model;
        _hierarchy = new Hierarchy(_model.States);
        _resolver = new Resolver(_model, _hierarchy);
        _context = context;
        _config = config;
        _hooks = hooks ?? new ReferenceHooks<TValue>();
        _slots = machine.StateTypes.Select(t => Activator.CreateInstance(t)).ToArray();
        _leaf = _hierarchy.InitialLeaf(_hierarchy.Root);
        _definition = new Lazy<MachineDefinition>(() => DefinitionBuilder.Build(machine));
    }

    /// <summary>Interprets <paramref name="machine"/>.</summary>
    /// <exception cref="InvalidMachineException">The machine has errors.</exception>
    public static ReferenceMachine<TValue> Create(ReflectedMachine machine, object? context = null, object? config = null, ReferenceHooks<TValue>? hooks = null)
    {
        if (machine.Spec.Value != typeof(TValue))
        {
            throw new ArgumentException($"Machine '{machine.Model.Name}' fires {machine.Spec.Value?.Name}, not {typeof(TValue).Name}.", nameof(machine));
        }

        var errors = machine.Validate().Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidMachineException(machine.Model.Name, errors);
        }

        return new ReferenceMachine<TValue>(machine, context, config, hooks);
    }

    /// <inheritdoc/>
    public MachineDefinition Definition => _definition.Value;

    /// <inheritdoc/>
    public MachineStatus Status { get; private set; } = MachineStatus.NotStarted;

    /// <inheritdoc/>
    public Type StateType => _machine.StateTypes[_leaf];

    /// <inheritdoc/>
    public bool IsIn<TState>()
        where TState : struct =>
        IndexOf(typeof(TState)) is var index && index >= 0 && _hierarchy.IsAncestorOrSelf(index, _leaf);

    /// <inheritdoc/>
    public bool TryGetState<TState>(out TState value)
        where TState : struct
    {
        if (IsIn<TState>())
        {
            value = (TState)_slots[IndexOf(typeof(TState))]!;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask StartAsync()
    {
        if (Status != MachineStatus.NotStarted)
        {
            throw new InvalidOperationException("The machine has already been started.");
        }

        await RunLifecycleAsync(ActionPhase.Entered, _hierarchy.PathFromRoot(_leaf));
        Status = MachineStatus.Running;
    }

    /// <inheritdoc/>
    public async ValueTask StopAsync()
    {
        if (Status != MachineStatus.Running)
        {
            Status = MachineStatus.Stopped;
            return;
        }

        Abandon();
        _lifetime.Cancel();
        await RunLifecycleAsync(ActionPhase.Exited, _hierarchy.PathFromRoot(_leaf).Reverse().ToList());
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => StopAsync();

    /// <inheritdoc/>
    public ValueTask FireAsync(TValue value) => SubmitAsync(Work.ForValues(new[] { value }));

    /// <inheritdoc/>
    public ValueTask FireAsync(ReadOnlyMemory<TValue> values) => SubmitAsync(Work.ForValues(values));

    /// <inheritdoc/>
    public ValueTask FireAsync<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        SubmitAsync(Work.ForEvent(e));

    /// <inheritdoc/>
    public void Enqueue<TEvent>(TEvent e)
        where TEvent : struct, IEvent
    {
        switch (_inside.Value)
        {
            case null:
                throw new InvalidOperationException("Enqueue is for code running inside the machine, such as an action, a hook or a decision. From outside, use FireAsync.");
            case { Decision: { } decision }:
                lock (_sync)
                {
                    if (!ReferenceEquals(decision, _pending))
                    {
                        return; // from a decision that is no longer pending: it can no longer affect the machine
                    }

                    _queue.Enqueue(Work.Queued(e));
                }

                // The machine is idle while a decision runs, so nothing else will look at the queue. Start a pump —
                // off the decision's stack, since Enqueue returns at once.
                using (ExecutionContext.SuppressFlow())
                {
                    _ = Task.Run(PumpAsync);
                }

                return;
            default:
                lock (_sync)
                {
                    _queue.Enqueue(Work.Queued(e));
                }

                return;
        }
    }

    /// <inheritdoc/>
    public TransitionPlan Plan(TValue value) => PlanFor(_resolver.ForValue(_leaf, ToInt64(value)), Trigger.OfValue(value));

    /// <inheritdoc/>
    public TransitionPlan Plan<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        PlanFor(_resolver.ForEvent(_leaf, typeof(TEvent).FullName!), Trigger.OfEvent(e));

    private async ValueTask RunLifecycleAsync(ActionPhase phase, IReadOnlyList<int> states)
    {
        // Lifecycle actions run inside the machine, so they may Enqueue; what they queue runs before the first trigger.
        // Their exception hooks apply as in a transition: Skip skips every remaining lifecycle action.
        _inside.Value = Inside.Transition;
        foreach (var state in states)
        {
            var info = new TransitionInfo<TValue>(
                "(lifecycle)", _machine.StateTypes[_hierarchy.Root], StateType, StateType, TransitionKind.Stay,
                phase == ActionPhase.Entered ? Phase.Entered : Phase.Exited, default, false, null, _machine.StateTypes[state]);
            if (!await RunStateActionsAsync(phase, state, info, snapshots: null))
            {
                break;
            }
        }
    }

    private IReadOnlyList<TransitionModel> Candidates(Trigger trigger) => trigger.HasValue
        ? _resolver.ForValue(_leaf, ToInt64(trigger.Value))
        : _resolver.ForEvent(_leaf, trigger.Event!.GetType().FullName!);

    private int IndexOf(Type stateType)
    {
        for (var i = 0; i < _machine.StateTypes.Count; i++)
        {
            if (_machine.StateTypes[i] == stateType)
            {
                return i;
            }
        }

        return -1;
    }

    private static long ToInt64(TValue value) => Convert.ToInt64(value);

    /// <summary>What fired: a value (with the run it starts, one value long unless a run transition takes more) or an event.</summary>
    private readonly record struct Trigger(TValue Value, bool HasValue, ReadOnlyMemory<TValue> Run, object? Event)
    {
        public static Trigger OfValue(TValue value) => new(value, true, new[] { value }, null);

        public static Trigger OfRun(ReadOnlyMemory<TValue> run) => new(run.Span[0], true, run, null);

        public static Trigger OfEvent(object e) => new(default, false, default, e);

        public override string ToString() => HasValue ? Value.ToString()! : "event " + Event!.GetType().Name;
    }
}
