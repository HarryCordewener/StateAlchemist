using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Suite;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

// Every contract, run against generated machines with Telemetry set.

[InheritsTests]
public sealed class TelemetryLifecycle : LifecycleContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryTransitions : TransitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryResolution : ResolutionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryEvents : EventContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryUnhandled : UnhandledContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryExceptions : ExceptionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryPlans : PlanContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryDefinitions : DefinitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryConcurrency : ConcurrencyContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryTelnet : TelnetContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryDecisions : DecisionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryBackpressure : BackpressureContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetryRuns : RunContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetrySerializedRuns : RunContract
{
    protected override MachineShape Shape => Shapes.RunsSerialized;
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class TelemetrySerialized : SerializedContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => TelemetryHarness.Create(shape, context, hooks);
}
