using System;

namespace StateAlchemist;

/// <summary>
/// Marks a leaf state as final: entering it completes its parent.
/// </summary>
/// <remarks>
/// A final state has no transitions of its own (diagnostic <c>SALCH0005</c>). Entering one fires the parent's
/// <see cref="OnDoneAttribute"/> transitions, right after the transition that entered it. Entering a final child of the
/// root completes the machine: its status becomes <see cref="MachineStatus.Completed"/>, its timers are cancelled, it
/// refuses further triggers, and <see cref="IStatechart{TValue}.Completion"/> completes.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class FinalAttribute : Attribute
{
}
