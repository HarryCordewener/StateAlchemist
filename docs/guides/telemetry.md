# Telemetry

`[Machine(Telemetry = true)]` makes the generator write tracing and metrics into the machine, using the .NET
`System.Diagnostics` types: an `ActivitySource` and a `Meter`, both named `StateAlchemist`. Anything that listens
to those names receives them, such as an `ActivityListener` and a `MeterListener`.

<!-- snippet: sample-telemetry -->
<a id='snippet-sample-telemetry'></a>
```cs
[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class ObservedRecorderMachine;
```
<sup><a href='/tests/StateAlchemist.Generated.Tests/TelemetryTests.cs#L18-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-telemetry' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Without the option the generator writes none of this, and the machine's code is the same as before.

## Activities

| Activity | When | Tags |
|---|---|---|
| The transition's name, such as `RecorderModule.Sibling` | Around a transition's steps: transform, commit, actions | `statealchemist.machine`, `statealchemist.transition`, `statealchemist.from`, `statealchemist.to` |
| The decision's name | Around a `Decide`, or a `DecideAsync` until it returns | `statealchemist.machine`, `statealchemist.decision` |

A decision's outcome is a transition named after the decision, so it gets a transition activity of its own once
the decision has chosen. Guards run before any activity starts. An exception that leaves a transition or a
decision sets the activity's status to `Error`, with the exception's message as the description. When an
[exception hook](../concepts/exceptions.md) recovers, the exception does not leave the transition, and the
activity is not an error.

When nothing listens, `StartActivity()` "will return `null` and avoid creating the Activity object"
([Learn](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)),
and the machine sets no tags.

## Metrics

| Instrument | Kind | Unit | Tags |
|---|---|---|---|
| `statealchemist.transitions` | `Counter<long>` | `{transition}` | `statealchemist.machine`, `statealchemist.transition` |
| `statealchemist.unhandled` | `Counter<long>` | `{trigger}` | `statealchemist.machine`, `statealchemist.state` (the active leaf) |
| `statealchemist.transition.duration` | `Histogram<double>` | `s` | `statealchemist.machine`, `statealchemist.transition`, and `error.type` if it threw |
| `statealchemist.decision.duration` | `Histogram<double>` | `s` | `statealchemist.machine`, `statealchemist.decision`, and `error.type` if it threw |

`statealchemist.transitions` counts the transitions that complete, where `OnTransitioned` is called.
`statealchemist.machine` is the machine class's full name; `error.type` is the exception's full type name. The
durations and the unhandled count are recorded only while something listens to them.

## Frameworks

The `Meter` and `ActivitySource` types are part of .NET from .NET 6. A `netstandard2.0` application that sets
`Telemetry` references the
[`System.Diagnostics.DiagnosticSource`](https://www.nuget.org/packages/System.Diagnostics.DiagnosticSource)
package; without it the machine reports [`SALCH0107`](../reference/diagnostics.md#salch0107) and gets no code.

The hooks are unchanged: a machine with `Telemetry` can still implement `OnTransitioned`, `OnUnhandled` and the
exception hooks.
