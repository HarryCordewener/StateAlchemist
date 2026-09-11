using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Suite;
using TUnit.Core;

namespace StateAlchemist.Reference.Tests.Contracts;

// Every contract, run against the reference interpreter. A generated machine's test project has the same list.

[InheritsTests]
public sealed class ReferenceLifecycle : LifecycleContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceTransitions : TransitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceResolution : ResolutionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceEvents : EventContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceUnhandled : UnhandledContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceExceptions : ExceptionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferencePlans : PlanContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceDefinitions : DefinitionContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceConcurrency : ConcurrencyContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}

[InheritsTests]
public sealed class ReferenceTelnet : TelnetContract
{
    protected override IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks) => ReferenceHarness.Create(shape, context, hooks);
}
