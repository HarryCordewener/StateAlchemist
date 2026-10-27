namespace StateAlchemist.Samples.Orders;

// An order that ships once it is both paid for and reserved, whichever happens first: the example for a join.

/// <summary>The order. Placing it is where the machine starts.</summary>
public struct Order : IRootState
{
}

/// <summary>Waiting for the payment and the reservation. What has arrived is kept with this state's data.</summary>
[Initial]
public struct Placed : IState<Order>
{
}

/// <summary>On its way.</summary>
public struct Shipped : IState<Order>
{
    /// <summary>Where it was shipped from, and what was paid.</summary>
    public string? Label;
}

/// <summary>Called off before it shipped.</summary>
public struct Cancelled : IState<Order>
{
}

/// <summary>The payment cleared.</summary>
public readonly struct PaymentReceived : IEvent
{
    public int Amount { get; init; }
}

/// <summary>The warehouse set the stock aside.</summary>
public readonly struct StockReserved : IEvent
{
    public string Warehouse { get; init; }
}

/// <summary>The customer changed their mind.</summary>
public readonly struct CancelRequested : IEvent
{
}

// begin-snippet: sample-join
[Module]
public static class OrderModule
{
    /// <summary>
    /// Fires once both events have arrived in <see cref="Placed"/>, in either order. The transform takes both
    /// payloads, whichever came first.
    /// </summary>
    [Transition(From = typeof(Placed), To = typeof(Shipped)), OnAll(typeof(PaymentReceived), typeof(StockReserved))]
    public static void Ship(in PaymentReceived payment, in StockReserved stock, ref Shipped to) =>
        to.Label = $"{payment.Amount} from {stock.Warehouse}";

    /// <summary>Leaving <see cref="Placed"/> forgets whatever had arrived.</summary>
    [Transition(From = typeof(Placed), To = typeof(Cancelled)), OnEvent(typeof(CancelRequested))]
    public static void Cancel()
    {
    }
}
// end-snippet

/// <summary>An order's triggers are all events; the machine's value type goes unused.</summary>
[Module]
public static class IgnoreValues
{
    [Transition(From = typeof(Order)), OnAny]
    public static void Ignore()
    {
    }
}

[Machine(Root = typeof(Order), Value = typeof(byte))]
[Include(typeof(OrderModule)), Include(typeof(IgnoreValues))]
public sealed partial class OrderMachine;
