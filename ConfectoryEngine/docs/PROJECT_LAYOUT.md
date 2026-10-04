# Engine and project boundary

`ConfectoryEngine` can be copied and built without a consumer project. Its source references remain inside that folder. It does not scan a sibling projects folder, contain a default project, or dispatch commands to a named game. Only the user state's project history remembers previously selected absolute paths.

A project contains its own `.packproject`, domain source, independent implementation packs, content, platform presentation, tests and project-specific authoring tools. Its native host is game presentation, not a copy of the engine. Build/start/verify wrappers and generic publishing belong to the engine.

## Build flow

1. A user passes a project folder or manifest to an engine command, or invokes a build on the currently opened project.
2. The generic runner reads the selected manifest. Opening and indexing alone never runs commands or generates binaries.
3. For `engineSdk="true"`, the selected engine builds its own contracts/runtime for the target framework and supplies `ConfectorySdkDirectory` to project commands.
4. Declared `EnginePack` dependencies are built by the engine and their DLLs are staged into the consumer's declared pack location. Project-side pack XML remains the dependency and integration declaration, not engine implementation source.
5. The runner builds project-owned implementation packs, then runs that target's declared host/publish commands in the selected project's directory.

Project frameworks such as `net10.0-android` consume the portable `net10.0` SDK. The engine folder is found from an explicit `--engine-root`, `CONFECTORY_ENGINE_ROOT`, or the engine's own installation marker. No consumer path is used to find the engine.

Project MSBuild files reference engine libraries through `ConfectorySdkDirectory`, never another project's path, a committed SDK copy, or a previously published output. SDK files are engine-owned generated inputs under `Builds/SDK`. Project `Builds` is output only; publishing copies dependencies from the freshly built native host.

The `{engine}` token in explicitly invoked manifest commands resolves to the selected engine's directory. `ConfectoryProjectRoot` is provided by the runner; common publishers use it instead of inferring a neighboring directory. All regular project file references remain scoped to the selected project.

## Migration

Pull the layout change, rebuild `ConfectoryEngine/BuildEditor.bat`, and choose the moved project manifest. Existing user-state history may still refer to a former absolute path; select the new path once. Existing project state/content files and images retain their contents. Move any untracked saves from the former project location into the new project's `Saves` if needed; commands do not delete user data.

All managed engine namespaces/assemblies and protocol tool prefixes use `Confectory`. Rebuild project and editor packs against the selected engine. Committed frozen SDK DLLs and the obsolete output-directory hash check have been removed.

## Verification

Engine-only checks run inside an isolated engine copy. Consumer integration tests receive `--project` or `CONFECTORY_TEST_PROJECT` explicitly. Project isolation checks use an external, renamed folder with spaces and empty output folders, build through the engine and run the complete project campaign.
