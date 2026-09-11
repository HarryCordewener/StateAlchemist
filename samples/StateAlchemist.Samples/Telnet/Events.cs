namespace StateAlchemist.Samples.Telnet;

// begin-snippet: sample-event
/// <summary>Something went wrong; recover to <see cref="Idle"/>.</summary>
public readonly struct Error : IEvent
{
}
// end-snippet
