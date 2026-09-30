# The Golemancer's Factory

- The game engine and authoritative simulation are .NET. Runtime DLL loading is a core experiment and must remain real; the host must not reference content-module projects.
- Object packs contain independently compiled implementations, XML settings/localization and optional art. Actions, conditions and failure handlers are objects too.
- Contracts specify semantics, ownership and partial effects. Keep module implementations local and depend on shared contracts, not other modules' concrete types.
- Maps, construction footprints, collision and action ranges use integer tiles. Actors move continuously with persisted sub-tile offsets; manual input and replay must never snap to tile centers.
- Native Windows WPF application targeting .NET Framework 4.8, Android targeting net10.0-android, Linux SDL2 targeting net10.0, and an experimental UIKit host targeting net10.0-ios. No browser host. Shared engine, client session and independent modules also target net10.0 for Android and portable verification.
- Android packages independent net10.0 module DLLs as assets, extracts them to private storage and loads them through PackLoadContext. Preserve JIT/reflection: no trimming or AOT. Keep the SDK's normal assembly store/compression for host runtime assemblies; that does not replace external module loading.
- Logical input actions belong to modules/XML. Platform/device bindings adapt keyboard, touch and gamepad without key enums in Contracts. Pointer/device release, cancellation and activity suspension must clear only the appropriate input sources.
- Track ready-to-run EXE/runtime DLLs in Builds/Windows and the compiled net48 module DLLs in Content/Packs/*/Bin/net48. Start.bat launches these files without rebuilding or unzipping. Build.bat rebuilds and publishes them through tools/Publish.proj. Keep Content at the game folder root so local images and saves remain in place across updates.
- Distribute Android APKs as separate download files; never commit or push APK binaries to Git. Keep Android build metadata and checksums in Builds/Android.
- Actual external SVG/PNG images are required. Tilesets are independent object packs. The user uploads images through their own Git client because image uploads hang the assistant's Git plugin. Never upload image bytes through the assistant's Git tools, and never ignore images in .gitignore: the user must be able to add them normally. Stage explicit code/binary paths, not the whole worktree. Never synthesize replacement tile or character artwork in the renderer.
- Characters use animation sheets with per-animation offsets and frame metadata. Do not animate a static character by bobbing its whole image. Animation display offsets are separate from continuous world position and tile collision footprints.
- Enrin MUST retain her emerald bracelet, head-mounted calculating mini golem and its chalkboard. Quest portraits include all three; the chalkboard draws emotion faces.
- Chapter 1 ends after the Springwater King, mining core, colourless mana crystals, mana tower, recorded automation, mini-golem logistics and opening the cave.
- Regular cores come from merchant/events/bosses and are recovered immediately on destruction. Only mini cores are craftable. Mini golems are controllable/recordable and have one inventory slot.
- Only the currently controlled, non-replaying golem runs at 50% without mana. Every other unpowered golem pauses movement, work, queues, tactical actions and pickup without losing its progress or reservations. Charging belongs to recordings.
- Preserve saved unknown object/component data when packs are absent. Do not discard over-capacity inventory.
- Verify each meaningful module and the full campaign before pushing. User authorizes commits and pushes throughout this task.

- Tab toggles combat/everyday mode; left click quick-uses, right click opens local interaction bubbles. Never restore a side inspector. Clicking another golem must allow direct item transfer without switching control.
- Harvest outputs and monster loot first exist as ground items, then the harvesting/killing golem auto-collects. Manual drops, demolition byproducts and dead golem cargo require E pickup; holding E collects nearby stacks. Only retail fixtures require shop placement; factories and storage can be built outdoors.

- On golem death, worn/carried equipment and upgrades are lost; ordinary inventory drops in stack-limited piles. Installed and carried cores return to the treasury immediately, exactly once.
- Region collection is a persistent, recordable logistics action around a fixed tile; E remains instant nearby pickup. Missing materials disable recipe buttons without hiding their hover details. Purchase choices obey shared quantity limits; permanent recipe books can be bought only once.

- Selecting a golem is observation/possession only: preserve playback, work, reservations and queues. World selection of a visible actor preserves the camera; management/hotbar/offscreen selection focuses it.
- Follow/rescue suspends the follower's existing commands. Powered followers spend their own mana; externally towed empty followers obey continuous collision/tether rules while slowing the leader. Never enable ordinary unpowered work as a towing shortcut. Charging another golem requires that golem to be adjacent to a fueled tower.
- Memory editing uses a detached draft with undo/redo, dependency validation and explicit save. Closing through any path discards unsaved changes; re-recording must never silently overwrite a saved memory. Active playback owns an immutable snapshot and keeps diagnostics for failed steps. Default retry scans later frames and waits only after a wholly failed cycle; explicit failure policies remain intact.

## Standalone game experiment

- All relative paths above are relative to this Golemancer folder. Keep game modules, content, native game clients, tests and documentation inside it.
- Game rules and models live in src/Golemancer.Contracts and src/Golemancer.Runtime. Game modules reference the local game-contract project and the frozen PackEngine.Contracts DLL; only clients/runtime/tests reference PackEngine.Runtime.
- Never add a project reference outside this folder. SDK/engine-lock.json pins only the independent PackEngine libraries. Do not alter the SDK or its lock to make a game-only extension pass.
- The domain DLL ABI is version 2 (pack.xml contracts="2"); rebuild old game DLLs against the new contracts. Existing JSON saves retain schema version 1.
- BuildPacks.bat builds content DLLs and the game contracts. TestIsolation.bat copies this folder without engine source and runs the existing game and full campaign while comparing engine/host hashes. Optional TeaBreak checks require --with-tea-break and are not part of the current separation verification.
- Generic UI WPF/Android backends are not implemented. The game uses its existing native screens; engine separation does not add renderer capabilities.
- Native hosts bind frame work through the SDK timing scheduler; see docs/TIMING.md. Keep simulation at priority 0 and camera preparation at render-update -10 before native submission 0. The engine must never learn game or native host types.
- All native hosts use Client.GameCamera, a compatibility adapter over the SDK rendering camera. Keep interpolation/projection in the engine, game tracking/bounds in the consumer, and picking on the last-drawn view. Wheel/buttons set zoom targets; zoom alone must not rebuild terrain caches. See SDK/CAMERA.md.

## Portable presentation hosts

- Android, Linux and iOS share Golemancer.Presentation for game-specific rendering, menus, gestures and dialogs. Keep OS types in native hosts. This module is not a generic engine UI backend. WPF retains its existing renderer and shares client logic and linked helper source.
- iOS bundles independent IL pack DLLs and uses the real loader with UseInterpreter=true and TrimMode=copy. Do not replace runtime pack loading with a static registry to make the experiment pass. Mac/Xcode native build and device execution remain separate validation gates.
- Linux full runtime packages are separate downloads; commit source, launch/build scripts and Builds/Linux metadata only. Never include saves in delivery packages.
