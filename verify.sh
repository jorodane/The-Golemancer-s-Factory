#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet build GolemancerFactory.sln -c Release -m:1 --disable-build-servers --nologo -v:minimal
dotnet tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll
