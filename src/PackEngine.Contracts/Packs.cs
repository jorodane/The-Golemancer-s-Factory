namespace PackEngine.Contracts;

/// <summary>A DLL registers through a caller-owned contract. The engine does not interpret that contract.</summary>
public interface IPackModule<in TRegistry>
{
    void Register(TRegistry registry);
}

/// <summary>Loaded manifest metadata. Domain content is owned by the caller's content reader.</summary>
public sealed record PackInfo(string Id, string Version, string Directory,
    IReadOnlyList<string> Dependencies, IReadOnlyList<string> Assemblies);
