"""List just one module, its pack definitions and shared contracts for local AI work."""
from pathlib import Path
import argparse
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("module", nargs="?", help="Module suffix, e.g. Crafting")
args = parser.parse_args()
modules = sorted((root / "modules").glob("Golemancer.*"))
if not args.module:
    for module in modules:
        print(module.name.removeprefix("Golemancer."))
else:
    module = next((m for m in modules if m.name.casefold() == ("Golemancer." + args.module).casefold()), None)
    if module is None:
        parser.error("Unknown module")
    project = next(module.glob("*.csproj"))
    pack_name = ET.parse(project).findtext(".//PackDirectory")
    pack = root / "Content" / "Packs" / pack_name
    print("Game API: docs/CONTRACTS.md + docs/API.txt; UI: docs/UI_CONTRACTS.md + SDK/API.txt")
    print("Public game signatures: docs/API.txt (search just the types used by this module)")
    print("References: local Golemancer.Contracts project + frozen SDK/<framework>/Confectory.Contracts.dll")
    print("Build: dotnet build " + str(project.relative_to(root)) + " -m:1 --disable-build-servers")
    print("Module sources:")
    for file in sorted(module.glob("*.cs")):
        print("  " + str(file.relative_to(root)))
    print("Pack XML:")
    for file in sorted(pack.glob("*.xml")):
        print("  " + str(file.relative_to(root)))
    print("Required packs: " + ", ".join(e.attrib["id"] for e in ET.parse(pack / "pack.xml").findall("Depends")))
