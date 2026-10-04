using Confectory.Contracts.UI;
using Confectory.Runtime.UI;
using System.Xml.Linq;
using System.Globalization;

namespace Confectory.EditorPacks;

/// <summary>Read-only shell presentation from the installed engine. No modules or project sources are executed.</summary>
public sealed class EditorStudioPresentation
{
    public UiCatalog Catalog { get; }
    private readonly UiDocument[] documents;
    private readonly XDocument[] xml;
    public XElement Template(string view) => new(xml.SelectMany(d => d.Root!.Elements("View")).Single(e => (string?)e.Attribute("id") == view).Element("Node")!);
    public UiCatalog Compose(string xml) => new(documents.Concat(new[] { UiXml.Read(new StringReader(xml)) }));
    public EditorStudioPresentation(EditorEngineDistribution engine)
    {
        engine.Verify();
        var core = engine.Sources.Single(s => s.Id == "editor.core.tools");
        xml = core.Manifest().Root!.Elements("Ui").Select(e => Confectory.Runtime.PackCompiler.ReadXml(core.PathFor((string)e.Attribute("path")!))).ToArray();
        documents = xml.Select(d => UiXml.Read(new StringReader(d.ToString()))).ToArray();
        Catalog = new UiCatalog(documents);
    }
    public EditorLiveView Start(IUiBackend backend, Action connect, Action later)
    {
        var context = new UiContext();
        context.AddCommand("editor.studio.connect", UiValueKind.None, _ => connect());
        context.AddCommand("editor.studio.later", UiValueKind.None, _ => later());
        return new(Catalog, "editor.studio.start", context, backend);
    }
}

/// <summary>Bounded, pack-authored polygons in a 96 by 96 coordinate space.</summary>
public static class EditorVector
{
    public sealed record Polygon(string Color, float[] Points);
    public static Polygon[] Parse(string value)
    {
        if (value.Length > 8192) throw new InvalidDataException("Vector data exceeds the native limit.");
        if (value.Length == 0) return [];
        return value.Split(';').Select(part =>
        {
            int separator = part.IndexOf(':');
            if (separator != 7) throw new InvalidDataException("Expected #RRGGBB:polygon.");
            string color = part.Substring(0, separator); EditorNativeSchema.ValidateValue("foreground", UiValue.Text(color));
            var points = part.Substring(separator + 1).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => float.Parse(n, CultureInfo.InvariantCulture)).ToArray();
            if (points.Length < 6 || points.Length > 256 || points.Length % 2 != 0 || points.Any(n => float.IsNaN(n) || float.IsInfinity(n) || n < 0 || n > 96))
                throw new InvalidDataException("Vector coordinates must be finite pairs inside 0..96.");
            return new Polygon(color, points);
        }).ToArray();
    }
}
