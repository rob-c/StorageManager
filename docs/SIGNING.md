# Code Signing & Notarization

The published binaries are currently **unsigned**, which is why first-time users
hit Windows SmartScreen ("More info → Run anyway") and macOS Gatekeeper
("right-click → Open"). Signing removes those prompts. This document records how
to obtain the credentials and how `publish.sh` uses them. Signing is opt-in:
`./publish.sh` produces the same unsigned output unless `SIGN=1` is set.

## Windows

Two supported paths — pick one:

### Option A — Azure Trusted Signing (recommended, no hardware token)
1. In the Azure portal create a **Trusted Signing** account and a certificate
   profile (Microsoft validates your organization identity once).
2. Install the signing tool: `dotnet tool install --global AzureSignTool`.
3. Provide these environment variables and run `SIGN=1 AZURE_SIGN=1 ./publish.sh`:
   - `AZURE_KEY_VAULT_URL`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
     `AZURE_CLIENT_SECRET`, `AZURE_CERT_NAME`

### Option B — Classic OV/EV certificate
1. Buy an OV (or EV) code-signing certificate from a CA (DigiCert, Sectigo, …).
   EV avoids SmartScreen reputation warnings immediately; OV builds reputation
   over time.
2. Export it as a `.pfx`. Install `osslsigncode` (works on Linux/macOS build hosts).
3. Run `SIGN=1 WIN_PFX=/path/cert.pfx WIN_PFX_PASSWORD=secret ./publish.sh`.

## macOS

1. Enroll in the **Apple Developer Program** ($99/year).
2. Create a **Developer ID Application** certificate in your Apple Developer
   account and install it in the build host's login keychain. Its identity
   string looks like `Developer ID Application: Your Org (TEAMID)`.
3. For notarization, store credentials once as a keychain profile:
   ```
   xcrun notarytool store-credentials NOTARY_PROFILE \
       --apple-id you@example.com --team-id TEAMID --password <app-specific-pw>
   ```
4. Run:
   ```
   SIGN=1 MAC_SIGN_ID="Developer ID Application: Your Org (TEAMID)" \
       NOTARY_PROFILE=NOTARY_PROFILE ./publish.sh
   ```

Note: .NET single-file binaries cannot be `stapler staple`d directly (there is
no app bundle). Notarization is still recorded against the submitted binary
hash, so Gatekeeper validates it online on first launch. For an offline-stapled
experience, wrap the binary in a `.app` bundle or a signed `.dmg` — out of scope
for the current single-file distribution.

## Linux

No signing ecosystem equivalent. Publish GPG-signed SHA-256 checksums alongside
the `linux-x64` binary so users can verify integrity:
```
sha256sum dist/linux-x64/StorageManager > StorageManager.sha256
gpg --detach-sign --armor StorageManager.sha256
```
