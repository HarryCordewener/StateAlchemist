using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Generated.Tests.Performance;

// The performance sample's modules, as three kinds of generated machine: inline, serialized, and one whose async
// decision gives it an inbox — the shape TNC's machine will have.

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule))]
public sealed partial class InlineMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters), Concurrency = Concurrency.Serialized)]
[Include(typeof(PerformanceModule))]
public sealed partial class SerializedMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule)), Include(typeof(PerformanceDecisionModule))]
public sealed partial class DecidingMachine;

[Machine(Root = typeof(StateAlchemist.Contracts.Machines.Recording.Root), Value = typeof(byte), Context = typeof(StateAlchemist.Contracts.Machines.RecordingContext),
    Concurrency = Concurrency.Serialized, InboxCapacity = 1)]
[Include(typeof(StateAlchemist.Contracts.Machines.Recording.RecorderModule))]
public sealed partial class BoundedRecorderMachine;
