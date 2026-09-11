namespace StateAlchemist;

/// <summary>
/// Marks a state struct as the root of a machine's state tree.
/// </summary>
/// <remarks>
/// The root is never exited, so its fields live as long as the machine: put data there that belongs to the
/// whole connection, such as a negotiated character set. A machine names its root with
/// <see cref="MachineAttribute.Root"/>.
/// </remarks>
public interface IRootState
{
}
