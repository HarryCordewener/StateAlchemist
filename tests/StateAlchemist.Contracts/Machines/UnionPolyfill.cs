#if !NET11_0_OR_GREATER
// The two types the C# 15 compiler needs to treat a type as a union. .NET 11 ships them; below it, a library
// declares them itself, internally, as the language allows — which is what a declaring library targeting net8.0 does.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class UnionAttribute : Attribute;

    internal interface IUnion
    {
        object? Value { get; }
    }
}
#endif
