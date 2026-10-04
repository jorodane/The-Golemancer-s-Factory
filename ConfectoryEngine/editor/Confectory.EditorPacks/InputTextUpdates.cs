namespace Confectory.EditorPacks;

// Both native hosts leave equal text completely untouched, including composing spans.
public sealed class EditorInputTextUpdates
{
    private string? deferred;
    public bool HasDeferred => deferred is not null;
    public string? Receive(string requested, string current, bool composing)
    {
        if (requested == current) { deferred = null; return null; }
        if (composing) { deferred = requested; return null; }
        deferred = null; return requested;
    }
    public string? Complete(string current)
    { var value = deferred; deferred = null; return value == current ? null : value; }
}
