using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/decisions.md, "Deferral and backpressure": the ordinary read loop, and a real pipe.</summary>
public abstract class BackpressureContract : MachineContract
{
    /// <summary>The loop the docs show, with nothing added.</summary>
    private static async Task ReadLoopAsync(PipeReader reader, IMachine<byte> machine)
    {
        while (true)
        {
            var read = await reader.ReadAsync();
            foreach (var segment in read.Buffer)
            {
                await machine.FireAsync(segment);
            }

            reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted)
            {
                break;
            }
        }

        await reader.CompleteAsync();
    }

    [Test]
    public async Task WhileADecisionIsPendingThePipeStopsTheWriter()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Deciding, context);
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 8, resumeWriterThreshold: 4, useSynchronizationContext: false));
        var reading = ReadLoopAsync(pipe.Reader, machine);

        await pipe.Writer.WriteAsync(new byte[] { 2 });
        if (await Task.WhenAny(context.Deciding.Task, reading) == reading)
        {
            await reading; // the loop ended or failed without the decision starting: surface that, rather than wait
        }

        var writing = pipe.Writer.WriteAsync(Enumerable.Repeat((byte)1, 16).ToArray()).AsTask();
        await Task.Delay(100);
        await Assert.That(writing.IsCompleted).IsFalse();
        await Assert.That(machine.TryGetState(out DecideRoot waiting) ? waiting.Ticks : -1).IsEqualTo(0);

        context.Answer.SetResult(new Verdict(new Accept("ann")));
        await writing;
        await pipe.Writer.CompleteAsync();
        await reading;
        await Assert.That(machine.TryGetState(out DecideRoot done) ? done.Ticks : -1).IsEqualTo(16);
    }
}
