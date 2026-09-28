# Playable chapter scope

This repository implements the first playable chapter, not an empty engine demo.

Acceptance route: collect herbs -> sell at the shop -> buy and assemble a crafting golem -> construct fumigators -> collect springwater drops and manufacture jelly -> grow the shop and fulfill an order -> defeat the Springwater King -> assemble the mining golem and mine colourless mana -> read the handbook and construct/fuel a mana tower -> record and replay a supply route -> assemble a one-slot mini golem and transport goods -> open and enter the cave.

The simulation, checks, inventory transfers, timed work, collisions, construction, production, sales, combat, quests, automation and persistence execute in .NET modules. The local browser is an input and vector-rendering client. It contains no authoritative economy or combat simulation. This first release targets .NET 10 and desktop Chromium/Edge/Firefox at 1280x800 or larger. It is a local single-player game; no internet is required to play after the SDK/build or published runtime is installed.

Unspecified numerical values are initial gameplay balance, marked in content XML. The next chapter is an unlocked entrance and introductory area, not unplanned chapter-two content. Long seasons are eight 15-day phases. Reference images are not redistributed in the source repository.
