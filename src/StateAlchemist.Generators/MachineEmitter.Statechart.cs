using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// Final states, done and eventless transitions, completion, pausing and the observer. Eventless and done transitions
// are tried by Settle, which the steps of a transition call last when its target leaf has something to try: the
// chain runs before the trigger that started it returns, so the next trigger always finds the machine settled.
// ReferenceMachine.Statechart.cs states the same rules.
internal sealed partial class MachineEmitter
{
    private const int MaxSettleDepth = 64;

    /// <summary>Whether a final child of the root can be entered, which completes the machine.</summary>
    private bool HasCompletion => _hierarchy.Leaves.Any(IsRootFinal);

    private bool IsRootFinal(int leaf) => _model.States[leaf].IsFinal && _hierarchy.ParentOf(leaf) == _hierarchy.Root;

    /// <summary>Whether arriving at <paramref name="leaf"/> leaves something to try: an eventless transition, or — when the leaf was entered — a final state's parent being done.</summary>
    private bool NeedsSettle(int leaf, bool entered) => _resolver.ForAlways(leaf).Count > 0 || (entered && _model.States[leaf].IsFinal);

    private bool HasSettle => _hierarchy.Leaves.Any(l => NeedsSettle(l, entered: true));

    /// <summary>What the steps of a transition into <paramref name="path"/>'s target leaf call last, or null when there is nothing to try.</summary>
    private string? SettleCall(TransitionPath path)
    {
        var entered = path.Entering.Contains(path.TargetLeaf);
        return NeedsSettle(path.TargetLeaf, entered) ? $"Settle({Bool(entered)})" : null;
    }

    private void WriteStatechartStorage()
    {
        _w.Line("private global::System.Threading.Tasks.TaskCompletionSource<bool> _completion;");
        _w.Line($"private {Rt}IMachineObserver<{V}> _observer;");
        if (HasSettle)
        {
            _w.Line("private int _settleDepth;");
        }

        foreach (var timer in _timers)
        {
            _w.Line($"private {TimeSpanType}? _pausedLeft{Num(timer.Index)};");
        }
    }

    private void WriteStatechart()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {Task} Completion"))
        {
            using (_w.Block("get"))
            {
                _w.Line("var completion = global::System.Threading.Volatile.Read(ref _completion);");
                _w.Line("if (completion != null) return completion.Task;");
                _w.Line($"completion = new global::System.Threading.Tasks.TaskCompletionSource<bool>(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);");
                _w.Line("var prior = global::System.Threading.Interlocked.CompareExchange(ref _completion, completion, null);");
                _w.Line("if (prior != null) return prior.Task;");
                _w.Line($"if (_status == {Rt}MachineStatus.Completed || _status == {Rt}MachineStatus.Stopped) completion.TrySetResult(true);");
                _w.Line("return completion.Task;");
            }
        }

        _w.Line();
        using (_w.Block("private void SignalCompletion()"))
        {
            _w.Line("var completion = global::System.Threading.Interlocked.CompareExchange(ref _completion, null, null);");
            _w.Line("if (completion != null) completion.TrySetResult(true);");
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        _w.Line($"public {Rt}IMachineObserver<{V}> Observer {{ get {{ return _observer; }} set {{ _observer = value; }} }}");

        WritePause();
        if (HasCompletion)
        {
            _w.Line();
            _w.Line("/// <summary>A final child of the root was entered: the machine refuses triggers, and its timers stop.</summary>");
            using (_w.Block("private void Complete()"))
            {
                if (HasInbox)
                {
                    _w.Line($"lock (_sync) {{ _status = {Rt}MachineStatus.Completed;{(HasTimers ? " DisposeTimers();" : string.Empty)} }}");
                }
                else
                {
                    _w.Line($"_status = {Rt}MachineStatus.Completed;");
                }

                _w.Line("SignalCompletion();");
            }
        }

        if (HasSettle)
        {
            WriteSettle();
        }
    }

