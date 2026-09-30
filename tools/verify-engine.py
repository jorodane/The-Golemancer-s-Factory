"""Build only the engine source in an isolated directory, without any consumer game."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    output = ROOT / "TestResults/engine"
    output.mkdir(parents=True, exist_ok=True)
    report = {"passed": False, "frameworks": [], "consumerSourcePresent": False}
    try:
        with tempfile.TemporaryDirectory(prefix="pack-engine-only-") as folder:
            isolated = Path(folder)
            shutil.copytree(ROOT / "src", isolated / "src", ignore=shutil.ignore_patterns("bin", "obj"))
            shutil.copytree(ROOT / "tests/PackEngine.Verification", isolated / "tests/PackEngine.Verification", ignore=shutil.ignore_patterns("bin", "obj"))
            for name in ("Engine.slnx", "Directory.Build.props", "global.json", "NuGet.Config"):
                shutil.copy2(ROOT / name, isolated / name)
            for project in (isolated / "src").rglob("*.csproj"):
                for reference in ET.parse(project).iter("ProjectReference"):
                    path = (project.parent / reference.attrib["Include"]).resolve()
                    if not path.is_relative_to(isolated / "src") or not path.is_file():
                        raise RuntimeError("Engine references a consumer project: " + str(path))
            for framework in ("net48", "net10.0"):
                result = subprocess.run([args.dotnet, "build", "Engine.slnx", "-c", "Release",
                    f"-p:EngineTargetFramework={framework}", "-m:1", "--disable-build-servers", "--nologo", "-v:minimal"],
                    cwd=isolated, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8", errors="replace")
                (output / (framework + ".log")).write_text(result.stdout, encoding="utf-8")
                if result.returncode:
                    raise RuntimeError(result.stdout)
                report["frameworks"].append(framework)
            result = subprocess.run([args.dotnet, "build", "tests/PackEngine.Verification/PackEngine.Verification.csproj", "-c", "Release",
                "-p:EngineTargetFramework=net10.0", "-m:1", "--disable-build-servers", "--nologo"], cwd=isolated,
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8", errors="replace")
            (output / "timing-build.log").write_text(result.stdout, encoding="utf-8")
            if result.returncode:
                raise RuntimeError(result.stdout)
            result = subprocess.run([args.dotnet, "tests/PackEngine.Verification/bin/Release/net10.0/PackEngine.Verification.dll"],
                cwd=isolated, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8", errors="replace")
            (output / "timing-tests.log").write_text(result.stdout, encoding="utf-8")
            if result.returncode:
                raise RuntimeError(result.stdout)
            report["timingPassCount"] = result.stdout.count("PASS:")
            report["passed"] = True
            print("PASS: engine builds on net48 and net10.0 and passes timing verification without consumer sources or SDK copies.")
    finally:
        (output / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
