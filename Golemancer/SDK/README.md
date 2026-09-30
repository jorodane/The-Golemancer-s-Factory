# Frozen PackEngine SDK

Only the game-independent PackEngine.Contracts and PackEngine.Runtime assemblies belong here. The game domain API and simulation are built from ../src/Golemancer.Contracts and ../src/Golemancer.Runtime; they are not engine dependencies.

engine-lock.json records each engine source file SHA256, their combined source baseline and the shipped DLL hashes. FrozenEngine.targets enforces those DLL hashes during all game builds. Game modules see PackEngine.Contracts plus Golemancer.Contracts; game runtime/clients/tests also reference PackEngine.Runtime. Game projects never compile engine source.

Read API.txt and RUNTIME_API.txt for engine signatures, ../docs/API.txt and ../docs/CONTRACTS.md for game contracts, and ../docs/UI_CONTRACTS.md for UI semantics. Nullable annotations and implementation details are omitted from generated signatures.

Do not replace the engine or edit its lock during a game-only experiment. An intentional engine upgrade is made from the parent repository with tools/export-sdk.py, followed by rebuilding and verifying the consumer. This split deliberately establishes a new baseline; it does not claim the previous c77bcdc engine is unchanged.
