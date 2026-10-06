using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

// Machines only these tests fire, so what the listeners see from them is what these tests did. Other tests run
// telemetry machines at the same time; they carry other machine names, and the listeners skip them.

// begin-snippet: sample-telemetry
[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class ObservedRecorderMachine;
// end-snippet

[Machine(Root = typeof(FailRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(FailureModule))]
public sealed partial class ObservedFailuresMachine;

[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(DecidingModule))]
public sealed partial class ObservedDecidingMachine;

/// <summary>docs/guides/telemetry.md: what <c>Telemetry = true</c> reports, read back through the .NET listeners.</summary>
[NotInParallel]
public sealed class TelemetryTests
{
    [Test]
    public async Task ATransitionIsAnActivityACountAndADuration()
    {
        using var observed = new Observer<ObservedRecorderMachine>();
        var machine = new ObservedRecorderMachine(new RecordingContext());
        await machine.StartAsync();

        await machine.FireAsync((byte)1);

        var activity = observed.Activities.Single();
        await Assert.That(activity.OperationName).IsEqualTo("RecorderModule.Sibling");
        await Assert.That(activity.GetTagItem("statealchemist.transition")).IsEqualTo("RecorderModule.Sibling");
        await Assert.That(activity.GetTagItem("statealchemist.from")).IsEqualTo("A1");
        await Assert.That(activity.GetTagItem("statealchemist.to")).IsEqualTo("A2");
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(observed.Show("statealchemist.transitions")).IsEqualTo("1 transition=RecorderModule.Sibling");
        await Assert.That(observed.Show("statealchemist.transition.duration")).IsEqualTo("transition=RecorderModule.Sibling");
    }

    [Test]
    public async Task AnUnhandledTriggerIsCountedWithTheLeaf()
    {
        using var observed = new Observer<ObservedRecorderMachine>();
        var machine = new ObservedRecorderMachine(new RecordingContext());
        await machine.StartAsync();

        await machine.FireAsync((byte)99);

        await Assert.That(observed.Show("statealchemist.unhandled")).IsEqualTo("1 state=A1");
        await Assert.That(observed.Activities.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AFailedTransitionIsAnErrorWithItsExceptionType()
    {
        using var observed = new Observer<ObservedFailuresMachine>();
        var context = new RecordingContext();
        context.Failing.Add("completed Go");
        var machine = new ObservedFailuresMachine(context);
        await machine.StartAsync();

        await Assert.That(async () => await machine.FireAsync((byte)1)).Throws<InvalidOperationException>();

        var activity = observed.Activities.Single();
        await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(activity.StatusDescription).IsEqualTo("completed Go failed");
        await Assert.That(observed.Show("statealchemist.transitions")).IsEqualTo("");
        await Assert.That(observed.Show("statealchemist.transition.duration"))
            .IsEqualTo("transition=FailureModule.Go error.type=System.InvalidOperationException");
    }

    [Test]
    public async Task ASynchronousDecisionIsTimedBeforeItsOutcome()
    {
        using var observed = new Observer<ObservedDecidingMachine>();
        var machine = new ObservedDecidingMachine(new RecordingContext());
        await machine.StartAsync();

        await machine.FireAsync((byte)3);

        await Assert.That(string.Join(" | ", observed.Activities.Select(a => $"{a.OperationName} {a.GetTagItem("statealchemist.decision") ?? a.GetTagItem("statealchemist.to")}")))
            .IsEqualTo("DecidingModule.Quick DecidingModule.Quick | DecidingModule.Quick Refused");
        await Assert.That(observed.Show("statealchemist.decision.duration")).IsEqualTo("decision=DecidingModule.Quick");
        await Assert.That(observed.Show("statealchemist.transitions")).IsEqualTo("1 transition=DecidingModule.Quick");
    }

    [Test]
    public async Task AnAsyncDecisionIsTimedUntilItsAnswer()
    {
        using var observed = new Observer<ObservedDecidingMachine>();
        var context = new RecordingContext();
        var machine = new ObservedDecidingMachine(context);
        context.Machine = machine;
        await machine.StartAsync();

        var fire = machine.FireAsync((byte)2).AsTask();
        await context.Deciding.Task;
        await Assert.That(observed.Show("statealchemist.decision.duration")).IsEqualTo("");

        context.Answer.SetResult(new Verdict(new Accept("ann")));
        await fire;

        await Assert.That(observed.Show("statealchemist.decision.duration")).IsEqualTo("decision=DecidingModule.Ask");
        await Assert.That(observed.Show("statealchemist.transitions")).IsEqualTo("1 transition=DecidingModule.Ask");
    }

    /// <summary>Listens to the <c>StateAlchemist</c> source and meter, keeping what <typeparamref name="TMachine"/> reports.</summary>
    private sealed class Observer<TMachine> : IDisposable
    {
        private static readonly string Machine = typeof(TMachine).FullName!;
        private readonly object _sync = new();
        private readonly List<Activity> _activities = [];
        private readonly List<(string Instrument, string Measurement)> _measurements = [];
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener = new();

        public Observer()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "StateAlchemist",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if ((string?)activity.GetTagItem("statealchemist.machine") == Machine)
                    {
                        lock (_sync)
                        {
                            _activities.Add(activity);
                        }
                    }
                },
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "StateAlchemist")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Measured(instrument, value.ToString(), tags));
            _meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Measured(instrument, null, tags));
            _meterListener.Start();
        }

        public IReadOnlyList<Activity> Activities
        {
            get
            {
                lock (_sync)
                {
                    return _activities.ToList();
                }
            }
        }

        /// <summary>One line per measurement of <paramref name="instrument"/>: a counter's value, then its tags but the machine's.</summary>
        public string Show(string instrument)
        {
            lock (_sync)
            {
                return string.Join(" | ", _measurements.Where(m => m.Instrument == instrument).Select(m => m.Measurement));
            }
        }

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
        }

        private void Measured(Instrument instrument, string? value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var all = tags.ToArray();
            if (!all.Any(t => t.Key == "statealchemist.machine" && (string?)t.Value == Machine))
            {
                return;
            }

            var shown = all.Where(t => t.Key != "statealchemist.machine").Select(t => $"{t.Key.Replace("statealchemist.", "")}={t.Value}");
            var measurement = string.Join(" ", value is null ? shown : shown.Prepend(value));
            lock (_sync)
            {
                _measurements.Add((instrument.Name, measurement));
            }
        }
    }
}
