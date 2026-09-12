using System;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StateAlchemist.Samples.Crossing;
using StateAlchemist.Samples.Door;
using StateAlchemist.Samples.Lines;
using StateAlchemist.Samples.Phone;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// The examples, run. Every one is in the documentation, so what it does is part of the documentation: a sample
/// that compiles but does not work would be worse than none.
/// </summary>
public class SampleTests
{
    [Test]
    public async Task ThePhoneCallConnectsAndHangsUp()
    {
        var log = new PhoneLog();
        await using var phone = new PhoneCall(log);
        await phone.StartAsync();

        await phone.FireAsync(Button.Lift);
        await phone.FireAsync(Button.Answered);
        await phone.FireAsync(Button.Second);
        await phone.FireAsync(Button.Second);
        await phone.FireAsync(Button.Second);
        await phone.FireAsync(Button.Hold);
        await phone.FireAsync(Button.Hold);
        await phone.FireAsync(Button.Second);
        await phone.FireAsync(Button.HangUp);

        phone.TryGetPhone(out var state);
        await Assert.That(state.Calls).IsEqualTo(1);
        await Assert.That(phone.IsIn<OnHook>()).IsTrue();
        // Hold leaves Talking, so its actions run again when the call resumes — and the seconds start over,
        // because they belong to Talking: three seconds before the hold, one after. Data that should survive a
        // hold belongs in a state above both.
        await Assert.That(log.Entries).IsEquivalentTo(new[]
        {
            "connected (call 1)", "talked for 3s", "connected (call 1)", "talked for 1s",
        });
    }

    /// <summary>Hanging up is declared on the root, so it applies wherever the phone is.</summary>
    [Test]
    [Arguments(new[] { Button.Lift })]
    [Arguments(new[] { Button.Lift, Button.Answered })]
    [Arguments(new[] { Button.Lift, Button.Answered, Button.Hold })]
    public async Task HangingUpWorksFromEveryState(Button[] route)
    {
        await using var phone = new PhoneCall(new PhoneLog());
        await phone.StartAsync();
        foreach (var button in route)
        {
            await phone.FireAsync(button);
        }

        await phone.FireAsync(Button.HangUp);

        await Assert.That(phone.IsIn<OnHook>()).IsTrue();
    }

    [Test]
    public async Task AButtonThatDoesNotApplyIsIgnored()
    {
        await using var phone = new PhoneCall(new PhoneLog());
        await phone.StartAsync();

        await phone.FireAsync(Button.Hold);

        await Assert.That(phone.IsIn<OnHook>()).IsTrue();
    }

    /// <summary>The crossing's guard: the light changes early for someone waiting, but not before three seconds.</summary>
    [Test]
    public async Task TheCrossingChangesEarlyForSomeoneWaiting()
    {
        var log = new CrossingLog();
        await using var crossing = new PedestrianCrossing(log);
        await crossing.StartAsync();

        await crossing.FireAsync(Signal.Tick);          // red -> green
        await crossing.FireAsync(Signal.Requested);     // the button, recorded on Working
        await crossing.FireAsync(Signal.Tick);          // 1s: too early to change
        await crossing.FireAsync(Signal.Tick);          // 2s
        await crossing.FireAsync(Signal.Tick);          // 3s
        await Assert.That(crossing.IsIn<Green>()).IsTrue();

        await crossing.FireAsync(Signal.Tick);          // now the guard passes
        await Assert.That(crossing.IsIn<Amber>()).IsTrue();

        await crossing.FireAsync(Signal.Tick);          // amber -> red, clearing the request
        crossing.TryGetWorking(out var working);
        crossing.TryGetJunction(out var junction);
        await Assert.That(working.Requested).IsFalse();
        await Assert.That(junction.Cycles).IsEqualTo(1);
        await Assert.That(log.Entries).IsEquivalentTo(new[] { "green", "green lasted 3s" });
    }

    /// <summary>A fault leaves two states at once, and clearing it enters the working side at its initial child.</summary>
    [Test]
    public async Task AFaultLeavesTwoStatesAndClearingItStartsAtRed()
    {
        var log = new CrossingLog();
        await using var crossing = new PedestrianCrossing(log);
        await crossing.StartAsync();
        await crossing.FireAsync(Signal.Tick);          // into green, so the fault exits two levels

        await crossing.FireAsync(Signal.Fault);
        await Assert.That(crossing.IsIn<Flashing>()).IsTrue();
        await Assert.That(crossing.IsIn<Faulted>()).IsTrue();

        await crossing.FireAsync(Signal.Cleared);
        await Assert.That(crossing.IsIn<Red>()).IsTrue();
        await Assert.That(log.Entries).IsEquivalentTo(new[] { "green", "green lasted 0s", "flashing" });
    }

    /// <summary>Green's seconds die with green: the next green starts from zero.</summary>
    [Test]
    public async Task WhatGreenCountsDiesWithGreen()
    {
        await using var crossing = new PedestrianCrossing(new CrossingLog());
        await crossing.StartAsync();
        await crossing.FireAsync(Signal.Tick);
        await crossing.FireAsync(Signal.Tick);          // 1s of green
        await crossing.FireAsync(Signal.Requested);
        await crossing.FireAsync(Signal.Tick);          // 2s
        await crossing.FireAsync(Signal.Tick);          // 3s
        await crossing.FireAsync(Signal.Tick);          // changes
        await crossing.FireAsync(Signal.Tick);          // back to red
        await crossing.FireAsync(Signal.Tick);          // green again

        crossing.TryGetGreen(out var green);
        await Assert.That(green.Seconds).IsEqualTo(0);
    }

