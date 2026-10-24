using System.Collections.Generic;

namespace StateAlchemist.AotSample;

// An order, to show the statechart features in a native binary: two invoked machines run side by side while the
// order is fulfilled, a cancel waits for fulfilment to finish, an internal event, an eventless transition and final
// states.
//
// OrderRoot ─┬─ Cart [Initial]
//            ├─ Fulfilling [Defer(Cancel)]   invokes AotPayment and AotPacking, forwarding to both
//            ├─ Review                         eventless to Shipped once approved
//            ├─ Shipped
//            ├─ Delivered [Final]
//            └─ Returned [Final]

public sealed class OrderContext
{
    public List<string> Log { get; } = [];

    public bool Approved { get; set; } = true;

    public IMachine<byte>? Machine { get; set; }
}

public struct OrderRoot : IRootState
{
}

[Initial]
public struct Cart : IState<OrderRoot>
{
    public int Items;
}

[Defer(typeof(Cancel))]
public struct Fulfilling : IState<OrderRoot>
{
}

public struct Review : IState<OrderRoot>
{
}

public struct Shipped : IState<OrderRoot>
{
}

[Final]
public struct Delivered : IState<OrderRoot>
{
}

[Final]
public struct Returned : IState<OrderRoot>
{
}

public struct Cancel : IEvent
{
}

/// <summary>Raised from inside the order only.</summary>
[Internal]
public struct Audit : IEvent
{
}

/// <summary>Known to the packing machine alone: the order forwards it.</summary>
public struct Packed : IEvent
{
}

[Module]
public static class OrderModule
{
    [Transition(From = typeof(Cart)), On(1)]
    public static void Add(ref Cart self) => self.Items++;

    [Transition(From = typeof(Cart), To = typeof(Fulfilling)), On(2)]
    public static class Checkout
    {
        public static bool Guard(in Cart cart) => cart.Items > 0;

        public static void Completed(OrderContext context) => context.Machine!.Enqueue(new Audit());
    }

    [Transition(From = typeof(Cart)), OnAny]
    public static void Browse()
    {
    }

    [Transition(From = typeof(OrderRoot)), OnEvent(typeof(Audit))]
    public static void Audited(OrderContext context) => context.Log.Add("audited");

    [Invoke(typeof(Fulfilling), Forward = true)]
    public static AotPayment Pay(OrderContext context) => new(context);

    [Invoke(typeof(Fulfilling), Forward = true)]
    public static AotPacking Pack(OrderContext context) => new(context);

    [Transition(From = typeof(Fulfilling), To = typeof(Review)), OnDone]
    public static class Fulfilled
    {
        public static void Completed(OrderContext context) => context.Log.Add("fulfilled");
    }

    [Transition(From = typeof(Review), To = typeof(Shipped)), Always]
    public static class Approve
    {
        public static bool Guard(OrderContext context) => context.Approved;
    }

    [Transition(From = typeof(Review)), OnAny]
    public static void Waiting()
    {
    }

    [Transition(From = typeof(Shipped), To = typeof(Delivered)), On(4)]
    public static void Deliver(in Shipped from)
    {
    }

    [Transition(From = typeof(Shipped), To = typeof(Returned)), OnEvent(typeof(Cancel))]
    public static void Return(OrderContext context) => context.Log.Add("returned");

    [Transition(From = typeof(Shipped)), OnAny]
    public static void InTransit()
    {
    }
}

[Machine(Root = typeof(OrderRoot), Value = typeof(byte), Context = typeof(OrderContext))]
[Include(typeof(OrderModule))]
public sealed partial class AotOrder;

public struct PaymentRoot : IRootState
{
}

[Initial]
public struct Unpaid : IState<PaymentRoot>
{
}

[Final]
public struct Paid : IState<PaymentRoot>
{
}

[Module]
public static class PaymentModule
{
    [Transition(From = typeof(Unpaid), To = typeof(Paid)), On(10)]
    public static void Settle(OrderContext context) => context.Log.Add("paid");

    [Transition(From = typeof(PaymentRoot)), OnAny]
    public static void Ignore()
    {
    }
}

[Machine(Root = typeof(PaymentRoot), Value = typeof(byte), Context = typeof(OrderContext))]
[Include(typeof(PaymentModule))]
public sealed partial class AotPayment;

public struct PackingRoot : IRootState
{
}

[Initial]
public struct Unpacked : IState<PackingRoot>
{
}

[Final]
public struct Boxed : IState<PackingRoot>
{
}

[Module]
public static class PackingModule
{
    [Transition(From = typeof(Unpacked), To = typeof(Boxed)), OnEvent(typeof(Packed))]
    public static void Box(OrderContext context) => context.Log.Add("packed");

    [Transition(From = typeof(PackingRoot)), OnAny]
    public static void Ignore()
    {
    }
}

[Machine(Root = typeof(PackingRoot), Value = typeof(byte), Context = typeof(OrderContext))]
[Include(typeof(PackingModule))]
public sealed partial class AotPacking;
