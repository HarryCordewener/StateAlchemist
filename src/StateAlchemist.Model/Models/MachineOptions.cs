namespace StateAlchemist.Model;

/// <summary>A machine's declared options.</summary>
/// <param name="ValueType">The value type's full name.</param>
/// <param name="Domain">Its values.</param>
/// <param name="ValueTypeSupported">Whether it is an integral type or enum of 16 bits or fewer.</param>
/// <param name="ContextType">The context type's full name, if any.</param>
/// <param name="ConfigType">The configuration type's full name, if any.</param>
/// <param name="Concurrency">The concurrency mode.</param>
/// <param name="InboxCapacity">The serialized inbox's capacity; 0 is unbounded.</param>
/// <param name="Purity">The purity mode.</param>
/// <param name="Unhandled">The unhandled-trigger mode.</param>
public sealed record MachineOptions(
    string ValueType,
    ValueDomain Domain,
    bool ValueTypeSupported = true,
    string? ContextType = null,
    string? ConfigType = null,
    ConcurrencyMode Concurrency = ConcurrencyMode.Checked,
    int InboxCapacity = 0,
    PurityMode Purity = PurityMode.Permissive,
    UnhandledMode Unhandled = UnhandledMode.Ignore);