    [Test]
    public async Task TheDoorOpensForAKnownBadge()
    {
        var access = new Access();
        await using var door = new CardDoor(access);
        await door.StartAsync();

        await door.FireAsync(new Badge(7));

        await Assert.That(door.IsIn<Unlocked>()).IsTrue();
        door.TryGetUnlocked(out var unlocked);
        await Assert.That(unlocked.Name).IsEqualTo("badge 7");
        await Assert.That(access.Log).IsEquivalentTo(new[] { "opened for badge 7" });
    }

    [Test]
    public async Task TheDoorStaysShutForAnUnknownBadge()
    {
        var access = new Access { Check = (_, _) => new ValueTask<bool>(false) };
        await using var door = new CardDoor(access);
        await door.StartAsync();

        await door.FireAsync(new Badge(9));

        await Assert.That(door.IsIn<Locked>()).IsTrue();
        await Assert.That(access.Log).IsEquivalentTo(new[] { "refused: unknown badge" });
    }

    /// <summary>What the door counts lives on the root, so it survives every lock and unlock.</summary>
    [Test]
    public async Task TheDoorCountsWhoItAdmits()
    {
        var access = new Access();
        await using var door = new CardDoor(access);
        await door.StartAsync();

        await door.FireAsync(new Badge(7));
        await door.FireAsync(new GaveUp());             // the door closes
        await door.FireAsync(new Badge(11));

        door.TryGetDoorFrame(out var frame);
        await Assert.That(frame.Admitted).IsEqualTo(2);
        await Assert.That(door.IsIn<Unlocked>()).IsTrue();
        await Assert.That(access.Log).IsEquivalentTo(new[] { "opened for badge 7", "opened for badge 11" });
    }

    /// <summary>
    /// The pending state a decision parks in is the machine's own, not one of yours: while the reader is thinking
    /// the door is still <see cref="Locked"/>, which is what <c>Handle</c>'s events are matched against.
    /// </summary>
    [Test]
    public async Task WhileTheReaderIsThinkingTheDoorIsStillLocked()
    {
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var access = new Access
        {
            Check = async (_, cancellation) =>
            {
                asked.TrySetResult(true);
                return await new TaskCompletionSource<bool>().Task.WaitAsync(cancellation);
            },
        };

        await using var door = new CardDoor(access);
        await door.StartAsync();
        var reading = door.FireAsync(new Badge(1));
        await asked.Task;

        await Assert.That(door.IsIn<Locked>()).IsTrue();
        await Assert.That(door.Status).IsEqualTo(MachineStatus.Running);

        await door.FireAsync(new GaveUp());
        await reading;
    }

    /// <summary>The event the decision handles ends the wait, and cancels the reader.</summary>
    [Test]
    public async Task GivingUpCancelsTheReader()
    {
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var access = new Access
        {
            Check = async (_, cancellation) =>
            {
                using var registration = cancellation.Register(() => cancelled.TrySetResult(true));
                asked.TrySetResult(true);
                return await new TaskCompletionSource<bool>().Task;   // never answers
            },
        };

        await using var door = new CardDoor(access);
        await door.StartAsync();
        var reading = door.FireAsync(new Badge(1));
        await asked.Task;

        await door.FireAsync(new GaveUp());

        await cancelled.Task;
        await reading;
        await Assert.That(door.IsIn<Locked>()).IsTrue();
        await Assert.That(access.Log).IsEquivalentTo(new[] { "gave up waiting" });
    }

    /// <summary>A reader that throws becomes a <see cref="DecisionFailed"/> the module recovers from.</summary>
    [Test]
    public async Task AReaderThatThrowsIsRecoveredFrom()
    {
        var access = new Access { Check = (_, _) => throw new InvalidOperationException("reader offline") };
        await using var door = new CardDoor(access);
        await door.StartAsync();

        await door.FireAsync(new Badge(3));

        await Assert.That(door.IsIn<Locked>()).IsTrue();
        await Assert.That(access.Log).IsEquivalentTo(new[] { "reader failed: reader offline" });
    }

    /// <summary>The reading sample, driven by a real pipe, with an event arriving from another task meanwhile.</summary>
    [Test]
    public async Task TheLineReaderReadsAPipe()
    {
        var lines = new Lines();
        await using var machine = new LineReader(lines);
        await machine.StartAsync();

        var pipe = new Pipe();
        var reading = Reading.ReadAsync(pipe.Reader, machine);

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("hello\nwor"));
        await machine.FireAsync(new Flush());
        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("ld!\n"));
        await pipe.Writer.CompleteAsync();
        await reading;

        machine.TryGetStream(out var stream);
        await Assert.That(stream.Count).IsEqualTo(2);
        await Assert.That(lines.Lengths).IsEquivalentTo(new[] { 5, 6 });
        await Assert.That(lines.Flushes).IsEqualTo(1);
    }
}
