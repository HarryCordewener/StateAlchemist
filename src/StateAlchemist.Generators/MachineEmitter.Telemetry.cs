using StateAlchemist.Model;

namespace StateAlchemist.Generators;

/// <summary>
/// <c>[Machine(Telemetry = true)]</c>: an <c>ActivitySource</c> and a <c>Meter</c>, both named <c>StateAlchemist</c>.
/// Each transition and each decision is an activity and a duration; completed transitions and unhandled triggers are
/// counted. A machine without the option gets none of this, and its generated code does not change.
/// </summary>
internal sealed partial class MachineEmitter
{
    private const string Diagnostics = "global::System.Diagnostics.";
    private const string Tag = "global::System.Collections.Generic.KeyValuePair<string, object>";

    private bool Telemetry => _machine.Telemetry;

    private string MachineName => _machine.Machine.ToDisplayString();

    private void WriteTelemetryStorage()
    {
        _w.Line();
        _w.Line($"private static readonly {Diagnostics}ActivitySource s_activitySource = new {Diagnostics}ActivitySource(\"StateAlchemist\");");
        _w.Line($"private static readonly {Diagnostics}Metrics.Meter s_meter = new {Diagnostics}Metrics.Meter(\"StateAlchemist\");");
        _w.Line($"private static readonly {Diagnostics}Metrics.Counter<long> s_transitions = s_meter.CreateCounter<long>(\"statealchemist.transitions\", \"{{transition}}\", \"Transitions completed.\");");
        _w.Line($"private static readonly {Diagnostics}Metrics.Counter<long> s_unhandled = s_meter.CreateCounter<long>(\"statealchemist.unhandled\", \"{{trigger}}\", \"Triggers nothing handled.\");");
        _w.Line($"private static readonly {Diagnostics}Metrics.Histogram<double> s_transitionDuration = s_meter.CreateHistogram<double>(\"statealchemist.transition.duration\", \"s\", \"How long a transition's steps took, its actions included.\");");
        _w.Line($"private static readonly {Diagnostics}Metrics.Histogram<double> s_decisionDuration = s_meter.CreateHistogram<double>(\"statealchemist.decision.duration\", \"s\", \"How long a decision took to choose its outcome.\");");
        _w.Line($"private static readonly {Tag} s_machineTag = new {Tag}(\"statealchemist.machine\", {Literal(MachineName)});");

        _w.Line();
        using (_w.Block($"private static {Diagnostics}Activity StartTransitionActivity(string transition, string from, string to)"))
        {
            _w.Line("var activity = s_activitySource.StartActivity(transition);");
            using (_w.Block("if (activity != null)"))
            {
                _w.Line($"activity.SetTag(\"statealchemist.machine\", {Literal(MachineName)});");
                _w.Line("activity.SetTag(\"statealchemist.transition\", transition);");
                _w.Line("activity.SetTag(\"statealchemist.from\", from);");
                _w.Line("activity.SetTag(\"statealchemist.to\", to);");
            }

            _w.Line("return activity;");
        }

        _w.Line();
        using (_w.Block($"private static {Diagnostics}Activity StartDecisionActivity(string decision)"))
        {
            _w.Line("var activity = s_activitySource.StartActivity(decision);");
            using (_w.Block("if (activity != null)"))
            {
                _w.Line($"activity.SetTag(\"statealchemist.machine\", {Literal(MachineName)});");
                _w.Line("activity.SetTag(\"statealchemist.decision\", decision);");
            }

            _w.Line("return activity;");
        }

        _w.Line();
        _w.Line("/// <summary>Ends a transition's or a decision's activity and records its duration, with <c>error.type</c> if it failed.</summary>");
        using (_w.Block($"private static void EndTelemetry({Diagnostics}Activity activity, {Diagnostics}Metrics.Histogram<double> duration, {Tag} name, long started, {Exception} failure)"))
        {
            using (_w.Block("if (duration.Enabled)"))
            {
                _w.Line($"var seconds = ({Diagnostics}Stopwatch.GetTimestamp() - started) / (double){Diagnostics}Stopwatch.Frequency;");
                _w.Line("if (failure == null) duration.Record(seconds, s_machineTag, name);");
                _w.Line($"else duration.Record(seconds, s_machineTag, name, new {Tag}(\"error.type\", failure.GetType().FullName));");
            }

            using (_w.Block("if (activity != null)"))
            {
                _w.Line($"if (failure != null) activity.SetStatus({Diagnostics}ActivityStatusCode.Error, failure.Message);");
                _w.Line("activity.Dispose();");
            }
        }
    }

    /// <summary>
    /// The method a transition's dispatch calls, which runs <paramref name="steps"/> inside the transition's activity
    /// and times it.
    /// </summary>
    private void WriteTelemetrySteps(string name, string steps, string parameters, string arguments, bool isAsync, TransitionModel transition, TransitionPath path)
    {
        _w.Line();
        if (isAsync)
        {
            AsyncMethod();
        }

        using (_w.Block($"private {(isAsync ? "async " : "")}{ValueTaskType} {name}({parameters})"))
        {
            _w.Line($"var activity = StartTransitionActivity({Literal(transition.Name)}, {Literal(_model.States[path.Leaf].Name)}, {Literal(_model.States[path.TargetLeaf].Name)});");
            _w.Line($"var started = {Diagnostics}Stopwatch.GetTimestamp();");
            _w.Line($"{Exception} failure = null;");
            _w.Line(isAsync ? $"try {{ await {steps}({arguments}); }}" : $"try {{ return {steps}({arguments}); }}");
            _w.Line($"catch ({Exception} exception) {{ failure = exception; throw; }}");
            _w.Line($"finally {{ EndTelemetry(activity, s_transitionDuration, {TransitionTag(transition)}, started, failure); }}");
        }
    }

    /// <summary>Starts a decision's activity and its clock, as <c>activity</c> and <c>started</c>.</summary>
    private void WriteDecisionTelemetryStart(TransitionModel decision)
    {
        _w.Line($"var activity = StartDecisionActivity({Literal(decision.Name)});");
        _w.Line($"var started = {Diagnostics}Stopwatch.GetTimestamp();");
    }

    /// <summary>Ends what <see cref="WriteDecisionTelemetryStart"/> started; <paramref name="failure"/> is the exception, or <c>null</c>.</summary>
    private void WriteDecisionTelemetryEnd(TransitionModel decision, string failure) =>
        _w.Line($"EndTelemetry(activity, s_decisionDuration, {DecisionTag(decision)}, started, {failure});");

    private void WriteTransitionCount(TransitionModel transition) =>
        _w.Line($"s_transitions.Add(1, s_machineTag, {TransitionTag(transition)});");

    private void WriteUnhandledCount() =>
        _w.Line("if (s_unhandled.Enabled) s_unhandled.Add(1, s_machineTag, " +
                $"new {Tag}(\"statealchemist.state\", TypeOf(_leaf).Name));");

    private static string TransitionTag(TransitionModel transition) => $"new {Tag}(\"statealchemist.transition\", {Literal(transition.Name)})";

    private static string DecisionTag(TransitionModel decision) => $"new {Tag}(\"statealchemist.decision\", {Literal(decision.Name)})";
}
