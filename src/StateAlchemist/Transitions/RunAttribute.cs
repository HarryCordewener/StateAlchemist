using System;

namespace StateAlchemist;

/// <summary>
/// Marks a stay transition on <see cref="OnAnyAttribute"/> or a range whose <c>Transform</c> takes a whole run of
/// values as <c>ReadOnlySpan&lt;TValue&gt;</c>. The machine hands it every consecutive value up to the next one some
/// other transition handles, found with a vectorised scan (spec §6.7).
/// </summary>
/// <remarks>
/// The transform's parameters are exactly <c>(ref TState self, ReadOnlySpan&lt;TValue&gt; run)</c>, optionally
/// followed by <c>in TConfig</c> and the context.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RunAttribute : Attribute
{
}