    /// <summary>What a restored machine's start does when its leaf is a final child of the root.</summary>
    private string RestoredCompletion() => HasCompletion
        ? $"if ({string.Join(" || ", _hierarchy.Leaves.Where(IsRootFinal).Select(l => $"_leaf == StateId.{_stateIds[l]}"))}) Complete(); "
        : string.Empty;

    /// <summary>The first lines of a dispatch: a completed machine drops what was already waiting.</summary>
    private void WriteCompletedGuard()
    {
        if (HasCompletion)
        {
            _w.Line($"if (_status == {Rt}MachineStatus.Completed) return default({ValueTaskType});");
        }
    }

    private void WritePause()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block("public void Pause()"))
        {
            if (HasInbox)
            {
                // An inbox machine's step may be running on another thread: the pump stops after it.
                using (_w.Block("lock (_sync)"))
                {
                    _w.Line($"if (_status != {Rt}MachineStatus.Running) throw new global::System.InvalidOperationException(\"Only a running machine can be paused.\");");
                    _w.Line($"_status = {Rt}MachineStatus.Paused;");
                    foreach (var timer in _timers)
                    {
                        var k = Num(timer.Index);
                        _w.Line($"if (_armed{k}) {{ _pausedLeft{k} = _armedFor{k} - _time.GetElapsedTime(_armedAt{k}); Disarm{k}(); }}");
                    }
                }

                if (HasInvokes)
                {
                    _w.Line("PauseChildren();");
                }
            }
            else
            {
                _w.Line("if (_inside) throw new global::System.InvalidOperationException(\"Pause is for code outside the machine.\");");
                _w.Line($"if (_status != {Rt}MachineStatus.Running) throw new global::System.InvalidOperationException(\"Only a running machine can be paused.\");");
                if (IsChecked)
                {
                    _w.Line("if (global::System.Threading.Interlocked.Exchange(ref _busy, 1) != 0) throw new global::System.InvalidOperationException(\"The machine is processing input. Pause it after FireAsync has completed.\");");
                }

                _w.Line($"_status = {Rt}MachineStatus.Paused;");
                if (IsChecked)
                {
                    _w.Line("global::System.Threading.Volatile.Write(ref _busy, 0);");
                }
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block("public void Resume()"))
        {
            if (HasInbox)
            {
                _w.Line(string.Concat(_timers.Select(t => $"var due{Num(t.Index)} = false; var generation{Num(t.Index)} = 0; ")).TrimEnd());
                using (_w.Block("lock (_sync)"))
                {
                    _w.Line($"if (_status != {Rt}MachineStatus.Paused) throw new global::System.InvalidOperationException(\"Only a paused machine can be resumed.\");");
                    _w.Line($"_status = {Rt}MachineStatus.Running;");
                    foreach (var timer in _timers)
                    {
                        var k = Num(timer.Index);
                        _w.Line($"if (_pausedLeft{k}.HasValue) {{ due{k} = Arm{k}(_pausedLeft{k}.Value); generation{k} = _armedGeneration{k}; _pausedLeft{k} = null; }}");
                    }
                }

                foreach (var timer in _timers)
                {
                    _w.Line($"if (due{Num(timer.Index)}) SubmitTimer({Num(timer.Index)}, generation{Num(timer.Index)});");
                }

                if (HasInvokes)
                {
                    _w.Line("ResumeChildren();");
                }

                _w.Line("PumpNow();");
            }
            else
            {
                _w.Line($"if (_status != {Rt}MachineStatus.Paused) throw new global::System.InvalidOperationException(\"Only a paused machine can be resumed.\");");
                _w.Line($"_status = {Rt}MachineStatus.Running;");
            }
        }
    }

    /// <summary>
    /// <c>Settle</c>: per leaf, its eventless candidates, nearest first, as any trigger's are tried; then, for a final
    /// leaf just entered, its parent's done transitions — or, under the root, completion. A chain is bounded.
    /// </summary>
    private void WriteSettle()
    {
        _w.Line();
        _w.Line("/// <summary>Tries what the active leaf fires on its own. The steps of each transition it fires call it again.</summary>");
        using (_w.Block($"private {ValueTaskType} Settle(bool entered)"))
        {
            _w.Line($"if (_settleDepth >= {MaxSettleDepth}) throw new global::System.InvalidOperationException({Literal($"Eventless transitions fired {MaxSettleDepth} times in a row without the machine settling: a guarded loop never ends.")});");
            _w.Line("_settleDepth++;");
            _w.Line($"{ValueTaskType} settled;");
            _w.Line("try { settled = SettleStep(entered); } catch { _settleDepth--; throw; }");
            _w.Line("if (settled.IsCompletedSuccessfully) { _settleDepth--; return settled; }");
            _w.Line("return SettleAsync(settled);");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} SettleAsync({ValueTaskType} settled)"))
        {
            _w.Line("try { await settled; } finally { _settleDepth--; }");
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} SettleStep(bool entered)"))
        {
            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.Leaves.Where(l => NeedsSettle(l, entered: true)))
                {
                    using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                    {
                        if (WriteSettleCandidates(_resolver.ForAlways(leaf), leaf))
                        {
                            continue;
                        }

                        if (_model.States[leaf].IsFinal)
                        {
                            if (IsRootFinal(leaf))
                            {
                                _w.Line($"if (entered) Complete();");
                            }
                            else
                            {
                                var parent = _hierarchy.ParentOf(leaf);
                                var done = _resolver.ForDone(parent);
                                if (done.Count > 0)
                                {
                                    using (_w.Block("if (entered)"))
                                    {
                                        WriteSettleCandidates(done, leaf);
                                    }
                                }
                            }
                        }

                        _w.Line($"return default({ValueTaskType});");
                    }
                }

                _w.Line($"default: return default({ValueTaskType});");
            }
        }
    }

    /// <summary>Each candidate in turn; true when the last is unguarded, so the code after is unreachable.</summary>
    private bool WriteSettleCandidates(IReadOnlyList<TransitionModel> candidates, int leaf)
    {
        foreach (var candidate in candidates)
        {
            _transitions.Add((candidate.Index, leaf));
            var call = $"{TransitionName(candidate.Index, leaf)}()";
            if (candidate.IsGuarded)
            {
                _guards.Add((candidate.Index, leaf));
                _w.Line($"if ({GuardName(candidate.Index, leaf)}()) return {call};");
            }
            else
            {
                _w.Line($"return {call};");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The start's tail when the initial leaf has something to try. A start runs outside the pump, so it holds the pump
    /// (or a Checked machine's busy flag) for the chain, and lets waiting inputs run after.
    /// </summary>
    private void WriteStartSettle()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} StartSettle()"))
        {
            if (HasInbox)
            {
                _w.Line("lock (_sync) { _pumping = true; }");
            }
            else if (IsChecked)
            {
                _w.Line("global::System.Threading.Volatile.Write(ref _busy, 1);");
            }

            _w.Line("_inside = true;");
            _w.Line($"{ValueTaskType} settled;");
            _w.Line("try { settled = Settle(true); } catch { StartSettled(); throw; }");
            _w.Line("if (settled.IsCompletedSuccessfully) { StartSettled(); return settled; }");
            _w.Line("return StartSettleAsync(settled);");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} StartSettleAsync({ValueTaskType} settled)"))
        {
            _w.Line("try { await settled; } finally { StartSettled(); }");
        }

        _w.Line();
        using (_w.Block("private void StartSettled()"))
        {
            _w.Line("_inside = false;");
            if (HasInbox)
            {
                _w.Line("lock (_sync) { _pumping = false; }");
                _w.Line("PumpNow();");
            }
            else if (IsChecked)
            {
                _w.Line("global::System.Threading.Volatile.Write(ref _busy, 0);");
            }
        }
    }
}
