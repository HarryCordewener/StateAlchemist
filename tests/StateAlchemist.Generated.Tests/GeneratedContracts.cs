using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Suite;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

// Every contract Plan 4's generator supports, run against generated machines. Plan 5 adds decisions, backpressure,
// runs and the serialized mode.

[InheritsTests]
public sealed class GeneratedLifecycle : LifecycleContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedTransitions : TransitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedResolution : ResolutionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedEvents : EventContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedUnhandled : UnhandledContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedExceptions : ExceptionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedPlans : PlanContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedDefinitions : DefinitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedConcurrency : ConcurrencyContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class GeneratedTelnet : TelnetContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => GeneratedHarness.Create(shape, context, hooks);
}
