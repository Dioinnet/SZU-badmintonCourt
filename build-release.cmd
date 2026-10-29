@echo off
setlocal
set "ROOT=%~dp0"
if "%ProgramFiles%"=="" set "ProgramFiles=C:\Program Files"
if "%ProgramData%"=="" set "ProgramData=C:\ProgramData"
set "DOTNET_CLI_HOME=%ROOT%.dotnet-home"
set "NUGET_PACKAGES=%ROOT%.nuget-packages"
"%ROOT%.dotnet\dotnet.exe" publish "%ROOT%src\SzuBadminton\SzuBadminton.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "%ROOT%artifacts\win-x64"
endlocal
