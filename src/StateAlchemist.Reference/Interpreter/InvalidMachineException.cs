using System;
using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>A machine with errors cannot be interpreted — just as it could not be generated.</summary>
public sealed class InvalidMachineException : Exception
{
    /// <summary>Creates the exception from the errors.</summary>
    public InvalidMachineException(string machine, IReadOnlyList<ModelDiagnostic> errors)
        : base($"Machine '{machine}' has errors:\n" + string.Join("\n", errors.Select(e => $"  {e.Id}: {e.Message}")))
    {
        Errors = errors;
    }

    /// <summary>The errors.</summary>
    public IReadOnlyList<ModelDiagnostic> Errors { get; }
}
