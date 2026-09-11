using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary><c>StartAsync</c>, <c>StopAsync</c> and <c>DisposeAsync</c> (spec D22), and the lifetime token actions may take.</summary>
    private void WriteLifecycle()
    {
        var initialPath = _hierarchy.PathFromRoot(_hierarchy.InitialLeaf(_hierarchy.Root));
        var starting = initialPath.SelectMany(s => StateActions(ActionPhase.Entered, s).Select(m => new TransitionAction(m, Use.Entered, "Entered", s))).ToList();

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} StartAsync()"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.NotStarted) return Faulted(new global::System.InvalidOperationException(\"The machine has already been started.\"));");
            _w.Line("return Start();");
        }

        WriteLifecycleActions("Start", starting, $"_status = {Rt}MachineStatus.Running;", null);

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {ValueTaskType} StopAsync()"))
        {
            _w.Line($"if (_status != {Rt}MachineStatus.Running) {{ _status = {Rt}MachineStatus.Stopped; return default({ValueTaskType}); }}");
            _w.Line($"_status = {Rt}MachineStatus.Stopped;");
            _w.Line("if (_lifetime != null) _lifetime.Cancel();");
            _w.Line("return Stop();");
        }

        var stopping = _hierarchy.Leaves.ToDictionary(
            leaf => leaf,
            leaf => _hierarchy.PathFromRoot(leaf).Reverse()
                .SelectMany(s => StateActions(ActionPhase.Exited, s).Select(m => new TransitionAction(m, Use.Exited, "Exited", s))).ToList());
        WriteLifecycleActions("Stop", stopping.Values.SelectMany(a => a).ToList(), null, stopping);

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        _w.Line($"public {ValueTaskType} DisposeAsync() {{ return StopAsync(); }}");

        _w.Line();
        using (_w.Block("private global::System.Threading.CancellationToken LifetimeToken"))
        {
            using (_w.Block("get"))
            {
                _w.Line($"if (_status == {Rt}MachineStatus.Stopped) return new global::System.Threading.CancellationToken(true);");
                _w.Line("if (_lifetime == null) _lifetime = new global::System.Threading.CancellationTokenSource();");
                _w.Line("return _lifetime.Token;");
            }
        }
    }

    /// <summary>
    /// A lifecycle's actions, run inside the machine so they may <c>Enqueue</c>. Their exception hooks apply as in a
    /// transition; <c>Skip</c> skips the rest.
    /// </summary>
    private void WriteLifecycleActions(string name, List<TransitionAction> all, string? after, Dictionary<int, List<TransitionAction>>? byLeaf)
    {
        var isAsync = all.Any(a => a.Method.IsAsync);
        var skip = all.Any(a => Implements($"On{a.Phase}Exception"));
        _w.Line();
        using (_w.Block($"private {(isAsync ? "async " : "")}{ValueTaskType} {name}()"))
        {
            if (all.Count > 0)
            {
                _w.Line("_inside = true;");
                using (_w.Block("try"))
                {
                    if (byLeaf is null)
                    {
                        foreach (var action in all)
                        {
                            WriteLifecycleAction(action);
                        }
                    }
                    else
                    {
                        using (_w.Block("switch (_leaf)"))
                        {
                            foreach (var pair in byLeaf.Where(p => p.Value.Count > 0))
                            {
                                _w.Line($"case StateId.{_stateIds[pair.Key]}:");
                                foreach (var action in pair.Value)
                                {
                                    WriteLifecycleAction(action);
                                }

                                _w.Line("break;");
                            }
                        }
                    }
                }

                _w.Line("finally { _inside = false; }");
                if (skip)
                {
                    _w.Line("skipped:");
                }
            }

            if (after is not null)
            {
                _w.Line(after);
            }

            _w.Line(isAsync ? "return;" : $"return default({ValueTaskType});");
        }
    }

    private void WriteLifecycleAction(TransitionAction action)
    {
        var arguments = string.Join(", ", action.Method.Parameters.Select(p => p.Kind switch
        {
            ParameterKind.State => (p.Passing == Passing.In ? "in " : string.Empty) + Field(p.State),
            ParameterKind.Context => "_context",
            ParameterKind.Config => (p.Passing == Passing.In ? "in " : string.Empty) + "_config",
            ParameterKind.CancellationToken => "LifetimeToken",
            ParameterKind.TransitionInfo => LifecycleInfo(action),
            _ => "default",
        }));
        var call = $"{Owner(action.Method)}({arguments})";
        var statement = action.Method.IsAsync ? $"await {call};" : $"{call};";
        if (!Implements($"On{action.Phase}Exception"))
        {
            _w.Line(statement);
            return;
        }

        _w.Line($"try {{ {statement} }}");
        using (_w.Block($"catch ({Exception} exception)"))
        {
            _w.Line($"var resolution = {Rt}ExceptionResolution.Rethrow;");
            _w.Line($"On{action.Phase}Exception(exception, {LifecycleInfo(action)}, ref resolution);");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Skip) goto skipped;");
            _w.Line($"if (resolution == {Rt}ExceptionResolution.Rethrow) throw;");
        }
    }

    private string LifecycleInfo(TransitionAction action) =>
        $"new {Rt}TransitionInfo<{V}>(\"(lifecycle)\", typeof({S(_hierarchy.Root)}), StateType, StateType, {Rt}TransitionKind.Stay, {Rt}Phase.{action.Phase}, default({V}), false, null, typeof({S(action.State)}))";
}
