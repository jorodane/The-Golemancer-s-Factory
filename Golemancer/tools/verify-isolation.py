"""Copy only the game folder and build an external pack against unchanged engine DLLs."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pinned_files(root: Path) -> dict[str, str]:
    lock = json.loads((root / "SDK/engine-lock.json").read_text(encoding="utf-8"))
    for name, expected in lock["files"].items():
        if digest(root / name) != expected:
            raise RuntimeError(f"Frozen engine/API differs from the baseline: {name}")
    # Include the existing Windows host and dependencies: adding the pack must not replace them.
    files = set(lock["files"])
    files.update(p.relative_to(root).as_posix() for p in (root / "Builds/Windows").iterdir() if p.is_file())
    return {name: digest(root / name) for name in sorted(files)}


def check_projects(root: Path) -> int:
    projects = sorted(root.rglob("*.csproj"))
    for project in projects:
        if {"obj", "bin", "TestResults"} & set(project.relative_to(root).parts):
            continue
        if project.stem in {"Golemancer.Engine", "Golemancer.Contracts"}:
            raise RuntimeError(f"Engine/API source project must not be inside the game experiment: {project}")
        for reference in ET.parse(project).iter("ProjectReference"):
            value = reference.attrib["Include"]
            if "$" in value:
                raise RuntimeError(f"Cannot verify an indirect project reference: {project}: {value}")
            target = (project.parent / value.replace("\\", "/")).resolve()
            if not target.is_relative_to(root) or not target.is_file():
                raise RuntimeError(f"Project reference leaves the standalone game folder: {project}: {value}")
    return len(projects)


def copy_filter(folder: str, names: list[str]) -> set[str]:
    return set(names) & {"bin", "obj", "TestResults", "Saves", "Artifacts", ".git", "node_modules"}


def run(root: Path, output: Path, label: str, command: list[str]) -> str:
    print(f"Checking {label}...", flush=True)
    # The working directory is the copied game; neither the original repository nor its solution is used.
    result = subprocess.run(command, cwd=root, encoding="utf-8", errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (output / f"{label}.log").write_text(result.stdout, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"{label} failed ({result.returncode}):\n" + "\n".join(result.stdout.splitlines()[-24:]))
    return result.stdout


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet", help=".NET 10 SDK executable")
    parser.add_argument("--keep", action="store_true", help="Keep the isolated copy for inspection")
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise RuntimeError("Install the .NET 10 SDK or provide --dotnet /path/to/dotnet.")
    output = ROOT / "TestResults/isolation"
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / "report.json"
    report = {"passed": False, "baselineCommit": json.loads((ROOT / "SDK/engine-lock.json").read_text())["baselineCommit"]}
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    original = pinned_files(ROOT)
    temporary = Path(tempfile.mkdtemp(prefix="golemancer-isolation-"))
    isolated = temporary / "Golemancer Only"
    try:
        shutil.copytree(ROOT, isolated, ignore=copy_filter)
        projects = check_projects(isolated)
        before = pinned_files(isolated)
        # Ensure no prebuilt optional extension can produce a false positive.
        for path in (isolated / "examples/TeaBreak/Pack").rglob("*.dll"):
            path.unlink()
        sdk_flags = ["-c", "Release", "-m:1", "--disable-build-servers", "--nologo", "-v:minimal"]
        run(isolated, output, "portable-build", [dotnet, "build", "Golemancer.Headless.slnf", "-p:GolemancerTargetFramework=net10.0", *sdk_flags])
        runner = "tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll"
        campaign = run(isolated, output, "campaign", [dotnet, runner])
        if "PASS: FULL CAMPAIGN:" not in campaign:
            raise RuntimeError("The full campaign was not executed.")
        for name in ["Golemancer.Contracts.dll", "Golemancer.Engine.dll"]:
            if digest(isolated / "SDK/net10.0" / name) != digest(isolated / Path(runner).parent / name):
                raise RuntimeError(f"Verification loaded a different engine/API build: {name}")
        run(isolated, output, "extension-net10", [dotnet, "build", "examples/TeaBreak/TeaBreak.csproj", "-p:GolemancerTargetFramework=net10.0", *sdk_flags])
        example = run(isolated, output, "extension-behavior", [dotnet, runner, "--example"])
        run(isolated, output, "extension-net48", [dotnet, "build", "examples/TeaBreak/TeaBreak.csproj", "-p:GolemancerTargetFramework=net48", *sdk_flags])
        # A deliberately altered API file must fail the same MSBuild gate used by every game project.
        probe = isolated / "SDK/net10.0/Golemancer.Contracts.dll"
        original_api = probe.read_bytes()
        try:
            probe.write_bytes(original_api + b"isolation-guard-probe")
            rejected = subprocess.run([dotnet, "msbuild", "examples/TeaBreak/TeaBreak.csproj", "-t:CheckFrozenEngine",
                                       "-p:GolemancerTargetFramework=net10.0", "-nologo", "-v:minimal"],
                                      cwd=isolated, encoding="utf-8", errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            (output / "engine-change-rejected.log").write_text(rejected.stdout, encoding="utf-8")
            if rejected.returncode == 0 or "Frozen engine/API changed:" not in rejected.stdout:
                raise RuntimeError("The build gate failed to reject an altered API DLL.")
        finally:
            probe.write_bytes(original_api)
        after = pinned_files(isolated)
        if before != after or original != pinned_files(ROOT):
            raise RuntimeError("An engine/API/Windows host file changed during the extension experiment.")
        report.update({"passed": True, "isolatedFolder": str(isolated), "kept": args.keep,
                       "sourceProjectsChecked": projects, "engineAndHostUnchanged": True, "changedApiRejected": True,
                       "frozenFilesBefore": before, "frozenFilesAfter": after,
                       "campaignPassCount": sum(s.startswith("PASS:") for s in campaign.splitlines()),
                       "extensionPassCount": sum(s.startswith("PASS:") for s in example.splitlines()),
                       "extensionFrameworks": ["net10.0", "net48"], "windowsGuiTested": False})
        print(f"PASS: standalone folder, external DLL behavior, full campaign, {len(before)} unchanged engine/host files.")
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        if not args.keep:
            shutil.rmtree(temporary)
        print(f"Report: {report_path}")


if __name__ == "__main__":
    main()
