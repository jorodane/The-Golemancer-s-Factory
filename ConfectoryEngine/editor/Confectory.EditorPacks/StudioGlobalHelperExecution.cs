using Confectory.Workspace;

namespace Confectory.EditorPacks;

/// <summary>Project-free provider/settings and private-directory service boundaries.</summary>
public interface IEditorStudioGlobalHelperHost
{
    bool Allowed { get; }
    void Dispatch(Action action);
    EditorStudioHelperAgentContext AgentContext(string helperId);
    void SaveDirectory();
}

/// <summary>Application-owned conversations; binding a selected project never opens it or starts a request.</summary>
public interface IEditorStudioGlobalHelperExecution : IEditorStudioHelperExecution
{
    void BindProject(EditorSession? session, IEditorStudioWorkspace? workspace, IEditorStudioHelperExecution? execution);
    void ReadDisplayed(string helperId, string projectIdentity, string messageId);
}
