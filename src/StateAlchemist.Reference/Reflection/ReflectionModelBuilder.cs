using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>
/// Builds a <see cref="MachineModel"/> from declarations by reflection — the same model the source generator builds
/// from Roslyn symbols, so both are checked by the same analysis.
/// </summary>
public static class ReflectionModelBuilder
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly string[] TransitionPhases = ["Guard", "Transform", "Completed", "CompletedAsync"];
    private static readonly string[] DecisionPhases = ["Guard", "Decide", "DecideAsync", "Complete", "Completed", "CompletedAsync"];

    /// <summary>Builds the model of a <c>[Machine]</c> class.</summary>
    public static ReflectedMachine FromMachine(Type machineType) => Build(MachineSpec.From(machineType));

    /// <summary>Builds the model of <paramref name="spec"/>. Problems become diagnostics; nothing throws for a bad declaration.</summary>
    public static ReflectedMachine Build(MachineSpec spec)
    {
        var builder = new Builder(spec);
        return builder.Run();
    }

    private sealed class Builder(MachineSpec spec)
    {
        private readonly List<ModelDiagnostic> _diagnostics = [];
        private readonly Dictionary<MethodModel, MethodInfo> _methods = new(ReferenceEqualityComparer.Instance);
        private readonly List<Type> _stateTypes = [];
        private MachineOptions _options = new("System.Byte", ValueDomain.Byte);

        public ReflectedMachine Run()
        {
            if (spec.Root is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, SourceSpan.None, spec.Name, "does not name a Root state"));
            }

            if (spec.Value is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, SourceSpan.None, spec.Name, "does not name a Value type"));
            }

            _options = Options(spec);
            var modules = spec.Modules.Where(IsModule).ToList();
            var declarations = modules.SelectMany(Declarations).ToList();
            CollectStates(declarations);
            var stateIndex = _stateTypes.Select((type, index) => (type, index)).ToDictionary(p => p.type, p => p.index);
            var states = _stateTypes.Select((type, index) => StateModelOf(type, index, stateIndex)).ToList();

            var transitions = new List<TransitionModel>();
            var actions = new List<StateActionModel>();
            foreach (var declaration in declarations)
            {
                switch (declaration)
                {
                    case TransitionDeclaration transition:
                        foreach (var transitionModel in TransitionModels(transition, stateIndex))
                        {
                            transitions.Add(transitionModel with { Index = transitions.Count });
                        }

                        break;
                    case ActionDeclaration action:
                        actions.Add(new StateActionModel(action.Phase, stateIndex[action.State], action.Order, action.Module.FullName!, action.DeclarationIndex,
                            MethodModelOf(action.Method, DisplayName(action.Module), stateIndex, [])));
                        break;
                }
            }

            var model = new MachineModel(spec.Name, _options, states, transitions, actions, _diagnostics);
            return new ReflectedMachine(spec, model, _stateTypes, _methods);
        }

        private bool IsModule(Type module)
        {
            if (module.GetCustomAttribute<ModuleAttribute>() is not null)
            {
                return true;
            }

            _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, SourceSpan.None, spec.Name, $"includes '{module.Name}', which is not a [Module]"));
            return false;
        }

        private static IEnumerable<object> Declarations(Type module)
        {
            var index = 0;
            foreach (var member in module.GetMembers(Declared).OrderBy(m => m.MetadataToken))
            {
                switch (member)
                {
                    case MethodInfo method when method.GetCustomAttribute<TransitionAttribute>() is { } attribute:
                        yield return new TransitionDeclaration(module, method, null, attribute.From, attribute.To, attribute.Order, false, []);
                        break;
                    case MethodInfo method:
                        foreach (var exited in method.GetCustomAttributes<ExitedAttribute>())
                        {
                            yield return new ActionDeclaration(module, method, ActionPhase.Exited, exited.State, exited.Order, index++);
                        }

                        foreach (var entered in method.GetCustomAttributes<EnteredAttribute>())
                        {
                            yield return new ActionDeclaration(module, method, ActionPhase.Entered, entered.State, entered.Order, index++);
                        }

                        break;
                    case Type nested when nested.GetCustomAttribute<TransitionAttribute>() is { } attribute:
                        yield return new TransitionDeclaration(module, null, nested, attribute.From, attribute.To, attribute.Order, false, []);
                        break;
                    case Type nested when nested.GetCustomAttribute<DecisionAttribute>() is { } attribute:
                        yield return new TransitionDeclaration(module, null, nested, attribute.From, null, attribute.Order, true, attribute.Handle);
                        break;
                }
            }
        }

        private void CollectStates(IReadOnlyList<object> declarations)
        {
            var pending = new List<Type>();
            if (spec.Root is not null)
            {
                pending.Add(spec.Root);
            }

            foreach (var declaration in declarations)
            {
                switch (declaration)
                {
                    case TransitionDeclaration t:
                        if (t.From is not null)
                        {
                            pending.Add(t.From);
                        }

                        if (t.To is not null)
                        {
                            pending.Add(t.To);
                        }

                        if (t.Class is not null)
                        {
                            pending.AddRange(t.Class.GetMethods(Declared).Select(m => m.GetCustomAttribute<ToAttribute>()?.Target).OfType<Type>());
                        }

                        break;
                    case ActionDeclaration a:
                        pending.Add(a.State);
                        break;
                }
            }

            var found = new HashSet<Type>();
            while (pending.Count > 0)
            {
                var type = pending[^1];
                pending.RemoveAt(pending.Count - 1);
                if (!found.Add(type))
                {
                    continue;
                }

                if (ParentOf(type) is { } parent && type != spec.Root)
                {
                    pending.Add(parent);
                }
            }

            var others = found.Where(t => t != spec.Root).OrderBy(t => t.FullName, StringComparer.Ordinal);
            _stateTypes.AddRange(spec.Root is null ? others : [spec.Root, .. others]);
        }

        private static Type? ParentOf(Type state) =>
            state.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IState<>))?.GetGenericArguments()[0];

        private StateModel StateModelOf(Type type, int index, IReadOnlyDictionary<Type, int> stateIndex)
        {
            var markers = type.GetInterfaces().Count(i => i == typeof(IRootState) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IState<>)));
            var parent = type == spec.Root || ParentOf(type) is not { } parentType ? -1 : stateIndex[parentType];
            if (type == spec.Root && ParentOf(type) is not null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, SourceSpan.None, type.Name, "is the machine's root but declares a parent"));
            }

            var reset = type.GetMethod("Reset", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return new StateModel(
                index,
                type.FullName!,
                parent,
                IsInitial: type.GetCustomAttribute<InitialAttribute>() is not null,
                HasData: type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0,
                HasReset: reset is not null && reset.ReturnType == typeof(void),
                IsPublicStruct: type.IsValueType && !type.IsEnum && IsVisible(type),
                ParentMarkers: markers,
                SourceSpan.None);
        }

        private IEnumerable<TransitionModel> TransitionModels(TransitionDeclaration declaration, IReadOnlyDictionary<Type, int> stateIndex)
        {
            var member = (MemberInfo?)declaration.Method ?? declaration.Class!;
            var name = DisplayName(declaration.Module) + "." + member.Name;
            if (declaration.From is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "does not name its From state"));
                yield break;
            }

            var triggers = Triggers(member, name);
            if (triggers.Count == 0)
            {
                yield break;
            }

            var declaringType = declaration.Class is null ? DisplayName(declaration.Module) : DisplayName(declaration.Class);
            var outcomes = declaration.IsDecision ? OutcomesOf(declaration.Class!) : [];
            MethodModel? Phase(string phase) => declaration.Class?.GetMethods(Declared).FirstOrDefault(m => m.Name == phase) is { } method
                ? MethodModelOf(method, declaringType, stateIndex, outcomes)
                : null;
            IReadOnlyList<MethodModel> Phases(params string[] phases) => declaration.Class?.GetMethods(Declared)
                .Where(m => phases.Contains(m.Name)).OrderBy(m => m.MetadataToken)
                .Select(m => MethodModelOf(m, declaringType, stateIndex, outcomes)).ToList() ?? [];

            var transform = declaration.Method is not null ? MethodModelOf(declaration.Method, declaringType, stateIndex, outcomes) : Phase("Transform");
            var known = declaration.IsDecision ? DecisionPhases : TransitionPhases;
            var unknown = declaration.Class?.GetMethods(Declared)
                .Where(m => !m.IsSpecialName && m.IsPublic && !known.Contains(m.Name) && m.DeclaringType == declaration.Class)
                .Select(m => m.Name).Distinct().ToList() ?? [];

            DecisionModel? decision = null;
            if (declaration.IsDecision)
            {
                var completions = declaration.Class!.GetMethods(Declared).Where(m => m.Name == "Complete").OrderBy(m => m.MetadataToken).Select(m =>
                {
                    var target = m.GetCustomAttribute<ToAttribute>()?.Target;
                    var complete = MethodModelOf(m, declaringType, stateIndex, outcomes);
                    if (target is null)
                    {
                        _diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, SourceSpan.None, complete.FullName, "declare its target with [To]"));
                    }

                    var outcome = m.GetParameters().Select(p => p.ParameterType).FirstOrDefault(t => outcomes.Contains(t.FullName!))?.FullName ?? string.Empty;
                    return new OutcomeCompletion(outcome, target is null ? -1 : stateIndex[target], complete);
                }).Where(c => c.Target >= 0).ToList();
                decision = new DecisionModel(Phase("Decide"), Phase("DecideAsync"), outcomes, completions, declaration.Handle.Select(h => h.FullName!).ToList());
            }

            var source = stateIndex[declaration.From];
            var target = declaration.To is null ? -1 : stateIndex[declaration.To];
            var isRun = member.GetCustomAttribute<RunAttribute>() is not null;
            foreach (var trigger in triggers)
            {
                yield return new TransitionModel(0, name, source, target, trigger, declaration.Order, isRun, Phase("Guard"), transform,
                    Phases("Completed", "CompletedAsync"), decision, unknown, declaration.Module.FullName!, SourceSpan.None);
            }
        }

        private List<TriggerModel> Triggers(MemberInfo member, string name)
        {
            var triggers = new List<TriggerModel>();
            foreach (var on in member.GetCustomAttributes<OnAttribute>())
            {
                if (ToInt64(on.Value) is { } value)
                {
                    triggers.Add(TriggerModel.Value(value));
                }
                else
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, $"fires on '{on.Value}', which is not an integral constant"));
                }
            }

            foreach (var range in member.GetCustomAttributes<OnRangeAttribute>())
            {
                if (ToInt64(range.From) is not { } low || ToInt64(range.To) is not { } high)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "has a range whose ends are not integral constants"));
                }
                else if (high < low)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, $"has an empty range {low}..{high}"));
                }
                else
                {
                    triggers.Add(TriggerModel.Range(low, high));
                }
            }

            if (member.GetCustomAttribute<OnAnyAttribute>() is not null)
            {
                triggers.Add(TriggerModel.Any);
            }

            var onEvent = member.GetCustomAttribute<OnEventAttribute>();
            if (onEvent is not null && triggers.Count > 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "mixes value and event triggers"));
                return [];
            }

            if (onEvent is not null)
            {
                triggers.Add(TriggerModel.Event(onEvent.EventType.FullName!));
            }

            if (triggers.Count == 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, SourceSpan.None, name, "has no trigger"));
            }

            return triggers;
        }

        private MethodModel MethodModelOf(MethodInfo method, string declaringType, IReadOnlyDictionary<Type, int> stateIndex, IReadOnlyList<string> outcomes)
        {
            var parameters = method.GetParameters().Select(p =>
            {
                var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
                var passing = !p.ParameterType.IsByRef ? Passing.Value : p.IsIn ? Passing.In : p.IsOut ? Passing.Out : Passing.Ref;
                var kind = ParameterClassifier.Classify(Facts(type), _options, n => IndexOf(n, stateIndex), outcomes, out var state);
                return new ParameterModel(p.Name ?? "_", FriendlyName(type), kind, passing, state);
            }).ToList();

            var model = new MethodModel(declaringType, method.Name, ReturnShapeOf(method.ReturnType), method.IsStatic, method.IsPublic && IsVisible(method.DeclaringType!), parameters, SourceSpan.None);
            _methods[model] = method;
            return model;
        }

        private static int IndexOf(string fullName, IReadOnlyDictionary<Type, int> stateIndex)
        {
            foreach (var pair in stateIndex)
            {
                if (pair.Key.FullName == fullName)
                {
                    return pair.Value;
                }
            }

            return -1;
        }

        private static TypeFacts Facts(Type type)
        {
            string? ArgumentOf(Type definition) =>
                type.IsGenericType && type.GetGenericTypeDefinition() == definition ? type.GetGenericArguments()[0].FullName : null;

            return new TypeFacts(
                type.FullName ?? type.Name,
                IsEvent: typeof(IEvent).IsAssignableFrom(type),
                SpanOf: ArgumentOf(typeof(ReadOnlySpan<>)),
                MemoryOf: ArgumentOf(typeof(ReadOnlyMemory<>)),
                IsCancellationToken: type == typeof(CancellationToken),
                TransitionInfoOf: ArgumentOf(typeof(TransitionInfo<>)));
        }

        private static IReadOnlyList<string> OutcomesOf(Type decision)
        {
            var decide = decision.GetMethods(Declared).FirstOrDefault(m => m.Name is "Decide" or "DecideAsync");
            var union = decide?.ReturnType;
            if (union is { IsGenericType: true } && (union.GetGenericTypeDefinition() == typeof(ValueTask<>) || union.GetGenericTypeDefinition() == typeof(Task<>)))
            {
                union = union.GetGenericArguments()[0];
            }

            if (union is null || !union.GetCustomAttributes().Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.UnionAttribute"))
            {
                return [];
            }

            return union.GetConstructors().Select(c => c.GetParameters()).Where(p => p.Length == 1).Select(p => p[0].ParameterType.FullName!).ToList();
        }

        private static MachineOptions Options(MachineSpec spec)
        {
            var value = spec.Value ?? typeof(byte);
            var underlying = value.IsEnum ? Enum.GetUnderlyingType(value) : value;
            var domain = underlying switch
            {
                _ when underlying == typeof(byte) => new ValueDomain(byte.MinValue, byte.MaxValue),
                _ when underlying == typeof(sbyte) => new ValueDomain(sbyte.MinValue, sbyte.MaxValue),
                _ when underlying == typeof(short) => new ValueDomain(short.MinValue, short.MaxValue),
                _ when underlying == typeof(ushort) => new ValueDomain(ushort.MinValue, ushort.MaxValue),
                _ when underlying == typeof(char) => new ValueDomain(char.MinValue, char.MaxValue),
                _ => null,
            };

            if (domain is not null && value.IsEnum)
            {
                domain = domain with { Members = Enum.GetValues(value).Cast<object>().Select(v => Convert.ToInt64(v)).Distinct().OrderBy(v => v).ToList() };
            }

            return new MachineOptions(
                value.FullName!,
                domain ?? new ValueDomain(0, 0),
                ValueTypeSupported: domain is not null,
                spec.Context?.FullName,
                spec.Config?.FullName,
                (ConcurrencyMode)spec.Concurrency,
                spec.InboxCapacity,
                (PurityMode)spec.Purity,
                (UnhandledMode)spec.Unhandled);
        }

        private static long? ToInt64(object value) => value switch
        {
            Enum or byte or sbyte or short or ushort or int or uint or long or char => Convert.ToInt64(value),
            _ => null,
        };

        private static ReturnShape ReturnShapeOf(Type type) => type switch
        {
            _ when type == typeof(void) => ReturnShape.Void,
            _ when type == typeof(bool) => ReturnShape.Bool,
            _ when type == typeof(ValueTask) => ReturnShape.ValueTask,
            _ when type == typeof(Task) => ReturnShape.Task,
            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(ValueTask<>) => ReturnShape.ValueTaskOfResult,
            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(Task<>) => ReturnShape.TaskOfResult,
            _ => ReturnShape.Other,
        };

        private static bool IsVisible(Type type) => type.IsPublic || (type.IsNestedPublic && IsVisible(type.DeclaringType!));

        private static string DisplayName(Type type) => type.DeclaringType is null ? type.Name : DisplayName(type.DeclaringType) + "." + type.Name;

        private static string FriendlyName(Type type) => type.IsGenericType
            ? $"{type.GetGenericTypeDefinition().FullName!.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>"
            : type.FullName ?? type.Name;
    }

    private sealed record TransitionDeclaration(Type Module, MethodInfo? Method, Type? Class, Type? From, Type? To, int Order, bool IsDecision, Type[] Handle);

    private sealed record ActionDeclaration(Type Module, MethodInfo Method, ActionPhase Phase, Type State, int Order, int DeclarationIndex);
}
