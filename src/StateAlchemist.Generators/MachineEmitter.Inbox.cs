using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// The inbox and the pump, written into machines that are Serialized or have an async decision — the design the
// reference interpreter runs (Plan 3), in generated C# 7.3. Every FireAsync is an input; whoever finds the machine
// idle pumps, one trigger per step; a pending decision pauses the input that started it while the machine accepts
// the events it handles. See ReferenceMachine.Inbox.cs for the rules, stated once. The pending-decision parts are
// written only into machines that have an async decision.
internal sealed partial class MachineEmitter
{
    private const string Task = "global::System.Threading.Tasks.Task";

    /// <summary>Whether the machine has an async decision: only then does its inbox carry a pending state.</summary>
    private bool Deciding => _model.Transitions.Any(t => t.Decision?.DecideAsync is not null);

    private void WriteInboxStorage()
    {
        _w.Line("private readonly object _sync = new object();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _inbox = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _queued = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private Input _current;");
        _w.Line("private bool _pumping;");
        if (IsChecked)
        {
            _w.Line("private bool _busy;");
        }

        if (Deciding)
        {
            _w.Line("private readonly global::System.Collections.Generic.List<Pending> _ended = new global::System.Collections.Generic.List<Pending>();");
            _w.Line("private readonly global::System.Threading.AsyncLocal<object> _flow = new global::System.Threading.AsyncLocal<object>();");
            _w.Line("private static readonly object s_step = new object();");
            _w.Line("private Input _owner;");
            _w.Line("private Pending _pending;");
            _w.Line("private Pending _started;");
        }
    }

    private void WriteInbox()
    {
        WriteInboxTypes();
        WriteSubmit();
        WritePump();
        WritePumpSteps();
        WriteEndings();
    }

    private void WriteInboxTypes()
    {
        _w.Line();
        _w.Line("/// <summary>One caller's input — a batch of values, or one event — or an event queued inside the machine.</summary>");
        using (_w.Block("private sealed class Input"))
        {
            _w.Line($"public global::System.ReadOnlyMemory<{V}> Values;");
            _w.Line("public int Next;");
            _w.Line("public int Tag = -2;");
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"public {Name(_events[i])} E{i};");
            }

            _w.Line($"public {TypeType} Unknown;");
            _w.Line($"public {Task}CompletionSource<bool> Done;");
            if (Deciding)
            {
                _w.Line("public bool ArrivedWhilePending;");
            }

