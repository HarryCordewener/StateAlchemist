using System;

namespace StateAlchemist;

/// <summary>
/// Offers a module to any machine that asks for what its references export: a library says this once, in its own
/// assembly, and an application takes it with <see cref="IncludeExportedAttribute"/>. Both ends opt in, so a
/// library's modules never arrive in a machine that did not ask, and a machine never picks up a module the library
/// meant to keep to itself.
/// </summary>
/// <param name="module">A static class marked <see cref="ModuleAttribute"/>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ExportsModuleAttribute(Type module) : Attribute
{
    /// <summary>The exported module.</summary>
    public Type Module { get; } = module;
}
