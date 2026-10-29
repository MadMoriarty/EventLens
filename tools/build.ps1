param([switch]$SkipTests)
$ErrorActionPreference='Stop'
$eventLensRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $eventLensRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_HOME=Join-Path $eventLensRoot '.dotnet-home'
$env:NUGET_PACKAGES=Join-Path $eventLensRoot '.nuget\packages'
$sdk=Join-Path $eventLensRoot '.tools\dotnet\dotnet.exe'
if(-not(Test-Path -LiteralPath $sdk)){$sdk='dotnet'}
if(-not $SkipTests){
    & $sdk test tests/EventLens.Tests/EventLens.Tests.csproj -c Release; if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
    & $sdk test tests/EventLens.App.Tests/EventLens.App.Tests.csproj -c Release; if($LASTEXITCODE -ne 0){throw 'UI tests failed'}
}
& $sdk publish src/EventLens.App/EventLens.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o artifacts/portable
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
& (Join-Path $PSScriptRoot 'collect-licenses.ps1')
foreach($name in @('README.md','LICENSE','SECURITY.md','THIRD-PARTY-NOTICES.md','CHANGELOG.md')){Copy-Item -LiteralPath $name -Destination (Join-Path 'artifacts/portable' $name) -Force}
Copy-Item -LiteralPath 'docs' -Destination 'artifacts/portable' -Recurse -Force
if(Test-Path -LiteralPath 'licenses'){Copy-Item -LiteralPath 'licenses' -Destination 'artifacts/portable' -Recurse -Force}
$distributionFiles=@('EventLens.exe','README.md','LICENSE','SECURITY.md','THIRD-PARTY-NOTICES.md','CHANGELOG.md','docs','licenses') | ForEach-Object {Join-Path 'artifacts/portable' $_}
Compress-Archive -LiteralPath $distributionFiles -DestinationPath artifacts/EventLens-win-x64.zip -Force
Get-FileHash -LiteralPath artifacts/portable/EventLens.exe,artifacts/EventLens-win-x64.zip -Algorithm SHA256
