# Signs a release file with ECDSA P-256 / SHA-256 and writes "<file>.sig" next to it (base64, 64 bytes).
# Campfire only installs an update whose signature matches the public key it was built with.
#
#   $env:CAMPFIRE_SIGNING_KEY = Get-Content <key file>
#   powershell -File tools/sign.ps1 -File Campfire.exe
param(
    [Parameter(Mandatory = $true)][string]$File
)

$ErrorActionPreference = 'Stop'
$encoded = "$env:CAMPFIRE_SIGNING_KEY".Trim()
if (-not $encoded) {
    Write-Error 'CAMPFIRE_SIGNING_KEY is not set.'
}
$raw = [Convert]::FromBase64String($encoded)
if ($raw.Length -ne 96) {
    Write-Error 'CAMPFIRE_SIGNING_KEY is not a key made by tools/new-signing-key.ps1.'
}

$parameters = New-Object System.Security.Cryptography.ECParameters
$parameters.Curve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
$point = New-Object System.Security.Cryptography.ECPoint
$point.X = $raw[0..31]
$point.Y = $raw[32..63]
$parameters.Q = $point
$parameters.D = $raw[64..95]
$key = [System.Security.Cryptography.ECDsa]::Create($parameters)

$path = (Resolve-Path $File).Path
$data = [System.IO.File]::ReadAllBytes($path)
$hash = [System.Security.Cryptography.HashAlgorithmName]::SHA256
$signature = $key.SignData($data, $hash)
if (-not $key.VerifyData($data, $signature, $hash)) {
    Write-Error 'The signature could not be verified.'
}

Set-Content -Path "$path.sig" -Value ([Convert]::ToBase64String($signature)) -Encoding ascii -NoNewline
Write-Host "Signed $path"
