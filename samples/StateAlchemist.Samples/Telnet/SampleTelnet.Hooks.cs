using System;

namespace StateAlchemist.Samples.Telnet;

// The hooks the documentation shows. Neither changes what the sample does: every value has a transition somewhere
// on the path, and no Completed action throws.

// begin-snippet: sample-unhandled-hook
public sealed partial class SampleTelnet
{
    partial void OnUnhandled(StateId state, byte value) => Enqueue(new Error());
}
// end-snippet

// begin-snippet: sample-exception-hook
public sealed partial class SampleTelnet
{
    partial void OnCompletedException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution)
    {
        Console.Error.WriteLine($"{transition} failed after the state changed: {exception.Message}");
        resolution = ExceptionResolution.Continue;      // keep going with the remaining actions
    }
}
// end-snippet
