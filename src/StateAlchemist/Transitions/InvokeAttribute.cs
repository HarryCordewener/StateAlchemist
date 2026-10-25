using System;

namespace StateAlchemist;

/// <summary>
/// Runs a child machine while a state is active. On a module method that creates the child: the parent calls it when
/// it enters <see cref="State"/>, starts the machine it returns, and stops that machine when it leaves the state.
/// </summary>
/// <remarks>
/// <para>
/// The method is <c>public static</c> and returns a type that implements <see cref="IStatechart{TValue}"/>. It may take
/// the active states (<c>in</c>), the configuration, and the context, as an <c>[Entered]</c> action does.
/// </para>
/// <para>
/// When the child completes (<see cref="MachineStatus.Completed"/>), the parent learns of it through its inbox, as it
/// does a timer. Once every machine a state invokes has completed, the state is done, and its
/// <see cref="OnDoneAttribute"/> transitions fire. Several <c>[Invoke]</c> methods on one state run side by side:
/// that is how a machine runs parallel regions.
/// </para>
/// <para>
/// With <see cref="Forward"/>, a trigger the parent does not handle while the state is active goes to the child:
/// events always, values when the child's value type is the parent's.
/// </para>
/// </remarks>
/// <param name="state">The state that runs the child.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class InvokeAttribute(Type state) : Attribute
{
    /// <summary>The state that runs the child.</summary>
    public Type State { get; } = state;

    /// <summary>Whether the triggers the parent does not handle go to the child.</summary>
    public bool Forward { get; set; }
}
