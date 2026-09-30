# Repository layout

- The game workspace is Golemancer/. Read Golemancer/AGENTS.md for gameplay and platform requirements. Keep game modules, XML/art, native game clients, tests, docs and ready-to-run builds together there.
- src/PackEngine.Contracts and src/PackEngine.Runtime are game-independent engine sources. Engine.slnx builds only those projects; they must never reference a game assembly, model, rule, content key or source path.
- All domain contracts, simulation, movement, recordings, inventory, economy, terrain integration and game-specific XML parsing belong in Golemancer/src/Golemancer.Contracts or Golemancer/src/Golemancer.Runtime. Other game modules depend on the game contracts, not concrete modules.
- Game projects use only the PackEngine binaries in Golemancer/SDK. Engine upgrades are explicit work: tools/export-sdk.py establishes a new source-hash baseline; game-only work must preserve that SDK and its lock.
- Runtime DLL loading is required. Content modules must not depend on one another's concrete types.
- Never upload image bytes or APKs through the assistant's Git tools. Existing image blobs may be relocated by reusing their Git SHAs. Do not change .gitignore. Stage explicit source/runtime paths, never an entire worktree containing generated files.
- Preserve local images and saves when changing paths. Root Start.bat delegates to the game and copies only missing legacy files without deleting originals.
- Verify meaningful modules and the full campaign before pushing. Existing user authorization for commits and pushes applies.
