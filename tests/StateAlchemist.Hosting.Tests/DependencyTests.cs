using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Hosting.Tests;

/// <summary>Hosting is where Microsoft.Extensions comes in; the core package still depends on nothing.</summary>
public class DependencyTests
{
    [Test]
    public async Task HostingReferencesTheRuntimeAndHostingAbstractionsOnly()
    {
        var foreign = typeof(MachineInbox<,>).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System", System.StringComparison.Ordinal))
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();

        await Assert.That(foreign).IsEquivalentTo(new[]
        {
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "StateAlchemist",
        });
    }
}
