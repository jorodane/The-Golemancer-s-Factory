#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
# Portable verification of shared simulation and native session logic. Does not launch WPF.
dotnet build Golemancer.Headless.slnf -c Release -p:GolemancerTargetFramework=net10.0 -m:1 --disable-build-servers --nologo -v:minimal
dotnet tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll
