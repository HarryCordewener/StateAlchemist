using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>Runs every check over a model. The generator reports the result as compiler diagnostics; the reference interpreter refuses a model with errors.</summary>
public static class ModelValidator
{
    /// <summary>
    /// Every diagnostic, front-end ones first. Checks that depend on the value type are skipped when it is unsupported,
    /// and checks that need a valid tree are skipped when the hierarchy is invalid, so one root cause is reported once.
    /// </summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model)
    {
        var diagnostics = new List<ModelDiagnostic>(model.FrontEndDiagnostics);
        diagnostics.AddRange(ShapeValidator.Validate(model));
        diagnostics.AddRange(TriggerValidator.Validate(model));
        if (!model.Options.ValueTypeSupported)
        {
            // Every value parameter would fail to bind; SALCH0105 is the one diagnostic worth reading.
            return diagnostics;
        }

        var hierarchyProblems = HierarchyValidator.Validate(model.States);
        diagnostics.AddRange(hierarchyProblems);
        if (hierarchyProblems.Count > 0 || model.States.Count == 0)
        {
            return diagnostics;
        }

        var hierarchy = new Hierarchy(model.States);
        diagnostics.AddRange(ConflictValidator.Validate(model));
        diagnostics.AddRange(BindingValidator.Validate(model));
        diagnostics.AddRange(RoleValidator.Validate(model, hierarchy));
        diagnostics.AddRange(RunValidator.Validate(model, hierarchy));
        diagnostics.AddRange(CoverageValidator.Validate(model, hierarchy));
        diagnostics.AddRange(ReachabilityValidator.Validate(model, hierarchy));
        return diagnostics;
    }
}
