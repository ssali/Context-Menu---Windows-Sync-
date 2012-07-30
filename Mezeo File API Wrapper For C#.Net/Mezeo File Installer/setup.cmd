@echo off
@echo Installing...
md "%Temp%\MezeoFile.2.0.66"
xcopy . "%Temp%\MezeoFile.2.0.66" /scrye
"%Temp%\MezeoFile.2.0.66\setup.exe"

rem test whether setup.exe returned success (0). Any failures, including reboot required (1641 or 3010) will cause the auto-start to be skipped.
if errorlevel 1 goto :eof
"%Temp%\MezeoFile.2.0.66\MezeoPostInstallLauncher.exe" "MezeoFile" "MezeoFile.exe"