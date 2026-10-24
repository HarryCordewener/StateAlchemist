using System;

namespace StateAlchemist;

/// <summary>
/// Fires the transition without a trigger: as soon as the source state is active and its guard passes.
/// </summary>
/// <remarks>
/// An eventless transition is tried right after every transition into, or within, its source state, and after
/// <c>StartAsync</c>, before the machine takes the next trigger. It must move to another state (diagnostic
/// <c>SALCH0110</c>), and unguarded eventless transitions may not form a cycle. A chain that keeps firing is stopped
/// after 64 steps with an <see cref="InvalidOperationException"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AlwaysAttribute : Attribute
{
}
