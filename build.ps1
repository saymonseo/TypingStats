param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\TypingStats-win-x64'))
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet restore App/TypingStats.App.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet run --project Tests/TypingStats.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet publish App/TypingStats.App.csproj -c Release -r win-x64 --self-contained false -p:DebugSymbols=false -p:DebugType=None -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    dotnet publish Updater/TypingStats.Updater.csproj -c Release -r win-x64 --self-contained false -p:DebugSymbols=false -p:DebugType=None -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $OutputDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $OutputDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $OutputDirectory -Recurse -Force
    Write-Output "Built: $OutputDirectory"
} finally { Pop-Location }
