param(
    [string]$EngineRoot = "C:/Users/Cameron.Airlie/dev/PlutoGE",
    [string]$EngineBuild = "out/build/msvc-nvidia",
    [string]$Configuration = "RelWithDebInfo"
)
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildDirectory = Join-Path $EngineRoot $EngineBuild
$outputDirectory = Join-Path $projectRoot "Build/Server"
$nativeDirectory = Join-Path $buildDirectory "runtime/$Configuration"
function CheckExit { if ($LASTEXITCODE -ne 0) { throw "Build command failed with exit code $LASTEXITCODE" } }
cmake --build $buildDirectory --config $Configuration --target PlutoGEServer PlutoGERuntime -j 8
CheckExit
dotnet build (Join-Path $EngineRoot "engine/scripting/managed/PlutoGE.ScriptCore/PlutoGE.ScriptCore.csproj") -c Release
CheckExit
dotnet build (Join-Path $projectRoot "CoD.Scripts.csproj") -c Release
CheckExit
dotnet publish (Join-Path $projectRoot "Server/CoD.Server.csproj") -c Release -o $outputDirectory
CheckExit
# Export an ordinary game pack; both native hosts use the same content reader.
# The graphical executable also remains available for deployment diagnostics.
$manifestPath = Join-Path $projectRoot "CoDplutoproject.plutoproject"
$exportArguments = @("--export", $manifestPath, (Join-Path $outputDirectory "CoDGame.exe"), "--prune")
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line.StartsWith("BUILD_ALWAYS_INCLUDE`t")) {
        $exportArguments += @("--include", $line.Split("`t")[1])
    }
}
& (Join-Path $nativeDirectory "PlutoGERuntime.exe") @exportArguments
CheckExit
Copy-Item -LiteralPath (Join-Path $nativeDirectory "PlutoGEServer.exe") -Destination $outputDirectory -Force
Get-ChildItem -LiteralPath $nativeDirectory -Filter "*.dll" -File | Copy-Item -Destination $outputDirectory -Force
Write-Host "Server package: $outputDirectory"
Write-Host "Run: .\CoDServer.exe --map Main --mode Hardpoint --port 7777 --bots 8 --max-players 12"
