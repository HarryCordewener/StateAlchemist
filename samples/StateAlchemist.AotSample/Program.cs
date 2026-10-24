using System;
using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Hosting;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.AotSample;

// A machine in a native binary: no reflection, no dictionaries, nothing the trimmer has to guess at. Publishing this
// with PublishAot must produce no warnings — that is what CI checks — and running it must negotiate the same way the
// sample does on the runtime.
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class AotTelnet;

public static class Program
{
    public static async Task<int> Main()
    {
        var context = new TelnetContext();
        await using var machine = new AotTelnet(context);
        await machine.StartAsync();

        // IAC SB NAWS 0 80 0 24 IAC SE: a window of 80×24, read byte by byte.
        await machine.FireAsync(new byte[] { 255, 250, 31, 0, 80, 0, 24, 255, 240 });
        await machine.StopAsync();

        machine.TryGetState<Connected>(out var root);
        Console.WriteLine($"window {root.Width}x{root.Height}");
        foreach (var line in context.Log)
        {
            Console.WriteLine(line);
        }

        var telnetOk = root.Width == 80 && root.Height == 24;
        var orderOk = await RunOrder();
        var setOk = await RunOrders();
        return telnetOk && orderOk && setOk ? 0 : 1;
    }

    /// <summary>Invoked machines, forwarding, a deferred event, an internal event, eventless and done transitions, completion.</summary>
    private static async Task<bool> RunOrder()
    {
        var context = new OrderContext();
        await using var order = new AotOrder(context);
        context.Machine = order;
        var observer = new CountingObserver();
        order.Observer = observer;
        await order.StartAsync();

        await order.FireAsync(1);
        await order.FireAsync(1);
        Console.WriteLine("permitted in Cart: " + string.Join(", ", order.PermittedTriggers()));
        await order.FireAsync(2);          // Fulfilling: AotPayment and AotPacking start
        await order.FireAsync(new Cancel()); // deferred until fulfilment is done
        order.Pause();
        order.Resume();
        await order.FireAsync(10);         // forwarded to both: the payment completes
        await order.FireAsync(new Packed()); // unknown to the order, forwarded: the packing completes
        await order.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Console.WriteLine($"order {order.Status} in {order.StateType.Name} after {observer.Transitions} transitions");
        Console.WriteLine(string.Join(" | ", context.Log));
        var paths = MachinePaths.Covering(AotOrder.Definition);
        Console.WriteLine($"{paths.Count} paths cover every transition of the order");
        return order.Status == MachineStatus.Completed && order.IsIn<Returned>()
            && string.Join(" | ", context.Log) == "audited | paid | packed | fulfilled | returned" && paths.Count > 0;
    }

    /// <summary>A machine per key: started on first use, stopped with the set.</summary>
    private static async Task<bool> RunOrders()
    {
        var set = new MachineSet<int, AotOrder, byte>(_ => new AotOrder(new OrderContext()));
        await set.FireAsync(7, 1);
        await set.FireAsync(8, 1);
        await set.FireAsync(7, 1);
        set.TryGet(7, out var seven);
        var items = seven!.TryGetState(out Cart cart) ? cart.Items : -1;
        var keys = string.Join(", ", set.Keys.Order());
        await set.StopAllAsync();
        Console.WriteLine($"orders {keys} stopped; order 7 had {items} items");
        return items == 2 && seven.Status == MachineStatus.Stopped && set.Count == 0;
    }

    private sealed class CountingObserver : IMachineObserver<byte>
    {
        public int Transitions { get; private set; }

        public void Transitioned(in TransitionInfo<byte> transition) => Transitions++;

        public void UnhandledValue(Type state, byte value)
        {
        }

        public void UnhandledEvent(Type state, Type eventType)
        {
        }
    }
}
