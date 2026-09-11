using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    private readonly Queue<object> _queue = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lazy<MachineDefinition> _definition;
    private int _leaf;
    private int _busy;
    private bool _inTransition;

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

        foreach (var state in _hierarchy.PathFromRoot(_leaf))
        {
            await RunStateActionsAsync(ActionPhase.Entered, state, Lifecycle(Phase.Entered, state), snapshots: null);
        }

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

        Status = MachineStatus.Stopped;
        _lifetime.Cancel();
        var path = _hierarchy.PathFromRoot(_leaf);
        for (var i = path.Count - 1; i >= 0; i--)
        {
            await RunStateActionsAsync(ActionPhase.Exited, path[i], Lifecycle(Phase.Exited, path[i]), snapshots: null);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => StopAsync();

    /// <inheritdoc/>
    public ValueTask FireAsync(TValue value) => GuardedAsync(() => FireValueAsync(value));

    /// <inheritdoc/>
    public ValueTask FireAsync(ReadOnlyMemory<TValue> values) => GuardedAsync(async () =>
    {
        for (var i = 0; i < values.Length; i++)
        {
            await FireValueAsync(values.Span[i]);
        }
    });

    /// <inheritdoc/>
    public ValueTask FireAsync<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        GuardedAsync(() => FireEventAsync(e));

    /// <inheritdoc/>
    public void Enqueue<TEvent>(TEvent e)
        where TEvent : struct, IEvent
    {
        if (!_inTransition)
        {
            throw new InvalidOperationException("Enqueue is for code running inside a transition, such as an action or a hook. From outside, use FireAsync.");
        }

        _queue.Enqueue(e);
    }

    /// <inheritdoc/>
    public TransitionPlan Plan(TValue value) => PlanFor(_resolver.ForValue(_leaf, ToInt64(value)), Trigger.OfValue(value));

    /// <inheritdoc/>
    public TransitionPlan Plan<TEvent>(TEvent e)
        where TEvent : struct, IEvent =>
        PlanFor(_resolver.ForEvent(_leaf, typeof(TEvent).FullName!), Trigger.OfEvent(e));

    private async ValueTask GuardedAsync(Func<ValueTask> fire)
    {
        if (Status != MachineStatus.Running)
        {
            throw new MachineNotRunningException(Status);
        }

        var checkedMode = _model.Options.Concurrency == ConcurrencyMode.Checked;
        if (checkedMode && Interlocked.Exchange(ref _busy, 1) != 0)
        {
            throw new ConcurrentUseException();
        }

        try
        {
            await fire();
        }
        finally
        {
            if (checkedMode)
            {
                Volatile.Write(ref _busy, 0);
            }
        }
    }

    private ValueTask FireValueAsync(TValue value) => ProcessAsync(Trigger.OfValue(value));

    private ValueTask FireEventAsync(object e) => ProcessAsync(Trigger.OfEvent(e));

    /// <summary>
    /// One trigger, then step 9: the events its transition queued. Events kept from a transition that threw run first,
    /// so queued events always run in the order they were queued.
    /// </summary>
    private async ValueTask ProcessAsync(Trigger trigger)
    {
        await DrainAsync();
        await RunOneAsync(Candidates(trigger), trigger);
        await DrainAsync();
    }

    private async ValueTask DrainAsync()
    {
        while (_queue.Count > 0)
        {
            var queued = Trigger.OfEvent(_queue.Dequeue());
            await RunOneAsync(Candidates(queued), queued);
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

    private TransitionInfo<TValue> Lifecycle(Phase phase, int state) =>
        new("(lifecycle)", _machine.StateTypes[_hierarchy.Root], StateType, StateType, TransitionKind.Stay, phase, default, false, null, _machine.StateTypes[state]);

    /// <summary>What fired: a value or an event.</summary>
    private readonly record struct Trigger(TValue Value, bool HasValue, object? Event)
    {
        public static Trigger OfValue(TValue value) => new(value, true, null);

        public static Trigger OfEvent(object e) => new(default, false, e);

        public override string ToString() => HasValue ? Value.ToString()! : "event " + Event!.GetType().Name;
    }
}
