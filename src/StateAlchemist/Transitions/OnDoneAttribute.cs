using System;

namespace StateAlchemist;

/// <summary>
/// Fires the transition when its <c>From</c> state is done: when one of its <see cref="FinalAttribute"/> children is
/// entered, or when every machine it invokes (<see cref="InvokeAttribute"/>) has completed.
/// </summary>
/// <remarks>
/// It fires right after the transition that entered the final child, before the next trigger; for invoked machines,
/// as the last of them completes. A <c>From</c> state that has neither final children nor invoked machines is never
/// done (diagnostic <c>SALCH0111</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class OnDoneAttribute : Attribute
{
}
