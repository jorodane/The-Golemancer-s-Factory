# Repository layout

- The game workspace is Golemancer/. Read Golemancer/AGENTS.md for gameplay and platform requirements. Keep game modules, XML/art, native game clients, tests, docs and ready-to-run builds together there.
- src/Golemancer.Engine and src/Golemancer.Contracts are the existing engine/API development sources. Engine.slnx is a separate development solution. Game builds must use Golemancer/SDK binaries and must not build or depend on these source projects.
- The game-only extension experiment freezes the engine/API from c77bcdc. Do not change engine source, SDK DLLs or the lock to make that experiment pass; identify missing APIs explicitly. The existing engine/API still carry game-specific rules.
- Runtime DLL loading is required. Content modules must not depend on one another's concrete types.
- Never upload image bytes or APKs through the assistant's Git tools. Existing image blobs may be relocated by reusing their Git SHAs. Do not change .gitignore. Stage explicit source/runtime paths, never an entire worktree containing generated files.
- Preserve local images and saves when changing paths. Root Start.bat delegates to the game and copies only missing legacy files without deleting originals.
- Verify meaningful modules and the full campaign before pushing. Existing user authorization for commits and pushes applies.
