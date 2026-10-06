using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StateAlchemist.Samples.Door;
using StateAlchemist.Samples.Phone;
using TUnit.Core;

namespace StateAlchemist.Hosting.Tests;

/// <summary>A machine run by a real Generic Host: started with it, fed from its inbox, stopped with it.</summary>
public class HostedMachineTests
{
    [Test]
    public async Task StartingTheHostStartsTheMachine()
    {
        using var host = PhoneHost();
        var phone = host.Services.GetRequiredService<PhoneCall>();
        await Assert.That(phone.Status).IsEqualTo(MachineStatus.NotStarted);

        await host.StartAsync();

        await Assert.That(phone.Status).IsEqualTo(MachineStatus.Running);
        await host.StopAsync();
    }

    /// <summary>An <c>[Entered]</c> action that throws while the machine starts fails the host's start.</summary>
    [Test]
    public async Task AMachineThatCannotStartFailsTheHostsStart()
    {
        var builder = Builder();
        builder.Services.AddHostedMachine<FailingStart, byte>(_ => new FailingStart());
        using var host = builder.Build();

        await Assert.That(async () => await host.StartAsync()).Throws<InvalidOperationException>().WithMessage("cannot start");
    }

    /// <summary>
    /// The inputs are written and the host stopped straight away: stopping fires what was queued before the
    /// machine's <c>[Exited]</c> actions run.
    /// </summary>
    [Test]
    public async Task StoppingTheHostFiresWhatIsQueuedThenStopsTheMachine()
    {
        using var host = PhoneHost();
        await host.StartAsync();

        // begin-snippet: hosting-write
        var inbox = host.Services.GetRequiredService<MachineInbox<PhoneCall, Button>>();
        await inbox.WriteAsync(Button.Lift);
        await inbox.WriteAsync(Button.Answered);
        await inbox.WriteAsync(Button.Second);
        await inbox.WriteAsync(Button.Second);
        // end-snippet

        await host.StopAsync();

        var phone = host.Services.GetRequiredService<PhoneCall>();
        await Assert.That(phone.Status).IsEqualTo(MachineStatus.Stopped);
        await Assert.That(host.Services.GetRequiredService<PhoneLog>().Entries)
            .IsEquivalentTo(new[] { "connected (call 1)", "talked for 2s" });
    }

    [Test]
    public async Task NothingIsAcceptedOnceTheHostHasStopped()
    {
        using var host = PhoneHost();
        await host.StartAsync();
        await host.StopAsync();

        var inbox = host.Services.GetRequiredService<MachineInbox<PhoneCall, Button>>();

        await Assert.That(inbox.TryWrite(Button.Lift)).IsFalse();
        await Assert.That(async () => await inbox.WriteAsync(Button.Lift)).Throws<ChannelClosedException>();
    }

    /// <summary>An inbox of events, fired through the delegate the registration was given.</summary>
    [Test]
    public async Task AnInboxCanHoldEvents()
    {
        var access = new Access();
        using var host = DoorHost(access);
        await host.StartAsync();

        await host.Services.GetRequiredService<MachineInbox<CardDoor, Badge>>().WriteAsync(new Badge(7));
        await host.StopAsync();

        await Assert.That(access.Log).IsEquivalentTo(new[] { "opened for badge 7" });
    }

    /// <summary>
    /// A bounded inbox fills while the machine waits on a decision, and the next write has to wait: the machine's
    /// backpressure reaches the producers.
    /// </summary>
    [Test]
    public async Task ABoundedInboxFillsWhileTheMachineIsBusy()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var access = new Access
        {
            Check = async (_, cancellation) =>
            {
                asked.TrySetResult();
                return await answer.Task.WaitAsync(cancellation);
            },
        };
        using var host = DoorHost(access, capacity: 1);
        await host.StartAsync();
        var inbox = host.Services.GetRequiredService<MachineInbox<CardDoor, Badge>>();

        await inbox.WriteAsync(new Badge(1));
        await asked.Task;                                // the service has taken it, and the reader is thinking
        await Assert.That(inbox.TryWrite(new Badge(2))).IsTrue();
        await Assert.That(inbox.TryWrite(new Badge(3))).IsFalse();

        answer.SetResult(true);
        await host.StopAsync();

        await Assert.That(access.Log).IsEquivalentTo(new[] { "opened for badge 1" });
    }

    /// <summary>
    /// A decision that never answers does not hold the host past its shutdown timeout: the machine is stopped,
    /// which cancels the decision, and the service ends without a fault.
    /// </summary>
    [Test]
    public async Task TheShutdownTimeoutStopsAMachineWaitingOnADecision()
    {
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var access = new Access
        {
            Check = async (_, cancellation) =>
            {
                asked.TrySetResult();
                return await new TaskCompletionSource<bool>().Task.WaitAsync(cancellation);
            },
        };
        using var host = DoorHost(access, shutdownTimeout: TimeSpan.FromMilliseconds(100));
        await host.StartAsync();
        await host.Services.GetRequiredService<MachineInbox<CardDoor, Badge>>().WriteAsync(new Badge(1));
        await asked.Task;

        await host.StopAsync();

        await Assert.That(host.Services.GetRequiredService<CardDoor>().Status).IsEqualTo(MachineStatus.Stopped);
        var service = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().Single();
        await service.ExecuteTask!;
    }

    /// <summary>An exception from firing ends the service, and the host's default is to stop.</summary>
    [Test]
    public async Task AFailureToFireStopsTheHost()
    {
        var builder = Builder();
        builder.Services.AddSingleton<PhoneLog>();
        builder.Services.AddHostedMachine<PhoneCall, Button, Button>(
            services => new PhoneCall(services.GetRequiredService<PhoneLog>()),
            (_, _) => throw new InvalidOperationException("broken"));
        using var host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        await host.StartAsync();

        await host.Services.GetRequiredService<MachineInbox<PhoneCall, Button>>().WriteAsync(Button.Lift);

        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await host.StopAsync();
        await Assert.That(host.Services.GetRequiredService<PhoneCall>().Status).IsEqualTo(MachineStatus.Stopped);
    }

    [Test]
    public async Task AMachineTypeIsRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddHostedMachine<PhoneCall, Button>(_ => new PhoneCall(new PhoneLog()));

        await Assert.That(() => services.AddHostedMachine<PhoneCall, Button>(_ => new PhoneCall(new PhoneLog())))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ACapacityIsAtLeastOne()
    {
        var services = new ServiceCollection();

        await Assert.That(() => services.AddHostedMachine<PhoneCall, Button>(_ => new PhoneCall(new PhoneLog()), capacity: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new MachineInbox<PhoneCall, Button>(0)).Throws<ArgumentOutOfRangeException>();
    }

    private static HostApplicationBuilder Builder()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.ClearProviders();
        return builder;
    }

    private static IHost PhoneHost()
    {
        var builder = Builder();

        // begin-snippet: hosting-register
        builder.Services.AddSingleton<PhoneLog>();
        builder.Services.AddHostedMachine<PhoneCall, Button>(
            services => new PhoneCall(services.GetRequiredService<PhoneLog>()));
        // end-snippet

        return builder.Build();
    }

    private static IHost DoorHost(Access access, int? capacity = null, TimeSpan? shutdownTimeout = null)
    {
        var builder = Builder();
        if (shutdownTimeout is { } timeout)
        {
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = timeout);
        }

        // begin-snippet: hosting-events
        builder.Services.AddHostedMachine<CardDoor, byte, Badge>(
            _ => new CardDoor(access),
            (door, badge) => door.FireAsync(badge),
            capacity);
        // end-snippet

        return builder.Build();
    }
}
