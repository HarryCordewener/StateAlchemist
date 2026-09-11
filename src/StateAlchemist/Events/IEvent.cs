namespace StateAlchemist;

/// <summary>
/// Marks a struct as an event trigger: a trigger with its own typed payload, such as <c>Error</c> or <c>Timeout</c>.
/// </summary>
/// <remarks>
/// Values (bytes, characters, small enums) are matched with a <c>switch</c>; events are matched by type.
/// <c>[OnAny]</c> never matches an event.
/// </remarks>
public interface IEvent
{
}
