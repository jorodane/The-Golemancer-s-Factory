"""Build this engine and explicitly establish a new binary SDK baseline in a consumer workspace."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workspace", type=Path, help="Consumer folder containing SDK and Builds/Windows")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    sources = {p.relative_to(ROOT).as_posix(): digest(p) for p in sorted((ROOT / "src").rglob("*"))
               if p.is_file() and not {"bin", "obj"} & set(p.parts)}
    source_id = hashlib.sha256(json.dumps(sources, sort_keys=True).encode()).hexdigest()
    files = {}
    for framework in ("net48", "net10.0"):
        subprocess.run([args.dotnet, "build", "Engine.slnx", "-c", "Release", f"-p:EngineTargetFramework={framework}",
                        "-p:UseSharedCompilation=false", "-m:1", "--disable-build-servers", "--nologo", "-v:minimal"], cwd=ROOT, check=True)
        target = workspace / "SDK" / framework
        target.mkdir(parents=True, exist_ok=True)
        for assembly in ("PackEngine.Contracts", "PackEngine.Runtime"):
            folder = ROOT / "src" / assembly / "bin/Release" / framework
            for extension in (".dll", ".xml"):
                source = folder / (assembly + extension)
                if not source.exists():
                    continue
                dest = target / source.name
                shutil.copy2(source, dest)
                if extension == ".dll":
                    files[dest.relative_to(workspace).as_posix()] = digest(dest)
                    if framework == "net48":
                        published = workspace / "Builds/Windows" / source.name
                        published.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(source, published)
                        files[published.relative_to(workspace).as_posix()] = digest(published)
        # Optional engine packs are distributed alongside the core, but remain replaceable.
        # They are deliberately outside FrozenEngine.targets and the consumer project graph.
        subprocess.run([args.dotnet, "build", "packs/Ui.Button/PackEngine.Ui.Button.csproj", "-c", "Release",
                        f"-p:EngineTargetFramework={framework}", "-p:UseSharedCompilation=false", "-m:1",
                        "--disable-build-servers", "--nologo", "-v:minimal"], cwd=ROOT, check=True)
        pack = workspace / "Content/Packs/01.EngineButton"
        binary = pack / "Bin" / framework
        binary.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / "packs/Ui.Button/Bin" / framework / "PackEngine.Ui.Button.dll", binary)
        for name in ("pack.xml", "ui.xml"):
            shutil.copy2(ROOT / "packs/Ui.Button" / name, pack / name)
    lock = {"schema": 2, "baselineCommit": "source-sha256:" + source_id, "engineSourceFiles": sources, "files": files}
    (workspace / "SDK/engine-lock.json").write_text(json.dumps(lock, indent=2) + "\n", encoding="utf-8")
    entries = "\n".join(f'    <FrozenEngineFile Include="$(MSBuildThisFileDirectory)../{name}"><ExpectedHash>{sha.upper()}</ExpectedHash></FrozenEngineFile>'
                        for name, sha in sorted(files.items()))
    (workspace / "SDK/FrozenEngine.targets").write_text('''<Project>
  <ItemGroup>
''' + entries + '''
  </ItemGroup>
  <Target Name="CheckFrozenEngine" BeforeTargets="PrepareForBuild">
    <Error Condition="!Exists('%(FrozenEngineFile.Identity)')" Text="Frozen engine/API file missing: %(FrozenEngineFile.Identity). Restore the complete workspace." />
    <GetFileHash Files="@(FrozenEngineFile)" Algorithm="SHA256" MetadataName="ActualHash"><Output TaskParameter="Items" ItemName="CheckedFrozenEngine" /></GetFileHash>
    <Error Condition="'%(CheckedFrozenEngine.ActualHash)' != '%(CheckedFrozenEngine.ExpectedHash)'" Text="Frozen engine/API changed: %(CheckedFrozenEngine.Identity). Restore the SDK baseline; engine upgrades are a separate operation." />
  </Target>
</Project>
''', encoding="utf-8")
    shutil.copy2(ROOT / "docs/TIMING.md", workspace / "SDK/TIMING.md")
    shutil.copy2(ROOT / "docs/CAMERA.md", workspace / "SDK/CAMERA.md")
    shutil.copy2(ROOT / "docs/UI_PACKS.md", workspace / "SDK/UI_PACKS.md")
    print("Exported engine source baseline:", source_id)


if __name__ == "__main__":
    main()
