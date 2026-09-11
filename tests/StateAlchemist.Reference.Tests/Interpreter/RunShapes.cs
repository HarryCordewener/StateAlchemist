using System;
using System.Collections.Generic;

namespace StateAlchemist.Reference.Tests.Interpreter;

// A run transform may take the configuration and the context after the run (spec §6.7). The contract machines have
// no configuration, so these shapes are checked here.

public readonly record struct RunConfig(int Weight);

public sealed class RunLog
{
    public List<string> Entries { get; } = [];
}

public struct Stream : IRootState
{
    public int Total;
}

[Module]
public static class ConfiguredRuns
{
    [Transition(From = typeof(Stream)), OnRange(0, 99), Run]
    public static void Low(ref Stream self, ReadOnlySpan<byte> run, in RunConfig config) => self.Total += run.Length * config.Weight;

    [Transition(From = typeof(Stream)), OnRange(100, 255), Run]
    public static void High(ref Stream self, ReadOnlySpan<byte> run, in RunConfig config, RunLog log)
    {
        self.Total += run.Length * config.Weight * 100;
        log.Entries.Add($"high {run.Length}");
    }
}
