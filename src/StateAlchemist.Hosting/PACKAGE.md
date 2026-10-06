# StateAlchemist.Hosting

Runs a [StateAlchemist](https://www.nuget.org/packages/StateAlchemist) machine for the lifetime of a .NET Generic
Host. The machine is started with the host, fed from a channel one input at a time, and stopped on shutdown.

```csharp
builder.Services.AddSingleton<PhoneLog>();
builder.Services.AddHostedMachine<PhoneCall, Button>(
    services => new PhoneCall(services.GetRequiredService<PhoneLog>()));
```

Anything that wants to fire the machine asks for its inbox:

```csharp
public sealed class Handset(MachineInbox<PhoneCall, Button> inbox)
{
    public ValueTask Lift() => inbox.WriteAsync(Button.Lift);
}
```

The core `StateAlchemist` package has no dependencies; this one adds `Microsoft.Extensions.Hosting.Abstractions`.

Documentation: https://github.com/HarryCordewener/StateAlchemist/blob/main/docs/guides/hosting.md
