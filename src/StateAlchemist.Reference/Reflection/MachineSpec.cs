using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StateAlchemist.Reference;

/// <summary>What a machine is made of: the same facts a <c>[Machine]</c> declaration states.</summary>
/// <param name="Name">The machine's name.</param>
/// <param name="Root">The root state, or <see langword="null"/> when the declaration omits it.</param>
/// <param name="Value">The value type, or <see langword="null"/> when the declaration omits it.</param>
/// <param name="Modules">The included modules, in order.</param>
/// <param name="Context">The context type, if any.</param>
/// <param name="Config">The configuration type, if any.</param>
/// <param name="Concurrency">The concurrency mode.</param>
/// <param name="InboxCapacity">The serialized inbox's capacity.</param>
/// <param name="Purity">The purity mode.</param>
/// <param name="Unhandled">The unhandled-trigger mode.</param>
public sealed record MachineSpec(
    string Name,
    Type? Root,
    Type? Value,
    IReadOnlyList<Type> Modules,
    Type? Context = null,
    Type? Config = null,
    Concurrency Concurrency = Concurrency.Checked,
    int InboxCapacity = 0,
    Purity Purity = Purity.Permissive,
    Unhandled Unhandled = Unhandled.Ignore)
{
    /// <summary>Reads a <c>[Machine]</c> class's attributes.</summary>
    /// <exception cref="ArgumentException">The class has no <c>[Machine]</c> attribute.</exception>
    public static MachineSpec From(Type machineType)
    {
        var machine = machineType.GetCustomAttribute<MachineAttribute>()
            ?? throw new ArgumentException($"'{machineType.Name}' is not marked [Machine].", nameof(machineType));
        return new MachineSpec(
            machineType.Name,
            machine.Root,
            machine.Value,
            machineType.GetCustomAttributes<IncludeAttribute>().Select(i => i.Module).ToList(),
            machine.Context,
            machine.Config,
            machine.Concurrency,
            machine.InboxCapacity,
            machine.Purity,
            machine.Unhandled);
    }
}
