using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>A declared method: a phase of a transition or decision, or a state action.</summary>
/// <param name="DeclaringType">Its declaring type as written, such as <c>TelnetCore</c> or <c>TelnetCore.Refuse</c>.</param>
/// <param name="Name">Its name.</param>
/// <param name="Returns">What it returns.</param>
/// <param name="IsStatic">Whether it is static.</param>
/// <param name="IsPublic">Whether it and its declaring types are public.</param>
/// <param name="Parameters">Its parameters.</param>
/// <param name="Location">Where it is declared.</param>
public sealed record MethodModel(
    string DeclaringType,
    string Name,
    ReturnShape Returns,
    bool IsStatic,
    bool IsPublic,
    IReadOnlyList<ParameterModel> Parameters,
    SourceSpan Location)
{
    /// <summary><c>DeclaringType.Name</c>.</summary>
    public string FullName => DeclaringType + "." + Name;

    /// <summary>Whether it returns a task.</summary>
    public bool IsAsync => Returns is ReturnShape.ValueTask or ReturnShape.ValueTaskOfResult or ReturnShape.Task or ReturnShape.TaskOfResult;
}
