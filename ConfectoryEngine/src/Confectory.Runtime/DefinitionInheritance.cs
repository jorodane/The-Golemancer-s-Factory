using System.Collections.ObjectModel;
using Confectory.Contracts;

namespace Confectory.Runtime;

/// <summary>Domain-independent single-parent resolver. Adapters own compatible merge semantics and member paths.</summary>
public static class DefinitionInheritance
{
    public static IReadOnlyDictionary<string, T> Resolve<T>(IEnumerable<InheritedDefinition<T>> declarations,
        Func<T?, InheritedDefinition<T>, IDictionary<string, DefinitionOrigin>, T> merge,
        Func<T, T> snapshot,
        out IReadOnlyDictionary<string, InheritanceTrace> traces) where T : class
    {
        var inputs = new Dictionary<string, InheritedDefinition<T>>(StringComparer.Ordinal);
        foreach (var item in declarations)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || inputs.ContainsKey(item.Id)) throw new InvalidDataException("Missing or duplicate inherited definition: " + item.Id);
            if (item.Value is null || item.Origin is null) throw new ArgumentException("A definition needs a payload and origin.");
            inputs.Add(item.Id, item with { Value = snapshot(item.Value) });
        }
        var values = new Dictionary<string, T>(StringComparer.Ordinal);
        var history = new Dictionary<string, InheritanceTrace>(StringComparer.Ordinal);
        var visiting = new List<string>();
        T Visit(string id)
        {
            if (values.TryGetValue(id, out var done)) return done;
            if (!inputs.TryGetValue(id, out var item)) throw new InvalidDataException("Missing inheritance parent: " + string.Join(" -> ", visiting.Concat(new[] { id })));
            if (visiting.Contains(id)) throw new InvalidDataException("Cyclic inheritance: " + string.Join(" -> ", visiting.Concat(new[] { id })));
            if (visiting.Count >= 64) throw new InvalidDataException("Inheritance exceeds 64 levels: " + id);
            visiting.Add(id);
            T? parent = item.Parent.Length == 0 ? null : snapshot(Visit(item.Parent));
            var members = item.Parent.Length == 0 ? new Dictionary<string, DefinitionOrigin>(StringComparer.Ordinal)
                : history[item.Parent].Members.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var previous = members.Keys.ToArray();
            T value = merge(parent, item, members) ?? throw new InvalidDataException("Inheritance adapter returned no definition: " + id);
            if (previous.Any(p => !members.ContainsKey(p))) throw new InvalidDataException("Inheritance cannot remove a published member: " + id);
            var lineage = item.Parent.Length == 0 ? new[] { id } : history[item.Parent].Lineage.Concat(new[] { id }).ToArray();
            if (lineage.Length > 64) throw new InvalidDataException("Inheritance exceeds 64 levels: " + id);
            history.Add(id, new(id, item.Parent, Array.AsReadOnly(lineage), new ReadOnlyDictionary<string, DefinitionOrigin>(members)));
            values.Add(id, value); visiting.RemoveAt(visiting.Count - 1); return value;
        }
        foreach (string id in inputs.Keys.OrderBy(k => k, StringComparer.Ordinal)) Visit(id);
        traces = new ReadOnlyDictionary<string, InheritanceTrace>(history);
        return new ReadOnlyDictionary<string, T>(values);
    }
}
