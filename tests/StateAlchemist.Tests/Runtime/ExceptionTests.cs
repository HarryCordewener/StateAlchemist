using System;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Tests.Runtime;

public class ExceptionTests
{
    [Test]
    public async Task EveryMachineExceptionIsAnInvalidOperation()
    {
        Exception[] exceptions =
        [
            new MachineNotRunningException(MachineStatus.NotStarted),
            new ConcurrentUseException(),
            new UnhandledTriggerException(typeof(int), "31"),
        ];

        foreach (var exception in exceptions)
        {
            await Assert.That(exception is InvalidOperationException).IsTrue();
        }
    }

    [Test]
    public async Task NotRunningSaysWhichWayItIsNotRunning()
    {
        await Assert.That(new MachineNotRunningException(MachineStatus.NotStarted).Message).Contains("StartAsync");
        await Assert.That(new MachineNotRunningException(MachineStatus.Stopped).Message).Contains("stopped");
    }

    [Test]
    public async Task UnhandledNamesTheStateAndTrigger()
    {
        var e = new UnhandledTriggerException(typeof(int), "31");
        await Assert.That(e.Message).IsEqualTo("No transition handles 31 in state 'Int32'.");
    }
}
