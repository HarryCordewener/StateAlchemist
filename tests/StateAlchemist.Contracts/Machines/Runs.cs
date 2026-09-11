using System;

namespace StateAlchemist.Contracts.Machines.Runs;

// RunRoot ─┬─ Text [Initial]
//          └─ Escape

public struct RunRoot : IRootState
{
    public int Lines;
}

[Initial]
public struct Text : IState<RunRoot>
{
    public int Length;
}

public struct Escape : IState<RunRoot>;

/// <summary>Text arrives in runs: everything up to the next line feed, bell or IAC is one call.</summary>
[Module]
public static class RunModule
{
    /// <summary>Any value: appended to the line, a whole run at a time.</summary>
    [Transition(From = typeof(Text)), OnAny, Run]
    public static class Append
    {
        public static void Transform(ref Text self, ReadOnlySpan<byte> run, RecordingContext context)
        {
            self.Length += run.Length;
            context.Record($"run {run.Length}");
        }

        public static void Completed(RecordingContext context, ReadOnlyMemory<byte> run) => context.Record($"appended {run.Length}");
    }

    /// <summary>A line feed ends the line.</summary>
    [Transition(From = typeof(Text)), On(10)]
    public static void Line(ref Text self, ref RunRoot root)
    {
        self.Length = 0;
        root.Lines++;
    }

    /// <summary>A bell rings only when allowed; otherwise it falls through to the run, as a run of one.</summary>
    [Transition(From = typeof(Text)), On(7)]
    public static class Bell
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Bell");

        public static void Completed(RecordingContext context) => context.Record("bell");
    }

    /// <summary>IAC leaves the text.</summary>
    [Transition(From = typeof(Text), To = typeof(Escape)), On(255)]
    public static void Iac(in Text from)
    {
    }

    /// <summary>Anything after IAC returns to a fresh line.</summary>
    [Transition(From = typeof(Escape), To = typeof(Text)), OnAny]
    public static void Back()
    {
    }
}