            _w.Line("public bool IsEvent { get { return Tag != -2; } }");
        }

        _w.Line();
        _w.Line(Deciding ? "private enum Step { None, Continue, Event, Apply }" : "private enum Step { None, Continue, Event }");
        if (!Deciding)
        {
            return;
        }

        var decisions = _model.Transitions.Where(t => t.Decision?.DecideAsync is not null).ToList();
        _w.Line();
        _w.Line("/// <summary>The pending state's slot: the decision in flight, and what cancels it.</summary>");
        using (_w.Block("private sealed class Pending"))
        {
            _w.Line("public int Decision;");
            _w.Line("public string Name;");
            _w.Line("public Input Owner;");
            if (decisions.Any(t => t.Trigger.Kind != MatchKind.Event))
            {
                _w.Line($"public {V} Value;");
            }

            if (decisions.Any(t => t.Trigger.Kind == MatchKind.Event))
            {
                _w.Line("public object Event;");
            }

            _w.Line("public readonly global::System.Threading.CancellationTokenSource Cancellation = new global::System.Threading.CancellationTokenSource();");
            _w.Line("public bool HasResult;");
            _w.Line("public object Outcome;");
            _w.Line($"public {Exception} Failure;");
        }
    }

    private void WriteSubmit()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} Submit(Input input)"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) return Faulted(new {Rt}MachineNotRunningException(_status));");
            if (Deciding)
            {
                // Code inside the machine that fires it would wait for itself: caught in a decision, and in any transition while one is pending.
                _w.Line("var flow = _flow.Value;");
                _w.Line($"if (flow != null && (flow is Pending || _pending != null)) return Faulted(new {Rt}ConcurrentUseException());");
            }

            if (IsChecked)
            {
                _w.Line("var holdsBusy = false;");
            }

            using (_w.Block("lock (_sync)"))
            {
                if (Deciding)
                {
                    _w.Line("input.ArrivedWhilePending = _pending != null;");
                }

                if (IsChecked)
                {
                    // One caller at a time — except events, which anyone may fire while a decision is pending.
                    var refuse = $"if (_busy) return Faulted(new {Rt}ConcurrentUseException()); _busy = holdsBusy = true;";
                    _w.Line(Deciding ? $"if (!(input.IsEvent && input.ArrivedWhilePending)) {{ {refuse} }}" : refuse);
                }

                _w.Line($"input.Done = new {Task}CompletionSource<bool>(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);");
                _w.Line("_inbox.Add(input);");
            }

            _w.Line(IsChecked ? "return Await(input, holdsBusy);" : "return Await(input);");
        }

        _w.Line();
        using (_w.Block($"private async {ValueTaskType} Await(Input input{(IsChecked ? ", bool holdsBusy" : "")})"))
        {
            if (IsChecked)
            {
                _w.Line("try { await Pump(); await input.Done.Task; }");
                _w.Line("finally { if (holdsBusy) { lock (_sync) { _busy = false; } } }");
            }
            else
            {
                _w.Line("await Pump();");
                _w.Line("await input.Done.Task;");
            }
        }

        _w.Line();
        using (_w.Block("private void EnqueueInput(Input queued)"))
        {
            if (Deciding)
            {
                _w.Line("var decision = _flow.Value as Pending;");
                using (_w.Block("if (decision != null)"))
                {
                    _w.Line("lock (_sync) { if (decision != _pending) return; _queued.Add(queued); }");
                    _w.Line("// The machine is idle while a decision runs: start a pump, off the decision's stack.");
                    _w.Line($"using (global::System.Threading.ExecutionContext.SuppressFlow()) {{ {Task}.Run(new global::System.Func<{Task}>(Pump)); }}");
                    _w.Line("return;");
                }
            }

            _w.Line("RefuseOutside();");
            _w.Line("lock (_sync) { _queued.Add(queued); }");
        }
    }

    private void WritePump()
    {
        _w.Line();
        using (_w.Block($"private async {Task} Pump()"))
        {
            _w.Line("lock (_sync) { if (_pumping) return; _pumping = true; }");
            using (_w.Block("try"))
            {
                using (_w.Block("while (true)"))
                {
                    _w.Line(Deciding ? "Step step; Input item; Input owner; Pending pending;" : "Step step; Input item; Input owner;");
                    _w.Line($"lock (_sync) {{ step = NextStep(out item, out owner{(Deciding ? ", out pending" : "")}); if (step == Step.None) {{ _pumping = false; return; }} }}");
                    _w.Line("if (step == Step.Continue) await ContinueStep(item);");
                    if (Deciding)
                    {
                        _w.Line("else if (step == Step.Event) await EventStep(item, owner);");
                        _w.Line("else await ApplyStep(pending);");
                    }
                    else
                    {
                        _w.Line("else await EventStep(item, owner);");
                    }
                }
            }

            _w.Line("catch { lock (_sync) { _pumping = false; } throw; }");
        }

        _w.Line();
        _w.Line("/// <summary>The next runnable step: while a decision is pending, its result or an event it handles; otherwise queued events, then the current input, then the next.</summary>");
        using (_w.Block($"private Step NextStep(out Input item, out Input owner{(Deciding ? ", out Pending pending" : "")})"))
        {
            _w.Line(Deciding ? "item = null; owner = null; pending = null;" : "item = null; owner = null;");
            _w.Line($"if (_status != {Rt}MachineStatus.Running) return Step.None;");
            if (Deciding)
            {
                using (_w.Block("if (_pending != null)"))
                {
                    _w.Line("if (_pending.HasResult) { pending = _pending; return Step.Apply; }");
                    using (_w.Block("for (var i = 0; i < _queued.Count; i++)"))
                    {
                        _w.Line("if (_queued[i].IsEvent && Handles(_pending.Decision, _queued[i].Tag)) { item = _queued[i]; _queued.RemoveAt(i); owner = item.Done == null ? _pending.Owner : item; return Step.Event; }");
                    }

                    using (_w.Block("for (var i = 0; i < _inbox.Count; i++)"))
                    {
                        _w.Line("if (_inbox[i].IsEvent && Handles(_pending.Decision, _inbox[i].Tag)) { item = _inbox[i]; _inbox.RemoveAt(i); owner = item; return Step.Event; }");
                    }

                    _w.Line("return Step.None;");
                }
            }

            _w.Line("if (_queued.Count > 0 && _queued[0].Done != null) { item = _queued[0]; _queued.RemoveAt(0); owner = item; return Step.Event; }");
            _w.Line("if (_current == null && _inbox.Count > 0) { _current = _inbox[0]; _inbox.RemoveAt(0); }");
            _w.Line("if (_current == null) return Step.None;");
            _w.Line("if (_queued.Count > 0) { item = _queued[0]; _queued.RemoveAt(0); owner = item.Done == null ? _current : item; return Step.Event; }");
            _w.Line("item = _current;");
            _w.Line("return Step.Continue;");
        }
    }

    private void WritePumpSteps()
    {
        var enter = Deciding ? "_flow.Value = s_step; _started = null; " : string.Empty;
        _w.Line();
        using (_w.Block($"private async {Task} ContinueStep(Input input)"))
        {
            _w.Line($"{enter}{(Deciding ? "_owner = input; " : "")}_inside = true;");
            using (_w.Block("try"))
            {
                using (_w.Block("if (input.IsEvent)"))
                {
                    _w.Line("if (input.Next == 0) { input.Next = 1; await DispatchInput(input); return; }");
                }

                using (_w.Block("else if (input.Next < input.Values.Length)"))
                {
                    _w.Line("var start = input.Next;");
                    _w.Line(HasRuns ? "var count = RunLength(input.Values.Slice(start));" : "var count = 1;");
                    _w.Line("input.Next += count;");
                    _w.Line("await DispatchAt(input.Values, start, count);");
                    _w.Line("return;");
                }

                _w.Line("lock (_sync) { _current = null; }");
                _w.Line("input.Done.TrySetResult(true);");
            }

            _w.Line($"catch ({Exception} exception) {{ Fail(input, exception); }}");
            _w.Line("finally { _inside = false; }");
        }

        _w.Line();
        using (_w.Block($"private async {Task} EventStep(Input item, Input owner)"))
        {
            _w.Line($"{enter}{(Deciding ? "_owner = owner; " : "")}_inside = true;");
            _w.Line(Deciding
                ? "try { await DispatchInput(item); if (_started == null && item.Done != null) item.Done.TrySetResult(true); }"
                : "try { await DispatchInput(item); if (item.Done != null) item.Done.TrySetResult(true); }");
            _w.Line($"catch ({Exception} exception) {{ Fail(owner, exception); }}");
            _w.Line(Deciding ? "finally { _inside = false; ReleaseEnded(); }" : "finally { _inside = false; }");
        }

        if (Deciding)
        {
            _w.Line();
            using (_w.Block($"private async {Task} ApplyStep(Pending pending)"))
            {
                _w.Line($"{enter}_owner = pending.Owner; _inside = true;");
                _w.Line("lock (_sync) { EndPending(pending); }");
                _w.Line("try { await ApplyDecision(pending); }");
                _w.Line($"catch ({Exception} exception) {{ Fail(pending.Owner, exception); }}");
                _w.Line("finally { _inside = false; ReleaseEnded(); }");
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchInput(Input input)"))
        {
            using (_w.Block("switch (input.Tag)"))
            {
                for (var i = 0; i < _events.Count; i++)
                {
                    _w.Line($"case {i}: return DispatchEvent{i}(input.E{i});");
                }

                _w.Line($"default: UnhandledUnknown(input.Unknown); return default({ValueTaskType});");
            }
        }
    }

    private void WriteEndings()
    {
        _w.Line();
        _w.Line("/// <summary>An input failed: its caller's FireAsync throws, and the rest of it — and any decision it started — is abandoned.</summary>");
        using (_w.Block($"private void Fail(Input input, {Exception} exception)"))
        {
            if (Deciding)
            {
                _w.Line("Pending abandoned = null;");
                using (_w.Block("lock (_sync)"))
                {
                    _w.Line("if (input == _current) _current = null;");
                    _w.Line("if (_pending != null && _pending.Owner == input) { abandoned = _pending; EndPending(abandoned); }");
                }

                _w.Line("if (abandoned != null) abandoned.Cancellation.Cancel();");
                _w.Line("if (input.Done != null) input.Done.TrySetException(exception);");
                _w.Line("ReleaseEnded();");
            }
            else
            {
                _w.Line("lock (_sync) { if (input == _current) _current = null; }");
                _w.Line("if (input.Done != null) input.Done.TrySetException(exception);");
            }
        }

        _w.Line();
        _w.Line("/// <summary>Stopping: cancel any pending decision; every waiting caller's FireAsync throws.</summary>");
        using (_w.Block("private void Abandon()"))
        {
            _w.Line("var waiting = new global::System.Collections.Generic.List<Input>();");
            if (Deciding)
            {
                _w.Line("Pending abandoned;");
            }

            using (_w.Block("lock (_sync)"))
            {
                _w.Line($"_status = {Rt}MachineStatus.Stopped;");
                if (Deciding)
                {
                    _w.Line("abandoned = _pending;");
                    _w.Line("_pending = null;");
                    _w.Line("_ended.Clear();");
                }

                _w.Line("waiting.AddRange(_inbox);");
                _w.Line("waiting.AddRange(_queued);");
                _w.Line("if (_current != null) waiting.Add(_current);");
                _w.Line("_inbox.Clear();");
                _w.Line("_queued.Clear();");
                _w.Line("_current = null;");
            }

            if (Deciding)
            {
                _w.Line("if (abandoned != null) abandoned.Cancellation.Cancel();");
            }

            _w.Line($"foreach (var input in waiting) if (input.Done != null) input.Done.TrySetException(new {Rt}MachineNotRunningException({Rt}MachineStatus.Stopped));");
        }

        if (!Deciding)
        {
            return;
        }

        _w.Line();
        _w.Line("/// <summary>Leaves the pending state. Called under the lock; <see cref=\"ReleaseEnded\"/> finishes the job.</summary>");
        using (_w.Block("private void EndPending(Pending pending)"))
        {
            _w.Line("if (_pending == pending) { _pending = null; _ended.Add(pending); }");
        }

        _w.Line();
        _w.Line("/// <summary>After a decision ends: cancel it, let the events that waited for it run next, and complete an owner that was a waiting event.</summary>");
        using (_w.Block("private void ReleaseEnded()"))
        {
            _w.Line("Pending[] ended;");
            _w.Line("var finished = new global::System.Collections.Generic.List<Input>();");
            using (_w.Block("lock (_sync)"))
            {
                _w.Line("if (_ended.Count == 0) return;");
                _w.Line("ended = _ended.ToArray();");
                _w.Line("_ended.Clear();");
                using (_w.Block("for (var i = 0; i < _inbox.Count;)"))
                {
                    _w.Line("var waited = _inbox[i];");
                    _w.Line("if (waited.IsEvent && waited.ArrivedWhilePending) { _inbox.RemoveAt(i); waited.ArrivedWhilePending = false; _queued.Add(waited); } else i++;");
                }

                _w.Line("foreach (var pending in ended) if (pending.Owner != _current && (_pending == null || _pending.Owner != pending.Owner)) finished.Add(pending.Owner);");
            }

            _w.Line("foreach (var pending in ended) pending.Cancellation.Cancel();");
            _w.Line("foreach (var owner in finished) if (owner != null && owner.Done != null) owner.Done.TrySetResult(true);");
        }
    }
}
