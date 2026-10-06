using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// Timers ([After]), written only into machines that declare one. Each timer is one ITimer per instance, created on
// its first arming and re-armed with Change after that, so entering a timed state does not allocate once the timer
// exists. Its state lives in fields guarded by _sync: whether it is armed, a generation bumped on every arming and
// cancelling, and when and for how long it was armed, by the machine's TimeProvider. A firing is submitted to the
// inbox as an input from inside the machine — it never holds a Checked machine's busy flag, and a pending decision
// always handles it — and is dropped when it reaches the pump if its generation is no longer the armed one.
// ReferenceMachine.Timers.cs states the same rules.
internal sealed partial class MachineEmitter
{
    /// <summary>The <c>Input.Tag</c> of a timer's firing.</summary>
    private const int TimerTag = -3;

    private const string TimeSpanType = "global::System.TimeSpan";

    private bool HasTimers => _timers.Count > 0;

    private void WriteTimerStorage()
    {
        _w.Line("private readonly global::System.TimeProvider _time;");
        _w.Line("/// <summary>Whether the restored snapshot recorded its timers; <c>_restoredDue{k}</c> is when each was due.</summary>");
        _w.Line("private bool _restoredTimers;");
        foreach (var timer in _timers)
        {
            var k = Num(timer.Index);
            _w.Line($"private global::System.Threading.ITimer _timer{k};");
            _w.Line($"private bool _armed{k};");
            _w.Line($"private int _armedGeneration{k};");
            _w.Line($"private long _armedAt{k};");
            _w.Line($"private {TimeSpanType} _armedFor{k};");
            _w.Line($"private global::System.DateTimeOffset? _restoredDue{k};");
        }
    }

