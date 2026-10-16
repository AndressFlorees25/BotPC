# Instala el bot: lo publica en %LOCALAPPDATA%\Programs\BotPC y crea una tarea programada que lo
# arranca al iniciar sesión, con permisos de administrador (para temperaturas y cerrar programas).
# Ejecutar como administrador. El token va en %LOCALAPPDATA%\BotPC\config.json (no en el código).
param([string]$Usuario = "$env:USERDOMAIN\$env:USERNAME", [string]$Destino = "$env:LOCALAPPDATA\Programs\BotPC")
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$tarea = 'BotPC\Bot de Telegram'
schtasks /End /TN $tarea 2>$null | Out-Null
Get-Process BotPC -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

dotnet publish BotPC.csproj -c Release -o $Destino -nologo -v q
$exe = Join-Path $Destino 'BotPC.exe'

$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Bot de Telegram para controlar y vigilar el PC.</Description></RegistrationInfo>
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>$Usuario</UserId></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$Usuario</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <RestartOnFailure><Interval>PT1M</Interval><Count>5</Count></RestartOnFailure>
    <Priority>5</Priority>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec><Command>$exe</Command></Exec>
  </Actions>
</Task>
"@
$archivo = Join-Path $env:TEMP 'botpc-tarea.xml'
[IO.File]::WriteAllText($archivo, $xml, [Text.Encoding]::Unicode)
schtasks /Create /TN $tarea /XML $archivo /F | Out-Null
Remove-Item $archivo
schtasks /Run /TN $tarea | Out-Null
"Bot instalado en $exe y en marcha."
