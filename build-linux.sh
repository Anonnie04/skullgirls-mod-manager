#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if ! command -v dotnet >/dev/null && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
if [[ "${1:-}" == "--test" ]]; then
    dotnet run --project Tests/Tests.csproj -c Release
fi
dotnet publish Desktop/Desktop.csproj -c Release -r linux-x64 --self-contained true -o dist/linux-x64 --nologo
printf 'Ready: %s/dist/linux-x64/SkullgirlsModManager\n' "$PWD"
