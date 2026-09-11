using System;

namespace StateAlchemist.Model;

/// <summary>What a method parameter binds to.</summary>
public enum ParameterKind
{
    State,
    Value,
    Run,
    RunMemory,
    Event,
    Config,
    Context,
    Outcome,
    CancellationToken,
    TransitionInfo,
    Unknown,
}

/// <summary>How a parameter is passed.</summary>
public enum Passing
{
    Value,
    In,
    Ref,
    Out,
}

/// <summary>What a method returns, as far as the rules care.</summary>
public enum ReturnShape
{
    Void,
    Bool,
    ValueTask,
    ValueTaskOfResult,
    Task,
    TaskOfResult,
    Other,
}

/// <summary>How a trigger matches.</summary>
public enum MatchKind
{
    Value,
    Range,
    Any,
    Event,
}

/// <summary>What a transition does to the active path.</summary>
public enum MoveKind
{
    Stay,
    Move,
    Reenter,
}

/// <summary>When a state action runs.</summary>
public enum ActionPhase
{
    Exited,
    Entered,
}

/// <summary>The machine's concurrency mode (spec §6.10).</summary>
public enum ConcurrencyMode
{
    Checked,
    Unchecked,
    Serialized,
}

/// <summary>Whether guards and transforms may take the context (decision D13).</summary>
public enum PurityMode
{
    Permissive,
    Strict,
}

/// <summary>What the machine does with a trigger nothing handles (spec §6.8).</summary>
public enum UnhandledMode
{
    Ignore,
    Throw,
}

/// <summary>A state's role in one transition, relative to the lowest common ancestor (spec §6.3).</summary>
[Flags]
public enum Role
{
    None = 0,
    Exiting = 1,
    Staying = 2,
    Entering = 4,
}
