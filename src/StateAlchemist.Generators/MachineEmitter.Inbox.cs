using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// The inbox and the pump, written into machines that are Serialized or have an async decision: the design the
// reference interpreter runs (Plan 3), in generated C# 7.3. Every FireAsync is an input; whoever finds the machine
// idle pumps, one trigger per step; a pending decision pauses the input that started it while the machine accepts
// the events it handles. ReferenceMachine.Inbox.cs states the rules once.
//
// Plan 6 made it cheap: inputs are pooled and are themselves the IValueTaskSource a caller awaits, so a call
// allocates nothing in steady state; the pump runs synchronously and continues asynchronously only when a step
// suspends; and the AsyncLocal that catches self-firing is set only while a decision is pending. The
// pending-decision parts are written only into machines that have an async decision.
internal sealed partial class MachineEmitter
{
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Sources = "global::System.Threading.Tasks.Sources.";

    /// <summary>Whether the machine has an async decision: only then does its inbox carry a pending state.</summary>
    private bool Deciding => _model.Transitions.Any(t => t.Decision?.DecideAsync is not null);

    /// <summary>A Serialized machine's bounded inbox (spec §6.10): callers wait for room instead of growing the queue.</summary>
    private bool Bounded => _model.Options.Concurrency == ConcurrencyMode.Serialized && _model.Options.InboxCapacity > 0;

    private void WriteInboxStorage()
    {
        _w.Line("private readonly object _sync = new object();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _inbox = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private readonly global::System.Collections.Generic.List<Input> _queued = new global::System.Collections.Generic.List<Input>();");
        _w.Line("private readonly global::System.Collections.Generic.Stack<Input> _pool = new global::System.Collections.Generic.Stack<Input>();");
        _w.Line("private Input _spare;");
        _w.Line("private Input _current;");
        _w.Line("private bool _pumping;");
        if (Bounded)
        {
            // Not disposed when the machine stops: Abandon releases the inbox's room but cannot reach a producer
            // already inside WaitAsync, and disposing under one of those is how a semaphore hangs its waiters.
            // Nothing takes its AvailableWaitHandle, so there is no unmanaged resource to reclaim.
            _w.Line($"private readonly global::System.Threading.SemaphoreSlim _room = new global::System.Threading.SemaphoreSlim({_model.Options.InboxCapacity}, {_model.Options.InboxCapacity});");
        }

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
        WritePool();
        WriteSubmit();
        WritePump();
        WritePumpSteps();
        WriteEndings();
    }

    private void WriteInboxTypes()
    {
        var machine = _machine.Machine.Name;
        _w.Line();
        _w.Line("/// <summary>One caller's input — a batch of values, or one event — or an event queued inside the machine. Pooled; the caller awaits it directly.</summary>");
        using (_w.Block($"private sealed class Input : {Sources}IValueTaskSource"))
        {
            _w.Line($"public readonly {machine} Machine;");
            _w.Line($"public readonly {V}[] Single = new {V}[1];");
            _w.Line($"public global::System.ReadOnlyMemory<{V}> Values;");
            _w.Line("public int Next;");
            _w.Line("public int Tag = -2;");
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"public {Name(_events[i])} E{i};");
            }

            _w.Line($"public {TypeType} Unknown;");
            _w.Line("public bool HasCaller;");
            _w.Line("public bool StopAtBoundary;");
            _w.Line("public global::System.Threading.Tasks.TaskCompletionSource<int> BoundaryDone;");
            _w.Line("public int Settled;");
            _w.Line("/// <summary>Bumped on every return to the pool: a reference taken before that one must not settle this input.</summary>");
            _w.Line("public int Generation;");
            if (IsChecked)
            {
                _w.Line("public bool HoldsBusy;");
            }

            if (Deciding)
            {
                _w.Line("public bool ArrivedWhilePending;");
            }

