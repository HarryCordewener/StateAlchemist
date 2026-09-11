using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

/// <summary>
/// Builds a <see cref="MachineModel"/> from a <c>[Machine]</c> class's symbols — rule for rule the model the reference
/// front-end builds by reflection, so the generator and the interpreter are checked by the same analysis. The
/// agreement is a test (<c>FrontEndAgreementTests</c>), not a hope.
/// </summary>
internal static class SymbolModelBuilder
{
    private static readonly string[] TransitionPhases = ["Guard", "Transform", "Completed", "CompletedAsync"];
    private static readonly string[] DecisionPhases = ["Guard", "Decide", "DecideAsync", "Complete", "Completed", "CompletedAsync"];

    /// <summary>Builds the model of <paramref name="machine"/>. Problems become diagnostics; nothing throws for a bad declaration.</summary>
    public static SymbolMachine Build(INamedTypeSymbol machine, Compilation compilation) => new Builder(machine, new KnownTypes(compilation)).Run();

    /// <summary>
    /// Builds the model of one <c>[Module]</c> on its own, for the analyzer that reports a declaring library's
    /// problems where the module is written (spec §8). There is no machine, so there is no root, no value type and
    /// no options: only what a module's own declarations can be wrong about is knowable here, which is exactly the
    /// diagnostics scoped to the declaring library.
    /// </summary>
    public static SymbolMachine BuildModule(INamedTypeSymbol module, Compilation compilation) => new Builder(module, new KnownTypes(compilation)).RunModule();

    private sealed class Builder(INamedTypeSymbol machine, KnownTypes known)
    {
        private readonly List<ModelDiagnostic> _diagnostics = [];
        private readonly Dictionary<MethodModel, IMethodSymbol> _methods = new(ReferenceComparer<MethodModel>.Instance);
        private readonly Dictionary<SourceSpan, Location> _locations = [];
        private readonly Dictionary<string, INamedTypeSymbol> _events = [];
        private readonly List<INamedTypeSymbol> _stateTypes = [];
        private MachineOptions _options = new("System.Byte", ValueDomain.Byte);
        private INamedTypeSymbol? _root;
        private ITypeSymbol? _value;
        private ITypeSymbol? _context;
        private ITypeSymbol? _config;

        /// <summary>One module, with no machine around it: its states are the ones its own declarations name.</summary>
        public SymbolMachine RunModule()
        {
            // Every trigger value fits, because no value type is chosen yet: a value out of range is the app's problem.
            _options = new MachineOptions("System.Int64", new ValueDomain(long.MinValue, long.MaxValue));
            return Build([machine], SourceSpan.None);
        }

        public SymbolMachine Run()
        {
            var attribute = machine.GetAttributes().First(a => Is(a.AttributeClass, known.Machine));
            var concurrency = ConcurrencyMode.Checked;
            var purity = PurityMode.Permissive;
            var unhandled = UnhandledMode.Ignore;
            var inbox = 0;
            foreach (var argument in attribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "Root":
                        _root = argument.Value.Value as INamedTypeSymbol;
                        break;
                    case "Value":
                        _value = argument.Value.Value as ITypeSymbol;
                        break;
                    case "Context":
                        _context = argument.Value.Value as ITypeSymbol;
                        break;
                    case "Config":
                        _config = argument.Value.Value as ITypeSymbol;
                        break;
                    case "Concurrency":
                        concurrency = (ConcurrencyMode)(int)argument.Value.Value!;
                        break;
                    case "InboxCapacity":
                        inbox = (int)argument.Value.Value!;
                        break;
                    case "Purity":
                        purity = (PurityMode)(int)argument.Value.Value!;
                        break;
                    case "Unhandled":
                        unhandled = (UnhandledMode)(int)argument.Value.Value!;
                        break;
                }
            }

            var machineLocation = Span(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
            if (_root is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, machineLocation, machine.Name, "does not name a Root state"));
            }

