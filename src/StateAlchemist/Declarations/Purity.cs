namespace StateAlchemist;

/// <summary>Whether guards and transforms may read the implementer's context (decision D13).</summary>
public enum Purity
{
    /// <summary>Guards, transforms and completions may take the context; the definition records that they do.</summary>
    Permissive,

    /// <summary>
    /// Guards, transforms and completions may not take the context (diagnostic <c>SALCH0205</c>), so they depend only
    /// on state data, the trigger and the configuration.
    /// </summary>
    Strict,
}
