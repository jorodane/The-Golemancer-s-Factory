using System.Collections;

namespace Confectory.Workspace;

/// <summary>An owner's editable layer snapshots. Unopened layers have no semantic objects.</summary>
public sealed class ConceptPackSpace
{
    public string PackId { get; }
    internal bool[] Loaded { get; } = new bool[3];
    internal List<ConceptCategory> Categories { get; } = [];
    internal List<ConceptDefinition> Concepts { get; } = [];
    internal List<ConceptObject> Objects { get; } = [];
    internal List<ConceptImplementation> Implementations { get; } = [];
    internal List<ConceptEditorView> Views { get; } = [];
    internal ConceptPackSpace(string pack) { PackId = pack; }
}

/// <summary>Compatibility collection; enumeration explicitly requests a combined layer.</summary>
public sealed class ConceptCollection<T> : ICollection<T>, IReadOnlyList<T> where T : IConceptElement
{
    private readonly Func<T[]> all;
    private readonly Func<string, T[]> local;
    private readonly Action<T> add;
    private readonly Func<T, bool> remove;
    internal ConceptCollection(Func<T[]> all, Func<string, T[]> local, Action<T> add, Func<T, bool> remove)
    { this.all = all; this.local = local; this.add = add; this.remove = remove; }
    public IReadOnlyList<T> InPack(string pack) => local(pack);
    public int Count => all().Length;
    public bool IsReadOnly => false;
    public T this[int index] => all()[index];
    public void Add(T item) => add(item);
    public void AddRange(IEnumerable<T> items) { foreach (var item in items) add(item); }
    public bool Remove(T item) => remove(item);
    public void Clear() { foreach (var item in all()) remove(item); }
    public bool Contains(T item) => all().Contains(item);
    public void CopyTo(T[] array, int index) => all().CopyTo(array, index);
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)all()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
