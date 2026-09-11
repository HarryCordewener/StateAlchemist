using System;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class ParameterClassifierTests
{
    private static readonly MachineOptions Options = new("System.Byte", ValueDomain.Byte, ContextType: "T.Context", ConfigType: "T.Config");

    private static ParameterKind Classify(TypeFacts type, params string[] outcomes) =>
        ParameterClassifier.Classify(type, Options, name => name == "T.Naws" ? 3 : -1, outcomes, out _);

    [Test]
    public async Task EachTypeBindsToWhatItIs()
    {
        await Assert.That(Classify(new TypeFacts("T.Naws"))).IsEqualTo(ParameterKind.State);
        await Assert.That(Classify(new TypeFacts("System.Byte"))).IsEqualTo(ParameterKind.Value);
        await Assert.That(Classify(new TypeFacts("System.ReadOnlySpan`1", SpanOf: "System.Byte"))).IsEqualTo(ParameterKind.Run);
        await Assert.That(Classify(new TypeFacts("System.ReadOnlyMemory`1", MemoryOf: "System.Byte"))).IsEqualTo(ParameterKind.RunMemory);
        await Assert.That(Classify(new TypeFacts("T.Context"))).IsEqualTo(ParameterKind.Context);
        await Assert.That(Classify(new TypeFacts("T.Config"))).IsEqualTo(ParameterKind.Config);
        await Assert.That(Classify(new TypeFacts("T.Error", IsEvent: true))).IsEqualTo(ParameterKind.Event);
        await Assert.That(Classify(new TypeFacts("T.Accept"), "T.Accept")).IsEqualTo(ParameterKind.Outcome);
        await Assert.That(Classify(new TypeFacts("System.Threading.CancellationToken", IsCancellationToken: true))).IsEqualTo(ParameterKind.CancellationToken);
        await Assert.That(Classify(new TypeFacts("StateAlchemist.TransitionInfo`1", TransitionInfoOf: "System.Byte"))).IsEqualTo(ParameterKind.TransitionInfo);
        await Assert.That(Classify(new TypeFacts("System.DateTime"))).IsEqualTo(ParameterKind.Unknown);
    }

    [Test]
    public async Task AStateReportsItsIndex()
    {
        ParameterClassifier.Classify(new TypeFacts("T.Naws"), Options, name => name == "T.Naws" ? 3 : -1, Array.Empty<string>(), out var state);
        await Assert.That(state).IsEqualTo(3);
    }
}
