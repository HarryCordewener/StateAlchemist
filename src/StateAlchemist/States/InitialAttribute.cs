using System;

namespace StateAlchemist;

/// <summary>
/// Marks the child a parent state enters first.
/// </summary>
/// <remarks>
/// The machine rests only in leaf states. Entering a state that has children continues down its
/// <c>[Initial]</c> children until it reaches a leaf, so every state with children in a machine marks exactly one
/// child <c>[Initial]</c> (diagnostic <c>SALCH0004</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class InitialAttribute : Attribute
{
}
