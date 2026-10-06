using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary>
    /// <c>TakeSnapshot</c> and <c>Restore</c> (issue #20): the active leaf, the data of each active state, what each
    /// history state recorded and what each active join has received, in plain classes a serializer can write.
    /// States, history and joins are named, not numbered, so a snapshot outlives states added or reordered.
    /// </summary>
    private void WriteSnapshots()
    {
        WriteSnapshotTypes();
        WriteTakeSnapshot();
        WriteRestore();
    }

    private void WriteSnapshotTypes()
    {
        var history = HistoryStates.ToList();
        _w.Line();
        _w.Line("/// <summary>");
        _w.Line("/// A machine at rest: its active leaf and the data of its active states, written by <see cref=\"TakeSnapshot\"/> and");
        _w.Line("/// read by <see cref=\"Restore\"/>. Every member is a settable property, so a serializer can write and read it.");
        _w.Line("/// </summary>");
        using (_w.Block("public sealed class Snapshot"))
        {
            _w.Line("/// <summary>The active leaf, as the name of its <see cref=\"StateId\"/> member.</summary>");
            _w.Line("public string State { get; set; }");
            _w.Line();
            _w.Line("/// <summary>Each active state's data. A state that is inactive, or missing, is null.</summary>");
            _w.Line("public SnapshotStates States { get; set; }");
            if (history.Count > 0)
            {
                _w.Line();
                _w.Line("/// <summary>The leaf each state with history was last exited from, by name; null before its first exit.</summary>");
                _w.Line("public SnapshotHistory History { get; set; }");
            }

            if (Joins.Count > 0)
            {
                _w.Line();
                _w.Line("/// <summary>What each join whose source is active has received.</summary>");
                _w.Line("public SnapshotJoins Joins { get; set; }");
            }

            if (HasTimers)
            {
                _w.Line();
                _w.Line("/// <summary>When each running timer is due; null from a machine that had not started, whose active states' timers then start afresh.</summary>");
                _w.Line("public SnapshotTimers Timers { get; set; }");
            }
        }

        if (HasTimers)
        {
            _w.Line();
            _w.Line("/// <summary>When each timer is due, one property per timer, named by its transition.</summary>");
            using (_w.Block("public sealed class SnapshotTimers"))
            {
                foreach (var timer in _timers)
                {
                    _w.Line($"/// <summary>When <c>{timer.Name}</c> is due, as a UTC instant; null when it is not running.</summary>");
                    _w.Line($"public global::System.DateTimeOffset? {TimerProperty(timer)} {{ get; set; }}");
                }
            }
        }

        _w.Line();
        _w.Line("/// <summary>The data of a <see cref=\"Snapshot\"/>'s active states, one property per state.</summary>");
        using (_w.Block("public sealed class SnapshotStates"))
        {
            for (var i = 0; i < _model.States.Count; i++)
            {
                _w.Line($"/// <summary><see cref=\"{S(i)}\"/>'s data, if it is active.</summary>");
                _w.Line($"public {S(i)}? {_stateIds[i]} {{ get; set; }}");
            }
        }

        if (history.Count > 0)
        {
            _w.Line();
            _w.Line("/// <summary>What each state with history recorded, one property per state.</summary>");
            using (_w.Block("public sealed class SnapshotHistory"))
            {
                foreach (var state in history)
                {
                    _w.Line($"/// <summary>The leaf under <see cref=\"{S(state)}\"/> active when it was last exited, or null.</summary>");
                    _w.Line($"public string {_stateIds[state]} {{ get; set; }}");
                }
            }
        }

        if (Joins.Count == 0)
        {
            return;
        }

        _w.Line();
        _w.Line("/// <summary>What each join has received, one property per join.</summary>");
        using (_w.Block("public sealed class SnapshotJoins"))
        {
            foreach (var join in Joins)
            {
                _w.Line($"/// <summary>What <c>{join.Name}</c> has received; null when its source is inactive or nothing has arrived.</summary>");
                _w.Line($"public {SnapshotJoinType(join)} {JoinProperty(join)} {{ get; set; }}");
            }
        }

        foreach (var join in Joins)
        {
            var names = JoinEventProperties(join);
            _w.Line();
            _w.Line($"/// <summary>The latest payload of each event <c>{join.Name}</c> waits for; null until it arrives.</summary>");
            using (_w.Block($"public sealed class {SnapshotJoinType(join)}"))
            {
                for (var i = 0; i < join.Model.Events.Count; i++)
                {
                    _w.Line($"/// <summary>The latest <see cref=\"{Event(join.Model.Events[i])}\"/>, if one has arrived.</summary>");
                    _w.Line($"public {Event(join.Model.Events[i])}? {names[i]} {{ get; set; }}");
                }
            }
        }
    }

    private void WriteTakeSnapshot()
    {
        var notIdle = Literal("The machine is processing input. Take a snapshot after FireAsync has completed.");
        _w.Line();
        _w.Line("/// <summary>");
        _w.Line("/// Copies the machine's active leaf, the data of its active states, what its history states recorded and what its");
        _w.Line("/// active joins have received. Runs nothing.");
        _w.Line("/// </summary>");
        _w.Line("/// <exception cref=\"global::System.InvalidOperationException\">");
        _w.Line("/// The machine is stopped, is processing input, has a decision pending, or the call comes from inside the machine.");
        _w.Line("/// </exception>");
        using (_w.Block("public Snapshot TakeSnapshot()"))
        {
            _w.Line($"if (_status == {Rt}MachineStatus.Stopped) throw new global::System.InvalidOperationException({Literal("The machine has been stopped: its states have been exited.")});");
            _w.Line($"if (_inside) throw new global::System.InvalidOperationException({Literal("TakeSnapshot is for code outside the machine. An action or hook runs mid-transition.")});");
            if (HasInbox)
            {
                using (_w.Block("lock (_sync)"))
                {
                    if (Deciding)
                    {
                        _w.Line($"if (_pending != null) throw new global::System.InvalidOperationException(\"Decision '\" + _pending.Name + \"' is pending. Take a snapshot after FireAsync has completed.\");");
                    }

                    _w.Line($"if (_pumping || _current != null || _inbox.Count != 0 || _queued.Count != 0) throw new global::System.InvalidOperationException({notIdle});");
                    _w.Line("return Capture();");
                }
            }
            else if (IsChecked)
            {
                _w.Line($"if (global::System.Threading.Interlocked.Exchange(ref _busy, 1) != 0) throw new global::System.InvalidOperationException({notIdle});");
                _w.Line("try { return Capture(); } finally { global::System.Threading.Volatile.Write(ref _busy, 0); }");
            }
            else
            {
                _w.Line("return Capture();");
            }
        }

        _w.Line();
        using (_w.Block("private Snapshot Capture()"))
        {
            _w.Line("var states = new SnapshotStates();");
            for (var i = 0; i < _model.States.Count; i++)
            {
                _w.Line($"if (IsIn(StateId.{_stateIds[i]})) states.{_stateIds[i]} = {Field(i)};");
            }

            _w.Line("var snapshot = new Snapshot { State = NameOf(_leaf), States = states };");
            var history = HistoryStates.ToList();
            if (history.Count > 0)
            {
                _w.Line("snapshot.History = new SnapshotHistory();");
                foreach (var state in history)
                {
                    _w.Line($"if ({HistoryField(state)} != StateId.{_stateIds[state]}) snapshot.History.{_stateIds[state]} = NameOf({HistoryField(state)});");
                }
            }

            if (Joins.Count > 0)
            {
                _w.Line("snapshot.Joins = new SnapshotJoins();");
                foreach (var join in Joins)
                {
                    var names = JoinEventProperties(join);
                    var field = JoinField(join);
                    using (_w.Block($"if (IsIn(StateId.{_stateIds[join.Source]}) && {field}.Mask != 0u)"))
                    {
                        _w.Line($"var arrivals = new {SnapshotJoinType(join)}();");
                        for (var i = 0; i < names.Count; i++)
                        {
                            _w.Line($"if (({field}.Mask & {Mask(1u << i)}) != 0u) arrivals.{names[i]} = {field}.E{i};");
                        }

                        _w.Line($"snapshot.Joins.{JoinProperty(join)} = arrivals;");
                    }
                }
            }

            if (HasTimers)
            {
                // A running timer is due when it was armed plus its delay; one restored but not yet started keeps the
                // due time it was restored with. A machine never started has no timers running yet: null.
                using (_w.Block($"if (_status == {Rt}MachineStatus.Running)"))
                {
                    _w.Line("snapshot.Timers = new SnapshotTimers();");
                    _w.Line("var now = _time.GetUtcNow();");
                    foreach (var timer in _timers)
                    {
                        var k = Num(timer.Index);
                        _w.Line($"if (_armed{k}) snapshot.Timers.{TimerProperty(timer)} = now + (_armedFor{k} - _time.GetElapsedTime(_armedAt{k}));");
                    }
                }

                using (_w.Block("else if (_restoredTimers)"))
                {
                    _w.Line("snapshot.Timers = new SnapshotTimers();");
                    foreach (var timer in _timers)
                    {
                        _w.Line($"snapshot.Timers.{TimerProperty(timer)} = _restoredDue{Num(timer.Index)};");
                    }
                }
            }

            _w.Line("return snapshot;");
        }

        _w.Line();
        using (_w.Block("private static string NameOf(StateId state)"))
        {
            using (_w.Block("switch (state)"))
            {
                for (var i = 0; i < _stateIds.Length; i++)
                {
                    _w.Line($"case StateId.{_stateIds[i]}: return {Literal(_stateIds[i])};");
                }

                _w.Line("default: throw new global::System.ArgumentOutOfRangeException(\"state\");");
            }
        }
    }

    private void WriteRestore()
    {
        var leaves = _hierarchy.Leaves.ToList();
        var history = HistoryStates.ToList();
        _w.Line();
        _w.Line("/// <summary>");
        _w.Line("/// Puts a machine that has not been started into the state <paramref name=\"snapshot\"/> describes. Runs nothing, and");
        _w.Line("/// the <see cref=\"StartAsync\"/> that follows runs no <c>[Entered]</c> actions: the states were entered before the");
        _w.Line("/// snapshot was taken. An active state the snapshot has no data for starts with <c>default</c> data.");
        _w.Line("/// </summary>");
        _w.Line("/// <param name=\"snapshot\">A snapshot from <see cref=\"TakeSnapshot\"/>, possibly of an earlier version of this machine.</param>");
        _w.Line("/// <exception cref=\"global::System.ArgumentException\">The snapshot names a leaf, or a recorded history leaf, this machine does not have.</exception>");
        _w.Line("/// <exception cref=\"global::System.InvalidOperationException\">The machine has been started.</exception>");
        using (_w.Block("public void Restore(Snapshot snapshot)"))
        {
            _w.Line("if (snapshot == null) throw new global::System.ArgumentNullException(\"snapshot\");");
            _w.Line($"if (_status != {Rt}MachineStatus.NotStarted) throw new global::System.InvalidOperationException({Literal("Restore is for a machine that has not been started.")});");
            _w.Line("StateId leaf;");
            _w.Line("if (!TryLeaf(snapshot.State, out leaf)) throw new global::System.ArgumentException(\"'\" + snapshot.State + \"' is not a leaf of this machine.\", \"snapshot\");");
            if (history.Count > 0)
            {
                _w.Line("var history = snapshot.History ?? new SnapshotHistory();");
                foreach (var state in history)
                {
                    var local = "h" + state.ToString(CultureInfo.InvariantCulture);
                    _w.Line($"var {local} = StateId.{_stateIds[state]};");
                    _w.Line($"if (history.{_stateIds[state]} != null && !(TryLeaf(history.{_stateIds[state]}, out {local}) && {LeafUnder(state, local)})) " +
                            $"throw new global::System.ArgumentException(\"'\" + history.{_stateIds[state]} + \"' is not a leaf under {_stateIds[state]}.\", \"snapshot\");");
                }
            }

            _w.Line("var states = snapshot.States ?? new SnapshotStates();");
            _w.Line("_leaf = leaf;");
            for (var i = 0; i < _model.States.Count; i++)
            {
                _w.Line($"{Field(i)} = IsIn(StateId.{_stateIds[i]}) && states.{_stateIds[i]}.HasValue ? states.{_stateIds[i]}.Value : default({S(i)});");
            }

            foreach (var state in history)
            {
                _w.Line($"{HistoryField(state)} = h{state.ToString(CultureInfo.InvariantCulture)};");
            }

            if (Joins.Count > 0)
            {
                _w.Line("var joins = snapshot.Joins ?? new SnapshotJoins();");
                foreach (var join in Joins)
                {
                    var names = JoinEventProperties(join);
                    var field = JoinField(join);
                    _w.Line($"{field} = default({JoinType(join)});");
                    using (_w.Block($"if (IsIn(StateId.{_stateIds[join.Source]}) && joins.{JoinProperty(join)} != null)"))
                    {
                        _w.Line($"var arrivals = joins.{JoinProperty(join)};");
                        for (var i = 0; i < names.Count; i++)
                        {
                            _w.Line($"if (arrivals.{names[i]}.HasValue) {{ {field}.E{i} = arrivals.{names[i]}.Value; {field}.Mask |= {Mask(1u << i)}; }}");
                        }
                    }
                }
            }

            if (HasTimers)
            {
                _w.Line("var timers = snapshot.Timers;");
                _w.Line("_restoredTimers = timers != null;");
                foreach (var timer in _timers)
                {
                    _w.Line($"_restoredDue{Num(timer.Index)} = timers != null && IsIn(StateId.{_stateIds[timer.Source]}) ? timers.{TimerProperty(timer)} : null;");
                }
            }

            _w.Line("_restored = true;");
        }

        if (HasTimers)
        {
            WriteStartRestored();
        }

        _w.Line();
        _w.Line("/// <summary>The leaf named <paramref name=\"name\"/>; false when no leaf of this machine has that name.</summary>");
        using (_w.Block("private static bool TryLeaf(string name, out StateId leaf)"))
        {
            using (_w.Block("switch (name)"))
            {
                foreach (var l in leaves)
                {
                    _w.Line($"case {Literal(_stateIds[l])}: leaf = StateId.{_stateIds[l]}; return true;");
                }

                _w.Line("default: leaf = default(StateId); return false;");
            }
        }
    }

    /// <summary>
    /// The <c>StartAsync</c> after <c>Restore</c>: each recorded timer runs for what is left of it, and one already due
    /// fires at once. A snapshot without timers starts the active states' timers afresh.
    /// </summary>
    private void WriteStartRestored()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} StartRestored()"))
        {
            _w.Line("var now = _time.GetUtcNow();");
            foreach (var timer in _timers)
            {
                var k = Num(timer.Index);
                _w.Line($"var delay{k} = {TimeSpanType}.Zero;");
                _w.Line($"var start{k} = false;");
                _w.Line($"if (_restoredTimers) {{ if (_restoredDue{k}.HasValue) {{ delay{k} = _restoredDue{k}.Value - now; start{k} = true; }} }}");
                _w.Line($"else if (IsIn(StateId.{_stateIds[timer.Source]})) {{ delay{k} = Delay{k}(); start{k} = true; }}");
            }

            var before = string.Concat(_timers.Select(t => $"var due{Num(t.Index)} = false; var generation{Num(t.Index)} = 0; "));
            var inside = string.Concat(_timers.Select(t =>
                $"if (start{Num(t.Index)}) {{ due{Num(t.Index)} = Arm{Num(t.Index)}(delay{Num(t.Index)}); generation{Num(t.Index)} = _armedGeneration{Num(t.Index)}; }} "));
            _w.Line($"{before}lock (_sync) {{ _status = {Rt}MachineStatus.Running; {inside}}}");
            foreach (var timer in _timers)
            {
                _w.Line($"if (due{Num(timer.Index)}) SubmitTimer({Num(timer.Index)}, generation{Num(timer.Index)});");
            }

            _w.Line($"return default({ValueTaskType});");
        }
    }

    /// <summary>A timer's property in <c>SnapshotTimers</c>: its transition's name, with the module's dot made an underscore.</summary>
    private static string TimerProperty(StateAlchemist.Model.TimerModel timer) => timer.Name.Replace('.', '_').Replace('+', '_');

    private string LeafUnder(int state, string local) =>
        "(" + string.Join(" || ", _hierarchy.LeavesUnder(state).Select(l => $"{local} == StateId.{_stateIds[l]}")) + ")";

    /// <summary>A join's property in <c>SnapshotJoins</c>: its name, with the module's dot made an underscore.</summary>
    private static string JoinProperty(Join join) => join.Name.Replace('.', '_').Replace('+', '_');

    private static string SnapshotJoinType(Join join) => "SnapshotJoin" + join.Slot.ToString(CultureInfo.InvariantCulture);

    /// <summary>Each listed event's property: its short name, or its full name when two listed events share a short name.</summary>
    private List<string> JoinEventProperties(Join join)
    {
        var types = join.Model.Events.Select(e => _machine.Events[e]).ToList();
        var shared = new HashSet<string>(types.GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key));
        return types.Select(t => shared.Contains(t.Name) ? SymbolModelBuilder.MetadataName(t).Replace('.', '_').Replace('+', '_').Replace('`', '_') : t.Name).ToList();
    }
}
