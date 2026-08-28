[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot 'PresentMon.exe')
)

$ErrorActionPreference = 'Stop'
$uri = 'https://github.com/GameTechDev/PresentMon/releases/download/v2.4.1/PresentMon-2.4.1-x64.exe'
$expectedSha256 = 'D74183E7AE630F72CD3690BE0373ECBFDC6CBB86578148AAB8FA2A7166068F34'

$destinationDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($Destination))
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
$temporary = Join-Path $destinationDirectory 'PresentMon.download.tmp'
try {
    Invoke-WebRequest -Uri $uri -OutFile $temporary -UseBasicParsing
    $actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporary).Hash
    if ($actualSha256 -ne $expectedSha256) { throw "PresentMon SHA-256 mismatch: $actualSha256" }
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
    Write-Output "PresentMon verified and installed at $([IO.Path]::GetFullPath($Destination))"
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}
