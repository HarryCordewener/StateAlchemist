using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StateAlchemist.Generators.Tests.Agreement;

/// <summary>
/// Writes a random, usually valid machine as C#: a random tree of states that each carry a value, transitions of
/// every kind between random states — declared on leaves and on ancestors, exact, ranged and or-else, some guarded,
/// some with a <c>Completed</c>, some runs — and <c>[Exited]</c>/<c>[Entered]</c> actions that log. Every transform names only
/// what it may: the target by <c>ref</c>, the root by <c>ref</c>, and a leaf source by <c>in</c>.
/// </summary>
internal static class RandomMachineSource
{
    public const string Namespace = "RandomMachines";

    public static string Generate(int seed)
    {
        var random = new Random(seed);
        var count = random.Next(3, 9);
        var parents = new int[count];
        parents[0] = -1;
        for (var i = 1; i < count; i++)
        {
            parents[i] = random.Next(0, i);
        }

        bool IsLeaf(int state) => !parents.Contains(state);
        var initial = Enumerable.Range(0, count).Where(s => !IsLeaf(s))
            .ToDictionary(s => s, s => Enumerable.Range(0, count).Where(c => parents[c] == s).OrderBy(_ => random.Next()).First());

        var text = new StringBuilder();
        text.AppendLine("using System;");
        text.AppendLine("using System.Collections.Generic;");
        text.AppendLine("using StateAlchemist;");
        text.AppendLine($"namespace {Namespace};");
        text.AppendLine("public sealed class Log { public readonly List<string> Entries = new List<string>(); public void Add(string entry) => Entries.Add(entry); }");
        for (var i = 0; i < count; i++)
        {
            var marker = i == 0 ? "IRootState" : $"IState<S{parents[i]}>";
            var isInitial = i > 0 && initial[parents[i]] == i;
            text.AppendLine($"{(isInitial ? "[Initial] " : "")}public struct S{i} : {marker} {{ public int Value; }}");
        }

        text.AppendLine("[Module] public static class RandomModule {");
        var transitions = random.Next(count, count * 3);
        var order = 1;
        for (var t = 0; t < transitions; t++)
        {
            var from = random.Next(count);
            var stay = random.Next(10) < 3;
            var to = stay ? -1 : random.Next(count);
            var trigger = random.Next(10) switch
            {
                < 6 => $"On({random.Next(6)})",
                < 8 => $"OnRange({random.Next(4)}, {random.Next(4, 7)})",
                _ => "OnAny",
            };
            var guarded = random.Next(4) == 0;
            var completed = random.Next(3) == 0;
            var attribute = $"[Transition(From = typeof(S{from}){(to < 0 ? "" : $", To = typeof(S{to})")}{(guarded ? $", Order = {order++}" : "")}), {trigger}]";
            var k = random.Next(1, 9);

            string parameters;
            string body;
            if (to < 0)
            {
                parameters = $"ref S{from} self";
                body = $"self.Value += {k};";
            }
            else
            {
                var list = new List<string> { $"ref S{to} to" };
                var source = "0";
                if (IsLeaf(from))
                {
                    list.Insert(0, $"in S{from} from");
                    source = "from.Value";
                }

                if (to != 0 && from != 0)
                {
                    list.Add("ref S0 root");
                }

                parameters = string.Join(", ", list);
                body = $"to.Value = {source} * 3 + {k};" + (list.Contains("ref S0 root") ? " root.Value += 1;" : "");
            }

            if (!guarded && !completed)
            {
                text.AppendLine($"  {attribute} public static void T{t}({parameters}) {{ {body} }}");
                continue;
            }

            text.AppendLine($"  {attribute} public static class T{t} {{");
            if (guarded)
            {
                text.AppendLine($"    public static bool Guard(in S{from} from) => from.Value % 3 != 1;");
            }

            text.AppendLine($"    public static void Transform({parameters}) {{ {body} }}");
            if (completed)
            {
                text.AppendLine($"    public static void Completed(Log log) => log.Add(\"completed T{t}\");");
            }

            text.AppendLine("  }");
        }

        // Runs: stays on or-else or a range, taking the whole run (spec §6.7).
        for (var i = 0; i < count; i++)
        {
            if (random.Next(4) == 0)
            {
                var trigger = random.Next(2) == 0 ? "OnAny" : $"OnRange({random.Next(3)}, {random.Next(3, 7)})";
                text.AppendLine($"  [Transition(From = typeof(S{i})), {trigger}, Run] public static void Run{i}(ref S{i} self, ReadOnlySpan<byte> run) {{ self.Value += run.Length * {random.Next(2, 9)} + run[0]; }}");
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (random.Next(3) == 0)
            {
                text.AppendLine($"  [Entered(typeof(S{i}))] public static void Enter{i}(Log log, S{i} state) => log.Add(\"entered S{i} \" + state.Value);");
            }

            if (random.Next(3) == 0)
            {
                text.AppendLine($"  [Exited(typeof(S{i}))] public static void Exit{i}(Log log, S{i} state) => log.Add(\"exited S{i} \" + state.Value);");
            }
        }

        text.AppendLine("}");
        text.AppendLine("[Machine(Root = typeof(S0), Value = typeof(byte), Context = typeof(Log))]");
        text.AppendLine("[Include(typeof(RandomModule))]");
        text.AppendLine("public sealed partial class RandomMachine { }");
        return text.ToString();
    }
}
