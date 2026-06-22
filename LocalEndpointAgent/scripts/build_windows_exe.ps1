Param(
    [string]$ProjectRoot = "",
    [string]$PythonCommand = "python",
    [string]$GatewayUrl = "https://2.26.89.86",
    [string]$ActivityServiceUrl = "2.26.89.86:5001",
    [string]$AgentManagementUrl = "2.26.89.86:5015",
    [string]$AgentAuthHeader = "x-agent-token",
    [string]$AgentAuthToken = $env:AGENT_AUTH_TOKEN,
    [bool]$GatewayTlsInsecure = $true
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
    if ([string]::IsNullOrWhiteSpace($AgentAuthToken)) {
        Write-Warning "AGENT_AUTH_TOKEN is empty. The EXE will be built without the shared gRPC agent token."
    }

    $embeddedConfigPath = Join-Path $ProjectRoot "src\endpoint_agent\embedded_config.py"
    $escapedGatewayUrl = $GatewayUrl.Replace("\", "\\").Replace("'", "\'")
    $escapedActivityUrl = $ActivityServiceUrl.Replace("\", "\\").Replace("'", "\'")
    $escapedAgentUrl = $AgentManagementUrl.Replace("\", "\\").Replace("'", "\'")
    $escapedAuthHeader = $AgentAuthHeader.Replace("\", "\\").Replace("'", "\'")
    $agentTokenValue = if ($null -eq $AgentAuthToken) { "" } else { $AgentAuthToken }
    $escapedAuthToken = $agentTokenValue.Replace("\", "\\").Replace("'", "\'")
    $tlsValue = if ($GatewayTlsInsecure) { "True" } else { "False" }

    @"
DEFAULT_GATEWAY_URL = '$escapedGatewayUrl'
DEFAULT_GATEWAY_TLS_INSECURE = $tlsValue
DEFAULT_ACTIVITY_SERVICE_URL = '$escapedActivityUrl'
DEFAULT_AGENT_MANAGEMENT_URL = '$escapedAgentUrl'
DEFAULT_AGENT_AUTH_HEADER = '$escapedAuthHeader'
DEFAULT_AGENT_AUTH_TOKEN = '$escapedAuthToken'
"@ | Set-Content -Encoding UTF8 $embeddedConfigPath

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
      --collect-submodules grpc `
      --collect-binaries grpc `
      --collect-binaries psutil `
      --hidden-import select `
      --hidden-import selectors `
      --hidden-import socket `
      --hidden-import _socket `
      --hidden-import _overlapped `
      --hidden-import _multiprocessing `
      --hidden-import multiprocessing `
      --hidden-import tkinter `
      --distpath $distDir `
      --workpath "$buildDir\work" `
      --specpath "$buildDir\spec" `
      "$ProjectRoot\scripts\pyinstaller_entry.py"

    $exePath = Join-Path $distDir "endpoint-agent-windows.exe"
    if (!(Test-Path $exePath)) {
        throw "Windows build failed: output file not found: $exePath"
    }

    & $exePath --help | Out-Null

    Write-Host "Built: $exePath"
}
finally {
    $embeddedConfigPath = Join-Path $ProjectRoot "src\endpoint_agent\embedded_config.py"
    if (Test-Path $embeddedConfigPath) {
        Remove-Item -Force $embeddedConfigPath
    }
    Pop-Location
}
