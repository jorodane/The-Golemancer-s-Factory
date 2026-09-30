using PackEngine.Contracts;
using PackEngine.Contracts.UI;

namespace PackEngine.Runtime.UI;

public sealed class UiMountedView : IDisposable
{
    private readonly List<IUiElement> elements = [];
    private readonly List<IDisposable> subscriptions = [];
    private bool disposed, active;
    public IUiElement Root { get; private set; } = null!;
    private UiMountedView() { }
    internal static UiMountedView Create(UiPlan plan, IUiBackend backend)
    {
        var view = new UiMountedView();
        try { view.Root = view.Build(plan, backend); view.active = true; return view; }
        catch (Exception original)
        {
            try { view.Dispose(); } catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
            throw;
        }
    }
    private IUiElement Build(UiPlan plan, IUiBackend backend)
    {
        var element = backend.Create(plan.Renderer, plan.Node.Id, plan.Node.Layout) ?? throw new InvalidOperationException("UI backend returned no element.");
        elements.Add(element);
        foreach (var value in plan.Values) element.Set(value.Key, value.Value);
        foreach (var child in plan.Children) element.Add(child.Slot, Build(child.Child, backend));
        foreach (var binding in plan.Sources)
        {
            void Update(UiValue value)
            {
                if (disposed) return;
                UiCatalog.CheckValue(binding.Property, value);
                element.Set(binding.Property.Name, value);
            }
            subscriptions.Add(binding.Source.Subscribe(Update));
            // Subscribe before re-reading so a source that changes during mounting cannot leave a stale value.
            Update(binding.Source.Read());
        }
        foreach (var command in plan.Commands)
            subscriptions.Add(element.Listen(command.Event, payload =>
            {
                if (!active || disposed) return;
                if (payload is null || payload.Kind != command.Command.Payload) throw UiCatalog.Invalid("Invalid UI event payload: " + plan.Node.Id + "/" + command.Event);
                command.Command.Execute(payload);
            }));
        return element;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; active = false;
        var errors = new List<Exception>();
        foreach (var owned in subscriptions.AsEnumerable().Reverse().Concat<IDisposable>(elements.AsEnumerable().Reverse()))
            try { owned.Dispose(); } catch (Exception e) { errors.Add(e); }
        subscriptions.Clear(); elements.Clear();
        if (errors.Count > 0) throw new AggregateException("UI cleanup failed.", errors);
    }
}

/// <summary>Useful for simple presentation models. The game decides what these named values and commands mean.</summary>
public sealed class UiContext : IUiContext
{
    private readonly Dictionary<string, IUiValueSource> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IUiCommand> commands = new(StringComparer.Ordinal);
    public void AddValue(string name, IUiValueSource source) { UiCatalog.Name(name); values.Add(name, source ?? throw new ArgumentNullException(nameof(source))); }
    public void AddCommand(string name, UiValueKind payload, Action<UiValue> execute)
    {
        UiCatalog.Name(name);
        if (!Enum.IsDefined(typeof(UiValueKind), payload)) throw new ArgumentOutOfRangeException(nameof(payload));
        commands.Add(name, new CommandBinding(payload, execute ?? throw new ArgumentNullException(nameof(execute))));
    }
    public IUiValueSource Value(string name) => values.TryGetValue(name, out var value) ? value : throw UiCatalog.Invalid("Missing UI source: " + name);
    public IUiCommand Command(string name) => commands.TryGetValue(name, out var command) ? command : throw UiCatalog.Invalid("Missing UI command: " + name);
    private sealed class CommandBinding(UiValueKind payload, Action<UiValue> execute) : IUiCommand
    {
        public UiValueKind Payload => payload;
        public void Execute(UiValue value)
        { if (value.Kind != Payload) throw UiCatalog.Invalid("UI command payload type mismatch."); execute(value); }
    }
}
public sealed class UiSignal(UiValue initial) : IUiValueSource
{
    private UiValue current = initial ?? throw new ArgumentNullException(nameof(initial));
    private event Action<UiValue>? changed;
    public UiValueKind Type => current.Kind;
    public UiValue Read() => current;
    public void Set(UiValue value)
    {
        if (value is null || value.Kind != Type) throw UiCatalog.Invalid("UI source type cannot change.");
        if (current == value) return;
        current = value;
        // An invalid binding must not prevent the remaining views from receiving this change.
        var errors = new List<Exception>();
        foreach (Action<UiValue> listener in changed?.GetInvocationList() ?? [])
            try { listener(value); } catch (Exception e) { errors.Add(e); }
        if (errors.Count > 0) throw new AggregateException("UI value notification failed.", errors);
    }
    public IDisposable Subscribe(Action<UiValue> listener)
    {
        if (listener is null) throw new ArgumentNullException(nameof(listener));
        changed += listener; return new Subscription(() => changed -= listener);
    }
    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? release = remove;
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }
}
