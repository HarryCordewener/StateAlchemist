using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private const string Exception = "global::System.Exception";

    /// <summary>The public entry points, the busy flag, and the event queue (spec §6.4, §6.10).</summary>
    private void WriteFiring()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync({V} value)"))
        {
            Entry("ProcessValue(value)");
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} FireAsync(global::System.ReadOnlyMemory<{V}> values)"))
        {
            Entry("ProcessValues(values)");
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            _w.Line($"/// <summary>Fires a <see cref=\"{Name(_events[i])}\"/>.</summary>");
            using (_w.Block($"public {ValueTaskType} FireAsync(in {Name(_events[i])} e)"))
            {
                Entry($"ProcessEvent{i}(e)");
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

            Entry("ProcessUnknown(typeof(TEvent))");
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            _w.Line($"/// <summary>Queues a <see cref=\"{Name(_events[i])}\"/> to run after the current transition. Only from code running inside the machine.</summary>");
            using (_w.Block($"public void Enqueue(in {Name(_events[i])} e)"))
            {
                _w.Line("RefuseOutside();");
                _w.Line($"var queued = new QueuedEvent {{ Tag = {i} }};");
                _w.Line($"queued.E{i} = e;");
                _w.Line("Queue().Enqueue(queued);");
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

            _w.Line("RefuseOutside();");
            _w.Line("Queue().Enqueue(new QueuedEvent { Tag = -1, Unknown = typeof(TEvent) });");
        }

        _w.Line();
        using (_w.Block("private void RefuseOutside()"))
        {
            _w.Line("if (!_inside) throw new global::System.InvalidOperationException(\"Enqueue is for code running inside the machine, such as an action, a hook or a decision. From outside, use FireAsync.\");");
        }

        _w.Line();
        _w.Line("private global::System.Collections.Generic.Queue<QueuedEvent> Queue() { return _queue ?? (_queue = new global::System.Collections.Generic.Queue<QueuedEvent>()); }");

        // Refuse, run, release. A Checked machine holds its busy flag for a caller's whole FireAsync (spec §6.10).
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

        _w.Line();
        _w.Line($"private static {ValueTaskType} Faulted({Exception} exception) {{ return new {ValueTaskType}(global::System.Threading.Tasks.Task.FromException(exception)); }}");

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
            _w.Line("for (var i = 0; i < values.Length; i++) await ProcessValue(values.Span[i]);");
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

        _w.Line();
        _w.Line($"private static {ValueTaskType} NotYetGenerated() {{ throw new global::System.NotSupportedException(\"Decisions and runs are generated from Plan 5.\"); }}");
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
    /// then the unguarded one — or, with none, the unhandled path.
    /// </summary>
    private void Candidates(IReadOnlyList<TransitionModel> candidates, int leaf, string argument, string unhandled)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.IsDecision || candidate.IsRun)
            {
                _w.Line("return NotYetGenerated();");
                return;
            }

            _transitions.Add((candidate.Index, leaf));
            if (candidate.IsGuarded)
            {
                _guards.Add((candidate.Index, leaf));
                _w.Line($"if ({GuardName(candidate.Index, leaf)}({argument})) return {TransitionName(candidate.Index, leaf)}({argument});");
            }
            else
            {
                _w.Line($"return {TransitionName(candidate.Index, leaf)}({argument});");
                return;
            }
        }

        _w.Line($"return {unhandled};");
    }

    private string TransitionName(int transition, int leaf) => $"T{transition}_{_stateIds[leaf]}";

    private string GuardName(int transition, int leaf) => $"G{transition}_{_stateIds[leaf]}";
}
