using System;
using System.Collections.Generic;
using System.Reflection;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>A machine's model, with the runtime types and methods its indices and method models stand for.</summary>
public sealed class ReflectedMachine
{
    private readonly Dictionary<MethodModel, MethodInfo> _methods;

    internal ReflectedMachine(MachineSpec spec, MachineModel model, IReadOnlyList<Type> stateTypes, Dictionary<MethodModel, MethodInfo> methods)
    {
        Spec = spec;
        Model = model;
        StateTypes = stateTypes;
        _methods = methods;
    }

    /// <summary>What the machine was built from.</summary>
    public MachineSpec Spec { get; }

    /// <summary>The model.</summary>
    public MachineModel Model { get; }

    /// <summary>The state struct for each state index.</summary>
    public IReadOnlyList<Type> StateTypes { get; }

    /// <summary>The method a method model stands for.</summary>
    public MethodInfo MethodOf(MethodModel method) => _methods[method];

    /// <summary>Every diagnostic: the front-end's, then the model's.</summary>
    public IReadOnlyList<ModelDiagnostic> Validate() => ModelValidator.Validate(Model);
}
