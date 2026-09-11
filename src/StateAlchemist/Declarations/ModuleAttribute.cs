using System;

namespace StateAlchemist;

/// <summary>
/// Marks a static class that groups transitions, decisions and state actions. A machine includes modules with
/// <see cref="IncludeAttribute"/>; a protocol plugin is typically one module.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModuleAttribute : Attribute
{
}
