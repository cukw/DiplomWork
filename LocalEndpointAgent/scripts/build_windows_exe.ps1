Param(
    [string]$ProjectRoot = "",
    [string]$PythonCommand = "python"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$distDir = Join-Path $ProjectRoot "dist\windows"
$buildDir = Join-Path $ProjectRoot "build\windows"
$repoRoot = (Resolve-Path (Join-Path $ProjectRoot "..")).Path

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

try {
    & $PythonCommand --version | Out-Null
}
catch {
    $PythonCommand = "py"
    & $PythonCommand --version | Out-Null
}

Push-Location $ProjectRoot
try {
    & $PythonCommand -m pip install --upgrade pip
    & $PythonCommand -m pip install grpcio protobuf psutil pyyaml pyinstaller grpcio-tools
    & $PythonCommand -m pip install -e .

    $activityProtoDir = Join-Path $repoRoot "Backend\services\ActivityService\Protos"
    $agentProtoDir = Join-Path $repoRoot "Backend\services\AgentManagementService\Protos"
    $outDir = Join-Path $ProjectRoot "src\endpoint_agent\generated"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    & $PythonCommand -m grpc_tools.protoc `
      -I "$activityProtoDir" `
      --python_out "$outDir" `
      --grpc_python_out "$outDir" `
      "$activityProtoDir\Activity.proto"

    & $PythonCommand -m grpc_tools.protoc `
      -I "$agentProtoDir" `
      --python_out "$outDir" `
      --grpc_python_out "$outDir" `
      "$agentProtoDir\agent.proto"

    Get-ChildItem $outDir -Filter "*_pb2_grpc.py" | ForEach-Object {
        $text = Get-Content $_.FullName -Raw
        $text = [regex]::Replace($text, '(?m)^import ([A-Za-z_][A-Za-z0-9_]*_pb2) as ', 'from . import $1 as ')
        Set-Content -Encoding UTF8 $_.FullName $text
    }

    & $PythonCommand -m PyInstaller `
      --noconfirm `
      --clean `
      --onefile `
      --name endpoint-agent-windows.exe `
      --paths src `
      --collect-submodules endpoint_agent.generated `
      --distpath $distDir `
      --workpath "$buildDir\work" `
      --specpath "$buildDir\spec" `
      "$ProjectRoot\scripts\pyinstaller_entry.py"

    $exePath = Join-Path $distDir "endpoint-agent-windows.exe"
    if (!(Test-Path $exePath)) {
        throw "Windows build failed: output file not found: $exePath"
    }

    Write-Host "Built: $exePath"
}
finally {
    Pop-Location
}
