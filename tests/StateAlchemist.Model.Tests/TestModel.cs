using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model.Tests;

/// <summary>Builds small models by hand, so each test states exactly the tree and transitions it is about.</summary>
internal sealed class TestModel
{
    private readonly List<StateModel> _states = [];
    private readonly List<TransitionModel> _transitions = [];
    private readonly List<StateActionModel> _actions = [];

    public ValueDomain Domain { get; set; } = ValueDomain.Byte;

    public PurityMode Purity { get; set; } = PurityMode.Permissive;

    public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Checked;

    public int Root(string name = "Root", bool hasData = false) => AddState(name, -1, false, hasData);

    public int State(string name, int parent, bool initial = false, bool hasData = false) => AddState(name, parent, initial, hasData);

    public string TypeOf(int state) => _states[state].TypeName;

    public ParameterModel In(int state) => new(_states[state].Name.ToLowerInvariant(), _states[state].TypeName, ParameterKind.State, Passing.In, state);

    public ParameterModel Copy(int state) => new(_states[state].Name.ToLowerInvariant(), _states[state].TypeName, ParameterKind.State, Passing.Value, state);

    public ParameterModel Ref(int state) => new(_states[state].Name.ToLowerInvariant(), _states[state].TypeName, ParameterKind.State, Passing.Ref, state);

    public static ParameterModel Outcome(string typeName) => new("outcome", typeName, ParameterKind.Outcome, Passing.Value);

    public static ParameterModel Value() => new("value", "System.Byte", ParameterKind.Value, Passing.Value);

    public static MethodModel Method(string declaringType, string name, ReturnShape returns, params ParameterModel[] parameters) =>
        new(declaringType, name, returns, IsStatic: true, IsPublic: true, parameters, SourceSpan.None);

    /// <summary>A method-form transition: <paramref name="to"/> −1 is a stay.</summary>
    public TransitionModel Add(string name, int from, int to, TriggerModel trigger, params ParameterModel[] transform) =>
        Add(name, from, to, trigger, guarded: false, order: 0, transform);

    public TransitionModel Add(string name, int from, int to, TriggerModel trigger, bool guarded, int order, params ParameterModel[] transform)
    {
        var transition = new TransitionModel(
            _transitions.Count, name, from, to, trigger, order, IsRun: false,
            guarded ? Method(name, "Guard", ReturnShape.Bool) : null,
            Method(name, "Transform", ReturnShape.Void, transform),
            [], null, [], "T.Module", SourceSpan.None);
        _transitions.Add(transition);
        return transition;
    }

    /// <summary>
    /// An async decision from <paramref name="from"/> on value 5 with outcomes <c>T.Accept</c> and <c>T.Reject</c>,
    /// completed to <paramref name="acceptTo"/> and <paramref name="rejectTo"/>.
    /// </summary>
    public TransitionModel Decision(string name, int from, int acceptTo, int rejectTo, ParameterModel[] decide, ParameterModel[] accept, ParameterModel[] reject, params MethodModel[] completed)
    {
        var decision = new DecisionModel(
            null,
            Method(name, "DecideAsync", ReturnShape.ValueTaskOfResult, decide),
            ["T.Accept", "T.Reject"],
            [
                new OutcomeCompletion("T.Accept", acceptTo, Method(name, "Complete", ReturnShape.Void, [.. accept, Outcome("T.Accept")])),
                new OutcomeCompletion("T.Reject", rejectTo, Method(name, "Complete", ReturnShape.Void, [.. reject, Outcome("T.Reject")])),
            ],
            []);
        return Add(new TransitionModel(0, name, from, -1, TriggerModel.Value(5), 0, IsRun: false, null, null, completed, decision, [], "T.Module", SourceSpan.None));
    }

    /// <summary>Adds a transition built by the caller, renumbered to its position.</summary>
    public TransitionModel Add(TransitionModel transition)
    {
        var numbered = transition with { Index = _transitions.Count };
        _transitions.Add(numbered);
        return numbered;
    }

    public void Action(ActionPhase phase, int state, string module, int order, params ParameterModel[] parameters) =>
        _actions.Add(new StateActionModel(phase, state, order, module, _actions.Count, Method(module, "On" + phase, ReturnShape.Void, parameters)));

    public void Replace(int state, StateModel replacement) => _states[state] = replacement;

    public MachineModel Build(string name = "TestMachine") =>
        new(name, new MachineOptions("System.Byte", Domain, Concurrency: Concurrency, Purity: Purity), _states.ToList(), _transitions.ToList(), _actions.ToList());

    private int AddState(string name, int parent, bool initial, bool hasData)
    {
        _states.Add(new StateModel(_states.Count, "T." + name, parent, initial, hasData, HasReset: false, IsPublicStruct: true, ParentMarkers: 1, SourceSpan.None));
        return _states.Count - 1;
    }
}
