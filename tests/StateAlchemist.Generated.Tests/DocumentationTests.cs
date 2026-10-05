using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// The documentation, checked against the code it describes. The docs' code is a snippet taken from the compiled
/// samples and tests, and cannot drift; what is left is the few blocks that cannot compile, each saying why, and
/// the table of generated members.
/// </summary>
public class DocumentationTests
{
    private static string Docs([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "docs"));

    /// <summary>
    /// Every C# block in the docs is a snippet from compiled code, or says why it cannot be one. A block written into
    /// the prose instead compiles nowhere, and drifts the first time the API it shows changes.
    /// </summary>
    [Test]
    public async Task EveryCodeBlockInTheDocsIsCompiledOrSaysWhyNot()
    {
        var unexplained = new List<string>();
        foreach (var (file, block) in HandWritten())
        {
            if (block.WhyNotCompiled is null)
            {
                unexplained.Add($"{Path.GetFileName(file)}:\n{block.Code}");
            }
        }

        await Assert.That(unexplained).IsEmpty().Because(
            "take each block from a sample or a test with `snippet: name`, or precede it with `<!-- not compiled: why -->`:\n"
            + string.Join("\n", unexplained));
    }

    /// <summary>The blocks that cannot compile are still C#: a block that does not parse is a typo in prose.</summary>
    [Test]
    public async Task EveryHandWrittenCodeBlockParses()
    {
        var checked_ = 0;
        foreach (var (file, block) in HandWritten())
        {
            checked_++;
            var errors = Parse(block.Code);
            await Assert.That(errors).IsEmpty().Because($"{Path.GetFileName(file)}:\n{block.Code}\n{string.Join("\n", errors)}");
        }

        await Assert.That(checked_).IsGreaterThan(0).Because("the docs keep a few blocks that cannot compile; this test must be reading them");
    }

    /// <summary>The docs' hand-written C# blocks, outside the specs and plans, which quote code not meant to stand alone.</summary>
    private static IEnumerable<(string File, Block Block)> HandWritten()
    {
        foreach (var file in Directory.EnumerateFiles(Docs(), "*.md", SearchOption.AllDirectories).OrderBy(f => f))
        {
            if (file.Contains("superpowers"))
            {
                continue;
            }

            foreach (var block in Blocks(File.ReadAllText(file)))
            {
                yield return (file, block);
            }
        }
    }

    /// <summary>
    /// What a block has to be valid as: a file, a class member, a statement, or an attribute. A docs block is often
    /// a fragment — one method, or two lines of a read loop — valid where the prose puts it.
    /// </summary>
    private static IReadOnlyList<string> Parse(string block)
    {
        var attempts = new[]
        {
            block,
            "class DocBlock {\n" + block + "\n}",
            "class DocBlock { void Method() {\n" + block + "\n} }",
            "class DocBlock {\n" + block + "\nvoid Member() { }\n}", // an attribute the prose shows on its own
        };
        var errors = new List<string>();
        foreach (var attempt in attempts)
        {
            var found = CSharpSyntaxTree.ParseText(attempt, new CSharpParseOptions(LanguageVersion.Preview))
                .GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            if (found.Count == 0)
            {
                return [];
            }

            errors = found;
        }

        return errors;
    }

    /// <summary>
    /// <c>docs/reference/generated-api.md</c> lists what the generator adds to a machine. Every member it names is
    /// on a real generated machine — the Telnet sample's, which has states, events, runs and a decision.
    /// </summary>
    [Test]
    public async Task EveryGeneratedMemberTheReferenceNamesExists()
    {
        var reference = File.ReadAllText(Path.Combine(Docs(), "reference", "generated-api.md"));
        // The hooks are private partial methods, so they are looked up too — an application implements them.
        var members = new[] { typeof(MudTelnet), typeof(TelnetMachine), typeof(RecorderMachine) }
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(m => m.Name)
            .ToHashSet();
        members.Add(nameof(MudTelnet)); // the constructor, whose member name is .ctor

        var missing = new List<string>();
        foreach (var named in Named(reference))
        {
            if (!members.Contains(named) && !members.Any(m => m.StartsWith(named.Replace("{State}", string.Empty).Replace("{Event}", string.Empty))))
            {
                missing.Add(named);
            }
        }

        await Assert.That(missing).IsEmpty().Because("the reference names members a generated machine does not have: " + string.Join(", ", missing));
    }

    /// <summary>The names in the reference's tables: the identifier in each <c>`…`</c> signature.</summary>
    private static IEnumerable<string> Named(string reference)
    {
        foreach (Match row in Regex.Matches(reference, @"^\| `(?<signature>[^`]+)`", RegexOptions.Multiline))
        {
            foreach (Match member in Regex.Matches(row.Groups["signature"].Value, @"(?<name>[A-Z][A-Za-z]*|\{State\}|\{Event\})\s*(?=[(<]|$|,)"))
            {
                var name = member.Groups["name"].Value;
                if (name is not ("{State}" or "{Event}" or "ReadOnlyMemory" or "ReadOnlySpan" or "ValueTask" or "TransitionInfo" or "MachineDefinition" or "MachineStatus" or "StateId" or "TransitionPlan" or "TEvent" or "Exception" or "ExceptionResolution"))
                {
                    yield return name;
                }
            }
        }
    }

    /// <summary>A hand-written C# block, and the reason it gives for not being taken from compiled source.</summary>
    private sealed record Block(string Code, string? WhyNotCompiled);

    /// <summary>Hand-written C# blocks: the ones mdsnippets did not take from compiled source.</summary>
    private static IEnumerable<Block> Blocks(string markdown)
    {
        var withoutSnippets = Regex.Replace(markdown, @"<!-- snippet:.*?<!-- endSnippet -->", string.Empty, RegexOptions.Singleline);
        // Either fence label, and a closing fence on a line of its own, so a ``` inside the code does not end it.
        var pattern = @"(?:^[ \t]*<!-- not compiled: (?<why>[^\n]*?) -->\n)?^[ \t]*```(?:csharp|cs)[ \t]*\n(?<code>.*?)^[ \t]*```[ \t]*$";
        foreach (Match block in Regex.Matches(withoutSnippets, pattern, RegexOptions.Singleline | RegexOptions.Multiline))
        {
            var why = block.Groups["why"];
            yield return new Block(block.Groups["code"].Value, why.Success && why.Value.Trim().Length > 0 ? why.Value : null);
        }
    }
}
