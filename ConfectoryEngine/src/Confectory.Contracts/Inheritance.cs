namespace Confectory.Contracts;

/// <summary>Authoring location, retained after inheritance is flattened. Not a native resource path.</summary>
public sealed record DefinitionOrigin(string Pack, string Document, string Definition);

/// <summary>One named declaration with at most one parent. Payload interpretation belongs to the contract adapter.</summary>
public sealed record InheritedDefinition<T>(string Id, string Parent, DefinitionOrigin Origin, T Value);

/// <summary>Resolved ancestry and the declaration responsible for each final member.</summary>
public sealed record InheritanceTrace(string Id, string Parent, IReadOnlyList<string> Lineage,
    IReadOnlyDictionary<string, DefinitionOrigin> Members);
