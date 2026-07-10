#!/bin/sh
# Builds standalone single-file binaries for all supported platforms into dist/.
set -e
cd "$(dirname "$0")/src/MountTool"

for rid in win-x64 osx-arm64 osx-x64 linux-x64; do
    dotnet publish -r "$rid" -c Release --self-contained \
        -p:PublishSingleFile=true -p:DebugType=none \
        -o "../../dist/$rid"
done
