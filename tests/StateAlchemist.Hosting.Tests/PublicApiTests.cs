using System.Threading.Tasks;
using PublicApiGenerator;
using TUnit.Core;
using VerifyTUnit;

namespace StateAlchemist.Hosting.Tests;

/// <summary>The hosting package's public surface, as text, so a change to it is deliberate and visible in review.</summary>
public class PublicApiTests
{
    [Test]
    public async Task HostingApiIsUnchanged()
    {
        var api = typeof(MachineInbox<,>).Assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false,
        });

        await Verifier.Verify(api).UseFileName("HostingApi");
    }
}
