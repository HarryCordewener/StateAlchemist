using System.Threading.Tasks;
using PublicApiGenerator;
using TUnit.Core;
using VerifyTUnit;

namespace StateAlchemist.Tests.Api;

/// <summary>
/// The whole public surface of the runtime, as text. Any change to it — a new member, a renamed parameter, a
/// changed default — fails this test until the snapshot is reviewed and accepted, so API changes are always
/// deliberate and always visible in review.
/// </summary>
public class PublicApiTests
{
    [Test]
    public async Task RuntimeApiIsUnchanged()
    {
        var api = typeof(IRootState).Assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false,
        });

        // One snapshot for every target framework: the API must not differ between them.
        await Verifier.Verify(api).UseFileName("RuntimeApi");
    }
}