    private void WriteTimers()
    {
        var machine = _machine.Machine.Name;
        foreach (var timer in _timers)
        {
            var k = Num(timer.Index);
            var source = _model.States[timer.Source].Name;
            _w.Line();
            _w.Line($"// Timer {k}: {source} --[{timer.Trigger}]-->");
            _w.Line($"private static readonly global::System.Threading.TimerCallback s_elapsed{k} = state => (({machine})state).Elapsed{k}();");

            _w.Line();
            _w.Line($"/// <summary>How long timer {k} waits, read when <see cref=\"{S(timer.Source)}\"/> is entered.</summary>");
            using (_w.Block($"private {TimeSpanType} Delay{k}()"))
            {
                if (timer.Delay is { } delay)
                {
                    _w.Line($"var delay = {Owner(delay)}({DelayArguments(delay)});");
                    _w.Line($"return delay < {TimeSpanType}.Zero ? {TimeSpanType}.Zero : delay;");
                }
                else
                {
                    _w.Line($"return new {TimeSpanType}({timer.Milliseconds.ToString(CultureInfo.InvariantCulture)}L * {TimeSpanType}.TicksPerMillisecond);");
                }
            }

            // A timer already due is not handed to the ITimer: a TimeProvider may run a due callback at once, on this
            // thread, inside the lock. The caller submits it once the lock is released.
            _w.Line();
            _w.Line("/// <summary>Called under the lock. True when the delay is already over: the caller submits the firing.</summary>");
            using (_w.Block($"private bool Arm{k}({TimeSpanType} delay)"))
            {
                _w.Line($"_armed{k} = true;");
                _w.Line($"_armedGeneration{k}++;");
                _w.Line($"_armedAt{k} = _time.GetTimestamp();");
                _w.Line($"_armedFor{k} = delay;");
                _w.Line($"if (delay <= {TimeSpanType}.Zero) {{ if (_timer{k} != null) _timer{k}.Change(global::System.Threading.Timeout.InfiniteTimeSpan, global::System.Threading.Timeout.InfiniteTimeSpan); return true; }}");
                _w.Line($"if (_timer{k} == null) _timer{k} = CreateTimer(s_elapsed{k}, delay);");
                _w.Line($"else _timer{k}.Change(delay, global::System.Threading.Timeout.InfiniteTimeSpan);");
                _w.Line("return false;");
            }

            _w.Line();
            _w.Line("/// <summary>Called under the lock.</summary>");
            using (_w.Block($"private void Disarm{k}()"))
            {
                _w.Line($"if (!_armed{k}) return;");
                _w.Line($"_armed{k} = false;");
                _w.Line($"_armedGeneration{k}++;");
                _w.Line($"if (_timer{k} != null) _timer{k}.Change(global::System.Threading.Timeout.InfiniteTimeSpan, global::System.Threading.Timeout.InfiniteTimeSpan);");
            }

            // A callback from an earlier arming can run after a re-arming, since the timer is reused: it finds the new
            // arming not yet due and sets the timer for what is left of it.
            _w.Line();
            using (_w.Block($"private void Elapsed{k}()"))
            {
                _w.Line("int generation;");
                using (_w.Block("lock (_sync)"))
                {
                    _w.Line($"if (!_armed{k} || _status != {Rt}MachineStatus.Running) return;");
                    _w.Line($"var left = _armedFor{k} - _time.GetElapsedTime(_armedAt{k});");
                    _w.Line($"if (left > {TimeSpanType}.Zero) {{ _timer{k}.Change(left, global::System.Threading.Timeout.InfiniteTimeSpan); return; }}");
                    _w.Line($"generation = _armedGeneration{k};");
                }

                _w.Line($"SubmitTimer({k}, generation);");
            }

            WriteTimerDispatch(timer);
        }

        _w.Line();
        _w.Line("/// <summary>A timer whose callback does not carry the flow that armed it: a decision's flow marks code inside the machine.</summary>");
        using (_w.Block($"private global::System.Threading.ITimer CreateTimer(global::System.Threading.TimerCallback callback, {TimeSpanType} delay)"))
        {
            _w.Line("if (global::System.Threading.ExecutionContext.IsFlowSuppressed()) return _time.CreateTimer(callback, this, delay, global::System.Threading.Timeout.InfiniteTimeSpan);");
            using (_w.Block("using (global::System.Threading.ExecutionContext.SuppressFlow())"))
            {
                _w.Line("return _time.CreateTimer(callback, this, delay, global::System.Threading.Timeout.InfiniteTimeSpan);");
            }
        }

        _w.Line();
        _w.Line("/// <summary>Stopping: every timer is cancelled and released. Called under the lock.</summary>");
        using (_w.Block("private void DisposeTimers()"))
        {
            foreach (var timer in _timers)
            {
                var k = Num(timer.Index);
                _w.Line($"_armed{k} = false;");
                _w.Line($"_armedGeneration{k}++;");
                _w.Line($"if (_timer{k} != null) {{ _timer{k}.Dispose(); _timer{k} = null; }}");
            }
        }

        // A firing joins the inbox the way a caller's input does, but holds no busy flag and takes no room: the
        // machine owns it. Whoever finds the machine idle runs it, which may be the timer's own thread.
        _w.Line();
        using (_w.Block("private void SubmitTimer(int timer, int generation)"))
        {
            _w.Line("var input = Rent();");
            _w.Line($"input.Tag = {TimerTag};");
            _w.Line("input.Timer = timer;");
            _w.Line("input.TimerGeneration = generation;");
            if (!Bounded)
            {
                _w.Line("var inline = false;");
            }

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

                    if (Bounded)
                    {
                        _w.Line("_inbox.Add(input);");
                    }
                    else
                    {
                        var idle = "!_pumping && _current == null && _inbox.Count == 0 && _queued.Count == 0" + (Deciding ? " && _pending == null" : "");
                        _w.Line($"if ({idle}) {{ _pumping = true; _current = input; inline = true; }} else _inbox.Add(input);");
                    }
                }
            }

