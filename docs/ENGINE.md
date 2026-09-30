# PackEngine development

`Engine.slnx` builds only `src/PackEngine.Contracts` and `src/PackEngine.Runtime`. Neither project references game code. All Golemancer rules, models and XML semantics are in the Golemancer workspace.

| Layer | Responsibility |
|---|---|
| PackEngine.Contracts | `IPackModule<TRegistry>`, timing registration/steps, rendering camera interface/immutable view, manifest metadata, typed UI values, providers, requests and platform interfaces |
| PackEngine.Runtime | Rendering camera state/interpolation, priority timing dispatch, dependency/version ordering, safe manifest paths, XML loading, DLL load contexts, content fingerprints, UI validation/composition/binding/disposal/layout |
| Consumer registry | Meaning and ownership of domain actions, systems, models and content |
| Consumer runtime | Simulation, save formats, controls, rendering integration and game lifecycle |
| Independent engine UI packs | Widget behavior and drawing policy, compiled against engine contracts only |
| Native UI adapter | Native input, primitive drawing and platform services |

The consumer supplies its registry type, accepted domain contract version, XML content reader and UI document receiver to `PackCompiler.Cook<TRegistry>`. DLLs implement `IPackModule<TRegistry>`; the engine shares that registry's contract assembly without referencing it at compile time. Additional shared identities can be supplied explicitly. Modules are loaded dynamically, never linked as engine projects.

XML `<Assembly path="Bin/{framework}/Feature.dll"/>`, `<Depends>`, `<Data>` and `<Ui>` retain their manifest roles. Engine-only packs declare `engineContracts="1"` independently of a domain ABI and cannot declare domain `Data`. The engine does not recognize domain-specific world keys, inventory, energy, quests, actors or recording rules. Typed UI catalogs choose renderer IDs; `UiModuleRegistry` resolves factories registered by independently loaded DLLs. Applications provide native adapters.

See [priority timing callbacks](TIMING.md) for host-driven lifecycle/frame points, stable priorities and per-session pack attachment.
See [rendering camera](CAMERA.md) for shared camera control, logical coordinates and immutable views used by drawing and picking.
See [engine UI object packs](UI_PACKS.md) for the independently loaded default button, canvas/input contracts and consumer integration scope.
See [pack and definition inheritance](INHERITANCE.md) for single-parent resolution, compatible overrides, reusable UI prototypes and member provenance. The common resolver is domain-independent; Widget/View provide the first concrete merge adapters.
See [Project Studio](EDITOR.md) for the chat-first native editor draft, generic project descriptors, relationship navigation, shared context, reviewed edits and per-pack build/run workflows. Golemancer is a loaded project; the editor has no game assembly reference.

Build with .NET 10 SDK:

```sh
dotnet build Engine.slnx -c Release -p:EngineTargetFramework=net48
dotnet build Engine.slnx -c Release -p:EngineTargetFramework=net10.0
python tools/verify-engine.py
```

The verification script copies engine sources, the independent button pack, tests and build configuration into a temporary directory. Core and pack builds must succeed without any consumer workspace. Tests load the external button DLL without a project reference. The game's regression suite also inspects the shipped engine assemblies for game dependencies.

An intentional engine update can export a new frozen SDK to a consumer:

```sh
python tools/export-sdk.py Golemancer
```

This rebuilds both targets, copies the engine DLLs to SDK and Builds/Windows, and writes a source-hash baseline plus enforced DLL hashes. It separately builds and distributes the replaceable button pack to the consumer's Content/Packs/01.EngineButton; that pack is not pinned by the core lock. Rebuild and verify the game before publishing this upgrade. Ordinary game or pack builds never call this exporter and must not modify the engine baseline.

Generate public signature references after a build with `tools/ApiReference`. It accepts an assembly path and an output text path, reads its XML documentation when available, and emits no implementation. Game and engine references are separate so pack work can read only the relevant contract.

The split changes Golemancer's domain DLL ABI to version 2; its JSON saves remain version 1. The old simulation is now `Golemancer.Runtime`, with gameplay semantics preserved. Separating the game from the engine does not imply each game rule is already an independently replaceable content pack.
