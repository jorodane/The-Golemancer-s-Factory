# Engine elements as object packs

The engine core has no built-in button implementation or widget definition. `packs/Ui.Button` is an independently compiled default object pack, using only `PackEngine.Contracts`. Its DLL owns pointer capture, keyboard activation, enabled/selected state, cancellation, subscriptions and drawing choices. Its `ui.xml` declares properties, defaults, events and renderer IDs.

`Engine.slnx` does not reference this pack. `tools/export-sdk.py` builds it separately and distributes it to `Golemancer/Content/Packs/01.EngineButton`. The consumer has no project or assembly reference to the button DLL. Runtime `PackCompiler` loads it from `Bin/{framework}`; the normal pack fingerprint includes its DLL and XML. Only the core SDK is frozen: the default button pack can be replaced independently.

## Boundaries

| Owner | Responsibility |
| --- | --- |
| Engine contracts | `IUiRendererRegistry`, `IUiElementFactory`, `IUiCanvasElement`, generic input and primitive canvas operations |
| Engine runtime | DLL discovery, dependencies, ABI validation, factory registration, UI preflight, binding and disposal |
| Button pack DLL | Button state transitions and drawing policy; no game, Skia, WPF, Android or SDL types |
| Button pack XML | `engine.button` properties and `engine.button.canvas` implementation selection |
| Game UI XML | Labels/bindings, game theme and the `control.activate` command |
| Consumer adapter | Native input conversion, bounds, focus/capture routing, primitive drawing and accessibility integration |

An engine pack declares `engineContracts="1"`, not a game `contracts` version. Its entry point implements `IPackModule<IUiRendererRegistry>` and registers factories. It cannot declare domain `Data`. A game declares an explicit `Depends` on a desired engine pack. Missing packs, DLLs, providers, incompatible contracts and duplicate IDs fail clearly; the engine has no hardcoded fallback. This is an ownership boundary, not a security sandbox for arbitrary DLL code.

## Drawing and input

`IUiCanvas` provides fill and centered text in top-left logical coordinates, with `#RRGGBBAA` colors. The host retains layout, text/font services and native canvas ownership. `IUiCanvasElement.Draw` accepts the current bounds. `Input` receives normalized pointer/key/focus events; the host performs hit testing and routes captured pointer movement/release even outside the bounds. A button activates once on release inside; cancelling, losing focus or disabling it clears the press without activation. Enter and Space activate on key release; repeats are ignored. Native accessibility adapters may supply `Activate`.

`Set` never emits user events. Each mounted button has independent state. `Listen` returns independent subscriptions, and disposing the mounted view detaches bindings/listeners and destroys the element. Input and drawing run on the host UI thread. The current canvas hosts do not yet implement general keyboard focus navigation or native accessibility trees.

To replace a button, replace the pack at application startup, preserving its declared widget/event contract or update the consumer's explicit view contract. Do not install two packs with identical IDs together. Editing XML defaults changes styling without recompiling the DLL; replacing DLL behavior does not require recompiling the engine. Live DLL unloading/replacement is outside this change. A pack supplies a factory for many instances; there is no DLL per button.

## Current consumer scope

The shared Android/Linux/iOS presentation uses the pack for ordinary buttons and virtual-control painting. Continuous logical movement/action input remains in the game's input adapter. Game-specific radial menu bubbles and the existing Windows WPF screen are not migrated. Both net48 and net10.0 pack DLLs are distributed; net48 compilation does not imply WPF rendering migration. iOS native build/device execution and platform-specific accessibility remain separate work.

`tools/verify-engine.py` builds the core and button pack without any consumer source, then loads the actual DLL for input, lifecycle and XML replacement tests. The game verification and native Linux smoke exercise the consumer integration separately.
