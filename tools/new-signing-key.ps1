# Creates the key pair that signs Campfire releases.
#
#   powershell -File tools/new-signing-key.ps1 -OutFile <path outside the repository>
#
# The file holds the private key: put its content into the GitHub secret CAMPFIRE_SIGNING_KEY and keep a
# backup somewhere safe. Never commit it. The public key printed at the end goes into
# src/Campfire/Online/Updater.cs (PublicKey). A new key pair means clients with the old public key can no
# longer update automatically, so only replace it when the private key has leaked.
param(
    [Parameter(Mandatory = $true)][string]$OutFile
)

$ErrorActionPreference = 'Stop'
if (Test-Path $OutFile) {
    Write-Error "$OutFile already exists. Choose another path rather than overwrite a key."
}

$key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$p = $key.ExportParameters($true)
# Private key: X, Y and D, 32 bytes each. Public key: X and Y.
$private = [Convert]::ToBase64String($p.Q.X + $p.Q.Y + $p.D)
$public = [Convert]::ToBase64String($p.Q.X + $p.Q.Y)

Set-Content -Path $OutFile -Value $private -Encoding ascii -NoNewline
Write-Host "Private key written to $OutFile"
Write-Host "Public key: $public"
