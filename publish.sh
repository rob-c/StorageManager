#!/bin/sh
# Builds standalone single-file binaries for all supported platforms into dist/.
#
# Code signing is opt-in and inert by default. Set SIGN=1 plus the relevant
# credential variables (see docs/SIGNING.md) to sign/notarize the output:
#   Windows: AZURE_SIGN=1 with AzureSignTool env, or WIN_PFX / WIN_PFX_PASSWORD
#   macOS:   MAC_SIGN_ID (Developer ID Application) and, to notarize,
#            NOTARY_PROFILE (a stored notarytool keychain profile)
set -e
cd "$(dirname "$0")/src/StorageManager"

for rid in win-x64 osx-arm64 osx-x64 linux-x64; do
    dotnet publish -r "$rid" -c Release --self-contained \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
        -o "../../dist/$rid"
done

# Mark the Unix binaries executable and package them: the macOS ones as
# double-clickable .app bundles and the Linux one as a tarball with a .desktop
# entry + icon (dist/StorageManager-*). VERSION is read from the csproj.
chmod +x ../../dist/linux-x64/StorageManager
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' StorageManager.csproj | head -1)
bash "../../packaging/make-linux-tarball.sh" "../../dist/linux-x64/StorageManager" "${VERSION:-0.0.0}" "../../dist"
for arch in osx-arm64 osx-x64; do
    chmod +x "../../dist/$arch/StorageManager"
    bash "../../packaging/make-macos-app.sh" "../../dist/$arch/StorageManager" "$arch" "${VERSION:-0.0.0}" "../../dist"
done

[ "$SIGN" = "1" ] || exit 0
echo "SIGN=1 — signing published binaries"

sign_windows() {
    exe="../../dist/win-x64/StorageManager.exe"
    [ -f "$exe" ] || return 0
    if [ "$AZURE_SIGN" = "1" ]; then
        # Azure Trusted Signing via AzureSignTool (dotnet tool install --global AzureSignTool).
        AzureSignTool sign \
            -kvu "$AZURE_KEY_VAULT_URL" -kvi "$AZURE_CLIENT_ID" \
            -kvt "$AZURE_TENANT_ID" -kvs "$AZURE_CLIENT_SECRET" \
            -kvc "$AZURE_CERT_NAME" -tr http://timestamp.digicert.com -td sha256 \
            "$exe"
    elif [ -n "$WIN_PFX" ]; then
        # Classic OV/EV certificate via osslsigncode (cross-platform signtool).
        osslsigncode sign -pkcs12 "$WIN_PFX" -pass "$WIN_PFX_PASSWORD" \
            -t http://timestamp.digicert.com -in "$exe" -out "$exe.signed"
        mv "$exe.signed" "$exe"
    else
        echo "  (skipping Windows: set AZURE_SIGN=1 or WIN_PFX)"
    fi
}

sign_macos() {
    [ -n "$MAC_SIGN_ID" ] || { echo "  (skipping macOS: set MAC_SIGN_ID)"; return 0; }
    for rid in osx-arm64 osx-x64; do
        app="../../dist/$rid/StorageManager"
        [ -f "$app" ] || continue
        codesign --force --options runtime --timestamp --sign "$MAC_SIGN_ID" "$app"
        if [ -n "$NOTARY_PROFILE" ]; then
            zip="../../dist/$rid/StorageManager.zip"
            ditto -c -k --keepParent "$app" "$zip"
            xcrun notarytool submit "$zip" --keychain-profile "$NOTARY_PROFILE" --wait
            # Single-file binaries can't be stapled directly; notarization is
            # recorded against the submitted hash. Distribute the signed binary.
            rm -f "$zip"
        fi
    done
}

sign_windows
sign_macos
echo "Signing complete."
