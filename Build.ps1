[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $AssetDirectory,
    [string] $OutputDirectory = "$PSScriptRoot/out/win-x64"
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $AssetDirectory).Path
$target = [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).Path)
if ($source.TrimEnd('\','/') -eq $target.TrimEnd('\','/')) {
    throw 'Use a different output directory from the extracted release.'
}
foreach ($model in 'B1','B2') {
    $archive = Join-Path $source "assets/$model/sprites.v1"
    if (!(Test-Path -LiteralPath $archive -PathType Leaf)) {
        throw "Missing sprite archive for $model. Extract the complete release ZIP first."
    }
}

dotnet publish "$PSScriptRoot/src/Vespera.csproj" -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$PSScriptRoot=/_/Vespera" -o $target
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

foreach ($model in 'B1','B2') {
    Copy-Item -LiteralPath "$source/assets/$model/sprites.v1" -Destination "$target/assets/$model/sprites.v1" -Force
}
Copy-Item -LiteralPath "$PSScriptRoot/Start-Vespera.cmd" -Destination "$target/Start-Vespera.cmd" -Force
$test = Start-Process -FilePath "$target/Vespera.exe" -WorkingDirectory $target -ArgumentList @('--check','--verify-sprites') -NoNewWindow -PassThru -Wait
if ($test.ExitCode -ne 0) { throw "Sprite verification failed ($($test.ExitCode))." }
"Vespera is ready in $target"
