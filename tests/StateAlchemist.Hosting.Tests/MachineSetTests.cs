using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TUnit.Core;

namespace StateAlchemist.Hosting.Tests;

/// <summary>Machines by key: started on first use, stopped when removed, completed, or the host stops.</summary>
public class MachineSetTests
{
    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold within 10 seconds.");
            }

            await Task.Delay(5);
        }
    }

    [Test]
    public async Task EachKeyGetsOneMachineStartedOnce()
    {
        var created = 0;
        var log = new List<string>();
        await using var set = new MachineSet<string, Session, byte>(_ =>
        {
            System.Threading.Interlocked.Increment(ref created);
            return new Session(log);
        });

        var machines = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => set.GetOrStartAsync("a").AsTask()));
        var other = await set.GetOrStartAsync("b");

        await Assert.That(machines.Distinct().Count()).IsEqualTo(1);
        await Assert.That(other).IsNotSameReferenceAs(machines[0]);
        await Assert.That(created).IsEqualTo(2);
        await Assert.That(machines[0].Status).IsEqualTo(MachineStatus.Running);
        await Assert.That(set.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FiringGoesToTheKeysMachine()
    {
        await using var set = new MachineSet<int, Session, byte>(_ => new Session([]));
        await set.FireAsync(1, 2);
        await set.FireAsync(1, 2);
        await set.FireAsync(2, 2);

        await Assert.That(set.TryGet(1, out var one)).IsTrue();
        await Assert.That(one!.TryGetState(out SessionOpen open) ? open.Hits : -1).IsEqualTo(2);
        await Assert.That(set.TryGet(2, out var two) && two.TryGetState(out SessionOpen other) ? other.Hits : -1).IsEqualTo(1);
    }

    [Test]
    public async Task RemovingAKeyStopsItsMachine()
    {
        var log = new List<string>();
        await using var set = new MachineSet<int, Session, byte>(_ => new Session(log));
        var machine = await set.GetOrStartAsync(7);

        await Assert.That(await set.RemoveAsync(7)).IsTrue();
        await Assert.That(await set.RemoveAsync(7)).IsFalse();
        await Assert.That(machine.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(log).IsEquivalentTo(new[] { "opened", "ended" });
        await Assert.That(set.TryGet(7, out _)).IsFalse();
    }

    [Test]
    public async Task AMachineThatCompletesLeavesTheSetAndIsStopped()
    {
        var log = new List<string>();
        await using var set = new MachineSet<int, Session, byte>(_ => new Session(log));
        var machine = await set.GetOrStartAsync(3);
        await machine.FireAsync(1);

        // StopAsync reports Stopped before it runs the Exited actions, so wait for those as well.
        await Eventually(() => set.Count == 0 && machine.Status == MachineStatus.Stopped && log.Count == 2);
        await Assert.That(log).IsEquivalentTo(new[] { "opened", "ended" });

        var next = await set.GetOrStartAsync(3);
        await Assert.That(next).IsNotSameReferenceAs(machine);
    }

    [Test]
    public async Task AStartThatFailsLeavesTheKeyEmpty()
    {
        var attempts = 0;
        await using var set = new MachineSet<int, Session, byte>(_ =>
            ++attempts == 1 ? throw new InvalidOperationException("not yet") : new Session([]));

        await Assert.That(async () => await set.GetOrStartAsync(1)).Throws<InvalidOperationException>().WithMessage("not yet");
        await Assert.That(set.Count).IsEqualTo(0);
        await Assert.That((await set.GetOrStartAsync(1)).Status).IsEqualTo(MachineStatus.Running);
    }

    [Test]
    public async Task StoppingTheHostStopsEveryMachine()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.ClearProviders();
        var log = new List<string>();

        // begin-snippet: hosting-machine-set
        builder.Services.AddMachineSet<string, Session, byte>((services, key) => new Session(log));
        // end-snippet
        using var host = builder.Build();
        await host.StartAsync();

        var set = host.Services.GetRequiredService<MachineSet<string, Session, byte>>();
        var a = await set.GetOrStartAsync("a");
        var b = await set.GetOrStartAsync("b");
        await host.StopAsync();

        await Assert.That(a.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(b.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(async () => await set.GetOrStartAsync("c")).Throws<ObjectDisposedException>();
    }
}
