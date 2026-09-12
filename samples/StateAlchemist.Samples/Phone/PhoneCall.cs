using System.Collections.Generic;

namespace StateAlchemist.Samples.Phone;

// Stateless's phone-call example, written for StateAlchemist so the two can be read side by side. It is the
// smallest machine worth having: a handful of states, one trigger each, and data that belongs to a state.

/// <summary>What the machine talks to: the call's history, so a test can see what happened.</summary>
public sealed class PhoneLog
{
    /// <summary>What happened, in order.</summary>
    public List<string> Entries { get; } = [];
}

// begin-snippet: sample-phone-triggers
/// <summary>
/// The triggers. A machine whose triggers are a fixed set names them in an enum and fires that: the compiler then
/// knows every trigger there is, and can say which states ignore which.
/// </summary>
public enum Button : byte
{
    /// <summary>Lift the handset.</summary>
    Lift = 1,

    /// <summary>The other end answers.</summary>
    Answered,

    /// <summary>Hold, or take off hold.</summary>
    Hold,

    /// <summary>Hang up.</summary>
    HangUp,
}
// end-snippet

// begin-snippet: sample-phone-states
/// <summary>The phone, and what it remembers for as long as it is switched on.</summary>
public struct Phone : IRootState
{
    /// <summary>How many calls have connected.</summary>
    public int Calls;
}

/// <summary>On the hook. Where the machine starts.</summary>
[Initial]
public struct OnHook : IState<Phone>
{
}

/// <summary>Off the hook, waiting for an answer.</summary>
public struct Ringing : IState<Phone>
{
}

/// <summary>
/// Talking. The seconds belong to this state, so putting the call on hold loses them: hold leaves
/// <c>Talking</c>. Data that has to survive a hold belongs in a state above both, which is what the crossing
/// example shows.
/// </summary>
public struct Talking : IState<Phone>
{
    /// <summary>Seconds this call has lasted.</summary>
    public int Seconds;
}

/// <summary>On hold, still connected.</summary>
public struct OnHold : IState<Phone>
{
}
// end-snippet

// begin-snippet: sample-phone-module
/// <summary>The transitions. A method is the whole transition when all it does is change data.</summary>
[Module]
public static class PhoneModule
{
    [Transition(From = typeof(OnHook), To = typeof(Ringing)), On(Button.Lift)]
    public static void Lift()
    {
    }

    [Transition(From = typeof(Ringing), To = typeof(Talking)), On(Button.Answered)]
    public static void Answer(ref Phone phone, ref Talking call)
    {
        phone.Calls++;
        call.Seconds = 0;
    }

    [Transition(From = typeof(Talking), To = typeof(OnHold)), On(Button.Hold)]
    public static void Hold()
    {
    }

    [Transition(From = typeof(OnHold), To = typeof(Talking)), On(Button.Hold)]
    public static void Resume()
    {
    }

    /// <summary>Hanging up works from anywhere: declared on the root, it applies to every state beneath it.</summary>
    [Transition(From = typeof(Phone), To = typeof(OnHook)), On(Button.HangUp)]
    public static void HangUp()
    {
    }

    /// <summary>A button that does not apply where the phone is. Without this, the compiler asks about it.</summary>
    [Transition(From = typeof(Phone)), OnAny]
    public static void Ignore()
    {
    }

    /// <summary>An action: past tense, so it runs after the state has changed.</summary>
    [Entered(typeof(Talking))]
    public static void Answered(PhoneLog log, Phone phone) => log.Entries.Add($"connected (call {phone.Calls})");

    /// <summary>Reads the state being left, which is still readable here and cleared straight after.</summary>
    [Exited(typeof(Talking))]
    public static void Ended(PhoneLog log, Talking call) => log.Entries.Add($"talked for {call.Seconds}s");
}
// end-snippet

// begin-snippet: sample-phone-machine
/// <summary>The machine: a root, the trigger type, and the modules this application wants.</summary>
[Machine(Root = typeof(Phone), Value = typeof(Button), Context = typeof(PhoneLog))]
[Include(typeof(PhoneModule))]
public sealed partial class PhoneCall;
// end-snippet
