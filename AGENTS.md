# Repository layout

- `ConfectoryEngine/` is the independent engine. Read its AGENTS.md for engine/editor rules.
- `ConfectoryProjects/` contains consumer projects. Read a project's AGENTS.md before changing domain behavior.
- The engine must never infer, scan, default to, or dispatch commands to a bundled consumer. Build/run/verify entry points require an explicit selected project path. Recently opened paths live in user state only.
- Engine source, generic build/run/publish tooling, editor and engine packs belong to ConfectoryEngine. Projects own domain source, content, native presentation, implementation packs, project-specific tools and configuration. Do not add engine wrappers or frozen SDK/runtime copies to projects.
- `Confectory.*` is the engine namespace and assembly family. Project builds consume the selected engine's freshly prepared SDK through `ConfectorySdkDirectory`.
- Do not change .gitignore. Never commit generated DLL/EXE/APK/runtime packages. Existing image blobs may be relocated while preserving their Git SHAs; do not upload new image bytes through assistant Git tools.
- Preserve images and local user state. Verify engine isolation and the full consumer campaign before pushing. Existing user authorization for commits and main pushes applies.
