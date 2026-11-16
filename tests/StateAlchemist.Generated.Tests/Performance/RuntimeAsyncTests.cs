#if NET11_0_OR_GREATER
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StateAlchemist.Samples.Performance;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests.Performance;

/// <summary>
/// The net11.0 builds turn runtime async on (Directory.Build.targets). Runtime async ignores
/// <c>[AsyncMethodBuilder]</c> and allocates on every suspension, so the generated pooled methods opt out of it and stay
/// compiler state machines: none of them carries <see cref="MethodImplAttributes.Async"/>.
/// </summary>
public class RuntimeAsyncTests
{
    [Test]
    public async Task ThePooledMethodsAreNotRuntimeAsync()
    {
        var pooled = typeof(InlineMachine)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AsyncMethodBuilderAttribute>()?.BuilderType == typeof(PoolingAsyncValueTaskMethodBuilder))
            .ToList();

        await Assert.That(pooled).IsNotEmpty();
        await Assert.That(pooled.Where(m => (m.MethodImplementationFlags & MethodImplAttributes.Async) != 0).Select(m => m.Name)).IsEmpty();
    }

    /// <summary>The control: with runtime async on, an async method without the opt-out is runtime async.</summary>
    [Test]
    public async Task AnAsyncMethodWithoutTheOptOutIsRuntimeAsync()
    {
        var method = typeof(RuntimeAsyncTests).GetMethod(nameof(Plain), BindingFlags.NonPublic | BindingFlags.Static)!;

        await Assert.That(method.MethodImplementationFlags & MethodImplAttributes.Async).IsEqualTo(MethodImplAttributes.Async);
    }

    private static async Task Plain() => await Task.Yield();
}
#endif
