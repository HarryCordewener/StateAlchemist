extern alias generator;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Core;
using MachineGenerator = generator::StateAlchemist.Generators.MachineGenerator;
using Generated = generator::StateAlchemist.Model;
using SymbolModelBuilder = generator::StateAlchemist.Generators.SymbolModelBuilder;

namespace StateAlchemist.Generators.Tests.Generation;

/// <summary>
/// D25: a library offers its modules with <c>[assembly: ExportsModule]</c>, and a machine takes what its
/// references offer with <c>[IncludeExported]</c> — "add a package, get a protocol", without a machine ever
/// receiving a module that did not offer itself.
/// </summary>
public class ExportedModuleTests
{
    /// <summary>
    /// The declarations every test shares, with <paramref name="exports"/> where C# requires assembly attributes
    /// to be — before any type — and <paramref name="machine"/> naming the modules however the test wants.
    /// </summary>
    private static string Source(string exports, string machine) => $$"""
        using System;
        using StateAlchemist;
        {{exports}}
        public struct Root : IRootState { public int Moves; }
        [Initial] public struct Idle : IState<Root> { }
        public struct Busy : IState<Root> { }
        [Module] public static class CoreModule
        {
            [Transition(From = typeof(Idle), To = typeof(Busy)), On(1)]
            public static void Start(ref Root root) => root.Moves++;
        }
        [Module] public static class ExtraModule
        {
            [Transition(From = typeof(Busy), To = typeof(Idle)), On(2)]
            public static void Stop(ref Root root) => root.Moves++;
        }
        [Module] public static class AlsoModule
        {
            [Transition(From = typeof(Busy)), On(3)]
            public static void Stay(ref Busy self) { }
        }
        public static class NotAModule { }
        [Machine(Root = typeof(Root), Value = typeof(byte))]
        {{machine}}
        public sealed partial class Machine { }
        """;

    [Test]
    public async Task AnExportedModuleJoinsAMachineThatAsksForIt()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start", "ExtraModule.Stop" });
    }

    [Test]
    public async Task AnExportedModuleStaysOutOfAMachineThatDidNotAsk()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start" });
    }

    [Test]
    public async Task ExceptLeavesOneOut()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]\n[assembly: ExportsModule(typeof(AlsoModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported(Except = new[] { typeof(AlsoModule) })]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start", "ExtraModule.Stop" });
    }

    /// <summary>A module named both ways is included once: an explicit include is not a duplicate.</summary>
    [Test]
    public async Task AModuleNamedTwiceIsIncludedOnce()
    {
        var machine = Build(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule)), Include(typeof(ExtraModule))]\n[IncludeExported]"));
        await Assert.That(machine.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "CoreModule.Start", "ExtraModule.Stop" });
    }

    [Test]
    public async Task ExportingSomethingThatIsNotAModuleIsSALCH0108()
    {
        var reported = Diagnostics(Source("[assembly: ExportsModule(typeof(NotAModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        await Assert.That(reported).Contains("SALCH0108");
    }

    [Test]
    public async Task AskingWhenNothingIsExportedIsSALCH0109()
    {
        var reported = Diagnostics(Source("", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        await Assert.That(reported).Contains("SALCH0109");
    }

    /// <summary>
    /// The machine an exported include builds is the machine an explicit include builds: same states, same
    /// transitions, same order. Anything else would make the two ways of naming a module mean different things.
    /// </summary>
    [Test]
    public async Task AnExportedIncludeBuildsTheSameMachineAsAnExplicitOne()
    {
        var exported = Text(Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]"));
        var explicitly = Text(Source("", "[Include(typeof(CoreModule)), Include(typeof(ExtraModule))]"));
        await Assert.That(exported).IsEqualTo(explicitly);
    }

    /// <summary>And the reference front-end, reflecting over the same declarations, agrees with all of it.</summary>
    [Test]
    public async Task TheReferenceFrontEndReadsExportedModulesTheSameWay()
    {
        var source = Source("[assembly: ExportsModule(typeof(ExtraModule))]", "[Include(typeof(CoreModule))]\n[IncludeExported]");
        var byRoslyn = Text(source);

        var reflected = Reference.ReflectionModelBuilder.FromMachine(Compile(source).GetType("Machine")!);
        await Assert.That(Model.ModelText.Of(reflected.Model)).IsEqualTo(byRoslyn);
    }

    private static Generated.MachineModel Build(string source)
    {
        var compilation = Compilation(source);
        return SymbolModelBuilder.Build(compilation.GetTypeByMetadataName("Machine")!, compilation).Model;
    }

    /// <summary>
    /// The source, compiled and checked: an assembly attribute in the wrong place is a C# error, and a test that
    /// did not look would silently prove nothing.
    /// </summary>
    private static CSharpCompilation Compilation(string source)
    {
        var compilation = TestCompilation.Create(source);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal)).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("the test's own source does not compile:\n" + string.Join("\n", errors) + "\n" + source);
        }

        return compilation;
    }

    private static string Text(string source) => Generated.ModelText.Of(Build(source));

    private static string[] Diagnostics(string source)
    {
        var compilation = Compilation(source);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics.Select(d => d.Id).ToArray();
    }

    /// <summary>Compiles <paramref name="source"/> with the generator and loads it, so reflection can read it.</summary>
    private static Assembly Compile(string source)
    {
        var compilation = TestCompilation.Create(LanguageVersion.Latest, TestCompilation.Net8Symbols, source);
        CSharpGeneratorDriver
            .Create([new MachineGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        using var image = new MemoryStream();
        var emitted = generated.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        image.Position = 0;
        return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(image);
    }
}
