using System;

namespace StateAlchemist;

/// <summary>
/// Declares a machine on a <c>partial class</c>. The source generator fills the class in with the machine's
/// storage, dispatch, plan layer and definition, built from the modules named by <see cref="IncludeAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// [Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
/// [Include(typeof(TelnetCore)), Include(typeof(NawsModule))]
/// public sealed partial class MudTelnet;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MachineAttribute : Attribute
{
    /// <summary>The root state: a struct implementing <see cref="IRootState"/>. Required.</summary>
    public Type? Root { get; set; }

    /// <summary>The value-trigger type: an integral type or an enum of 16 bits or fewer, such as <see cref="byte"/>. Required.</summary>
    public Type? Value { get; set; }

    /// <summary>The implementer's context passed to actions and decisions, or <see langword="null"/> for none.</summary>
    public Type? Context { get; set; }

    /// <summary>The machine's immutable configuration passed to transforms as <c>in TConfig</c>, or <see langword="null"/> for none.</summary>
    public Type? Config { get; set; }

    /// <summary>How the machine guards against concurrent callers. Defaults to <see cref="StateAlchemist.Concurrency.Checked"/>.</summary>
    public Concurrency Concurrency { get; set; } = Concurrency.Checked;

    /// <summary>For <see cref="StateAlchemist.Concurrency.Serialized"/>: the inbox capacity, or 0 for an unbounded inbox.</summary>
    public int InboxCapacity { get; set; }

    /// <summary>Whether guards and transforms may take the context. Defaults to <see cref="StateAlchemist.Purity.Permissive"/>.</summary>
    public Purity Purity { get; set; } = Purity.Permissive;

    /// <summary>What to do with a trigger nothing handles. Defaults to <see cref="StateAlchemist.Unhandled.Ignore"/>.</summary>
    public Unhandled Unhandled { get; set; } = Unhandled.Ignore;
}
