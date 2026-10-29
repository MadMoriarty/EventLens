$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$eventLensRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $eventLensRoot '.tools'
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdkVersion = $metadata.'latest-sdk'
$sdk = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $sdkVersion } | Select-Object -First 1
$package = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.url.EndsWith('.zip') } | Select-Object -First 1
if (-not $package) { throw 'Official Windows x64 SDK package not found' }
$zipPath = Join-Path $toolsRoot 'dotnet-sdk.zip'
$sdkPath = Join-Path $toolsRoot 'dotnet'
if (-not (Test-Path -LiteralPath (Join-Path $sdkPath 'dotnet.exe'))) {
    Invoke-WebRequest $package.url -OutFile $zipPath
    $actual = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA512).Hash
    if ($actual -ne $package.hash) { throw 'SDK SHA512 does not match Microsoft release metadata' }
    Expand-Archive -LiteralPath $zipPath -DestinationPath $sdkPath -Force
}
$metadata | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $toolsRoot 'release-metadata.json')
& (Join-Path $sdkPath 'dotnet.exe') --version
