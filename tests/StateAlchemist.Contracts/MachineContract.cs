using System.Threading.Tasks;

namespace StateAlchemist.Contracts;

/// <summary>
/// The base of every contract. An implementation's test project derives from each contract with
/// <c>[InheritsTests]</c> and says how to build a machine; every test is then written against
/// <see cref="IMachine{TValue}"/> alone.
/// </summary>
public abstract class MachineContract
{
    /// <summary>Builds a machine of <paramref name="shape"/>, not yet started.</summary>
    /// <param name="shape">The machine.</param>
    /// <param name="context">Its context.</param>
    /// <param name="hooks">Its hooks, or <see langword="null"/> for none implemented.</param>
    protected abstract IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks);

    /// <summary>Builds and starts a machine.</summary>
    protected async Task<IMachine<byte>> StartAsync(MachineShape shape, object context, ContractHooks? hooks = null)
    {
        var machine = Create(shape, context, hooks);
        if (hooks is not null)
        {
            hooks.Machine = machine;
        }

        if (context is Machines.RecordingContext recording)
        {
            recording.Machine = machine;
        }

        await machine.StartAsync();
        return machine;
    }
}
