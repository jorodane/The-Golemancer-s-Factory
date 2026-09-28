# The Golemancer's Factory

- The game engine and authoritative simulation are .NET. Runtime DLL loading is a core experiment and must remain real; the host must not reference content-module projects.
- Object packs contain independently compiled implementations, XML settings/localization and optional art. Actions, conditions and failure handlers are objects too.
- Contracts specify semantics, ownership and partial effects. Keep module implementations local and depend on shared contracts, not other modules' concrete types.
- Map, movement destinations, construction footprints and save data use integer tiles.
- Vector graphics. Art assets are delivered in a separate ZIP for the owner to upload. Do not commit generated image assets. Procedural vector rendering keeps the game usable before asset import.
- Enrin MUST retain her emerald bracelet, head-mounted calculating mini golem and its chalkboard. Quest portraits include all three; the chalkboard draws emotion faces.
- Chapter 1 ends after the Springwater King, mining core, colourless mana crystals, mana tower, recorded automation, mini-golem logistics and opening the cave.
- Regular cores come from merchant/events/bosses and are recovered immediately on destruction. Only mini cores are craftable. Mini golems are controllable/recordable and have one inventory slot.
- Unpowered manual golems run at 50%; unpowered replay stops. Charging belongs to recordings.
- Preserve saved unknown object/component data when packs are absent. Do not discard over-capacity inventory.
- Verify each meaningful module and the full campaign before pushing. User authorizes commits and pushes throughout this task.
