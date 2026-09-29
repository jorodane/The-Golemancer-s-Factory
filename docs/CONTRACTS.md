# Object pack contract, v1

`Golemancer.Contracts.dll` is the shared type identity. The host and modules share exactly this assembly. The host references Contracts and Engine only. Pack assemblies are discovered at runtime through `pack.xml` and loaded with `Assembly.LoadFrom` on .NET Framework 4.8. Portable net10.0 engine verification uses separate `AssemblyLoadContext`s. Each DLL supplies `IGameModule.Register` and registers independent action, condition, failure, system or world objects.

## Lifecycle and ownership

Registration -> XML definition load -> reference validation -> deterministic cook fingerprint -> world creation or save restoration -> fixed simulation steps. The host owns the single simulation thread and persistence. Modules operate only during calls through `IGameContext`. No module retains the context on background threads. The WPF dispatcher sends intent directly to the authoritative simulation at fixed 0.05-second steps. No HTTP transport is involved.

Definition IDs are stable content identities. WorldObject.Id is a persistent instance identity. Pack ID is a distribution identity. Multiple definitions, instances and implementation DLLs may belong to a pack. Constructors do not mutate the world. `IWorldGenerator.Populate` creates the initial instances.

## Actions and work

`Check` must have no effects. It is evaluated before navigation/work and again before permanent effects. All Work requirements must finish. Completed work is not rolled back after failure. `Execute` owns the transaction and returns `Applied`, status and reason. Checks and execution are serialized. Implementations validate quantity/capacity before debit and credit; partial transfers are intentional only for All and FillTo.

Exact transfers require the full specified amount at both ends. FillTo uses destination's current item count and transfers up to the target. All uses currently available source stock and destination capacity. A satisfied FillTo is a successful no-op. Inventories store total item quantities; slots constrain further additions, not existing saved quantities.

Unavailable/unknown actions remain in recordings and are skipped by default. Retriable failures retain the action request and partial effects. Failure XML maps to an independently registered `IFailureHandler`. Its result uses the executor's three control operations (advance/repeat/halt); handlers may perform additional effects themselves. Explode is an optional example implementation, never a shipped default.

## Conditions and composition

Conditions are independently registered objects. AND/OR recursively call the registered evaluator; NOT requires one child. Placement conditions receive a provisional footprint tile as the target. Display checks never replace execution checks. An ActionSet expands to action references; concrete actions retain unique IDs even when display names collide.

## Pack and save compatibility

The cooker topologically sorts declared dependencies and validates minimum versions and contract major version. A missing dependency or invalid pack stops cooking with a precise diagnostic. An unresolved individual action is disabled with a warning. Later data definitions with the same ID override earlier definitions in deterministic pack order. Tilesets merge by tile ID, and sprites merge by animation state; one matching tile or animation is replaced as a whole. Visual offsets never alter simulation tile coordinates. XML never loads DTDs or external entities; pack paths remain inside the pack directory.

Unknown object definitions remain serialized. Unknown per-object values/data/inventory and JSON extension fields are retained. Pack removal does not erase inventory, recordings or entity state. Save writes use a temporary file and replacement, retaining the previous file as `.bak`. Mid-work and queued production state are saved; committed ingredients are not paid twice on reload.

DLL modules are executable code and run with the game's local permissions. Only install packs you trust; in-process loading is not a security sandbox.

## AI-local development

Each module can be built from its csproj and shared Contracts. Read its implementation, pack XML and only the relevant contract types. Concrete module types never appear in another module's references. `tools/context.py` will expose pack dependencies and module-scoped source lists. Changes to Contracts require verification of all modules; changes to a pack require its focused checks plus the campaign regression when behavior affects progression.

Framework modules share one AppDomain and must use unique assembly identities. They cannot be individually unloaded, and runtime hot reload is not implemented. Image pack metadata and animation coordinates are documented in ART_PACKS.md.
