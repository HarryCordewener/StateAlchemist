using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace StateAlchemist.Hosting;

/// <summary>Registers a machine that runs for the lifetime of the host.</summary>
public static class HostedMachineServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TMachine"/> as a singleton, a <see cref="MachineInbox{TMachine,TInput}"/> of
    /// its values, and a <see cref="MachineHostedService{TMachine,TValue,TInput}"/> that fires each value written to
    /// the inbox.
    /// </summary>
    /// <typeparam name="TMachine">The machine.</typeparam>
    /// <typeparam name="TValue">The machine's value type, which is also what the inbox holds.</typeparam>
    /// <param name="services">The services.</param>
    /// <param name="factory">Creates the machine, without starting it. The host starts it.</param>
    /// <param name="capacity">The most values waiting at once, or <see langword="null"/> for no limit.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TMachine"/> is already registered.</exception>
    public static IServiceCollection AddHostedMachine<TMachine, TValue>(
        this IServiceCollection services,
        Func<IServiceProvider, TMachine> factory,
        int? capacity = null)
        where TMachine : class, IMachine<TValue>
        where TValue : struct =>
        services.AddHostedMachine<TMachine, TValue, TValue>(factory, static (machine, value) => machine.FireAsync(value), capacity);

    /// <summary>
    /// Registers <typeparamref name="TMachine"/> as a singleton, a <see cref="MachineInbox{TMachine,TInput}"/> of
    /// <typeparamref name="TInput"/>, and a <see cref="MachineHostedService{TMachine,TValue,TInput}"/> that hands
    /// each input written to the inbox to <paramref name="fire"/>.
    /// </summary>
    /// <typeparam name="TMachine">The machine.</typeparam>
    /// <typeparam name="TValue">The machine's value type.</typeparam>
    /// <typeparam name="TInput">What the inbox holds: an event, a batch of values, or a message to translate.</typeparam>
    /// <param name="services">The services.</param>
    /// <param name="factory">Creates the machine, without starting it. The host starts it.</param>
    /// <param name="fire">Fires one input, for example <c>(door, badge) =&gt; door.FireAsync(badge)</c>.</param>
    /// <param name="capacity">The most inputs waiting at once, or <see langword="null"/> for no limit.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TMachine"/> is already registered.</exception>
    public static IServiceCollection AddHostedMachine<TMachine, TValue, TInput>(
        this IServiceCollection services,
        Func<IServiceProvider, TMachine> factory,
        Func<TMachine, TInput, ValueTask> fire,
        int? capacity = null)
        where TMachine : class, IMachine<TValue>
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(fire);
        if (capacity is { } limit)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1, nameof(capacity));
        }

        // A second registration would run two services against whichever machine the container resolves last.
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(TMachine))
            {
                throw new InvalidOperationException(
                    $"{typeof(TMachine)} is already registered. A host runs one instance of each machine type.");
            }
        }

        services.AddSingleton(factory);
        services.AddSingleton(_ => capacity is { } c ? new MachineInbox<TMachine, TInput>(c) : new MachineInbox<TMachine, TInput>());
        services.AddHostedService(provider => new MachineHostedService<TMachine, TValue, TInput>(
            provider.GetRequiredService<TMachine>(),
            provider.GetRequiredService<MachineInbox<TMachine, TInput>>(),
            fire));
        return services;
    }
}
