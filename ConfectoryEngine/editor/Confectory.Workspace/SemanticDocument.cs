using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml;

namespace Confectory.Workspace;

public sealed class SemanticMember
{
    public string Target { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Signature { get; set; } = "";
    public string Body { get; set; } = "";
    public int Offset { get; set; }
    public int Length { get; set; }
    public int BodyOffset { get; set; } = -1;
    public int BodyLength { get; set; }
    public string Text { get; set; } = "";
    public override string ToString() => Kind + " · " + Name;
}

/// <summary>Syntax-only boundaries. Parsing is never advertised as a successful compilation.</summary>
public static class SemanticDocument
{
    public static SyntaxTree Parse(string text) => CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));
    public static void Validate(string path, string text)
    {
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var errors = Parse(text).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(5).ToArray();
            if (errors.Length > 0) throw new InvalidDataException(string.Join("\n", errors.Select(e => e.ToString())));
        }
        else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read()) { }
        }
    }
    private static string Identity(MemberDeclarationSyntax n) => n switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.Text + (t is TypeDeclarationSyntax td ? td.TypeParameterList?.ToString() : ""),
        MethodDeclarationSyntax m => (m.ExplicitInterfaceSpecifier?.ToString() ?? "") + m.Identifier.Text + m.TypeParameterList + "(" + string.Join(",", m.ParameterList.Parameters.Select(p => p.Modifiers + " " + p.Type)) + ")",
        ConstructorDeclarationSyntax c => c.Identifier.Text + "(" + string.Join(",", c.ParameterList.Parameters.Select(p => p.Modifiers + " " + p.Type)) + ")",
        DestructorDeclarationSyntax d => "~" + d.Identifier.Text,
        PropertyDeclarationSyntax p => (p.ExplicitInterfaceSpecifier?.ToString() ?? "") + p.Identifier.Text,
        IndexerDeclarationSyntax i => "this" + i.ParameterList,
        BaseFieldDeclarationSyntax f => string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.Text)),
        EventDeclarationSyntax e => e.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text + d.TypeParameterList + d.ParameterList,
        OperatorDeclarationSyntax o => "operator" + o.OperatorToken + o.ParameterList,
        ConversionOperatorDeclarationSyntax c => c.ImplicitOrExplicitKeyword + " " + c.Type + c.ParameterList,
        _ => n.Kind().ToString()
    };
    public static IReadOnlyList<SemanticMember> Members(string text)
    {
        var root = Parse(text).GetRoot();
        var result = new List<SemanticMember>();
        foreach (var n in root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(n => n is not BaseNamespaceDeclarationSyntax && n is not GlobalStatementSyntax))
        {
            string parent = string.Join("/", n.Ancestors().Reverse().Select(a => a switch { BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(), BaseTypeDeclarationSyntax t => t.Identifier.Text, _ => "" }).Where(s => s.Length > 0));
            string key = parent + "/" + n.Kind() + ":" + Identity(n);
            SyntaxNode? body = n switch
            {
                BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                PropertyDeclarationSyntax p => (SyntaxNode?)p.AccessorList ?? p.ExpressionBody,
                IndexerDeclarationSyntax i => (SyntaxNode?)i.AccessorList ?? i.ExpressionBody,
                EventDeclarationSyntax e => e.AccessorList,
                _ => null
            };
            result.Add(new() { Target = key, Name = Identity(n), Kind = n.Kind().ToString().Replace("Declaration", ""), Offset = n.SpanStart, Length = n.Span.Length,
                Text = text.Substring(n.SpanStart, n.Span.Length), BodyOffset = body?.SpanStart ?? -1, BodyLength = body?.Span.Length ?? 0,
                Signature = body is null ? n.ToString() : text.Substring(n.SpanStart, body.SpanStart - n.SpanStart), Body = body?.ToString() ?? "" });
        }
        return result;
    }
    public static Dictionary<string, string> SignatureFields(string member)
    {
        var node = SyntaxFactory.ParseMemberDeclaration(member);
        SyntaxTokenList modifiers = node switch { BaseMethodDeclarationSyntax m => m.Modifiers, BaseFieldDeclarationSyntax f => f.Modifiers, PropertyDeclarationSyntax p => p.Modifiers, _ => default };
        var result = new Dictionary<string, string>();
        if (node is not MethodDeclarationSyntax and not ConstructorDeclarationSyntax and not PropertyDeclarationSyntax && node is not FieldDeclarationSyntax { Declaration.Variables.Count: 1 }) return result;
        result["Access"] = string.Join(" ", modifiers.Where(t => t.Text is "public" or "private" or "protected" or "internal" or "file").Select(t => t.Text));
        result["Modifiers"] = string.Join(" ", modifiers.Where(t => t.Text is not "public" and not "private" and not "protected" and not "internal" and not "file").Select(t => t.Text));
        switch (node)
        {
            case MethodDeclarationSyntax m: result["Type"] = m.ReturnType.ToString(); result["Name"] = m.Identifier.Text; result["Parameters"] = string.Join(", ", m.ParameterList.Parameters); break;
            case ConstructorDeclarationSyntax c: result["Name"] = c.Identifier.Text; result["Parameters"] = string.Join(", ", c.ParameterList.Parameters); break;
            case PropertyDeclarationSyntax p: result["Type"] = p.Type.ToString(); result["Name"] = p.Identifier.Text; break;
            case FieldDeclarationSyntax f: var v = f.Declaration.Variables.Single(); result["Type"] = f.Declaration.Type.ToString(); result["Name"] = v.Identifier.Text; result["Initializer"] = v.Initializer?.Value.ToString() ?? ""; break;
        }
        return result;
    }
    public static string RewriteSignature(string member, IReadOnlyDictionary<string, string> fields)
    {
        var node = SyntaxFactory.ParseMemberDeclaration(member) ?? throw new InvalidDataException("Invalid member.");
        var modifiers = SyntaxFactory.TokenList(SyntaxFactory.ParseTokens(fields["Access"] + " " + fields["Modifiers"]).Where(t => !t.IsKind(SyntaxKind.EndOfFileToken)));
        string Value(string key) => fields.TryGetValue(key, out var value) ? value : "";
        switch (node)
        {
            case MethodDeclarationSyntax m: node = m.WithModifiers(modifiers).WithReturnType(SyntaxFactory.ParseTypeName(Value("Type"))).WithIdentifier(SyntaxFactory.Identifier(Value("Name"))).WithParameterList(SyntaxFactory.ParseParameterList("(" + Value("Parameters") + ")")); break;
            case ConstructorDeclarationSyntax c: node = c.WithModifiers(modifiers).WithIdentifier(SyntaxFactory.Identifier(Value("Name"))).WithParameterList(SyntaxFactory.ParseParameterList("(" + Value("Parameters") + ")")); break;
            case PropertyDeclarationSyntax p: node = p.WithModifiers(modifiers).WithType(SyntaxFactory.ParseTypeName(Value("Type"))).WithIdentifier(SyntaxFactory.Identifier(Value("Name"))); break;
            case FieldDeclarationSyntax f:
                var variable = f.Declaration.Variables.Single().WithIdentifier(SyntaxFactory.Identifier(Value("Name"))).WithInitializer(Value("Initializer").Length == 0 ? null : SyntaxFactory.EqualsValueClause(SyntaxFactory.ParseExpression(Value("Initializer"))));
                node = f.WithModifiers(modifiers).WithDeclaration(f.Declaration.WithType(SyntaxFactory.ParseTypeName(Value("Type"))).WithVariables(SyntaxFactory.SingletonSeparatedList(variable))); break;
            default: throw new InvalidOperationException("Edit this declaration directly.");
        }
        string text = node.NormalizeWhitespace().ToFullString();
        string wrapped = "class __SignatureForm {\n" + text + "\n}"; Validate("signature.cs", wrapped);
        return Members(wrapped).First(m => m.Kind != "Class").Signature;
    }
    public static string Replace(string text, string target, string replacement)
    {
        var member = Members(text).Single(m => m.Target == target);
        var updated = text.Substring(0, member.Offset) + replacement + text.Substring(member.Offset + member.Length);
        Validate("document.cs", updated); return updated;
    }
    public static List<ChangeOperation>? Compare(string path, string before, string after)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return null;
        try { Validate(path, before); Validate(path, after); } catch (InvalidDataException) { return null; }
        var left = Members(before).Where(m => m.Kind is not "Class" and not "Struct" and not "Interface" and not "Record" and not "RecordStruct" and not "Enum").ToArray();
        var right = Members(after).Where(m => m.Kind is not "Class" and not "Struct" and not "Interface" and not "Record" and not "RecordStruct" and not "Enum").ToArray();
        if (left.Length == 0 || !left.Select(m => m.Target).SequenceEqual(right.Select(m => m.Target)) || left.Select(m => m.Target).Distinct().Count() != left.Length) return null;
        var result = new List<ChangeOperation>();
        void Add(SemanticMember m, string part, int offset, string a, string b)
        {
            if (a != b) result.Add(new() { Path = path, Kind = "csharp", Target = m.Target + part, Offset = offset,
                Line = before.Take(offset).Count(c => c == '\n') + 1, Before = a, After = b });
        }
        for (int i = 0; i < left.Length; i++)
        {
            var a = left[i]; var b = right[i];
            if (a.BodyOffset >= 0 && b.BodyOffset >= 0)
            {
                Add(a, "/Signature", a.Offset, a.Signature, b.Signature);
                Add(a, "/Body", a.BodyOffset, a.Body, b.Body);
                Add(a, "/Suffix", a.BodyOffset + a.BodyLength, before.Substring(a.BodyOffset + a.BodyLength, a.Offset + a.Length - a.BodyOffset - a.BodyLength), after.Substring(b.BodyOffset + b.BodyLength, b.Offset + b.Length - b.BodyOffset - b.BodyLength));
            }
            else Add(a, "", a.Offset, a.Text, b.Text);
        }
        // A header/using/comment/ordering/rename change falls back to conservative text comparison; no trivia is discarded.
        return ChangeDifference.Compose(before, result) == after ? result : null;
    }
    public static bool ScopeOverlap(string left, string right) => left.Length == 0 || right.Length == 0 || left == right || left.StartsWith(right + "/", StringComparison.Ordinal) || right.StartsWith(left + "/", StringComparison.Ordinal);
    public static bool Dependency(ChangeOperation a, ChangeOperation b) => a.Path == b.Path && a.Kind == "csharp" && b.Kind == "csharp" &&
        a.Target.Substring(0, Math.Max(0, a.Target.LastIndexOf('/'))) == b.Target.Substring(0, Math.Max(0, b.Target.LastIndexOf('/'))) && !ScopeOverlap(a.Target, b.Target);
}
