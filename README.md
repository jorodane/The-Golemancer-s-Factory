# Confectory

The engine and its consumer projects are independent folders.

| Folder | Owns |
| --- | --- |
| `ConfectoryEngine/` | Engine contracts/runtime, packs, editor, common build/run/verification commands |
| `ConfectoryProjects/Golemancer/` | Game contracts, simulation, independent game modules, content, native presentation and project manifest |

Build the editor with `ConfectoryEngine/BuildEditor.bat`, then launch `ConfectoryEngine/StartEditor.bat`. The engine starts with its project picker and stores recently opened paths in user state. It never selects or builds a bundled game automatically.

To build a project, pass its folder or `.packproject` explicitly:

```bat
ConfectoryEngine\Build.bat "ConfectoryProjects\Golemancer" windows
ConfectoryEngine\Start.bat "ConfectoryProjects\Golemancer" windows
ConfectoryEngine\Verify.bat "ConfectoryProjects\Golemancer" windows
```

Absolute paths, paths with spaces, and projects outside this repository work the same way. Calling `Build.bat` without a project only shows usage.

On Linux:

```sh
CONFECTORY_DOTNET=dotnet ConfectoryEngine/build.sh "/path/to/project" portable
CONFECTORY_DOTNET=dotnet ConfectoryEngine/verify.sh "/path/to/project" portable
```

The selected engine builds its SDK into its own `Builds/SDK/<framework>` and supplies that path to the consumer's MSBuild commands. No existing project `Builds` directory or committed DLL is required. Project outputs and external pack DLLs are generated locally and ignored by Git.

`PackEngine.*` has been renamed to `Confectory.*` throughout source, assemblies, providers and tool names. Rebuild the editor and project packs together; old generated DLLs are not source inputs.

See [layout and build contract](ConfectoryEngine/docs/PROJECT_LAYOUT.md) and [game controls](ConfectoryProjects/Golemancer/README.md).

The native Linux editor is built with `ConfectoryEngine/BuildEditorLinux.sh`
(`linux-x64` or `linux-arm64`) and launched with
`ConfectoryEngine/StartEditorLinux.sh --project /path/to/Project.packproject`.
See [Linux editor build and native verification](ConfectoryEngine/docs/LINUX_EDITOR.md).
