using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StateAlchemist.Hosting;

/// <summary>Registers a <see cref="MachineSet{TKey,TMachine,TValue}"/> that the host stops on shutdown.</summary>
public static class MachineSetServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="MachineSet{TKey,TMachine,TValue}"/> as a singleton, and a hosted service that stops
    /// every machine in it when the host stops.
    /// </summary>
    /// <typeparam name="TKey">What identifies a machine.</typeparam>
    /// <typeparam name="TMachine">The machine.</typeparam>
    /// <typeparam name="TValue">The machine's value type.</typeparam>
    /// <param name="services">The services.</param>
    /// <param name="factory">Creates the machine for a key, without starting it. The set starts it.</param>
    /// <param name="comparer">Compares keys; the default comparer when <see langword="null"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <exception cref="InvalidOperationException">A set of <typeparamref name="TMachine"/> by <typeparamref name="TKey"/> is already registered.</exception>
    public static IServiceCollection AddMachineSet<TKey, TMachine, TValue>(
        this IServiceCollection services,
        Func<IServiceProvider, TKey, TMachine> factory,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        where TMachine : class, IMachine<TValue>
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(MachineSet<TKey, TMachine, TValue>))
            {
                throw new InvalidOperationException($"{typeof(MachineSet<TKey, TMachine, TValue>)} is already registered.");
            }
        }

        services.AddSingleton(provider => new MachineSet<TKey, TMachine, TValue>(key => factory(provider, key), comparer));
        services.AddHostedService(provider => new MachineSetService<TKey, TMachine, TValue>(provider.GetRequiredService<MachineSet<TKey, TMachine, TValue>>()));
        return services;
    }

    /// <summary>Stops the set's machines when the host stops.</summary>
    private sealed class MachineSetService<TKey, TMachine, TValue>(MachineSet<TKey, TMachine, TValue> set) : IHostedService
        where TKey : notnull
        where TMachine : class, IMachine<TValue>
        where TValue : struct
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => set.StopAllAsync().AsTask();
    }
}
