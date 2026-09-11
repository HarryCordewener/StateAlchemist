using System;

namespace StateAlchemist;

/// <summary>Includes a module's transitions, decisions and state actions in a machine.</summary>
/// <param name="module">A static class marked <see cref="ModuleAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IncludeAttribute(Type module) : Attribute
{
    /// <summary>The included module.</summary>
    public Type Module { get; } = module;
}
