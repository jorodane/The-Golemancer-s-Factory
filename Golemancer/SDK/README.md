# Frozen PackEngine SDK

Only the game-independent PackEngine.Contracts and PackEngine.Runtime assemblies belong here. The game domain API and simulation are built from ../src/Golemancer.Contracts and ../src/Golemancer.Runtime; they are not engine dependencies.

engine-lock.json records each engine source file SHA256, their combined source baseline and the shipped DLL hashes. FrozenEngine.targets enforces those DLL hashes during all game builds. Game modules see PackEngine.Contracts plus Golemancer.Contracts; game runtime/clients/tests also reference PackEngine.Runtime. Game projects never compile engine source.

Read API.txt and RUNTIME_API.txt for engine signatures, ../docs/API.txt and ../docs/CONTRACTS.md for game contracts, and ../docs/UI_CONTRACTS.md for UI semantics. Nullable annotations and implementation details are omitted from generated signatures.

[TIMING.md](TIMING.md) specifies priority callbacks and their lifetime. [The game binding](../docs/TIMING.md) shows DLL registration and Windows/Android frame stages. Both documents travel with this standalone folder.
[CAMERA.md](CAMERA.md) specifies the shared rendering camera, smooth target control, viewport units and last-drawn picking. Existing `IGameCamera` callers keep their direct properties; native cameras also expose the engine `ICamera2D` capability.
[UI_PACKS.md](UI_PACKS.md) specifies external widget factories and canvas/input contracts. The default button DLL is shipped in Content/Packs/01.EngineButton, outside the frozen SDK. Game-specific styling/bindings live in Content/Packs/02.Controls. Neither the consumer nor core references the button assembly at compile time.
[INHERITANCE.md](INHERITANCE.md) specifies pack/definition parents, omitted versus explicit settings, reusable widget/view prototypes and final-definition provenance inspection.

Do not replace the engine or edit its lock during a game-only experiment. An intentional engine upgrade is made from the parent repository with tools/export-sdk.py, followed by rebuilding and verifying the consumer. This split deliberately establishes a new baseline; it does not claim the previous c77bcdc engine is unchanged.
