@echo off
set rootPath=.
set buildMode=%~1
if "%~1"=="" set buildMode=Rebuild

@echo.
@echo *** %buildMode% 32-bit MezeoFileSupport
msbuild "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileSupport\MezeoFileSupport.sln" /t:%buildMode% "/p:Configuration=Release;Platform=Any CPU" /v:m /nologo
if errorlevel 1 goto :eof
if /i "%buildmode%"=="clean" (
rd /s /q "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileSupport\bin\Release"
)

@echo.
@echo *** %buildMode% MezeoPostInstallLauncher
msbuild "%rootPath%\MezeoPostInstallLauncher\MezeoPostInstallLauncher.sln" /t:%buildMode% "/p:Configuration=Release;Platform=Any CPU" /v:m /nologo
if errorlevel 1 goto :eof
if /i "%buildmode%"=="clean" (
rd /s /q "%rootPath%\MezeoPostInstallLauncher\MezeoPostInstallLauncher\bin\Release"
)

@echo.
@echo *** %buildMode% 32-bit MezeoFileRmml
@echo Convert "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileRmvFile\MezeoFileRmml.sln" from VS2008 to VS2010
msbuild "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileRmvFile\MezeoFileRmml.sln" /t:%buildMode% /p:Configuration=Release;Platform=Win32 /v:m /nologo
if errorlevel 1 goto :eof
if /i "%buildmode%"=="clean" (
rd /s /q "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileRmvFile\Release"
)

@echo.
@echo *** %buildMode% 32-bit Mezeo
if /i not "%buildmode%"=="clean" (
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileSupport\bin\Release\SupportFile.dll" "%rootPath%\Mezeo\Mezeo\bin\Release\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\System.Data.SQLite.dll" "%rootPath%\Mezeo\Mezeo\bin\Release\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\AppLimit.NetSparkle.Net40.dll" "%rootPath%\Mezeo\Mezeo\bin\Release\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\app.ico" "%rootPath%\Mezeo\Mezeo\bin\Release\*.*" /cry
if errorlevel 1 goto :eof
)
msbuild "%rootPath%\Mezeo\Mezeo.sln" /t:%buildMode% /p:Configuration=Release;Platform=x86 /v:m /nologo
if errorlevel 1 goto :eof
if /i "%buildmode%"=="clean" (
rd /s /q "%rootPath%\Mezeo\Mezeo\bin\Release"
)

@echo.
@echo *** %buildMode% 32-bit Install
if /i not "%buildmode%"=="clean" (
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileSupport\bin\Release\SupportFile.dll" "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileRmvFile\Release\MezeoFileRmml.exe" "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo\Mezeo\bin\Release\MezeoFile.exe" "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\MezeoPostInstallLauncher\MezeoPostInstallLauncher\bin\Release\MezeoPostInstallLauncher.exe" "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\*.*" /cry
if errorlevel 1 goto :eof
)
@echo Manually change the ProductVersion and ProductCode values in the project. TODO: find a way to automatically set this.
devenv.exe /%buildMode% Release "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\MezeoFile.sln"
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\Setup.exe" "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\Release\*.*" /cry
if errorlevel 1 goto :eof
if /i "%buildmode%"=="clean" (
rd /s /q "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\Release"
)
if /i not "%buildmode%"=="clean" (
pushd "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer"
%windir%\SysWOW64\IExpress.exe /N /Q MezeoFile_32.sed
if errorlevel 1 goto :eof
popd
)

@echo.
@echo *** %buildMode% 64-bit Mezeo
if /i not "%buildmode%"=="clean" (
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\SupportFile.dll" "%rootPath%\Mezeo\Mezeo\bin\x64\Release\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\System.Data.SQLite.dll" "%rootPath%\Mezeo\Mezeo\bin\x64\Release\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\AppLimit.NetSparkle.Net40.dll" "%rootPath%\Mezeo\Mezeo\bin\x64\Release\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\Mezeo File Installer\app.ico" "%rootPath%\Mezeo\Mezeo\bin\x64\Release\*.*" /cry
if errorlevel 1 goto :eof
)
msbuild "%rootPath%\Mezeo\Mezeo.sln" /t:%buildMode% /p:Configuration=Release;Platform=x64 /v:m /nologo
if errorlevel 1 goto :eof
if /i "%buildmode%"=="clean" (
rd /s /q "%rootPath%\Mezeo\Mezeo\bin\x64\Release"
)

@echo.
@echo *** %buildMode% 64-bit Install
if /i not "%buildmode%"=="clean" (
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileSupport\bin\Release\SupportFile.dll" "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For C#.Net\MezeoFileRmvFile\Release\MezeoFileRmml.exe" "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo\Mezeo\bin\x64\Release\MezeoFile.exe" "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\*.*" /cry
if errorlevel 1 goto :eof
xcopy "%rootPath%\MezeoPostInstallLauncher\MezeoPostInstallLauncher\bin\Release\MezeoPostInstallLauncher.exe" "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\*.*" /cry
if errorlevel 1 goto :eof
)
@echo Manually change the ProductVersion and ProductCode values in the project. TODO: find a way to automatically set this.
devenv.exe /%buildMode% Release "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\MezeoFile.sln"
if errorlevel 1 goto :eof
xcopy "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\Setup.exe" "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\Release\*.*" /cry
if errorlevel 1 goto :eof
if /i "%buildmode%"=="clean" (
rd /s /q "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64\Release"
)
if /i not "%buildmode%"=="clean" (
pushd "%rootPath%\Mezeo File API Wrapper For x64\Mezeo File Sync _ 64"
%windir%\System32\IExpress.exe /N /Q MezeoFile_64.sed
if errorlevel 1 goto :eof
popd
)
