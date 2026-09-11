namespace StateAlchemist.Model;

/// <summary>How serious a diagnostic is.</summary>
public enum Severity
{
    Error,
    Warning,
    Info,
    Hidden,
}

/// <summary>A kind of problem the analysis reports.</summary>
/// <param name="Id">Its identifier, <c>SALCH</c> and four digits.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Title">A short title.</param>
/// <param name="MessageFormat">The message, with <c>{0}</c>-style placeholders.</param>
/// <param name="ReportedIn">Where it is reported: <c>declaring library</c>, <c>app</c> or <c>anywhere</c>.</param>
public sealed record DiagnosticDescriptor(string Id, Severity Severity, string Title, string MessageFormat, string ReportedIn);
