using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// Invoked machines ([Invoke]), written only into machines that invoke one; such a machine has an inbox. A transition
// stops the children of the states it exits at its commit, before the Exited actions, and starts the children of the
// states it enters after its actions, before OnTransitioned. A child that completes submits an input, as a timer does;
// once every child of a state has, the state's done transitions are tried. With Forward, what the parent leaves
// unhandled goes to its running children. ReferenceMachine.Invoke.cs states the same rules.
internal sealed partial class MachineEmitter
{
    private const int ChildTag = -4;

    private bool HasInvokes => _model.Invokes.Count > 0;

    /// <summary>Whether inputs the machine submits to itself — timer firings, child completions — exist: they take no room.</summary>
    private bool HasOwnInputs => HasTimers || HasInvokes;

    /// <summary>Whether an input is one the machine submitted to itself.</summary>
    private string IsOwnInput(string input) => HasInvokes ? $"{input}.Tag < -2" : $"{input}.Tag == {TimerTag}";

    private IEnumerable<int> InvokesOf(int state) => Enumerable.Range(0, _model.Invokes.Count).Where(k => _model.Invokes[k].State == state);

    private bool Invokes(int state) => _model.Invokes.Any(i => i.State == state);

    /// <summary>The states whose children start or stop along <paramref name="states"/>, in its order.</summary>
    private List<int> Invoking(IEnumerable<int> states) => states.Where(Invokes).ToList();

    private ITypeSymbol ChildValue(int k) => SymbolModelBuilder.ChildValueSymbol(_machine.Methods[_model.Invokes[k].Method].ReturnType)!;

    private string ChildType(int k) => $"{Rt}IStatechart<{Name(ChildValue(k))}>";

    private List<int> Forwarding => Enumerable.Range(0, _model.Invokes.Count).Where(k => _model.Invokes[k].Forward).ToList();

    /// <summary>The forwarding children that take the parent's values: their value type is the parent's.</summary>
    private List<int> ForwardingValues => Forwarding.Where(k => SymbolEqualityComparer.Default.Equals(ChildValue(k), _machine.Value)).ToList();

    private void WriteInvokeStorage()
    {
        for (var k = 0; k < _model.Invokes.Count; k++)
        {
            _w.Line($"private {ChildType(k)} _child{k};");
            _w.Line($"private int _childGeneration{k};");
            _w.Line($"private bool _childDone{k};");
        }
    }

    private void WriteInvokes()
    {
        if (!HasInvokes)
        {
            return;
        }

        foreach (var state in Enumerable.Range(0, _model.States.Count).Where(Invokes))
        {
            WriteStartChildren(state);
            WriteStopChildren($"StopChildren{_stateIds[state]}", InvokesOf(state).Reverse());
            WriteChildrenDone(state);
        }

        WriteStopChildren("StopAllChildren", Enumerable.Range(0, _model.Invokes.Count).Reverse());
        for (var k = 0; k < _model.Invokes.Count; k++)
        {
            _w.Line();
            _w.Line("/// <summary>Learns when the child ends; only a completion, while the child is still the current one, is submitted.</summary>");
            using (_w.Block($"private void WatchChild{k}({ChildType(k)} child, int generation)"))
            {
                _w.Line("var completion = child.Completion;");
                _w.Line($"if (completion.IsCompleted) {{ ChildEnded{k}(child, generation); return; }}");
                _w.Line($"completion.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() => ChildEnded{k}(child, generation));");
            }

            _w.Line();
            _w.Line($"private void ChildEnded{k}({ChildType(k)} child, int generation) {{ if (child.Status == {Rt}MachineStatus.Completed) SubmitChild({k}, generation); }}");
        }

        WriteSubmitChild();
        WriteDispatchChild();
        WritePauseChildren();
        WriteForwarding();
    }

