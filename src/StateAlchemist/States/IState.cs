namespace StateAlchemist;

/// <summary>
/// Marks a state struct as a child of <typeparamref name="TParent"/> in a machine's state tree.
/// </summary>
/// <typeparam name="TParent">The parent state: another state struct, or the root.</typeparam>
/// <remarks>
/// A state's fields are its data. They are reset when the machine enters the state and cleared when it leaves,
/// so data that must outlive a state belongs in one of its ancestors. A state may declare one parent only.
/// </remarks>
public interface IState<TParent>
    where TParent : struct
{
}
