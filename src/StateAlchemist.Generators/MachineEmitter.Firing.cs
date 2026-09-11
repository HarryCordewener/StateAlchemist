using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private const string Exception = "global::System.Exception";

    /// <summary>
    /// The public entry points. A machine without an inbox runs each call inline, guarded by the busy flag and draining
    /// its event queue; a machine with one submits each call as an input to the pump (<see cref="WriteInbox"/>).
    /// </summary>
    private void WriteFiring()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync({V} value)"))
        {
            if (HasInbox)
            {
                _w.Line($"return Submit(new Input {{ Values = new {V}[] {{ value }} }});");
            }
            else
            {
                Entry("ProcessValue(value)");
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync(global::System.ReadOnlyMemory<{V}> values)"))
        {
            if (HasInbox)
            {
                _w.Line("return Submit(new Input { Values = values });");
            }
            else
            {
                Entry("ProcessValues(values)");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            _w.Line($"/// <summary>Fires a <see cref=\"{Name(_events[i])}\"/>.</summary>");
            using (_w.Block($"public {ValueTaskType} FireAsync(in {Name(_events[i])} e)"))
            {
                if (HasInbox)
                {
                    _w.Line($"var input = new Input {{ Tag = {i} }};");
                    _w.Line($"input.E{i} = e;");
                    _w.Line("return Submit(input);");
                }
                else
                {
                    Entry($"ProcessEvent{i}(e)");
                }
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync<TEvent>(TEvent e) where TEvent : struct, {Rt}IEvent"))
        {
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"if (typeof(TEvent) == typeof({Name(_events[i])})) return FireAsync(in global::System.Runtime.CompilerServices.Unsafe.As<TEvent, {Name(_events[i])}>(ref e));");
            }

            if (HasInbox)
            {
                _w.Line("return Submit(new Input { Tag = -1, Unknown = typeof(TEvent) });");
            }
            else
            {
                Entry("ProcessUnknown(typeof(TEvent))");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            _w.Line($"/// <summary>Queues a <see cref=\"{Name(_events[i])}\"/> to run after the current transition. Only from code running inside the machine.</summary>");
            using (_w.Block($"public void Enqueue(in {Name(_events[i])} e)"))
            {
                if (HasInbox)
                {
                    _w.Line($"var queued = new Input {{ Tag = {i} }};");
                    _w.Line($"queued.E{i} = e;");
                    _w.Line("EnqueueInput(queued);");
                }
                else
                {
                    _w.Line("RefuseOutside();");
                    _w.Line($"var queued = new QueuedEvent {{ Tag = {i} }};");
                    _w.Line($"queued.E{i} = e;");
                    _w.Line("Queue().Enqueue(queued);");
                }
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public void Enqueue<TEvent>(TEvent e) where TEvent : struct, {Rt}IEvent"))
        {
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"if (typeof(TEvent) == typeof({Name(_events[i])})) {{ Enqueue(in global::System.Runtime.CompilerServices.Unsafe.As<TEvent, {Name(_events[i])}>(ref e)); return; }}");
            }

            if (HasInbox)
            {
                _w.Line("EnqueueInput(new Input { Tag = -1, Unknown = typeof(TEvent) });");
            }
            else
            {
                _w.Line("RefuseOutside();");
                _w.Line("Queue().Enqueue(new QueuedEvent { Tag = -1, Unknown = typeof(TEvent) });");
            }
        }

        _w.Line();
        using (_w.Block("private void RefuseOutside()"))
        {
            _w.Line("if (!_inside) throw new global::System.InvalidOperationException(\"Enqueue is for code running inside the machine, such as an action, a hook or a decision. From outside, use FireAsync.\");");
        }

        _w.Line();
        _w.Line($"private static {ValueTaskType} Faulted({Exception} exception) {{ return new {ValueTaskType}(global::System.Threading.Tasks.Task.FromException(exception)); }}");

        // The values of a batch: a run where the leaf gives the value to a run transition, otherwise one value.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchAt(global::System.ReadOnlyMemory<{V}> values, int start, int count)"))
        {
            if (HasRuns)
            {
                _w.Line("if (RunOf(values.Span[start]) >= 0) return DispatchRun(values.Slice(start, count));");
            }

            _w.Line("return DispatchValue(values.Span[start]);");
        }

        if (!HasInbox)
        {
            WriteDirectFiring();
        }
    }

    /// <summary>The inline path: refuse, run, release. A Checked machine holds its busy flag for a caller's whole FireAsync.</summary>
    private void WriteDirectFiring()
    {
        _w.Line();
        _w.Line("private global::System.Collections.Generic.Queue<QueuedEvent> Queue() { return _queue ?? (_queue = new global::System.Collections.Generic.Queue<QueuedEvent>()); }");
        _w.Line();
        using (_w.Block($"private {Exception} Refuse()"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) return new {Rt}MachineNotRunningException(_status);");
            if (IsChecked)
            {
                _w.Line($"if (global::System.Threading.Interlocked.Exchange(ref _busy, 1) != 0) return new {Rt}ConcurrentUseException();");
            }

            _w.Line("return null;");
        }

        _w.Line();
        using (_w.Block("private void Release()"))
        {
            if (IsChecked)
            {
                _w.Line("global::System.Threading.Volatile.Write(ref _busy, 0);");
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} Finish({ValueTaskType} pending)"))
        {
            _w.Line("if (pending.IsCompletedSuccessfully) { Release(); return default(" + ValueTaskType + "); }");
            _w.Line("return FinishAsync(pending);");
        }

        _w.Line();
        using (_w.Block($"private async {ValueTaskType} FinishAsync({ValueTaskType} pending)"))
        {
            _w.Line("try { await pending; } finally { Release(); }");
        }

        // One trigger: events kept from a transition that threw run first, then the trigger, then step 9.
        _w.Line();
        using (_w.Block($"private async {ValueTaskType} ProcessValue({V} value)"))
        {
            _w.Line("await DrainQueue();");
            _w.Line("_inside = true;");
            _w.Line("try { await DispatchValue(value); } finally { _inside = false; }");
            _w.Line("await DrainQueue();");
        }

        _w.Line();
        using (_w.Block($"private async {ValueTaskType} ProcessValues(global::System.ReadOnlyMemory<{V}> values)"))
        {
            using (_w.Block("for (var i = 0; i < values.Length;)"))
            {
                _w.Line("await DrainQueue();");
                _w.Line(HasRuns ? "var count = RunLength(values.Slice(i));" : "var count = 1;");
                _w.Line("_inside = true;");
                _w.Line("try { await DispatchAt(values, i, count); } finally { _inside = false; }");
                _w.Line("i += count;");
                _w.Line("await DrainQueue();");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            using (_w.Block($"private async {ValueTaskType} ProcessEvent{i}({Name(_events[i])} e)"))
            {
                _w.Line("await DrainQueue();");
                _w.Line("_inside = true;");
                _w.Line($"try {{ await DispatchEvent{i}(e); }} finally {{ _inside = false; }}");
                _w.Line("await DrainQueue();");
            }
        }

        _w.Line();
        using (_w.Block($"private async {ValueTaskType} ProcessUnknown({TypeType} type)"))
        {
            _w.Line("await DrainQueue();");
            _w.Line("UnhandledUnknown(type);");
        }

        _w.Line();
        using (_w.Block($"private {(_events.Count > 0 ? "async " : "")}{ValueTaskType} DrainQueue()"))
        {
            using (_w.Block("while (_queue != null && _queue.Count != 0)"))
            {
                _w.Line("var queued = _queue.Dequeue();");
                _w.Line("_inside = true;");
                using (_w.Block("try"))
                {
                    using (_w.Block("switch (queued.Tag)"))
                    {
                        for (var i = 0; i < _events.Count; i++)
                        {
                            _w.Line($"case {i}: await DispatchEvent{i}(queued.E{i}); break;");
                        }

                        _w.Line("default: UnhandledUnknown(queued.Unknown); break;");
                    }
                }

                _w.Line("finally { _inside = false; }");
            }

            if (_events.Count == 0)
            {
                _w.Line($"return default({ValueTaskType});");
            }
        }
    }

    /// <summary>The body of a public entry point: refuse, start, and release when done.</summary>
    private void Entry(string process)
    {
        _w.Line("var refused = Refuse();");
        _w.Line("if (refused != null) return Faulted(refused);");
        _w.Line($"{ValueTaskType} pending;");
        _w.Line($"try {{ pending = {process}; }}");
        _w.Line($"catch ({Exception} exception) {{ Release(); return Faulted(exception); }}");
        _w.Line("return Finish(pending);");
    }

    /// <summary>The dispatch <c>switch</c>es: on the active leaf, then on the value (spec §6.1, resolved at compile time).</summary>
    private void WriteDispatch()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchValue({V} value)"))
        {
            _w.Line("var v = (int)value;");
            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.Leaves)
                {
                    using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                    {
                        WriteValueCases(leaf, "value", Candidates);
                    }
                }

                _w.Line("default: return UnhandledValue(value);");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            var eventName = SymbolModelBuilder.MetadataName(_events[i]);
            _w.Line();
            using (_w.Block($"private {ValueTaskType} DispatchEvent{i}({Name(_events[i])} e)"))
            {
                using (_w.Block("switch (_leaf)"))
                {
                    foreach (var leaf in _hierarchy.Leaves)
                    {
                        var candidates = _resolver.ForEvent(leaf, eventName);
                        if (candidates.Count == 0)
                        {
                            continue;
                        }

                        using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                        {
                            Candidates(candidates, leaf, "e", $"UnhandledEvent{i}(e)");
                        }
                    }

                    _w.Line($"default: return UnhandledEvent{i}(e);");
                }
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} UnhandledValue({V} value)"))
        {
            _w.Line("OnUnhandled(_leaf, value);");
            Unhandled("value.ToString()");
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            using (_w.Block($"private {ValueTaskType} UnhandledEvent{i}({Name(_events[i])} e)"))
            {
                _w.Line("OnUnhandled(_leaf, in e);");
                Unhandled(Literal("event " + _events[i].Name));
            }
        }

        _w.Line();
        using (_w.Block($"private void UnhandledUnknown({TypeType} type)"))
        {
            if (_model.Options.Unhandled == UnhandledMode.Throw)
            {
                _w.Line($"throw new {Rt}UnhandledTriggerException(StateType, \"event \" + type.Name);");
            }
        }
    }

    private void Unhandled(string trigger)
    {
        if (_model.Options.Unhandled == UnhandledMode.Throw)
        {
            _w.Line($"throw new {Rt}UnhandledTriggerException(StateType, {trigger});");
        }
        else
        {
            _w.Line($"return default({ValueTaskType});");
        }
    }

    /// <summary>
    /// A leaf's value cases. Values with the same candidates form runs; the commonest candidate list is the
    /// <c>default</c>; short runs become <c>case</c> labels and long ones range checks, so a 16-bit value type does not
    /// write 65,536 labels.
    /// </summary>
    private void WriteValueCases(int leaf, string argument, System.Action<IReadOnlyList<TransitionModel>, int, string, string> write)
    {
        var runs = new List<(long Low, long High, IReadOnlyList<TransitionModel> Candidates, string Key)>();
        foreach (var value in _model.Options.Domain.Values)
        {
            var candidates = _resolver.ForValue(leaf, value);
            var key = string.Join(",", candidates.Select(c => c.Index));
            if (runs.Count > 0 && runs[runs.Count - 1].Key == key && runs[runs.Count - 1].High == value - 1)
            {
                var last = runs[runs.Count - 1];
                runs[runs.Count - 1] = (last.Low, value, last.Candidates, key);
            }
            else
            {
                runs.Add((value, value, candidates, key));
            }
        }

        var fallback = runs.GroupBy(r => r.Key).OrderByDescending(g => g.Sum(r => r.High - r.Low + 1)).First();
        var unhandled = $"UnhandledValue({argument})";
        foreach (var run in runs.Where(r => r.Key != fallback.Key && r.High - r.Low >= 8))
        {
            using (_w.Block($"if (v >= {run.Low} && v <= {run.High})"))
            {
                write(run.Candidates, leaf, argument, unhandled);
            }
        }

        var labelled = runs.Where(r => r.Key != fallback.Key && r.High - r.Low < 8).GroupBy(r => r.Key).ToList();
        if (labelled.Count == 0)
        {
            write(fallback.First().Candidates, leaf, argument, unhandled);
            return;
        }

        using (_w.Block("switch (v)"))
        {
            foreach (var group in labelled)
            {
                foreach (var run in group)
                {
                    for (var value = run.Low; value <= run.High; value++)
                    {
                        _w.Line($"case {value}:");
                    }
                }

                using (_w.Block(string.Empty))
                {
                    write(group.First().Candidates, leaf, argument, unhandled);
                }
            }

            _w.Line("default:");
            using (_w.Block(string.Empty))
            {
                write(fallback.First().Candidates, leaf, argument, unhandled);
            }
        }
    }

    /// <summary>
    /// The candidates for one trigger in one leaf, in the order they are tried: each guarded one if its guard passes,
    /// then the unguarded one — or, with none, the unhandled path. A decision's candidate decides; a run's is handed
    /// the value as a run of one.
    /// </summary>
    private void Candidates(IReadOnlyList<TransitionModel> candidates, int leaf, string argument, string unhandled)
    {
        foreach (var candidate in candidates)
        {
            string call;
            if (candidate.IsDecision)
            {
                _decisions.Add((candidate.Index, leaf));
                call = $"{DecisionName(candidate.Index, leaf)}({argument})";
            }
            else
            {
                _transitions.Add((candidate.Index, leaf));
                call = candidate.IsRun ? $"{TransitionName(candidate.Index, leaf)}(One({argument}))" : $"{TransitionName(candidate.Index, leaf)}({argument})";
            }

            if (candidate.IsGuarded)
            {
                _guards.Add((candidate.Index, leaf));
                _w.Line($"if ({GuardName(candidate.Index, leaf)}({argument})) return {call};");
            }
            else
            {
                _w.Line($"return {call};");
                return;
            }
        }

        _w.Line($"return {unhandled};");
    }

    private string TransitionName(int transition, int leaf) => $"T{transition}_{_stateIds[leaf]}";

    private string GuardName(int transition, int leaf) => $"G{transition}_{_stateIds[leaf]}";

    private string DecisionName(int transition, int leaf) => $"D{transition}_{_stateIds[leaf]}";
}
