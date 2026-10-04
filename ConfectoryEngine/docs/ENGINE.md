# Confectory development

`Engine.slnx` builds only `src/Confectory.Contracts` and `src/Confectory.Runtime`. Neither project references game code. All consumer rules, models and XML semantics stay in their selected project.

| Layer | Responsibility |
|---|---|
| Confectory.Contracts | `IPackModule<TRegistry>`, timing registration/steps, rendering camera interface/immutable view, manifest metadata, typed UI values, providers, requests and platform interfaces |
| Confectory.Runtime | Rendering camera state/interpolation, priority timing dispatch, dependency/version ordering, safe manifest paths, XML loading, DLL load contexts, content fingerprints, UI validation/composition/binding/disposal/layout |
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
See [Project Studio](EDITOR.md) for the chat-first native editor draft, generic project descriptors, relationship navigation, shared context, reviewed edits and per-pack build/run workflows. Projects are selected explicitly; the editor has no consumer assembly reference.

Build with .NET 10 SDK:

```sh
dotnet build Engine.slnx -c Release -p:EngineTargetFramework=net48
dotnet build Engine.slnx -c Release -p:EngineTargetFramework=net10.0
python tools/verify-engine.py
```

The verification script copies engine sources, the independent button pack, tests and build configuration into a temporary directory. Core and pack builds must succeed without any consumer workspace. Tests load the external button DLL without a project reference. The game's regression suite also inspects the shipped engine assemblies for game dependencies.

Projects consume the selected engine through its generic runner:

```sh
./build.sh "/absolute/path/to/project" portable
```

The runner builds the engine libraries first, stages them under the engine’s own `Builds/SDK/<framework>`, and supplies `ConfectorySdkDirectory` to the project's build commands. Declared engine packs are prepared from the engine's own registry and supplied to the project’s declared integration location. Previous consumer output is never a build prerequisite.

Generate public signature references after a build with `tools/ApiReference`. It accepts an assembly path and an output text path, reads its XML documentation when available, and emits no implementation. Game and engine references are separate so pack work can read only the relevant contract.


The SDK is generated under the engine’s own `Builds/SDK` and supplied through `ConfectorySdkDirectory`. See [project layout](PROJECT_LAYOUT.md); no consumer SDK copy or previously published output is required.
