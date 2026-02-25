param(
  [string]$PythonExe = "python",
  [string]$AgentRoot = (Resolve-Path "$PSScriptRoot/.."),
  [string]$ConfigPath = "config/agent.local.yaml",
  [string]$TaskName = "LocalEndpointAgent"
)

$Action = New-ScheduledTaskAction -Execute $PythonExe -Argument "-m endpoint_agent.main --config `"$ConfigPath`"" -WorkingDirectory $AgentRoot
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Force
Write-Host "Autostart task '$TaskName' installed."
