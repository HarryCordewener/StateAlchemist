using System;
using System.Collections.Generic;

namespace StateAlchemist.Contracts;

/// <summary>
/// A contract machine, described the way a <c>[Machine]</c> declaration would describe it. Each implementation under
/// test turns a shape into a machine: the reference interpreter by reflection, the generated test project by the
/// <c>[Machine]</c> class it declares for the same shape.
/// </summary>
/// <param name="Name">The machine's name.</param>
/// <param name="Root">The root state.</param>
/// <param name="Modules">The included modules, in order.</param>
/// <param name="Context">The context type.</param>
/// <param name="Concurrency">The concurrency mode.</param>
/// <param name="Purity">The purity mode.</param>
/// <param name="Unhandled">The unhandled-trigger mode.</param>
public sealed record MachineShape(
    string Name,
    Type Root,
    IReadOnlyList<Type> Modules,
    Type Context,
    Concurrency Concurrency = Concurrency.Checked,
    Purity Purity = Purity.Permissive,
    Unhandled Unhandled = Unhandled.Ignore);
