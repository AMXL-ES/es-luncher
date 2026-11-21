#requires -version 5.1
<#
  Sign one file with Authenticode using a PFX (no signtool / Windows SDK required).

  Used by scripts/pack-velopack.ps1 through vpk's --signTemplate {{file}} placeholder,
  and usable standalone:

    powershell -NoProfile -File scripts\sign-file.ps1 -File artifacts\releases\ESLuncher-win-Setup.exe `
        -PfxPath D:\dev\test-signing\es-luncher-test.pfx -PfxPassword es-luncher-test

  Status semantics from Set-AuthenticodeSignature:
    Valid        = signed and the chain is trusted by this machine
    UnknownError = signed correctly, but the root (self-signed cert) is not trusted here
                   -> expected for the local test certificate, still cryptographically valid
  Anything else is treated as a failure (non-zero exit).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$File,
    [Parameter(Mandatory = $true)][string]$PfxPath,
    [string]$PfxPassword = '',
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $File)) { throw "找不到要签名的文件：$File" }
if (-not (Test-Path $PfxPath)) { throw "找不到证书文件：$PfxPath" }

$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword, $flags)

$signature = Set-AuthenticodeSignature -FilePath $File -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer

if ($signature.Status -eq 'Valid' -or $signature.Status -eq 'UnknownError') {
    Write-Host ("signed: {0} -> {1}" -f (Split-Path -Leaf $File), $signature.Status)
    exit 0
}

Write-Error ("签名失败：{0} -> {1} {2}" -f (Split-Path -Leaf $File), $signature.Status, $signature.StatusMessage)
exit 1
