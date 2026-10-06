namespace StateAlchemist.Samples.Radio;

// A radio that comes back on the band it was playing when it was switched off: a move by history.

/// <summary>The radio's controls.</summary>
public enum Knob : byte
{
    /// <summary>Switch on, or off.</summary>
    Power = 1,

    /// <summary>Change band.</summary>
    Band,

    /// <summary>Start over on the first band.</summary>
    Reset,
}

// begin-snippet: sample-radio-states
/// <summary>The radio.</summary>
public struct Radio : IRootState
{
}

/// <summary>Switched off. Where the machine starts.</summary>
[Initial]
public struct Off : IState<Radio>
{
}

/// <summary>Switched on, playing one band or the other.</summary>
public struct Playing : IState<Radio>
{
}

/// <summary>FM, the band a fresh start plays.</summary>
[Initial]
public struct Fm : IState<Playing>
{
}

/// <summary>AM.</summary>
public struct Am : IState<Playing>
{
}
// end-snippet

// begin-snippet: sample-radio-module
/// <summary>The transitions.</summary>
[Module]
public static class RadioModule
{
    /// <summary>Switching on enters the band that was playing when the radio was switched off.</summary>
    [Transition(From = typeof(Off), To = typeof(Playing), History = History.Deep), On(Knob.Power)]
    public static void SwitchOn()
    {
    }

    [Transition(From = typeof(Playing), To = typeof(Off)), On(Knob.Power)]
    public static void SwitchOff()
    {
    }

    [Transition(From = typeof(Fm), To = typeof(Am)), On(Knob.Band)]
    public static void ToAm()
    {
    }

    [Transition(From = typeof(Am), To = typeof(Fm)), On(Knob.Band)]
    public static void ToFm()
    {
    }

    /// <summary>Without <c>History</c>, a move into <see cref="Playing"/> enters its <c>[Initial]</c> path.</summary>
    [Transition(From = typeof(Radio), To = typeof(Playing)), On(Knob.Reset)]
    public static void Reset()
    {
    }

    /// <summary>A control that does not apply where the radio is.</summary>
    [Transition(From = typeof(Radio)), OnAny]
    public static void Ignore()
    {
    }
}
// end-snippet

/// <summary>The machine.</summary>
[Machine(Root = typeof(Radio), Value = typeof(Knob))]
[Include(typeof(RadioModule))]
public sealed partial class CarRadio;
