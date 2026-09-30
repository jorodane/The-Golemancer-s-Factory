#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
# .NET 10 SDK + android workload + Android SDK API 36 / build-tools 36.0.0 + JDK 21.
# Extra arguments are passed to the Android build, e.g. -p:AndroidSdkDirectory=/path.
dotnet build Golemancer.Headless.slnf -c Release -p:GolemancerTargetFramework=net10.0 -m:1 --disable-build-servers --nologo -v:minimal
dotnet tests/Golemancer.Verification/bin/Release/net10.0/Golemancer.Verification.dll
dotnet build src/Golemancer.Android/Golemancer.Android.csproj -c Release -p:GolemancerTargetFramework=net10.0 -m:1 --disable-build-servers --nologo -v:minimal "$@"
revision=$(git rev-parse HEAD)
dotnet msbuild tools/PublishAndroid.proj -t:Publish -p:SourceRevision="$revision" -nologo -v:minimal
