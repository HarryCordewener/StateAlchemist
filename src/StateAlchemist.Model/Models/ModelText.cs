using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StateAlchemist.Model;

/// <summary>
/// A model written out line by line: everything but source locations, which only some front-ends know. Two front-ends
/// that agree produce the same text, which is how their agreement is tested.
/// </summary>
public static class ModelText
{
    /// <summary>The model as text.</summary>
    public static string Of(MachineModel model)
    {
        var text = new StringBuilder();
        var o = model.Options;
        text.AppendLine($"machine {model.Name}: value {o.ValueType} [{o.Domain.Low}..{o.Domain.High}{Members(o.Domain.Members)}] supported={o.ValueTypeSupported} " +
                        $"context={o.ContextType} config={o.ConfigType} {o.Concurrency} inbox={o.InboxCapacity} {o.Purity} {o.Unhandled}");
        foreach (var s in model.States)
        {
            text.AppendLine($"state {s.Index} {s.TypeName} parent={s.Parent} initial={s.IsInitial} data={s.HasData} reset={s.HasReset} public={s.IsPublicStruct} markers={s.ParentMarkers}");
        }

        foreach (var t in model.Transitions)
        {
            text.AppendLine($"transition {t.Index} {t.Name} {t.Source}->{t.Target} on {t.Trigger} order={t.Order} run={t.IsRun} module={t.Module} unknown=[{string.Join(",", t.UnknownMembers)}]");
            Method(text, "guard", t.Guard);
            Method(text, "transform", t.Transform);
            foreach (var completed in t.Completed)
            {
                Method(text, "completed", completed);
            }

            if (t.Decision is { } d)
            {
                text.AppendLine($"  decision outcomes=[{string.Join(",", d.Outcomes)}] handle=[{string.Join(",", d.Handle)}]");
                Method(text, "decide", d.Decide);
                Method(text, "decide-async", d.DecideAsync);
                foreach (var c in d.Completions)
                {
                    Method(text, $"complete {c.OutcomeType}->{c.Target}", c.Complete);
                }
            }
        }

        foreach (var a in model.StateActions)
        {
            text.AppendLine($"action {a.Phase} state={a.State} order={a.Order} module={a.Module} index={a.DeclarationIndex}");
            Method(text, "method", a.Method);
        }

        foreach (var d in model.FrontEndDiagnostics)
        {
            text.AppendLine($"diagnostic {d.Id}: {d.Message}");
        }

        return text.ToString();
    }

    private static string Members(IReadOnlyList<long>? members) => members is null ? "" : " {" + string.Join(",", members) + "}";

    private static void Method(StringBuilder text, string role, MethodModel? method)
    {
        if (method is null)
        {
            return;
        }

        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Passing} {p.TypeName} {p.Name}:{p.Kind}{(p.State >= 0 ? "#" + p.State : "")}"));
        text.AppendLine($"  {role}: {method.Returns} {method.FullName}({parameters}) static={method.IsStatic} public={method.IsPublic}");
    }
}
