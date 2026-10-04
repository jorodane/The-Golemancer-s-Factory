# Independent Confectory engine

- Engine contracts/runtime, editor, providers and default UI packs must remain game-independent. All engine project references stay inside this folder.
- The engine starts on a generic project picker. Build/run/verify commands require a user-supplied project folder or manifest; no bundled project or neighboring projects folder is known to runtime code.
- `ConfectoryEngine.xml` identifies this engine installation and its default pack sources. The engine builds its own SDK under Builds/SDK and supplies ConfectorySdkDirectory to selected project commands. No consumer output is a prerequisite.
- Opening a project must not execute commands/DLLs or automatically resume model requests. Only explicitly invoked actions prepare SDKs and execute project commands.
- Use Confectory namespaces, assembly names and tool prefixes. Preserve dynamic DLL loading, ABI contracts and separate project overlays.
- Preserve Agent–Worker–Helper/YogiBox semantics, user-specific histories, reviewed changes, consent and collaboration boundaries. Do not restore embedded web AI or retired plugin/tunnel bridges.
- UI should feel like a strategy simulation: project-owned surfaces, directly editable object cards and movable worker characters. Keep XML, inspectors, full transcripts and management behind explicit actions. Preserve the minimal startup/project home and independent main-project workspace.
- Follow docs/TIMING.md, docs/CAMERA.md, docs/INHERITANCE.md, docs/UI_PACKS.md and docs/PROJECT_EXECUTION.md when changing those contracts.
- Integration tests must receive the consumer path explicitly; do not encode a default consumer path. Engine-only build/verification must work with no consumer present.
- Never commit generated binaries or new image bytes. Do not change .gitignore. Verify engine/editor and selected consumer checks before pushing. Existing commit/push authorization applies.