            if (_value is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, machineLocation, machine.Name, "does not name a Value type"));
            }

            _options = Options(concurrency, inbox, purity, unhandled);
            var modules = machine.GetAttributes()
                .Where(a => Is(a.AttributeClass, known.Include))
                .Select(a => a.ConstructorArguments[0].Value)
                .OfType<INamedTypeSymbol>()
                .Where(module => IsModule(module, machineLocation))
                .ToList();
            return Build(modules, machineLocation);
        }

        private SymbolMachine Build(List<INamedTypeSymbol> modules, SourceSpan machineLocation)
        {
            var declarations = modules.SelectMany(Declarations).ToList();
            CollectStates(declarations);
            var stateIndex = new Dictionary<ITypeSymbol, int>(SymbolEqualityComparer.Default);
            for (var i = 0; i < _stateTypes.Count; i++)
            {
                stateIndex[_stateTypes[i]] = i;
            }

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
                        actions.Add(new StateActionModel(action.Phase, stateIndex[action.State], action.Order, MetadataName(action.Module), action.DeclarationIndex,
                            MethodModelOf(action.Method, DisplayName(action.Module), stateIndex, [])));
                        break;
                }
            }

            // A failed decision fires DecisionFailed whether or not a transition handles it: the machine must know the type.
            if (transitions.Any(t => t.IsDecision) && known.DecisionFailed is { } failed)
            {
                Event(failed);
            }

            var model = new MachineModel(machine.Name, _options, states, transitions, actions, _diagnostics);
            return new SymbolMachine(machine, model, _stateTypes, _methods, _events, _value, _context, _config, _locations, machineLocation,
                modules.Select(m => MetadataName(m)).ToList());
        }

        private bool IsModule(INamedTypeSymbol module, SourceSpan at)
        {
            if (module.GetAttributes().Any(a => Is(a.AttributeClass, known.Module)))
            {
                return true;
            }

            _diagnostics.Add(new(DiagnosticCatalog.IncompleteMachine, at, machine.Name, $"includes '{module.Name}', which is not a [Module]"));
            return false;
        }

        /// <summary>
        /// A module's declarations in the order reflection sees them — by metadata token, so nested types (transition and
        /// decision classes) before methods, each in declaration order.
        /// </summary>
        private IEnumerable<object> Declarations(INamedTypeSymbol module)
        {
            foreach (var nested in module.GetTypeMembers())
            {
                if (nested.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.Transition)) is { } transition)
                {
                    yield return new TransitionDeclaration(module, null, nested, TypeArgument(transition, "From"), TypeArgument(transition, "To"),
                        IntArgument(transition, "Order"), false, []);
                }
                else if (nested.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.Decision)) is { } decision)
                {
                    var handle = decision.NamedArguments.FirstOrDefault(a => a.Key == "Handle").Value;
                    var events = handle.Kind == TypedConstantKind.Array ? handle.Values.Select(v => v.Value).OfType<INamedTypeSymbol>().ToArray() : [];
                    yield return new TransitionDeclaration(module, null, nested, TypeArgument(decision, "From"), null, IntArgument(decision, "Order"), true, events);
                }
            }

            var index = 0;
            foreach (var method in module.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
            {
                if (method.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.Transition)) is { } transition)
                {
                    yield return new TransitionDeclaration(module, method, null, TypeArgument(transition, "From"), TypeArgument(transition, "To"),
                        IntArgument(transition, "Order"), false, []);
                    continue;
                }

                foreach (var exited in method.GetAttributes().Where(a => Is(a.AttributeClass, known.Exited)))
                {
                    if (exited.ConstructorArguments[0].Value is INamedTypeSymbol state)
                    {
                        yield return new ActionDeclaration(module, method, ActionPhase.Exited, state, IntArgument(exited, "Order"), index++);
                    }
                }

                foreach (var entered in method.GetAttributes().Where(a => Is(a.AttributeClass, known.Entered)))
                {
                    if (entered.ConstructorArguments[0].Value is INamedTypeSymbol state)
                    {
                        yield return new ActionDeclaration(module, method, ActionPhase.Entered, state, IntArgument(entered, "Order"), index++);
                    }
                }
            }
        }

        private void CollectStates(IReadOnlyList<object> declarations)
        {
            var pending = new List<INamedTypeSymbol>();
            if (_root is not null)
            {
                pending.Add(_root);
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
                            pending.AddRange(t.Class.GetMembers().OfType<IMethodSymbol>()
                                .Select(m => m.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.To))?.ConstructorArguments[0].Value)
                                .OfType<INamedTypeSymbol>());
                        }

                        break;
                    case ActionDeclaration a:
                        pending.Add(a.State);
                        break;
                }
            }

            var found = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            while (pending.Count > 0)
            {
                var type = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);
                if (!found.Add(type))
                {
                    continue;
                }

                if (ParentOf(type) is { } parent && !Is(type, _root))
                {
                    pending.Add(parent);
                }
            }

            var others = found.Where(t => !Is(t, _root)).OrderBy(MetadataName, StringComparer.Ordinal);
            _stateTypes.AddRange(_root is null ? others : [_root, .. others]);
        }

        private INamedTypeSymbol? ParentOf(INamedTypeSymbol state) =>
            state.AllInterfaces.FirstOrDefault(i => Is(i.OriginalDefinition, known.State))?.TypeArguments[0] as INamedTypeSymbol;

        private StateModel StateModelOf(INamedTypeSymbol type, int index, IReadOnlyDictionary<ITypeSymbol, int> stateIndex)
        {
            var markers = type.AllInterfaces.Count(i => Is(i, known.RootState) || Is(i.OriginalDefinition, known.State));
            var isRoot = Is(type, _root);
            var parent = isRoot || ParentOf(type) is not { } parentType ? -1 : stateIndex[parentType];
            var location = Span(type.Locations.FirstOrDefault());
            if (isRoot && ParentOf(type) is not null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, location, type.Name, "is the machine's root but declares a parent"));
            }

            var reset = type.GetMembers("Reset").OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 0 && m.ReturnsVoid);
            return new StateModel(
                index,
                MetadataName(type),
                parent,
                IsInitial: type.GetAttributes().Any(a => Is(a.AttributeClass, known.Initial)),
                HasData: type.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic),
                HasReset: reset,
                IsPublicStruct: type.TypeKind == TypeKind.Struct && IsVisible(type),
                ParentMarkers: markers,
                location);
        }

        private IEnumerable<TransitionModel> TransitionModels(TransitionDeclaration declaration, IReadOnlyDictionary<ITypeSymbol, int> stateIndex)
        {
            var member = (ISymbol?)declaration.Method ?? declaration.Class!;
            var name = DisplayName(declaration.Module) + "." + member.Name;
            var location = Span(member.Locations.FirstOrDefault());
            if (declaration.From is null)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "does not name its From state"));
                yield break;
            }

            var triggers = Triggers(member, name, location);
            if (triggers.Count == 0)
            {
                yield break;
            }

            var declaringType = declaration.Class is null ? DisplayName(declaration.Module) : DisplayName(declaration.Class);
            var outcomes = declaration.IsDecision ? OutcomesOf(declaration.Class!) : [];
            var classMethods = declaration.Class?.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).ToList() ?? [];
            MethodModel? Phase(string phase) => classMethods.FirstOrDefault(m => m.Name == phase) is { } method
                ? MethodModelOf(method, declaringType, stateIndex, outcomes)
                : null;
            IReadOnlyList<MethodModel> Phases(params string[] phases) =>
                classMethods.Where(m => phases.Contains(m.Name)).Select(m => MethodModelOf(m, declaringType, stateIndex, outcomes)).ToList();

            var transform = declaration.Method is not null ? MethodModelOf(declaration.Method, declaringType, stateIndex, outcomes) : Phase("Transform");
            var knownPhases = declaration.IsDecision ? DecisionPhases : TransitionPhases;
            var unknown = classMethods
                .Where(m => m.DeclaredAccessibility == Accessibility.Public && !knownPhases.Contains(m.Name))
                .GroupBy(m => m.Name)
                .Select(group => new UnknownMember(group.Key, Span(group.First().Locations.FirstOrDefault())))
                .ToList();

            DecisionModel? decision = null;
            if (declaration.IsDecision)
            {
                var completions = classMethods.Where(m => m.Name == "Complete").Select(m =>
                {
                    var target = m.GetAttributes().FirstOrDefault(a => Is(a.AttributeClass, known.To))?.ConstructorArguments[0].Value as INamedTypeSymbol;
                    var complete = MethodModelOf(m, declaringType, stateIndex, outcomes);
                    if (target is null)
                    {
                        _diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, complete.Location, complete.FullName, "declare its target with [To]"));
                    }

                    var outcome = m.Parameters.Select(p => MetadataName(p.Type)).FirstOrDefault(outcomes.Contains) ?? string.Empty;
                    return new OutcomeCompletion(outcome, target is null ? -1 : stateIndex[target], complete);
                }).Where(c => c.Target >= 0).ToList();
                decision = new DecisionModel(Phase("Decide"), Phase("DecideAsync"), outcomes, completions, declaration.Handle.Select(h => Event(h)).ToList());
            }

            var source = stateIndex[declaration.From];
            var target = declaration.To is null ? -1 : stateIndex[declaration.To];
            var isRun = member.GetAttributes().Any(a => Is(a.AttributeClass, known.Run));
            foreach (var trigger in triggers)
            {
                yield return new TransitionModel(0, name, source, target, trigger, declaration.Order, isRun, Phase("Guard"), transform,
                    Phases("Completed", "CompletedAsync"), decision, unknown, MetadataName(declaration.Module), location);
            }
        }

        private List<TriggerModel> Triggers(ISymbol member, string name, SourceSpan location)
        {
            var triggers = new List<TriggerModel>();
            var attributes = member.GetAttributes();
            foreach (var on in attributes.Where(a => Is(a.AttributeClass, known.On)))
            {
                var value = on.ConstructorArguments[0];
                if (ToInt64(value) is { } number)
                {
                    triggers.Add(TriggerModel.Value(number));
                }
                else
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, $"fires on '{Text(value)}', which is not an integral constant"));
                }
            }

            foreach (var range in attributes.Where(a => Is(a.AttributeClass, known.OnRange)))
            {
                if (ToInt64(range.ConstructorArguments[0]) is not { } low || ToInt64(range.ConstructorArguments[1]) is not { } high)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "has a range whose ends are not integral constants"));
                }
                else if (high < low)
                {
                    _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, $"has an empty range {low}..{high}"));
                }
                else
                {
                    triggers.Add(TriggerModel.Range(low, high));
                }
            }

            if (attributes.Any(a => Is(a.AttributeClass, known.OnAny)))
            {
                triggers.Add(TriggerModel.Any);
            }

            var onEvent = attributes.FirstOrDefault(a => Is(a.AttributeClass, known.OnEvent));
            if (onEvent is not null && triggers.Count > 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "mixes value and event triggers"));
                return [];
            }

            if (onEvent?.ConstructorArguments[0].Value is INamedTypeSymbol eventType)
            {
                triggers.Add(TriggerModel.Event(Event(eventType)));
            }

            if (triggers.Count == 0)
            {
                _diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, location, name, "has no trigger"));
            }

            return triggers;
        }

        private string Event(INamedTypeSymbol type)
        {
            var name = MetadataName(type);
            _events[name] = type;
            return name;
        }

        private MethodModel MethodModelOf(IMethodSymbol method, string declaringType, IReadOnlyDictionary<ITypeSymbol, int> stateIndex, IReadOnlyList<string> outcomes)
        {
            var parameters = method.Parameters.Select(p =>
            {
                var passing = p.RefKind switch
                {
                    RefKind.None => Passing.Value,
                    RefKind.Ref => Passing.Ref,
                    RefKind.Out => Passing.Out,
                    _ => Passing.In,
                };
                var kind = ParameterClassifier.Classify(Facts(p.Type), _options, n => IndexOf(n, stateIndex), outcomes, out var state);
                if (kind == ParameterKind.Event && p.Type is INamedTypeSymbol eventType)
                {
                    Event(eventType);
                }

                return new ParameterModel(p.Name.Length == 0 ? "_" : p.Name, FriendlyName(p.Type), kind, passing, state, Span(p.Locations.FirstOrDefault()));
            }).ToList();

            var model = new MethodModel(declaringType, method.Name, ReturnShapeOf(method.ReturnType), method.IsStatic,
                method.DeclaredAccessibility == Accessibility.Public && IsVisible(method.ContainingType), parameters, Span(method.Locations.FirstOrDefault()));
            _methods[model] = method;
            return model;
        }

        private static int IndexOf(string fullName, IReadOnlyDictionary<ITypeSymbol, int> stateIndex)
        {
            foreach (var pair in stateIndex)
            {
                if (MetadataName(pair.Key) == fullName)
                {
                    return pair.Value;
                }
            }

            return -1;
        }

        private TypeFacts Facts(ITypeSymbol type)
        {
            string? ArgumentOf(INamedTypeSymbol? definition) =>
                type is INamedTypeSymbol { IsGenericType: true } generic && Is(generic.OriginalDefinition, definition) ? MetadataName(generic.TypeArguments[0]) : null;

            return new TypeFacts(
                MetadataName(type),
                IsEvent: type.AllInterfaces.Any(i => Is(i, known.Event)),
                SpanOf: ArgumentOf(known.ReadOnlySpan),
                MemoryOf: ArgumentOf(known.ReadOnlyMemory),
                IsCancellationToken: Is(type, known.CancellationToken),
                TransitionInfoOf: ArgumentOf(known.TransitionInfo));
        }

        private IReadOnlyList<string> OutcomesOf(INamedTypeSymbol decision)
        {
            var decide = decision.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.Name is "Decide" or "DecideAsync");
            var union = decide?.ReturnType as INamedTypeSymbol;
            if (union is { IsGenericType: true } && (Is(union.OriginalDefinition, known.ValueTaskOfT) || Is(union.OriginalDefinition, known.TaskOfT)))
            {
                union = union.TypeArguments[0] as INamedTypeSymbol;
            }

            if (union is null || !union.GetAttributes().Any(a => a.AttributeClass is { } c && MetadataName(c) == "System.Runtime.CompilerServices.UnionAttribute"))
            {
                return [];
            }

            return union.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 1)
                .Select(c => MetadataName(c.Parameters[0].Type))
                .ToList();
        }

        private MachineOptions Options(ConcurrencyMode concurrency, int inbox, PurityMode purity, UnhandledMode unhandled)
        {
            var value = _value;
            var underlying = value is INamedTypeSymbol { EnumUnderlyingType: { } enumType } ? enumType : value;
            ValueDomain? domain = underlying?.SpecialType switch
            {
                SpecialType.System_Byte => new ValueDomain(byte.MinValue, byte.MaxValue),
                SpecialType.System_SByte => new ValueDomain(sbyte.MinValue, sbyte.MaxValue),
                SpecialType.System_Int16 => new ValueDomain(short.MinValue, short.MaxValue),
                SpecialType.System_UInt16 => new ValueDomain(ushort.MinValue, ushort.MaxValue),
                SpecialType.System_Char => new ValueDomain(char.MinValue, char.MaxValue),
                _ => null,
            };

            if (domain is not null && value is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumSymbol)
            {
                domain = domain with
                {
                    Members = enumSymbol.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)
                        .Select(f => Convert.ToInt64(f.ConstantValue)).Distinct().OrderBy(v => v).ToList(),
                };
            }

            return new MachineOptions(
                value is null ? "System.Byte" : MetadataName(value),
                domain ?? new ValueDomain(0, 0),
                ValueTypeSupported: domain is not null,
                _context is null ? null : MetadataName(_context),
                _config is null ? null : MetadataName(_config),
                concurrency,
                inbox,
                purity,
                unhandled);
        }

        private SourceSpan Span(Location? location)
        {
            if (location is not { IsInSource: true })
            {
                return SourceSpan.None;
            }

            var line = location.GetLineSpan();
            var span = new SourceSpan(line.Path, line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1);
            _locations[span] = location;
            return span;
        }

        private bool Is(ISymbol? symbol, ISymbol? other) => other is not null && SymbolEqualityComparer.Default.Equals(symbol, other);

        private static INamedTypeSymbol? TypeArgument(AttributeData attribute, string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value as INamedTypeSymbol;

        private static int IntArgument(AttributeData attribute, string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is int value ? value : 0;

        private static long? ToInt64(TypedConstant constant) => constant.Kind == TypedConstantKind.Array ? null : constant.Value switch
        {
            byte or sbyte or short or ushort or int or uint or long or char => Convert.ToInt64(constant.Value),
            _ => null,
        };

        private static string Text(TypedConstant constant) => constant.Kind == TypedConstantKind.Array ? "array" : constant.Value?.ToString() ?? "null";

        private ReturnShape ReturnShapeOf(ITypeSymbol type) => type switch
        {
            _ when type.SpecialType == SpecialType.System_Void => ReturnShape.Void,
            _ when type.SpecialType == SpecialType.System_Boolean => ReturnShape.Bool,
            _ when Is(type, known.ValueTask) => ReturnShape.ValueTask,
            _ when Is(type, known.Task) => ReturnShape.Task,
            INamedTypeSymbol { IsGenericType: true } g when Is(g.OriginalDefinition, known.ValueTaskOfT) => ReturnShape.ValueTaskOfResult,
            INamedTypeSymbol { IsGenericType: true } g when Is(g.OriginalDefinition, known.TaskOfT) => ReturnShape.TaskOfResult,
            _ => ReturnShape.Other,
        };
    }

    private static bool IsVisible(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public && (type.ContainingType is null || IsVisible(type.ContainingType));

    /// <summary>As written in C#, without the namespace: <c>TelnetCore</c>, <c>TelnetCore.Refuse</c>.</summary>
    internal static string DisplayName(INamedTypeSymbol type) => type.ContainingType is null ? type.Name : DisplayName(type.ContainingType) + "." + type.Name;

    /// <summary>What reflection's <c>Type.FullName</c> gives for the types a machine uses: <c>Ns.Outer+Inner</c>, <c>System.Byte[]</c>.</summary>
    internal static string MetadataName(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return MetadataName(array.ElementType) + "[]";
            case INamedTypeSymbol { ContainingType: { } outer } nested:
                return MetadataName(outer) + "+" + nested.MetadataName;
            default:
                var ns = type.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() + "." : string.Empty;
                return ns + type.MetadataName;
        }
    }

    /// <summary>A parameter type's name as the reference front-end records it: generic types written out with their arguments.</summary>
    internal static string FriendlyName(ITypeSymbol type) => type is INamedTypeSymbol { IsGenericType: true } generic
        ? $"{MetadataName(generic.OriginalDefinition).Split('`')[0]}<{string.Join(", ", generic.TypeArguments.Select(FriendlyName))}>"
        : MetadataName(type);

    private sealed record TransitionDeclaration(INamedTypeSymbol Module, IMethodSymbol? Method, INamedTypeSymbol? Class, INamedTypeSymbol? From, INamedTypeSymbol? To, int Order, bool IsDecision, INamedTypeSymbol[] Handle);

    private sealed record ActionDeclaration(INamedTypeSymbol Module, IMethodSymbol Method, ActionPhase Phase, INamedTypeSymbol State, int Order, int DeclarationIndex);
}
