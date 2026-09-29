# Playable chapter scope

This repository implements the first playable chapter, not an empty engine demo.

Acceptance route: collect herbs -> sell at the shop -> buy and assemble a crafting golem -> construct fumigators -> collect springwater drops and manufacture jelly -> grow the shop and fulfill an order -> defeat the Springwater King -> assemble the mining golem and mine colourless mana -> read the handbook and construct/fuel a mana tower -> record and replay a supply route -> assemble a one-slot mini golem and transport goods -> open and enter the cave.

The simulation, checks, inventory transfers, timed work, collisions, construction, production, sales, combat, quests, automation and persistence execute in independently loaded .NET modules. The native Windows host is WPF targeting .NET Framework 4.8. It displays actual external tile images and animation sheets; tilesets are independent object packs. Animation display offsets are separate from integer world coordinates. Static-image bobbing is not used.

Source builds use .NET SDK 10 and Framework 4.8 reference assemblies. The portable net10.0 target verifies shared logic on non-Windows machines; it is not a second game UI. The WPF window has a 1100x720 minimum size. The packaged game needs Framework 4.8 and its separate image packs, and works offline. The net48 build and portable campaign passed; actual Windows GUI execution remains unverified in this environment.

Unspecified numerical values are initial gameplay balance, marked in content XML. The next chapter is an unlocked entrance and introductory area, not unplanned chapter-two content. Long seasons are eight 15-day phases. Reference images are not redistributed in the source repository.
