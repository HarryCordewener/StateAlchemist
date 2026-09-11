using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Samples.Performance;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// The synchronous <c>Fire</c>: the same call, without the <c>await</c>. A machine whose actions are all
/// synchronous is the case it exists for — a read loop that has nothing to await should not have to pretend — and
/// what it does must be what <c>FireAsync</c> does, which is what these tests compare.
/// </summary>
public class SyncFireTests
{
    [Test]
    public async Task FiringSynchronouslyDoesWhatAwaitingDoes()
    {
        var counters = new Counters();
        var machine = new SyncMachine(counters);
        await machine.StartAsync();

        machine.Fire((byte)1);
        machine.Fire((byte)1);
        machine.TryGetText(out var text);
        await Assert.That(text.Count).IsEqualTo(2);

        await machine.FireAsync((byte)1);
        machine.TryGetText(out text);
        await Assert.That(text.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ABatchFiredSynchronouslyConsumesRuns()
    {
        var machine = new SyncMachine(new Counters());
        await machine.StartAsync();

        machine.Fire(new ReadOnlyMemory<byte>(Enumerable.Repeat((byte)'a', 16).ToArray()));
        machine.TryGetText(out var text);
        await Assert.That(text.Length).IsEqualTo(16);
    }

    [Test]
    public async Task AnEventFiredSynchronouslyRuns()
    {
        var machine = new SyncMachine(new Counters());
        await machine.StartAsync();

        machine.Fire(new Tick());
        machine.TryGetRoot(out var root);
        await Assert.That(root.Moves).IsEqualTo(1);
    }

    /// <summary>What a transform throws reaches the caller of <c>Fire</c>, not a task nobody awaits.</summary>
    [Test]
    public async Task WhatATransformThrowsIsThrownBySyncFire()
    {
        var machine = new SyncThrowingMachine(new Counters());
        await machine.StartAsync();

        await Assert.That(() => machine.Fire((byte)7)).Throws<InvalidOperationException>();
    }
}
