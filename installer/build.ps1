$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$publishPath = Join-Path $repoRoot 'artifacts\publish\win-x64'
$outputPath = Join-Path $repoRoot 'artifacts\installer'
$compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$compilerPath = if ($compilerCommand) {
    $compilerCommand.Source
} else {
    Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'Inno Setup 6 (ISCC.exe) precisa estar instalado para gerar o instalador gráfico.'
}

$publishArguments = @(
    'publish'
    (Join-Path $repoRoot 'src\PresenceTracker\PresenceTracker.csproj')
    '-c'
    'Release'
    '-r'
    'win-x64'
    '--self-contained'
    'true'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-o'
    $publishPath
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw 'A publicação .NET falhou.'
}

New-Item -ItemType Directory -Force $outputPath | Out-Null
& $compilerPath (Join-Path $PSScriptRoot 'PresenceTracker.iss')
if ($LASTEXITCODE -ne 0) {
    throw "O Inno Setup não conseguiu compilar o instalador (código $LASTEXITCODE)."
}
