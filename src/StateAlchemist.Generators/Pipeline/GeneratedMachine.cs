namespace StateAlchemist.Generators;

/// <summary>What the generator produces for one <c>[Machine]</c>: its source, if it has no errors, and its diagnostics.</summary>
internal sealed record GeneratedMachine(string HintName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);
