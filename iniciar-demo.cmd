@echo off
setlocal DisableDelayedExpansion

if not exist "%~dp0TestResults\lan\ambiente.env" (
    echo Falta preparar el entorno LAN. Consulta infra\lan\README.md.
    exit /b 1
)

where docker.exe >nul 2>nul
if errorlevel 1 (
    echo No se encuentra Docker. Abri Docker Desktop y volve a ejecutar este archivo.
    exit /b 1
)

set "demoLanIp="
for /f "tokens=2 delims==" %%I in ('findstr /r /x "LAN_IP=[0-9][0-9.]*" "%~dp0TestResults\lan\ambiente.env"') do set "demoLanIp=%%I"
if not defined demoLanIp (
    echo No se encontro una IP LAN valida en la configuracion del entorno.
    exit /b 1
)

docker.exe compose --env-file "%~dp0TestResults\lan\ambiente.env" -f "%~dp0infra\lan\compose.yml" up -d --wait --wait-timeout 180
set "demoExitCode=%ERRORLEVEL%"
if not "%demoExitCode%"=="0" (
    echo No se pudo iniciar la demo. Comproba que Docker Desktop este listo y revisa el error anterior.
    exit /b %demoExitCode%
)

echo.
echo PWA disponible en https://%demoLanIp%:8443/repartidor/
echo Los servicios quedan en segundo plano. Podes cerrar esta terminal.
exit /b 0
