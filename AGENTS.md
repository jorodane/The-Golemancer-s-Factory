# The Golemancer's Factory

- The game engine and authoritative simulation are .NET. Runtime DLL loading is a core experiment and must remain real; the host must not reference content-module projects.
- Object packs contain independently compiled implementations, XML settings/localization and optional art. Actions, conditions and failure handlers are objects too.
- Contracts specify semantics, ownership and partial effects. Keep module implementations local and depend on shared contracts, not other modules' concrete types.
- Maps, construction footprints, collision and action ranges use integer tiles. Actors move continuously with persisted sub-tile offsets; manual input and replay must never snap to tile centers.
- Native Windows WPF application targeting .NET Framework 4.8. No browser host. Portable net10.0 is for shared-engine verification only.
- Track the verified Windows distribution ZIP at Builds/The-Golemancers-Factory-Windows-net48.zip with its source commit and SHA-256 in Builds/README.md. Intermediate bin/obj outputs stay ignored; generated image packs remain separate.
- Actual external SVG/PNG images are required. Tilesets are independent object packs. Deliver images in a separate ZIP; do not commit generated image assets. Never synthesize replacement tile or character artwork in the renderer.
- Characters use animation sheets with per-animation offsets and frame metadata. Do not animate a static character by bobbing its whole image. Animation display offsets are separate from continuous world position and tile collision footprints.
- Enrin MUST retain her emerald bracelet, head-mounted calculating mini golem and its chalkboard. Quest portraits include all three; the chalkboard draws emotion faces.
- Chapter 1 ends after the Springwater King, mining core, colourless mana crystals, mana tower, recorded automation, mini-golem logistics and opening the cave.
- Regular cores come from merchant/events/bosses and are recovered immediately on destruction. Only mini cores are craftable. Mini golems are controllable/recordable and have one inventory slot.
- Unpowered manual golems run at 50%; unpowered replay stops. Charging belongs to recordings.
- Preserve saved unknown object/component data when packs are absent. Do not discard over-capacity inventory.
- Verify each meaningful module and the full campaign before pushing. User authorizes commits and pushes throughout this task.

- Tab toggles combat/everyday mode; left click quick-uses, right click opens local interaction bubbles. Never restore a side inspector. Clicking another golem must allow direct item transfer without switching control.
- Harvest outputs first exist as ground items, then the harvesting golem auto-collects. Other ground items require E pickup; holding E collects nearby stacks. Only retail fixtures require shop placement; factories and storage can be built outdoors.
