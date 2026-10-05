$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$publishPath = Join-Path $repoRoot 'artifacts\publish\win-x64'
$outputPath = Join-Path $repoRoot 'artifacts\installer'
$compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$programFilesX86 = ${env:ProgramFiles(x86)}
$compilerCandidates = @(
    $(if ($compilerCommand) { $compilerCommand.Source }),
    $(if ($programFilesX86) { Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe' }),
    $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$compilerPath = $compilerCandidates | Select-Object -First 1
if (-not $compilerPath) {
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
