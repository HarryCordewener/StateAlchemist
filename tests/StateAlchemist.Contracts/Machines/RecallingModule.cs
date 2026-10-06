namespace StateAlchemist.Contracts.Machines.Recalling;

/// <summary>Moves into <see cref="Player"/> with and without history, recording what is entered.</summary>
[Module]
public static class RecallingModule
{
    /// <summary>1: start playing.</summary>
    [Transition(From = typeof(Stopped), To = typeof(Playing)), On(1)]
    public static void Start()
    {
    }

    /// <summary>2: faster.</summary>
    [Transition(From = typeof(Slow), To = typeof(Fast)), On(2)]
    public static void Faster()
    {
    }

    /// <summary>8: count, in <see cref="Fast"/>.</summary>
    [Transition(From = typeof(Fast)), On(8)]
    public static void Count(ref Fast self) => self.Count++;

    /// <summary>9: leave the player, from wherever in it.</summary>
    [Transition(From = typeof(Player), To = typeof(Idle)), On(9)]
    public static void Leave(ref Jukebox root) => root.Leaves++;

    /// <summary>3: back to the player, from the start.</summary>
    [Transition(From = typeof(Idle), To = typeof(Player)), On(3)]
    public static void Restart()
    {
    }

    /// <summary>4: back to the child of the player that was active.</summary>
    [Transition(From = typeof(Idle), To = typeof(Player), History = History.Shallow), On(4)]
    public static void ResumeShallow(ref Player to) => to.Resumes++;

    /// <summary>5: back to the leaf of the player that was active.</summary>
    [Transition(From = typeof(Idle), To = typeof(Player), History = History.Deep), On(5)]
    public static void ResumeDeep(ref Player to) => to.Resumes++;

    /// <summary>6: start the player over, from inside it, keeping where it was.</summary>
    [Transition(From = typeof(Player), To = typeof(Player), History = History.Deep), On(6)]
    public static void Refresh(in Player from, ref Player to) => to.Resumes = from.Resumes + 100;

    /// <summary>7: a decision whose one outcome resumes and the other starts over.</summary>
    [Decision(From = typeof(Idle)), On(7)]
    public static class Ask
    {
        public static Choice Decide(Jukebox root) => root.Leaves % 2 == 1 ? new Resume() : new Fresh();

        [To(typeof(Player), History = History.Deep)]
        public static void Complete(ref Player to, Resume outcome) => to.Resumes++;

        [To(typeof(Player))]
        public static void Complete(Fresh outcome)
        {
        }
    }

    [Entered(typeof(Player))]
    public static void EnteredPlayer(RecordingContext context) => context.Record("entered Player");

    [Entered(typeof(Stopped))]
    public static void EnteredStopped(RecordingContext context) => context.Record("entered Stopped");

    [Entered(typeof(Playing))]
    public static void EnteredPlaying(RecordingContext context) => context.Record("entered Playing");

    [Entered(typeof(Slow))]
    public static void EnteredSlow(RecordingContext context) => context.Record("entered Slow");

    [Entered(typeof(Fast))]
    public static void EnteredFast(RecordingContext context) => context.Record("entered Fast");

    [Exited(typeof(Fast))]
    public static void ExitedFast(RecordingContext context) => context.Record("exited Fast");

    [Exited(typeof(Player))]
    public static void ExitedPlayer(RecordingContext context) => context.Record("exited Player");
}