            _w.Line("if (stopped) { Return(input); return; }");
            _w.Line(Bounded ? "PumpNow();" : "if (inline) RunInline(input); else PumpNow();");
        }

        _w.Line();
        _w.Line("/// <summary>A timer's firing, if it is still the armed one: it is disarmed, and its transitions are tried.</summary>");
        using (_w.Block($"private {ValueTaskType} DispatchTimer(int timer, int generation)"))
        {
            using (_w.Block("switch (timer)"))
            {
                foreach (var timer in _timers)
                {
                    var k = Num(timer.Index);
                    using (_w.Block($"case {k}:"))
                    {
                        using (_w.Block("lock (_sync)"))
                        {
                            _w.Line($"if (!_armed{k} || _armedGeneration{k} != generation) return default({ValueTaskType});");
                            _w.Line($"_armed{k} = false;");
                        }

                        _w.Line($"return DispatchTimer{k}();");
                    }
                }

                _w.Line($"default: return default({ValueTaskType});");
            }
        }
    }

    /// <summary>
    /// One timer's candidates from each leaf below its state, tried as any trigger's are; nothing happens when every
    /// guard refuses. With <c>OnTimerException</c> implemented, what escapes is handed to it first, with the name of
    /// the transition that was running.
    /// </summary>
    private void WriteTimerDispatch(TimerModel timer)
    {
        var k = Num(timer.Index);
        var hook = Implements("OnTimerException");
        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchTimer{k}()"))
        {
            var scope = hook ? _w.Block("try") : null;
            if (hook)
            {
                _w.Line($"_timerTransition = {Literal(timer.Name)};");
            }

            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.LeavesUnder(timer.Source))
                {
                    using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                    {
                        foreach (var candidate in timer.Candidates)
                        {
                            _transitions.Add((candidate.Index, leaf));
                            var call = $"{TransitionName(candidate.Index, leaf)}()";
                            if (hook)
                            {
                                _w.Line($"_timerTransition = {Literal(candidate.Name)};");
                                call = $"Timed({call})";
                            }

                            if (candidate.IsGuarded)
                            {
                                _guards.Add((candidate.Index, leaf));
                                _w.Line($"if ({GuardName(candidate.Index, leaf)}()) return {call};");
                            }
                            else
                            {
                                _w.Line($"return {call};");
                            }
                        }

                        if (timer.Candidates.All(c => c.IsGuarded))
                        {
                            _w.Line($"return default({ValueTaskType});");
                        }
                    }
                }

                _w.Line($"default: return default({ValueTaskType});");
            }

            if (scope is not null)
            {
                scope.Dispose();
                _w.Line($"catch ({Exception} exception) {{ OnTimerException(exception, _timerTransition); throw; }}");
            }
        }
    }

    /// <summary>With <c>OnTimerException</c>: the hook sees what a timer's transition throws after it suspends, too.</summary>
    private void WriteTimerHookSupport()
    {
        if (!HasTimers || !Implements("OnTimerException"))
        {
            return;
        }

        _w.Line();
        _w.Line("private string _timerTransition;");
        _w.Line();
        using (_w.Block($"private {ValueTaskType} Timed({ValueTaskType} running)"))
        {
            _w.Line("if (running.IsCompletedSuccessfully) return running;");
            _w.Line("return TimedAsync(running, _timerTransition);");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} TimedAsync({ValueTaskType} running, string transition)"))
        {
            _w.Line($"try {{ await running; }} catch ({Exception} exception) {{ OnTimerException(exception, transition); throw; }}");
        }
    }

    /// <summary>
    /// Arming <paramref name="arming"/> with the delay <paramref name="delay"/> gives each, after
    /// <paramref name="underLock"/> and the disarming of <paramref name="disarming"/>, under the lock; then submitting
    /// any already due, outside it.
    /// </summary>
    private string ArmStatements(IReadOnlyList<TimerModel> arming, IReadOnlyList<TimerModel> disarming, System.Func<TimerModel, string> delay, string underLock = "")
    {
        var before = string.Concat(arming.Select(t => $"var due{Num(t.Index)} = false; var generation{Num(t.Index)} = 0; "));
        var inside = underLock
                     + string.Concat(disarming.Select(t => $"Disarm{Num(t.Index)}(); "))
                     + string.Concat(arming.Select(t => $"due{Num(t.Index)} = Arm{Num(t.Index)}({delay(t)}); generation{Num(t.Index)} = _armedGeneration{Num(t.Index)}; "));
        var after = string.Concat(arming.Select(t => $" if (due{Num(t.Index)}) SubmitTimer({Num(t.Index)}, generation{Num(t.Index)});"));
        return $"{before}lock (_sync) {{ {inside}}}{after}";
    }

    /// <summary>A <c>Delay</c>'s arguments: the states it reads, the configuration and the context.</summary>
    private string DelayArguments(MethodModel delay) => string.Join(", ", delay.Parameters.Select(p => p.Kind switch
    {
        ParameterKind.State => (p.Passing == Passing.In ? "in " : string.Empty) + Field(p.State),
        ParameterKind.Config => (p.Passing == Passing.In ? "in " : string.Empty) + "_config",
        ParameterKind.Context => "_context",
        _ => "default",
    }));

    /// <summary>The timers of the states a path enters.</summary>
    private List<TimerModel> TimersOn(IEnumerable<int> states) => Timers.On(_timers, states).ToList();

    private static string Num(int index) => index.ToString(CultureInfo.InvariantCulture);
}
