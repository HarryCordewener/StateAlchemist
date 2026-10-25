using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

// Deferred events ([Defer]), written only into machines whose states defer one. An event the active state defers is
// kept, in arrival order, without its caller; when a transition moves the machine, every kept event is queued again
// at the commit, ahead of what the transition's actions queue, and is resolved afresh. ReferenceMachine.Statechart.cs
// states the same rules.
internal sealed partial class MachineEmitter
{
    private bool HasDefer => _model.States.Any(s => s.Defers.Count > 0);

    /// <summary>The indexes, in <c>_events</c>, of the events some state defers.</summary>
    private List<int> DeferredEvents => _model.States.SelectMany(s => s.Defers).Distinct().Select(EventTag).Where(i => i >= 0).OrderBy(i => i).ToList();

    private void WriteDeferStorage()
    {
        if (!HasDefer)
        {
            return;
        }

        _w.Line(HasInbox
            ? "private readonly global::System.Collections.Generic.List<Input> _deferred = new global::System.Collections.Generic.List<Input>();"
            : "private global::System.Collections.Generic.List<QueuedEvent> _deferred;");
    }

    /// <summary>What a dispatch does with an event its leaf defers.</summary>
    private static string DeferCall(int tag) => $"Defer{tag}(e)";

    private void WriteDefer()
    {
        if (!HasDefer)
        {
            return;
        }

        foreach (var i in DeferredEvents)
        {
            _w.Line();
            _w.Line($"/// <summary>Keeps a <see cref=\"{Name(_events[i])}\"/> the active state defers.</summary>");
            using (_w.Block($"private {ValueTaskType} Defer{i}({Name(_events[i])} e)"))
            {
                if (HasInbox)
                {
                    _w.Line("var kept = Rent();");
                    _w.Line($"kept.Tag = {i};");
                    _w.Line($"kept.E{i} = e;");
                    _w.Line("lock (_sync) { _deferred.Add(kept); }");
                }
                else
                {
                    _w.Line($"var kept = new QueuedEvent {{ Tag = {i} }};");
                    _w.Line($"kept.E{i} = e;");
                    _w.Line("(_deferred ?? (_deferred = new global::System.Collections.Generic.List<QueuedEvent>())).Add(kept);");
                }

                _w.Line($"return default({ValueTaskType});");
            }
        }

        _w.Line();
        _w.Line("/// <summary>The machine moved: every kept event is queued again, to be resolved in the new state.</summary>");
        using (_w.Block("private void Redeliver()"))
        {
            if (HasInbox)
            {
                _w.Line("lock (_sync) { if (_deferred.Count == 0) return; _queued.AddRange(_deferred); _deferred.Clear(); }");
            }
            else
            {
                _w.Line("if (_deferred == null || _deferred.Count == 0) return;");
                _w.Line("var queue = Queue();");
                _w.Line("foreach (var kept in _deferred) queue.Enqueue(kept);");
                _w.Line("_deferred.Clear();");
            }
        }
    }

    /// <summary>At the commit of a move, when the machine defers anything.</summary>
    private void WriteRedeliver(TransitionPath path)
    {
        if (HasDefer && path.Exiting.Count > 0)
        {
            _w.Line("Redeliver();");
        }
    }

    /// <summary>Each deferrable event's property in <c>SnapshotEvent</c>: its short name, or its full name when two share one.</summary>
    private List<(int Tag, string Property)> DeferredProperties()
    {
        var tags = DeferredEvents;
        var shared = new HashSet<string>(tags.GroupBy(t => _events[t].Name).Where(g => g.Count() > 1).Select(g => g.Key));
        return tags.Select(t => (t, shared.Contains(_events[t].Name) ? SymbolModelBuilder.MetadataName(_events[t]).Replace('.', '_').Replace('+', '_').Replace('`', '_') : _events[t].Name)).ToList();
    }

    private void WriteDeferSnapshotType()
    {
        _w.Line();
        _w.Line("/// <summary>One deferred event: exactly one property is set.</summary>");
        using (_w.Block("public sealed class SnapshotEvent"))
        {
            foreach (var (tag, property) in DeferredProperties())
            {
                _w.Line($"/// <summary>A deferred <see cref=\"{Name(_events[tag])}\"/>.</summary>");
                _w.Line($"public {Name(_events[tag])}? {property} {{ get; set; }}");
            }
        }
    }

    private void WriteDeferCapture()
    {
        _w.Line("snapshot.Deferred = new global::System.Collections.Generic.List<SnapshotEvent>();");
        var source = HasInbox ? "_deferred" : "(_deferred ?? new global::System.Collections.Generic.List<QueuedEvent>())";
        using (_w.Block($"foreach (var kept in {source})"))
        {
            _w.Line("var item = new SnapshotEvent();");
            using (_w.Block("switch (kept.Tag)"))
            {
                foreach (var (tag, property) in DeferredProperties())
                {
                    _w.Line($"case {tag}: item.{property} = kept.E{tag}; break;");
                }
            }

            _w.Line("snapshot.Deferred.Add(item);");
        }
    }

    private void WriteDeferRestore()
    {
        _w.Line(HasInbox ? "_deferred.Clear();" : "_deferred = null;");
        using (_w.Block("if (snapshot.Deferred != null)"))
        {
            using (_w.Block("foreach (var item in snapshot.Deferred)"))
            {
                _w.Line("if (item == null) continue;");
                foreach (var (tag, property) in DeferredProperties())
                {
                    _w.Line($"if (item.{property}.HasValue) _ = Defer{tag}(item.{property}.Value);");
                }
            }
        }
    }
}
