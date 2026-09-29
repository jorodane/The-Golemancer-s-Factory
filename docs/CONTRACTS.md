# Object pack contract, v1

`Golemancer.Contracts.dll` is the shared type identity. The host and modules share exactly this assembly. The host references Contracts and Engine only. Pack assemblies are discovered at runtime through `pack.xml` and loaded with `Assembly.LoadFrom` on .NET Framework 4.8. Portable net10.0 engine verification uses separate `AssemblyLoadContext`s. Each DLL supplies `IGameModule.Register` and registers independent action, condition, failure, system or world objects.

## Lifecycle and ownership

Registration -> XML definition load -> reference validation -> deterministic cook fingerprint -> world creation or save restoration -> fixed simulation steps. The host owns the single simulation thread and persistence. Modules operate only during calls through `IGameContext`. No module retains the context on background threads. The WPF dispatcher sends held input and actions directly to the authoritative simulation at fixed 1/60-second steps. Systems use the supplied dt; headless callers may use other steps up to 0.25 seconds. No HTTP transport is involved.

Definition IDs are stable content identities. WorldObject.Id is a persistent instance identity. Pack ID is a distribution identity. Multiple definitions, instances and implementation DLLs may belong to a pack. Constructors do not mutate the world. `IWorldGenerator.Populate` creates the initial instances.

WorldObject X/Y identify integer collision tiles; SubX/SubY persist continuous offsets in [-0.5, 0.5). WorldX/WorldY are their sums. SetPosition updates tile and offset together; assigning X/Y directly is an explicit tile teleport and resets that axis's offset. Old saves default offsets to zero. Manual movement, navigation, rolls and knockback advance in small continuous steps with tile collision checks. Releasing input or arriving at a path tile never aligns an actor to its center. Rendering offsets belong to animation metadata and do not affect these coordinates. Held manual input is transient and stops on pause, loss of focus, loading or control changes.

## Actions and work

`Check` must have no effects. It is evaluated before navigation/work and again before permanent effects. All Work requirements must finish. Completed work is not rolled back after failure. `Execute` owns the transaction and returns `Applied`, status and reason. Checks and execution are serialized. Implementations validate quantity/capacity before debit and credit; partial transfers are intentional only for All and FillTo.

Exact transfers require the full specified amount at both ends. FillTo uses destination's current item count and transfers up to the target. All uses currently available source stock and destination capacity. A satisfied FillTo is a successful no-op. Inventory stores normal stock or machine inputs; OutputInventory stores machine products. Count/Stock aggregate both for retrieval, but Has/Pay consume inputs only. Take retrieves products first, then input stock. FillTo counts destination inputs, not products. Slot restrictions constrain further additions, not existing saved quantities.

Direct transfers use the same contract for another golem or a facility. They do not change ControlledId. The native host uses left-click quick actions and right-click local bubble choices; selection is a separate explicit action. Inputs are overridable XML data keyed by action ID. Tab binds toggle_mode, and E binds pickup. E's rising edge picks the nearest stack within one tile; after 0.35 seconds held, one area pickup collects stacks within Manhattan distance two.

Harvesting first creates a dropped_items object with pickupOwner and autoPickupAt (0.35 seconds later). The harvesting golem collects within two tiles up to capacity; overflow remains on the ground. Manual drops and destruction byproducts have no pickup owner. Rules.Drop preserves material quantities, and the logistics system owns pickup. Ground items and pending ownership survive saves.

Manual movement recordings store a Route of visited tiles as one movement intent per uninterrupted input segment. Replay follows tile directions, preserves continuous offsets, and uses normal navigation if the recorded start is no longer adjacent. Tile routes, interaction ranges and placement footprints remain integer based.

Unavailable/unknown actions remain in recordings and are skipped by default. Retriable failures retain the action request and partial effects. Failure XML maps to an independently registered `IFailureHandler`. Its result uses the executor's three control operations (advance/repeat/halt); handlers may perform additional effects themselves. Explode is an optional example implementation, never a shipped default.

## Conditions and composition

Conditions are independently registered objects. AND/OR recursively call the registered evaluator; NOT requires one child. Placement conditions receive a provisional footprint tile as the target. Display checks never replace execution checks. An ActionSet expands to action references; concrete actions retain unique IDs even when display names collide.

Rules.Placement is shared by the native preview and construction checks. Ground factories and storage can be placed outdoors. Retail fixtures declare both Shop placement and shopOnly=true; only these fixtures count against shop capacity.

## Pack and save compatibility

The cooker topologically sorts declared dependencies and validates minimum versions and contract major version. A missing dependency or invalid pack stops cooking with a precise diagnostic. An unresolved individual action is disabled with a warning. Later data definitions with the same ID override earlier definitions in deterministic pack order. Tilesets merge by tile ID, and sprites merge by animation state; one matching tile or animation is replaced as a whole. Visual offsets never alter simulation tile coordinates. XML never loads DTDs or external entities; pack paths remain inside the pack directory.

Unknown object definitions remain serialized. Unknown per-object values/data/inventory and JSON extension fields are retained. Pack removal does not erase inventory, recordings or entity state. Save writes use a temporary file and replacement, retaining the previous file as `.bak`. Mid-work and queued production state are saved; committed ingredients are not paid twice on reload.

DLL modules are executable code and run with the game's local permissions. Only install packs you trust; in-process loading is not a security sandbox.

## AI-local development

Each module can be built from its csproj and shared Contracts. Read its implementation, pack XML and only the relevant contract types. Concrete module types never appear in another module's references. `tools/context.py` will expose pack dependencies and module-scoped source lists. Changes to Contracts require verification of all modules; changes to a pack require its focused checks plus the campaign regression when behavior affects progression.

Framework modules share one AppDomain and must use unique assembly identities. They cannot be individually unloaded, and runtime hot reload is not implemented. Image pack metadata and animation coordinates are documented in ART_PACKS.md.

## Recursive interaction menus and machine inputs

Giving and taking use one BubbleMenu projection with reversed source/destination. Favorites, ordered per-object categories and recursive single-child compression are shared. All game choices use the same paged bubble renderer; only quantity selection opens a single input. Central back navigation and Escape pop one level. Title/pause, dialogue, journal and help remain dedicated screens. Quantity commands recheck live bounds and authoritative action checks still run on execution.

InputSlotDef declares accepted item IDs/tags, a capacity and one item type per compartment. Rules.Room/Give enforce restrictions even for module calls; logistics rejects incompatible inputs in every quantity mode. OutputRoom/GiveOutput handle a separate product tray. Old incompatible/mixed/overcapacity inventory remains stored and retrievable. Automatic production waits for an unambiguous input combination, unlocked recipe, fuel/residual heat and product room before paying one batch. Output does not feed itself back into input. Committed jobs, output stock and residual heat survive reload. Setting resolves new definition defaults for old objects without overwriting instance settings. Existing committed manual queues still finish.

GameState.FavoriteItems and TransferCategories persist preferences. Item category/tags and facility InputSlots are pack XML; UI classes never reference concrete content modules.
