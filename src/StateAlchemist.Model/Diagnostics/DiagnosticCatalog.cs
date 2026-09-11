using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>
/// Every diagnostic the analysis reports (spec §8). The generator maps these onto Roslyn diagnostics; the reference
/// interpreter throws on errors; <c>docs/reference/diagnostics.md</c> documents each one, and a test keeps the two in step.
/// </summary>
public static class DiagnosticCatalog
{
    private const string Library = "declaring library";
    private const string App = "app";
    private const string Anywhere = "anywhere";

    public static readonly DiagnosticDescriptor InvalidState = new("SALCH0001", Severity.Error, "Invalid state declaration",
        "State '{0}' must be a public struct with exactly one parent marker; {1}", Library);

    public static readonly DiagnosticDescriptor NotPublicStatic = new("SALCH0002", Severity.Error, "Member is not public static",
        "'{0}' must be public and static", Library);

    public static readonly DiagnosticDescriptor InvalidHierarchy = new("SALCH0003", Severity.Error, "Invalid state hierarchy",
        "State '{0}' {1}", App);

    public static readonly DiagnosticDescriptor InitialChild = new("SALCH0004", Severity.Error, "Initial child required",
        "State '{0}' has {1} [Initial] children; it needs exactly one", App);

    public static readonly DiagnosticDescriptor ConflictingTransitions = new("SALCH0101", Severity.Error, "Conflicting transitions",
        "'{0}' and '{1}' both handle {2} in state '{3}' without a guard", App);

    public static readonly DiagnosticDescriptor AmbiguousGuardOrder = new("SALCH0102", Severity.Error, "Ambiguous guard order",
        "Guarded transitions '{0}' and '{1}' both handle {2} in state '{3}' with Order {4}", App);

    public static readonly DiagnosticDescriptor AmbiguousActionOrder = new("SALCH0103", Severity.Error, "Ambiguous action order",
        "'{0}' and '{1}' both run when '{2}' is {3}, from different modules, with Order {4}", App);

    public static readonly DiagnosticDescriptor TriggerOutOfRange = new("SALCH0104", Severity.Error, "Trigger outside the value type",
        "'{0}' fires on {1}, which is outside the value type '{2}'", App);

    public static readonly DiagnosticDescriptor UnsupportedValueType = new("SALCH0105", Severity.Error, "Unsupported value type",
        "The value type '{0}' is not supported: use an integral type or an enum of 16 bits or fewer", App);

    public static readonly DiagnosticDescriptor InvalidTransition = new("SALCH0106", Severity.Error, "Invalid transition declaration",
        "'{0}' {1}", Library);

    public static readonly DiagnosticDescriptor IncompleteMachine = new("SALCH0107", Severity.Error, "Incomplete machine declaration",
        "Machine '{0}' {1}", App);

    public static readonly DiagnosticDescriptor RefOnExitingState = new("SALCH0201", Severity.Error, "Writing to an exiting state",
        "Parameter '{0}' of '{1}' takes '{2}' by ref, but the transition exits it; take it as in", App);

    public static readonly DiagnosticDescriptor StateNotAvailable = new("SALCH0202", Severity.Error, "State not available here",
        "Parameter '{0}' of '{1}' names '{2}', which {3}", App);

    public static readonly DiagnosticDescriptor InvalidPhaseSignature = new("SALCH0203", Severity.Error, "Invalid phase signature",
        "'{0}' must {1}", App);

    public static readonly DiagnosticDescriptor UnbindableParameter = new("SALCH0204", Severity.Error, "Unbindable parameter",
        "Parameter '{0}' of '{1}' cannot be bound: {2}", App);

    public static readonly DiagnosticDescriptor ContextUnderStrictPurity = new("SALCH0205", Severity.Error, "Context under strict purity",
        "'{0}' takes the context, which machine '{1}' forbids with Purity.Strict", App);

