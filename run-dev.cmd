@echo off
setlocal
set "ROOT=%~dp0"
if "%ProgramFiles%"=="" set "ProgramFiles=C:\Program Files"
if "%ProgramData%"=="" set "ProgramData=C:\ProgramData"
set "DOTNET_CLI_HOME=%ROOT%.dotnet-home"
set "NUGET_PACKAGES=%ROOT%.nuget-packages"
"%ROOT%.dotnet\dotnet.exe" run --project "%ROOT%src\SzuBadminton\SzuBadminton.csproj" --configuration Release
endlocal
