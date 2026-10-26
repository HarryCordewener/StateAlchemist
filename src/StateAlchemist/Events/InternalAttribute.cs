using System;

namespace StateAlchemist;

/// <summary>
/// Marks an event that only code inside the machine may raise, with <c>Enqueue</c>.
/// </summary>
/// <remarks>
/// A machine writes no <c>FireAsync</c> or <c>Fire</c> overload for an internal event, and its generic
/// <c>FireAsync&lt;TEvent&gt;</c> throws <see cref="InvalidOperationException"/> for one. Calling the generic overload
/// with an internal event is diagnostic <c>SALCH0602</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class InternalAttribute : Attribute
{
}
