# Creates the key pair that signs Campfire releases.
#
#   powershell -File tools/new-signing-key.ps1 -OutFile <path outside the repository>
#
# The file holds the private key: put its content into the GitHub secret CAMPFIRE_SIGNING_KEY and keep a
# backup somewhere safe. Never commit it. The public key printed at the end goes into
# src/Campfire/Online/Updater.cs (PublicKeys).
#
# Campfire accepts a release signed by any key in PublicKeys. To be ready for a leak, make a second key pair
# now, keep its private key offline and add its public key to PublicKeys. If the current private key leaks,
# sign releases with the backup key from then on and remove the leaked public key in that release. Clients
# that only know the leaked key cannot update automatically and must download the new version by hand.
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
