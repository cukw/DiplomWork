Param(
    [string]$ProjectRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$distDir = Join-Path $ProjectRoot "dist\windows"
$buildDir = Join-Path $ProjectRoot "build\windows"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Push-Location $ProjectRoot
try {
    py -m pip install --upgrade pip
    py -m pip install grpcio protobuf psutil pyyaml pyinstaller grpcio-tools
    py -m pip install -e .

    $protoDir = Join-Path $ProjectRoot "protos"
    $outDir = Join-Path $ProjectRoot "src\endpoint_agent\protos"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    py -m grpc_tools.protoc `
      -I "$protoDir" `
      --python_out "$outDir" `
      --grpc_python_out "$outDir" `
      "$protoDir\activity.proto" `
      "$protoDir\agent.proto"

    $activityGrpc = Join-Path $outDir "activity_pb2_grpc.py"
    $agentGrpc = Join-Path $outDir "agent_pb2_grpc.py"

    (Get-Content $activityGrpc -Raw).Replace(
      "import activity_pb2 as ",
      "from . import activity_pb2 as "
    ) | Set-Content -Encoding UTF8 $activityGrpc

    (Get-Content $agentGrpc -Raw).Replace(
      "import agent_pb2 as ",
      "from . import agent_pb2 as "
    ) | Set-Content -Encoding UTF8 $agentGrpc

    py -m PyInstaller `
      --noconfirm `
      --clean `
      --onefile `
      --name endpoint-agent-windows.exe `
      --paths src `
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
