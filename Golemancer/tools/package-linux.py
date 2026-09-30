#!/usr/bin/env python3
"""Package a published Linux runtime plus real content; never include saves or source code."""
import argparse
import hashlib
import json
import pathlib
import tarfile

parser = argparse.ArgumentParser()
parser.add_argument("--rid", default="linux-x64", choices=["linux-x64", "linux-arm64"])
parser.add_argument("--output", required=True)
parser.add_argument("--source", required=True, help="Verified source revision")
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
runtime = root / "Builds/Linux" / args.rid
if not (runtime / "Golemancer.Linux").is_file():
    parser.error("Run BuildLinux.sh before packaging.")
files = [root / "StartLinux.sh", root / "docs/LINUX.md"] + [p for p in runtime.rglob("*") if p.is_file() and p.suffix != ".pdb"]
content = root / "Content/Packs"
for path in content.rglob("*"):
    if not path.is_file():
        continue
    if path.suffix.lower() in {".xml", ".svg", ".png"} or ("net10.0" in path.parts and path.suffix == ".dll"):
        files.append(path)
if not any(p.suffix == ".dll" and "net10.0" in p.parts and "Packs" in p.parts for p in files):
    parser.error("Independent net10.0 object-pack DLLs are missing.")
info = runtime.parent / "build-info.json"
info.write_text(json.dumps({"sourceCommit": args.source, "rid": args.rid, "selfContained": True,
    "renderer": "SDL2 + shared Skia presentation", "requires": ["SDL2", "fontconfig", "Korean fonts"],
    "physicalDisplayTested": False}, indent=2) + "\n")
files.append(info)
out = pathlib.Path(args.output).resolve()
out.parent.mkdir(parents=True, exist_ok=True)
with tarfile.open(out, "w:gz") as archive:
    for path in sorted(set(files)):
        archive.add(path, arcname="Golemancer/" + str(path.relative_to(root)), recursive=False)
digest = hashlib.file_digest(out.open("rb"), "sha256").hexdigest()
out.with_suffix(out.suffix + ".sha256").write_text(f"{digest}  {out.name}\n")
print(json.dumps({"file": str(out), "bytes": out.stat().st_size, "sha256": digest, "files": len(set(files))}))
