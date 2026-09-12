using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StateAlchemist.Reference;

/// <summary>A module an assembly offers with <c>[assembly: ExportsModule]</c> (D25).</summary>
/// <param name="Assembly">The assembly that exported it, for the diagnostic when it is not a module.</param>
/// <param name="Module">The exported type.</param>
public sealed record ExportedModule(string Assembly, Type Module);

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
/// <param name="Exported">
/// What this assembly and its references export, when the machine asked for it with <c>[IncludeExported]</c>;
/// <see langword="null"/> when it did not ask, and empty when it asked and nothing was offered.
/// </param>
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
    Unhandled Unhandled = Unhandled.Ignore,
    IReadOnlyList<ExportedModule>? Exported = null)
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
            machine.Unhandled,
            ExportedFor(machineType));
    }

    /// <summary>
    /// What the machine's own assembly and its references export, or <see langword="null"/> when the machine did
    /// not ask. A reference that is not loadable at runtime cannot have offered anything this application uses.
    /// </summary>
    private static IReadOnlyList<ExportedModule>? ExportedFor(Type machineType)
    {
        if (machineType.GetCustomAttribute<IncludeExportedAttribute>() is not { } asked)
        {
            return null;
        }

        var except = new HashSet<Type>(asked.Except);
        var exported = new List<ExportedModule>();
        foreach (var assembly in Assemblies(machineType.Assembly))
        {
            foreach (var export in assembly.GetCustomAttributes<ExportsModuleAttribute>())
            {
                if (!except.Contains(export.Module))
                {
                    exported.Add(new ExportedModule(assembly.GetName().Name ?? assembly.FullName ?? "?", export.Module));
                }
            }
        }

        return exported;
    }

    private static IEnumerable<Assembly> Assemblies(Assembly machine)
    {
        yield return machine;
        foreach (var reference in machine.GetReferencedAssemblies())
        {
            Assembly? loaded = null;
            try
            {
                loaded = Assembly.Load(reference);
            }
            catch (Exception exception) when (exception is BadImageFormatException or System.IO.FileNotFoundException or System.IO.FileLoadException)
            {
            }

            if (loaded is not null)
            {
                yield return loaded;
            }
        }
    }
}
