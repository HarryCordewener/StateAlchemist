using System.Reflection;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// What the inbox locks: a <c>System.Threading.Lock</c> where the target has one and the language is C# 13 or later,
/// and an <c>object</c> below that. This project builds at the latest language on every target, so the framework
/// alone decides.
/// </summary>
public class InboxLockTests
{
    [Test]
    public async Task TheInboxLocksALockWhereTheFrameworkHasOne()
    {
        var field = typeof(SyncSerializedMachine).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic);

#if NET9_0_OR_GREATER
        await Assert.That(field!.FieldType).IsEqualTo(typeof(System.Threading.Lock));
#else
        await Assert.That(field!.FieldType).IsEqualTo(typeof(object));
#endif
    }
}