    public static readonly DiagnosticDescriptor UnknownPhase = new("SALCH0206", Severity.Error, "Unknown phase method",
        "'{0}' is not a phase; a class-form transition may declare Guard, Transform, Decide, DecideAsync, Complete, Completed and CompletedAsync", Library);

    public static readonly DiagnosticDescriptor AsyncSuffix = new("SALCH0207", Severity.Error, "Async suffix does not match",
        "'{0}' {1}", Library);

    public static readonly DiagnosticDescriptor TwoDecideMethods = new("SALCH0208", Severity.Error, "Two decide methods",
        "Decision '{0}' declares both Decide and DecideAsync", Library);

    public static readonly DiagnosticDescriptor UncheckedWithAsync = new("SALCH0209", Severity.Warning, "Unchecked machine with async actions",
        "Machine '{0}' is Unchecked but has async actions or decisions; await every FireAsync before calling the next", App);

    public static readonly DiagnosticDescriptor ReentryClearsData = new("SALCH0301", Severity.Warning, "Re-entry clears data",
        "'{0}' re-enters '{1}', which has data; the data is cleared", App);

    public static readonly DiagnosticDescriptor OutcomeCompletions = new("SALCH0401", Severity.Error, "Decision outcome not completed exactly once",
        "Decision '{0}' has {1} Complete methods for outcome '{2}'; it needs exactly one", App);

    public static readonly DiagnosticDescriptor UnknownOutcome = new("SALCH0402", Severity.Error, "Unknown decision outcome",
        "'{0}' completes '{1}', which is not an outcome of decision '{2}'", App);

    public static readonly DiagnosticDescriptor UnhandledValues = new("SALCH0501", Severity.Warning, "Unhandled values",
        "Leaf '{0}' leaves {1} values unhandled and no [OnAny] covers them", App);

    public static readonly DiagnosticDescriptor UnreachableState = new("SALCH0502", Severity.Warning, "Unreachable state",
        "State '{0}' cannot be reached from the initial state", App);

    public static readonly DiagnosticDescriptor SyncFireOnAsyncMachine = new("SALCH0601", Severity.Error, "Synchronous fire on an async machine",
        "Synchronous Fire is not generated for '{0}': it has async actions or decisions", App);

    public static readonly DiagnosticDescriptor InvalidRun = new("SALCH0701", Severity.Error, "Invalid run transition",
        "[Run] on '{0}' is invalid: {1}", App);

    public static readonly DiagnosticDescriptor FiredBeforeStarted = new("SALCH0801", Severity.Warning, "Fired before started",
        "'{0}' is fired before StartAsync on some path", Anywhere);

    public static readonly DiagnosticDescriptor NoPhases = new("SALCH0901", Severity.Info, "Transition declares no phases",
        "Transition '{0}' declares no phases", Library);

    public static readonly DiagnosticDescriptor PhaseCanBeAdded = new("SALCH0902", Severity.Hidden, "Phase can be added",
        "Transition '{0}' can declare {1}", Library);

    /// <summary>Every descriptor, in identifier order.</summary>
    public static IReadOnlyList<DiagnosticDescriptor> All { get; } =
    [
        InvalidState, NotPublicStatic, InvalidHierarchy, InitialChild,
        ConflictingTransitions, AmbiguousGuardOrder, AmbiguousActionOrder, TriggerOutOfRange, UnsupportedValueType, InvalidTransition, IncompleteMachine,
        RefOnExitingState, StateNotAvailable, InvalidPhaseSignature, UnbindableParameter, ContextUnderStrictPurity, UnknownPhase, AsyncSuffix, TwoDecideMethods, UncheckedWithAsync,
        ReentryClearsData,
        OutcomeCompletions, UnknownOutcome,
        UnhandledValues, UnreachableState,
        SyncFireOnAsyncMachine,
        InvalidRun,
        FiredBeforeStarted,
        NoPhases, PhaseCanBeAdded,
    ];
}
