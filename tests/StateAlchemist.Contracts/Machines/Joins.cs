namespace StateAlchemist.Contracts.Machines.Joins;

// JoinRoot ─┬─ Checkout [Initial] ─┬─ Browsing [Initial]
//           │                      └─ Reviewing
//           └─ Shipped

public struct JoinRoot : IRootState
{
    public int Restocked;
}

[Initial]
public struct Checkout : IState<JoinRoot>
{
}

[Initial]
public struct Browsing : IState<Checkout>
{
}

public struct Reviewing : IState<Checkout>
{
}

public struct Shipped : IState<JoinRoot>
{
    public string? Label;
}

public readonly struct Paid : IEvent
{
    public int Amount { get; init; }
}

public readonly struct Reserved : IEvent
{
    public string Warehouse { get; init; }
}

public readonly struct Supplied : IEvent
{
}

public readonly struct Counted : IEvent
{
    public int Count { get; init; }
}

/// <summary>
/// Joins: <c>Ship</c> waits in <see cref="Checkout"/> for a payment and a reservation, in either order, while the
/// machine moves between its children; <c>Restock</c> is a stay on the root that fires again each time both arrive.
/// </summary>
[Module]
public static class JoinModule
{
    [Transition(From = typeof(Checkout), To = typeof(Shipped)), OnAll(typeof(Paid), typeof(Reserved))]
    public static class Ship
    {
        public static void Transform(in Reserved reserved, in Paid paid, ref Shipped to) => to.Label = $"{reserved.Warehouse}:{paid.Amount}";

        public static void Completed(RecordingContext context, Paid paid, Shipped shipped) => context.Record($"shipped {shipped.Label} paid {paid.Amount}");
    }

    [Transition(From = typeof(JoinRoot)), OnAll(typeof(Supplied), typeof(Counted))]
    public static void Restock(ref JoinRoot root, in Counted counted) => root.Restocked += counted.Count;

    [Transition(From = typeof(Browsing), To = typeof(Reviewing)), On(1)]
    public static void Review()
    {
    }

    [Transition(From = typeof(Reviewing), To = typeof(Browsing)), On(2)]
    public static void Browse()
    {
    }

    [Transition(From = typeof(Checkout), To = typeof(Checkout)), On(9)]
    public static void StartOver()
    {
    }

    [Transition(From = typeof(Shipped), To = typeof(Checkout)), On(0)]
    public static void Again(in Shipped from)
    {
    }

    [Transition(From = typeof(JoinRoot)), OnAny]
    public static void Other()
    {
    }
}