            _w.Line($"public {Sources}ManualResetValueTaskSourceCore<bool> Core;");
            _w.Line($"public Input({machine} machine) {{ Machine = machine; Core.RunContinuationsAsynchronously = true; }}");
            _w.Line("public bool IsEvent { get { return Tag != -2; } }");
            _w.Line($"public {Sources}ValueTaskSourceStatus GetStatus(short token) {{ return Core.GetStatus(token); }}");
            _w.Line($"public void OnCompleted(global::System.Action<object> continuation, object state, short token, {Sources}ValueTaskSourceOnCompletedFlags flags) {{ Core.OnCompleted(continuation, state, token, flags); }}");
            _w.Line("public void GetResult(short token) { try { Core.GetResult(token); } finally { Machine.Return(this); } }");
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
            _w.Line("public int OwnerGeneration;");
            if (decisions.Any(t => t.Trigger.Kind != MatchKind.Event))
            {
                _w.Line($"public {V} Value;");
            }

            if (decisions.Any(t => t.Trigger.Kind == MatchKind.Event))
            {
                _w.Line("public object Event;");
            }

            // Cancelled when the decision ends, never disposed: a decision still running may yet look at its
            // token. ReferenceMachine.Decisions.cs says the same of its own.
            _w.Line("public readonly global::System.Threading.CancellationTokenSource Cancellation = new global::System.Threading.CancellationTokenSource();");
            _w.Line("public bool HasResult;");
            _w.Line("public object Outcome;");
            _w.Line($"public {Exception} Failure;");
        }
    }

    private void WritePool()
    {
        _w.Line();
        using (_w.Block("private Input Rent()"))
        {
            _w.Line("var spare = global::System.Threading.Interlocked.Exchange(ref _spare, null);");
            _w.Line("if (spare != null) return spare;");
            _w.Line("lock (_sync) { if (_pool.Count > 0) return _pool.Pop(); }");
            _w.Line("return new Input(this);");
        }

        _w.Line();
        using (_w.Block("private void Return(Input input)"))
        {
            _w.Line("input.Values = default(global::System.ReadOnlyMemory<" + V + ">);");
            _w.Line("input.Next = 0;");
            _w.Line("input.Tag = -2;");
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"input.E{i} = default({Name(_events[i])});");
            }

            _w.Line("input.Unknown = null;");
            _w.Line("input.HasCaller = false;");
            _w.Line("input.StopAtBoundary = false;");
            _w.Line("input.BoundaryDone = null;");
            _w.Line("input.Settled = 0;");
            if (IsChecked)
            {
                _w.Line("input.HoldsBusy = false;");
            }

            if (Deciding)
            {
                _w.Line("input.ArrivedWhilePending = false;");
            }

            _w.Line("input.Core.Reset();");
            _w.Line("input.Generation++;");
            _w.Line("if (global::System.Threading.Interlocked.CompareExchange(ref _spare, input, null) == null) return;");
            _w.Line("lock (_sync) { if (_pool.Count < 16) _pool.Push(input); }");
        }

        // Settling an input releases the busy flag it holds, then completes its caller's ValueTask — or, for an event
        // queued inside the machine, which has no caller, returns it to the pool. Exactly once: the pump and Abandon may race.
        _w.Line();
        using (_w.Block("private void Succeed(Input input)"))
        {
            _w.Line("if (global::System.Threading.Interlocked.Exchange(ref input.Settled, 1) != 0) return;");
            if (IsChecked)
            {
                _w.Line("if (input.HoldsBusy) { lock (_sync) { _busy = false; } }");
            }

            _w.Line("if (input.BoundaryDone != null) input.BoundaryDone.TrySetResult(input.Next);");
            _w.Line("if (input.HasCaller) input.Core.SetResult(true); else Return(input);");
        }

        _w.Line();
        using (_w.Block($"private void Fault(Input input, {Exception} exception)"))
        {
            _w.Line("if (global::System.Threading.Interlocked.Exchange(ref input.Settled, 1) != 0) return;");
            if (IsChecked)
            {
                _w.Line("if (input.HoldsBusy) { lock (_sync) { _busy = false; } }");
            }

            _w.Line("if (input.StopAtBoundary) { lock (_sync) { _boundaryRequested = false; } }");
            _w.Line("if (input.BoundaryDone != null) input.BoundaryDone.TrySetException(exception);");
            _w.Line("if (input.HasCaller) input.Core.SetException(exception); else Return(input);");
        }
    }

    private void WriteSubmit()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} Submit(Input input)"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) {{ Return(input); return Faulted(new {Rt}MachineNotRunningException(_status)); }}");
            if (Deciding)
            {
                // Code inside the machine that fires it would wait for itself: caught in a decision, and in any transition while one is pending.
                _w.Line("var flow = _flow.Value;");
                _w.Line($"if (flow != null && (flow is Pending || _pending != null)) {{ Return(input); return Faulted(new {Rt}ConcurrentUseException()); }}");
            }

            _w.Line("input.HasCaller = true;");
            _w.Line("var token = input.Core.Version;");
            if (Bounded)
            {
                _w.Line("if (!_room.Wait(0)) return SubmitWhenRoom(input, token);");
            }
            if (IsChecked)
            {
                _w.Line("var refused = false;");
            }

            // A caller that finds the machine idle runs its input at once, inline; otherwise it joins the inbox.
            var idle = "!_pumping && _current == null && _inbox.Count == 0 && _queued.Count == 0" + (Deciding ? " && _pending == null" : "");
            if (!Bounded)
            {
                _w.Line("var inline = false;");
            }

            // Stopping happens under this lock, so the check above is not enough: a machine that was running when
            // this call started can be stopped before it joins, and Abandon would never see it.
            _w.Line("var stopped = false;");
            using (_w.Block("lock (_sync)"))
            {
                _w.Line($"if (_status != {Rt}MachineStatus.Running) stopped = true;");
                using (_w.Block("else"))
                {
                    if (Deciding)
                    {
                        _w.Line("input.ArrivedWhilePending = _pending != null;");
                    }

                    if (IsChecked)
                    {
                        // One caller at a time — except events, which anyone may fire while a decision is pending.
                        var claim = "if (_busy) refused = true; else { _busy = true; input.HoldsBusy = true; }";
                        _w.Line(Deciding ? $"if (!(input.IsEvent && input.ArrivedWhilePending)) {{ {claim} }}" : claim);
                    }

                    // A bounded inbox releases room as inputs leave it, so its callers always go through it.
                    var join = Bounded ? "_inbox.Add(input);" : $"if ({idle}) {{ _pumping = true; _current = input; inline = true; }} else _inbox.Add(input);";
                    _w.Line(IsChecked ? $"if (!refused) {{ {join} }}" : join);
                }
            }

            _w.Line($"if (stopped) {{ {(Bounded ? "_room.Release(); " : string.Empty)}Return(input); return Faulted(new {Rt}MachineNotRunningException(_status)); }}");
            if (IsChecked)
            {
                _w.Line($"if (refused) {{ {(Bounded ? "_room.Release(); " : string.Empty)}Return(input); return Faulted(new {Rt}ConcurrentUseException()); }}");
            }

            _w.Line(Bounded ? "PumpNow();" : "if (inline) RunInline(input); else PumpNow();");
            _w.Line($"var result = new {ValueTaskType}(input, token);");
            _w.Line("if (!result.IsCompletedSuccessfully) return result;");
            _w.Line("result.GetAwaiter().GetResult();");
            _w.Line($"return default({ValueTaskType});");
        }

        _w.Line();
        using (_w.Block($"private async global::System.Threading.Tasks.ValueTask<int> SubmitUntilBoundary(Input input, global::System.Threading.Tasks.TaskCompletionSource<int> completion)"))
        {
            _w.Line("await Submit(input);");
            _w.Line("return await completion.Task;");
        }

        if (!Bounded)
        {
            WriteRunInline();
        }
        else
        {
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} SubmitWhenRoom(Input input, short token)"))
            {
                _w.Line("await _room.WaitAsync();");
                _w.Line("var stopped = false;");
                _w.Line($"lock (_sync) {{ if (_status != {Rt}MachineStatus.Running) stopped = true; else _inbox.Add(input); }}");
                _w.Line($"if (stopped) {{ _room.Release(); Return(input); throw new {Rt}MachineNotRunningException(_status); }}");
                _w.Line("PumpNow();");
                _w.Line($"await new {ValueTaskType}(input, token);");
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
                    _w.Line("var stale = false;");
                    _w.Line("lock (_sync) { if (decision != _pending) stale = true; else _queued.Add(queued); }");
                    _w.Line("if (stale) { Return(queued); return; }");
                    _w.Line("// The machine is idle while a decision runs: start a pump, off the decision's stack.");
                    _w.Line("global::System.Threading.ThreadPool.UnsafeQueueUserWorkItem(s_pump, this);");
                    _w.Line("return;");
                }
            }

            _w.Line("if (!_inside) { Return(queued); RefuseOutside(); }");
            _w.Line("lock (_sync) { _queued.Add(queued); }");
        }

        if (Deciding)
        {
            _w.Line();
            _w.Line($"private static readonly global::System.Threading.WaitCallback s_pump = state => (({_machine.Machine.Name})state).PumpNow();");
        }
    }

    // The idle caller's input, trigger by trigger, holding the pump; the general pump takes over as soon as there
    // is anything else to consider — an event queued by an action, or a decision that started — or a step suspends.
    private void WriteRunInline()
    {
        _w.Line();
        using (_w.Block("private void RunInline(Input input)"))
        {
            using (_w.Block("while (true)"))
            {
                _w.Line($"if (_queued.Count != 0{(Deciding ? " || _pending != null" : "")}) {{ PumpSteps(); return; }}");
                _w.Line("var done = input.IsEvent ? input.Next != 0 : input.Next >= input.Values.Length;");
                using (_w.Block("if (done)"))
                {
                    _w.Line("var more = false;");
                    using (_w.Block("lock (_sync)"))
                    {
                        _w.Line("_current = null;");
                        if (IsChecked)
                        {
                            _w.Line("if (input.HoldsBusy) { input.HoldsBusy = false; _busy = false; }");
                        }

                        _w.Line("if (_inbox.Count == 0 && _queued.Count == 0) _pumping = false; else more = true;");
                    }

                    _w.Line("Succeed(input);");
                    _w.Line("if (more) PumpSteps();");
                    _w.Line("return;");
                }

                _w.Line($"{ValueTaskType} stepped;");
                _w.Line("try { stepped = ContinueStep(input); }");
                _w.Line("catch { lock (_sync) { _pumping = false; } throw; }");
                _w.Line("if (!stepped.IsCompletedSuccessfully) { _ = PumpAfter(stepped); return; }");
                _w.Line("if (_current != input) { PumpSteps(); return; }");
            }
        }
    }

    private void WritePump()
    {
        // Runs steps inline until nothing is runnable — or until one suspends, when the rest continues after it.
        _w.Line();
        using (_w.Block("private void PumpNow()"))
        {
            _w.Line("lock (_sync) { if (_pumping) return; _pumping = true; }");
            _w.Line("PumpSteps();");
        }

        _w.Line();
        using (_w.Block("private void PumpSteps()"))
        {
            using (_w.Block("while (true)"))
            {
                _w.Line(Deciding ? "Step step; Input item; Input owner; Pending pending;" : "Step step; Input item; Input owner;");
                _w.Line($"lock (_sync) {{ step = NextStep(out item, out owner{(Deciding ? ", out pending" : "")}); if (step == Step.None) {{ _pumping = false; return; }} }}");
                _w.Line($"{ValueTaskType} stepped;");
                using (_w.Block("try"))
                {
                    _w.Line("if (step == Step.Continue) stepped = ContinueStep(item);");
                    if (Deciding)
                    {
                        _w.Line("else if (step == Step.Event) stepped = EventStep(item, owner);");
                        _w.Line("else stepped = ApplyStep(pending);");
                    }
                    else
                    {
                        _w.Line("else stepped = EventStep(item, owner);");
                    }
                }

                _w.Line("catch { lock (_sync) { _pumping = false; } throw; }");
                _w.Line("if (!stepped.IsCompletedSuccessfully) { _ = PumpAfter(stepped); return; }");
            }
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} PumpAfter({ValueTaskType} stepped)"))
        {
            _w.Line("try { await stepped; } catch { lock (_sync) { _pumping = false; } throw; }");
            _w.Line("PumpSteps();");
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
                        _w.Line("if (_queued[i].IsEvent && Handles(_pending.Decision, _queued[i].Tag)) { item = _queued[i]; _queued.RemoveAt(i); owner = item.HasCaller ? item : _pending.Owner; return Step.Event; }");
                    }

                    using (_w.Block("for (var i = 0; i < _inbox.Count; i++)"))
                    {
                        // Leaving the inbox frees its room, here as anywhere: an event the decision handles is
                        // taken straight out of it, and a bounded inbox that never released this would fill up.
                        _w.Line($"if (_inbox[i].IsEvent && Handles(_pending.Decision, _inbox[i].Tag)) {{ item = _inbox[i]; _inbox.RemoveAt(i);{(Bounded ? " _room.Release();" : string.Empty)} owner = item; return Step.Event; }}");
                    }

                    _w.Line("return Step.None;");
                }
            }

            _w.Line("if (_queued.Count > 0 && _queued[0].HasCaller) { item = _queued[0]; _queued.RemoveAt(0); owner = item; return Step.Event; }");
            _w.Line($"if (_current == null && _inbox.Count > 0) {{ _current = _inbox[0]; _inbox.RemoveAt(0);{(Bounded ? " _room.Release();" : "")} }}");
            _w.Line("if (_current == null) return Step.None;");
            _w.Line("if (_queued.Count > 0) { item = _queued[0]; _queued.RemoveAt(0); owner = item.HasCaller ? item : _current; return Step.Event; }");
            _w.Line("item = _current;");
            _w.Line("return Step.Continue;");
        }
    }

    private void WritePumpSteps()
    {
        // The current input's next trigger — inline, continuing asynchronously only if its transition suspends.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} ContinueStep(Input input)"))
        {
            if (Deciding)
            {
                _w.Line("_owner = input; _started = null;");
            }

            using (_w.Block("if (input.StopAtBoundary && _boundaryRequested)"))
            {
                _w.Line("_boundaryRequested = false;");
                _w.Line("lock (_sync) { _current = null; }");
                _w.Line("Succeed(input);");
                _w.Line($"return default({ValueTaskType});");
            }

            _w.Line("_inside = true;");
            _w.Line($"{ValueTaskType} dispatched;");
            using (_w.Block("try"))
            {
                _w.Line("if (input.IsEvent && input.Next == 0) { input.Next = 1; dispatched = DispatchInput(input); }");
                using (_w.Block("else if (!input.IsEvent && input.Next < input.Values.Length)"))
                {
                    _w.Line("var start = input.Next;");
                    _w.Line(HasRuns ? "var count = RunLength(input.Values.Slice(start));" : "var count = 1;");
                    _w.Line("input.Next += count;");
                    _w.Line("dispatched = DispatchAt(input.Values, start, count);");
                }

                using (_w.Block("else"))
                {
                    _w.Line("_inside = false;");
                    _w.Line("lock (_sync) { _current = null; }");
                    _w.Line("Succeed(input);");
                    _w.Line($"return default({ValueTaskType});");
                }
            }

            _w.Line($"catch ({Exception} exception) {{ _inside = false; Fail(input, exception); return default({ValueTaskType}); }}");
            _w.Line("if (!dispatched.IsCompletedSuccessfully) return FinishContinue(dispatched, input);");
            _w.Line("_inside = false;");
            _w.Line($"return default({ValueTaskType});");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} FinishContinue({ValueTaskType} dispatched, Input input)"))
        {
            _w.Line($"try {{ await dispatched; }} catch ({Exception} exception) {{ Fail(input, exception); }} finally {{ _inside = false; }}");
        }

        // An event that is not the current input's own: queued inside the machine, or handled while a decision is
        // pending. Only then — rarely — is the transition marked for self-fire detection, which needs an async method.
        _w.Line();
        using (_w.Block($"private {ValueTaskType} EventStep(Input item, Input owner)"))
        {
            if (Deciding)
            {
                _w.Line("if (_pending != null) return EventStepWhilePending(item, owner);");
                _w.Line("_owner = owner; _started = null;");
            }

            _w.Line("_inside = true;");
            _w.Line($"{ValueTaskType} dispatched;");
            _w.Line($"try {{ dispatched = DispatchInput(item); }}");
            _w.Line($"catch ({Exception} exception) {{ _inside = false; Fail(owner, exception); {(Deciding ? "ReleaseEnded(); " : "")}return default({ValueTaskType}); }}");
            _w.Line("if (!dispatched.IsCompletedSuccessfully) return FinishEvent(dispatched, item, owner);");
            _w.Line("_inside = false;");
            _w.Line(Deciding ? "if (_started == null || !item.HasCaller) Succeed(item);" : "Succeed(item);");
            if (Deciding)
            {
                _w.Line("ReleaseEnded();");
            }

            _w.Line($"return default({ValueTaskType});");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} FinishEvent({ValueTaskType} dispatched, Input item, Input owner)"))
        {
            _w.Line($"try {{ await dispatched; {(Deciding ? "if (_started == null || !item.HasCaller) " : "")}Succeed(item); }}");
            _w.Line($"catch ({Exception} exception) {{ Fail(owner, exception); }}");
            _w.Line(Deciding ? "finally { _inside = false; ReleaseEnded(); }" : "finally { _inside = false; }");
        }

        if (Deciding)
        {
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} EventStepWhilePending(Input item, Input owner)"))
            {
                _w.Line("_flow.Value = s_step; _owner = owner; _started = null; _inside = true;");
                _w.Line("try { await DispatchInput(item); if (_started == null || !item.HasCaller) Succeed(item); }");
                _w.Line($"catch ({Exception} exception) {{ Fail(owner, exception); }}");
                _w.Line("finally { _inside = false; ReleaseEnded(); }");
            }

            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} ApplyStep(Pending pending)"))
            {
                _w.Line("_flow.Value = s_step; _owner = pending.Owner; _started = null; _inside = true;");
                _w.Line("lock (_sync) { EndPending(pending); }");
                _w.Line("try { await ApplyDecision(pending); }");
                _w.Line($"catch ({Exception} exception) {{ if (pending.Owner != null && pending.Owner.Generation == pending.OwnerGeneration) Fail(pending.Owner, exception); }}");
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
                    _w.Line("if (_pending != null && _pending.Owner == input && _pending.OwnerGeneration == input.Generation) { abandoned = _pending; EndPending(abandoned); }");
                }

                _w.Line("if (abandoned != null) abandoned.Cancellation.Cancel();");
                _w.Line("Fault(input, exception);");
                _w.Line("ReleaseEnded();");
            }
            else
            {
                _w.Line("lock (_sync) { if (input == _current) _current = null; }");
                _w.Line("Fault(input, exception);");
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
                if (Bounded)
                {
                    _w.Line("if (_inbox.Count > 0) _room.Release(_inbox.Count);");
                }

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

            _w.Line($"foreach (var input in waiting) Fault(input, new {Rt}MachineNotRunningException({Rt}MachineStatus.Stopped));");
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
            _w.Line("global::System.Collections.Generic.List<Input> finished = null;");
            using (_w.Block("lock (_sync)"))
            {
                _w.Line("if (_ended.Count == 0) return;");
                _w.Line("ended = _ended.ToArray();");
                _w.Line("_ended.Clear();");
                using (_w.Block("for (var i = 0; i < _inbox.Count;)"))
                {
                    _w.Line("var waited = _inbox[i];");
                    _w.Line($"if (waited.IsEvent && waited.ArrivedWhilePending) {{ _inbox.RemoveAt(i);{(Bounded ? " _room.Release();" : string.Empty)} waited.ArrivedWhilePending = false; _queued.Add(waited); }} else i++;");
                }

                using (_w.Block("foreach (var pending in ended)"))
                {
                    _w.Line("if (pending.Owner != null && pending.Owner.Generation == pending.OwnerGeneration && pending.Owner != _current && (_pending == null || _pending.Owner != pending.Owner))");
                    _w.Line("{ if (finished == null) finished = new global::System.Collections.Generic.List<Input>(); finished.Add(pending.Owner); }");
                }
            }

            _w.Line("foreach (var pending in ended) pending.Cancellation.Cancel();");
            _w.Line("if (finished != null) foreach (var owner in finished) Succeed(owner);");
        }
    }
}
