$ErrorActionPreference='Stop'
$eventLensRoot=Split-Path -Parent $PSScriptRoot
$assets=Get-Content -LiteralPath (Join-Path $eventLensRoot 'src\EventLens.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
$destinationRoot=Join-Path $eventLensRoot 'licenses\packages'
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
foreach($library in $assets.libraries.PSObject.Properties){
    if($library.Value.type -ne 'package'){continue}
    $source=Join-Path $packageRoot $library.Value.path
    $destination=Join-Path $destinationRoot ($library.Name.Replace('/','-'))
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach($file in Get-ChildItem -LiteralPath $source -File -Recurse){
        if($file.Name -match '(?i)license|licence|notice|copying|\.nuspec$'){
            $relative=$file.FullName.Substring($source.Length).TrimStart('\','/')
            $target=Join-Path $destination $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
}
$sdkRoot=Join-Path $eventLensRoot '.tools\dotnet'
foreach($name in @('LICENSE.txt','ThirdPartyNotices.txt')){
    $source=Join-Path $sdkRoot $name
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $eventLensRoot ('licenses\dotnet-'+$name)) -Force}
}
