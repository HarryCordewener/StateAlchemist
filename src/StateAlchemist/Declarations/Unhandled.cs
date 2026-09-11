namespace StateAlchemist;

/// <summary>What a machine does with a trigger that matches no transition at any level (spec §6.8).</summary>
public enum Unhandled
{
    /// <summary>Call the <c>OnUnhandled</c> hook, if implemented, and ignore the trigger.</summary>
    Ignore,

    /// <summary>Call the <c>OnUnhandled</c> hook, if implemented, then throw <c>UnhandledTriggerException</c>.</summary>
    Throw,
}
