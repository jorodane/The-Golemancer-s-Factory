#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0'
  exit 1
fi
dotnet build GolemancerFactory.sln -c Release -m:1 --disable-build-servers --nologo -v:minimal
exec dotnet src/Golemancer.Host/bin/Release/net10.0/Golemancer.Host.dll --open
