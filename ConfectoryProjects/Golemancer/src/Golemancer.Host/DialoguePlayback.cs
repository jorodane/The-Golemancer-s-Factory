using System.Globalization;
namespace Golemancer.Desktop;

internal sealed class DialoguePlayback
{
    private string text = "";
    private int[] elements = [];
    private double started;
    private bool skipped;
    public string Id { get; private set; } = "";
    public void Begin(string id, string value, double now)
    { Id = id; text = value; elements = StringInfo.ParseCombiningCharacters(value); started = now; skipped = false; }
    public int Count(double now) => skipped ? elements.Length : Math.Min(elements.Length, (int)Math.Max(0, (now - started - .25) / .028));
    public bool Revealing(double now) => Count(now) < elements.Length;
    public bool MouthOpen(double now) => Revealing(now) && Count(now) > 0 && !char.IsWhiteSpace(text[elements[Count(now) - 1]]) && (int)((now - started) / .11) % 2 == 0;
    public string Visible(double now) { int count = Count(now); return count >= elements.Length ? text : text.Substring(0, elements[count]); }
    // False means this click only completed the current sentence; it must not advance too.
    public bool Advance(double now) { if (!Revealing(now)) return true; skipped = true; return false; }
}