    private void WriteStartChildren(int state)
    {
        _w.Line();
        _w.Line($"/// <summary>Creates and starts the machines <see cref=\"{S(state)}\"/> invokes.</summary>");
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} StartChildren{_stateIds[state]}()"))
        {
            foreach (var k in InvokesOf(state))
            {
                var method = _model.Invokes[k].Method;
                var arguments = string.Join(", ", method.Parameters.Select(p => p.Kind switch
                {
                    ParameterKind.State => (p.Passing == Passing.In ? "in " : string.Empty) + Field(p.State),
                    ParameterKind.Context => "_context",
                    ParameterKind.Config => (p.Passing == Passing.In ? "in " : string.Empty) + "_config",
                    ParameterKind.CancellationToken => "LifetimeToken",
                    _ => "default",
                }));
                _w.Line($"{ChildType(k)} child{k} = {Owner(method)}({arguments});");
                _w.Line($"if (child{k} == null) throw new global::System.InvalidOperationException({Literal($"[Invoke] '{method.FullName}' returned null.")});");
                _w.Line($"_child{k} = child{k}; _childDone{k} = false; var generation{k} = ++_childGeneration{k};");
                _w.Line($"await child{k}.StartAsync();");
                _w.Line($"WatchChild{k}(child{k}, generation{k});");
            }
        }
    }

    private void WriteStopChildren(string name, IEnumerable<int> invokes)
    {
        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} {name}()"))
        {
            foreach (var k in invokes)
            {
                _w.Line($"var child{k} = _child{k}; _child{k} = null; _childGeneration{k}++;");
                _w.Line($"if (child{k} != null) await child{k}.StopAsync();");
            }
        }
    }

    /// <summary>A child of <paramref name="state"/> completed: once all have, the state's done transitions are tried from the active leaf.</summary>
    private void WriteChildrenDone(int state)
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} ChildrenDone{_stateIds[state]}()"))
        {
            _w.Line($"if (!({string.Join(" && ", InvokesOf(state).Select(k => $"_childDone{k}"))})) return default({ValueTaskType});");
            var done = _resolver.ForDone(state);
            if (done.Count > 0)
            {
                using (_w.Block("switch (_leaf)"))
                {
                    foreach (var leaf in _hierarchy.LeavesUnder(state))
                    {
                        using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                        {
                            if (!WriteSettleCandidates(done, leaf))
                            {
                                _w.Line($"return default({ValueTaskType});");
                            }
                        }
                    }
                }
            }

            _w.Line($"return default({ValueTaskType});");
        }
    }

    // A completion joins the inbox as a timer's firing does: it holds no busy flag and takes no room. A paused parent
    // keeps it for when it resumes.
    private void WriteSubmitChild()
    {
        _w.Line();
        using (_w.Block("private void SubmitChild(int child, int generation)"))
        {
            _w.Line("var input = Rent();");
            _w.Line($"input.Tag = {ChildTag};");
            _w.Line("input.Child = child;");
            _w.Line("input.ChildGeneration = generation;");
            if (!Bounded)
            {
                _w.Line("var inline = false;");
            }

            _w.Line("var stopped = false;");
            using (_w.Block("lock (_sync)"))
            {
                _w.Line($"if (_status != {Rt}MachineStatus.Running && _status != {Rt}MachineStatus.Paused) stopped = true;");
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
                        var idle = $"_status == {Rt}MachineStatus.Running && !_pumping && _current == null && _inbox.Count == 0 && _queued.Count == 0" + (Deciding ? " && _pending == null" : "");
                        _w.Line($"if ({idle}) {{ _pumping = true; _current = input; inline = true; }} else _inbox.Add(input);");
                    }
                }
            }

            _w.Line("if (stopped) { Return(input); return; }");
            _w.Line(Bounded ? "PumpNow();" : "if (inline) RunInline(input); else PumpNow();");
        }
    }

    private void WriteDispatchChild()
    {
        _w.Line();
        _w.Line("/// <summary>A child's completion, if it is still the current child: it is done, and so, perhaps, is its state.</summary>");
        using (_w.Block($"private {ValueTaskType} DispatchChild(int child, int generation)"))
        {
            WriteCompletedGuard();
            using (_w.Block("switch (child)"))
            {
                for (var k = 0; k < _model.Invokes.Count; k++)
                {
                    _w.Line($"case {k}: if (_child{k} == null || _childGeneration{k} != generation) return default({ValueTaskType}); _childDone{k} = true; return ChildrenDone{_stateIds[_model.Invokes[k].State]}();");
                }

                _w.Line($"default: return default({ValueTaskType});");
            }
        }
    }

    private void WritePauseChildren()
    {
        _w.Line();
        using (_w.Block("private void PauseChildren()"))
        {
            for (var k = 0; k < _model.Invokes.Count; k++)
            {
                _w.Line($"var child{k} = _child{k}; if (child{k} != null && child{k}.Status == {Rt}MachineStatus.Running) child{k}.Pause();");
            }
        }

        _w.Line();
        using (_w.Block("private void ResumeChildren()"))
        {
            for (var k = 0; k < _model.Invokes.Count; k++)
            {
                _w.Line($"var child{k} = _child{k}; if (child{k} != null && child{k}.Status == {Rt}MachineStatus.Paused) child{k}.Resume();");
            }
        }
    }

    /// <summary>What the steps of a transition do at its commit: stop the children of the states it exits, leaf first.</summary>
    private void WriteStopping(TransitionPath path)
    {
        foreach (var state in Invoking(path.Exiting))
        {
            _w.Line($"await StopChildren{_stateIds[state]}();");
        }
    }

    /// <summary>What the steps of a transition do after its actions: start the children of the states it enters, outermost first.</summary>
    private void WriteStarting(IEnumerable<int> entering)
    {
        foreach (var state in Invoking(entering))
        {
            _w.Line($"await StartChildren{_stateIds[state]}();");
        }
    }

    private bool StartsOrStopsChildren(TransitionPath path) => HasInvokes && (path.Exiting.Any(Invokes) || path.Entering.Any(Invokes));

    // Forwarding: a trigger the parent leaves unhandled goes to each running forwarding child, in declaration order,
    // instead of being reported. An event type the parent does not know is carried to the child by a Forwarded<TEvent>.
    private bool HasForwarding => Forwarding.Count > 0;

    private string Running(int k) => $"(_child{k} != null && _child{k}.Status == {Rt}MachineStatus.Running)";

    private void WriteForwarding()
    {
        var forwarding = Forwarding;
        if (forwarding.Count == 0)
        {
            return;
        }

        _w.Line();
        _w.Line($"private bool Forwards() {{ return {string.Join(" || ", forwarding.Select(Running))}; }}");

        var values = ForwardingValues;
        if (values.Count > 0)
        {
            _w.Line();
            _w.Line($"private bool ForwardsValues() {{ return {string.Join(" || ", values.Select(Running))}; }}");
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} ForwardValue({V} value)"))
            {
                foreach (var k in values)
                {
                    _w.Line($"var child{k} = _child{k}; if (child{k} != null && child{k}.Status == {Rt}MachineStatus.Running) await child{k}.FireAsync(value);");
                }
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            _w.Line();
            AsyncMethod();
            using (_w.Block($"private async {ValueTaskType} ForwardEvent{i}({Name(_events[i])} e)"))
            {
                foreach (var k in forwarding)
                {
                    _w.Line($"var child{k} = _child{k}; if (child{k} != null && child{k}.Status == {Rt}MachineStatus.Running) await child{k}.FireAsync(e);");
                }
            }
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} ForwardUnknown({TypeType} type, Forwarded forwarded)"))
        {
            _w.Line("if (forwarded != null && Forwards()) return ForwardUnknownAsync(forwarded);");
            _w.Line("UnhandledUnknown(type);");
            _w.Line($"return default({ValueTaskType});");
        }

        _w.Line();
        AsyncMethod();
        using (_w.Block($"private async {ValueTaskType} ForwardUnknownAsync(Forwarded forwarded)"))
        {
            foreach (var k in forwarding)
            {
                _w.Line($"var child{k} = _child{k}; if (child{k} != null && child{k}.Status == {Rt}MachineStatus.Running) await forwarded.To{k}(child{k});");
            }
        }

        _w.Line();
        _w.Line("/// <summary>An event of a type the machine does not know, kept with its type so a child can be handed it.</summary>");
        using (_w.Block("private abstract class Forwarded"))
        {
            foreach (var k in forwarding)
            {
                _w.Line($"public abstract {ValueTaskType} To{k}({ChildType(k)} child);");
            }
        }

        _w.Line();
        using (_w.Block($"private sealed class Forwarded<TEvent> : Forwarded where TEvent : struct, {Rt}IEvent"))
        {
            _w.Line("public TEvent E;");
            foreach (var k in forwarding)
            {
                _w.Line($"public override {ValueTaskType} To{k}({ChildType(k)} child) {{ return child.FireAsync(E); }}");
            }
        }
    }
}
