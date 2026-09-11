using System.Text;

namespace StateAlchemist.Generators.Tests.Performance;

/// <summary>
/// A machine the size of TNC's (spec §9): a root, <see cref="Groups"/> groups of <see cref="LeavesPerGroup"/> leaves —
/// 81 states — and 1,000 transitions: every leaf stays on some values and moves to a leaf in the next group on others,
/// every group moves on its own values, the first leaf captures text as a run, and every group has actions on the way
/// in and out. Nothing about it is realistic but its size.
/// </summary>
internal static class TncSizedMachineSource
{
    public const string Namespace = "TncSized";
    public const int Groups = 8;
    public const int LeavesPerGroup = 9;
    public const int StaysPerLeaf = 6;
    public const int MovesPerLeaf = 7;

    public const int States = 1 + Groups + Groups * LeavesPerGroup;

    /// <summary>Leaf transitions, group transitions, and the run: 993.</summary>
    public const int Transitions = Groups * LeavesPerGroup * (StaysPerLeaf + MovesPerLeaf) + Groups * (Groups - 1) + 1;

    public static string Generate()
    {
        var text = new StringBuilder();
        text.AppendLine("using System;");
        text.AppendLine("using StateAlchemist;");
        text.AppendLine($"namespace {Namespace};");
        text.AppendLine("public sealed class Log { public int Count; }");
        text.AppendLine("public struct Root : IRootState { public int Moves; }");
        for (var g = 0; g < Groups; g++)
        {
            text.AppendLine($"{(g == 0 ? "[Initial] " : "")}public struct G{g} : IState<Root> {{ public int Visits; }}");
            for (var l = 0; l < LeavesPerGroup; l++)
            {
                text.AppendLine($"{(l == 0 ? "[Initial] " : "")}public struct L{g}_{l} : IState<G{g}> {{ public int Count; public int Length; }}");
            }
        }

        text.AppendLine("[Module] public static class TncSizedModule {");
        for (var g = 0; g < Groups; g++)
        {
            var next = (g + 1) % Groups;
            for (var l = 0; l < LeavesPerGroup; l++)
            {
                var leaf = $"L{g}_{l}";
                for (var v = 0; v < StaysPerLeaf; v++)
                {
                    text.AppendLine($"  [Transition(From = typeof({leaf})), On({v})] public static void Stay{leaf}_{v}(ref {leaf} self) => self.Count += {v + 1};");
                }

                for (var v = 0; v < MovesPerLeaf; v++)
                {
                    var target = $"L{next}_{(l + v) % LeavesPerGroup}";
                    text.AppendLine($"  [Transition(From = typeof({leaf}), To = typeof({target})), On({StaysPerLeaf + v})] public static void Move{leaf}_{v}(in {leaf} from, ref {target} to, ref Root root) {{ to.Count = from.Count + {v}; root.Moves++; }}");
                }
            }

            // A group moves to the initial leaf of any other group on a value of its own, whichever leaf is active.
            for (var other = 0; other < Groups; other++)
            {
                if (other != g)
                {
                    text.AppendLine($"  [Transition(From = typeof(G{g}), To = typeof(G{other})), On({100 + other})] public static void Jump{g}_{other}(ref G{other} to) => to.Visits++;");
                }
            }

            text.AppendLine($"  [Entered(typeof(G{g}))] public static void Enter{g}(Log log) => log.Count++;");
            text.AppendLine($"  [Exited(typeof(G{g}))] public static void Exit{g}(Log log) => log.Count++;");
        }

        text.AppendLine("  [Transition(From = typeof(L0_0)), OnRange(32, 99), Run] public static void Capture(ref L0_0 self, ReadOnlySpan<byte> run) => self.Length += run.Length;");
        text.AppendLine("}");
        text.AppendLine("[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Log))]");
        text.AppendLine("[Include(typeof(TncSizedModule))]");
        text.AppendLine("public sealed partial class TncSizedMachine { public static object Create(object log) => new TncSizedMachine((Log)log); }");
        return text.ToString();
    }
}
