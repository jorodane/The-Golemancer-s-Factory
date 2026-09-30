# Playable chapter scope

This repository implements the first playable chapter, not an empty engine demo.

Acceptance route: collect herbs -> sell at the shop -> buy and assemble a crafting golem -> construct fumigators -> collect springwater drops and manufacture jelly -> grow the shop and fulfill an order -> defeat the Springwater King -> assemble the mining golem and mine colourless mana -> read the handbook and construct/fuel a mana tower -> record and replay a supply route -> assemble a one-slot mini golem and transport goods -> open and enter the cave.

The simulation, checks, inventory transfers, timed work, collisions, construction, production, sales, combat, quests, automation and persistence execute in independently loaded .NET modules. The native Windows host is WPF targeting .NET Framework 4.8. It displays actual external tile images and animation sheets; tilesets are independent object packs. Animation display offsets are separate from integer world coordinates. Static-image bobbing is not used.

Source builds use .NET SDK 10 and Framework 4.8 reference assemblies. Shared net10.0 assemblies serve portable verification and a native net10.0-android host. The WPF window has a 1100x720 minimum size. The Windows game needs Framework 4.8 and its separate image packs. Android packages the same XML, artwork and independently built module DLLs in an offline APK; it extracts packs and loads them dynamically without host references to content modules. Android uses SkiaSharp for native rendering, with touch/gamepad/keyboard adapters and private saves. See ANDROID.md for the build and input contracts. Actual Windows GUI execution remains unverified in this environment.

Unspecified numerical values are initial gameplay balance, marked in content XML. The next chapter is an unlocked entrance and introductory area, not unplanned chapter-two content. Long seasons are eight 15-day phases. Reference images are not redistributed in the source repository.
