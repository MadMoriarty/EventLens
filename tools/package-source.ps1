$ErrorActionPreference='Stop'
$eventLensRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $eventLensRoot
$files=@(& git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if($LASTEXITCODE -ne 0){throw 'Could not enumerate public source files'}
if($files | Where-Object {$_ -match '(?i)(^|/)(qa/private|\.tools|\.nuget|\.research|bin|obj|artifacts)/|\.evtx$|eventlens.*\.settings\.json$'}){throw 'Private/build data was unexpectedly included'}
Add-Type -AssemblyName System.IO.Compression
$archivePath=Join-Path $eventLensRoot 'artifacts\EventLens-source.zip'
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::Create)
$archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($file in $files){
        $entry=$archive.CreateEntry('EventLens/'+$file,[IO.Compression.CompressionLevel]::Optimal)
        $inputStream=[IO.File]::OpenRead((Join-Path $eventLensRoot $file))
        $outputStream=$entry.Open()
        try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
    }
}finally{$archive.Dispose();$stream.Dispose()}
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
