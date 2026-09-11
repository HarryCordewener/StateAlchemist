namespace StateAlchemist.Model;

/// <summary>A state in a machine.</summary>
/// <param name="Index">Its position in <see cref="MachineModel.States"/>.</param>
/// <param name="TypeName">The state struct's full name.</param>
/// <param name="Parent">The parent's index, or −1 for a root.</param>
/// <param name="IsInitial">Whether it is marked <c>[Initial]</c>.</param>
/// <param name="HasData">Whether it declares instance fields.</param>
/// <param name="HasReset">Whether it declares <c>public void Reset()</c>.</param>
/// <param name="IsPublicStruct">Whether it is a public struct.</param>
/// <param name="ParentMarkers">How many parent markers (<c>IState&lt;T&gt;</c>, <c>IRootState</c>) it implements.</param>
/// <param name="Location">Where it is declared.</param>
public sealed record StateModel(
    int Index,
    string TypeName,
    int Parent,
    bool IsInitial,
    bool HasData,
    bool HasReset,
    bool IsPublicStruct,
    int ParentMarkers,
    SourceSpan Location)
{
    /// <summary>Whether it is a root.</summary>
    public bool IsRoot => Parent < 0;

    /// <summary>The short name, after the last <c>.</c> or <c>+</c>.</summary>
    public string Name
    {
        get
        {
            var at = TypeName.LastIndexOfAny(['.', '+']);
            return at < 0 ? TypeName : TypeName.Substring(at + 1);
        }
    }
}
