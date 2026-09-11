using System.Collections.Concurrent;
using StateAlchemist.Contracts;

namespace StateAlchemist.Reference.Tests.Contracts;

/// <summary>Builds contract machines on the reference interpreter.</summary>
internal static class ReferenceHarness
{
    // Reflecting a machine is slow; each shape is reflected once per test run.
    private static readonly ConcurrentDictionary<MachineShape, ReflectedMachine> Reflected = new();

    public static IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks)
    {
        var machine = Reflected.GetOrAdd(shape, static s => ReflectionModelBuilder.Build(new MachineSpec(
            s.Name, s.Root, typeof(byte), s.Modules, s.Context, null, s.Concurrency, 0, s.Purity, s.Unhandled)));
        return ReferenceMachine<byte>.Create(machine, context, hooks: hooks is null ? null : Adapt(hooks));
    }

    private static ReferenceHooks<byte> Adapt(ContractHooks hooks) => new()
    {
        Transitioned = hooks.Transitioned,
        UnhandledValue = hooks.UnhandledValue,
        UnhandledEvent = hooks.UnhandledEvent,
        Exception = hooks.HandleExceptions
            ? (System.Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) =>
                resolution = hooks.Exception(exception, transition)
            : null,
    };
}
