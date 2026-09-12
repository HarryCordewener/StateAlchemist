using System;

namespace StateAlchemist;

/// <summary>
/// Includes every module that this assembly and its references export with
/// <see cref="ExportsModuleAttribute"/> — so adding a protocol is adding a package reference. Modules named by
/// <see cref="IncludeAttribute"/> are included as well; a module named twice is included once.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IncludeExportedAttribute : Attribute
{
    /// <summary>Exported modules to leave out, for a machine that wants all but a few.</summary>
    public Type[] Except { get; set; } =
#if NET8_0_OR_GREATER
        [];
#else
        Array.Empty<Type>();
#endif
}
