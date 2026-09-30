# Frozen engine/API

These are the existing compiled engine and API, included so the game folder can be copied and developed without access to engine source. They are dependencies, not editable game code.

`engine-lock.json` records the baseline commit and SHA256 values. `FrozenEngine.targets` checks the same values before game project builds. `Directory.Build.targets` supplies the binary references. Modules and examples receive only the Contracts reference; native game clients and verification explicitly opt into the Engine reference as well.

For game work, read `../docs/CONTRACTS.md`, `../docs/UI_CONTRACTS.md`, the public signatures in `API.txt`, the XML documentation beside each Contracts DLL, and the relevant module/pack files. `API.txt` is generated from the frozen DLL by reflection; it is a reading reference with no implementation, and omits nullable reference annotations. Do not add a project reference to the engine or API source. Do not replace these DLLs or edit the lock when testing a game-only extension. An API gap is a result to report.

The net48 files exactly match the Windows distribution at `c77bcdc`. The net10.0 files are the already-built portable counterparts of the same UI-contract source (`cc1d670`). They were copied from the verified build without rebuilding engine source during this folder experiment.
