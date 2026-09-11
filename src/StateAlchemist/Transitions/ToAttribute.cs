using System;

namespace StateAlchemist;

/// <summary>Names the target state of a decision's <c>Complete</c> overload.</summary>
/// <param name="target">The state this outcome moves to.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToAttribute(Type target) : Attribute
{
    /// <summary>The target state.</summary>
    public Type Target { get; } = target;
}
