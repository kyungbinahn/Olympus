@echo off
rem Run Olympus.Core tests without launching Unity.
rem
rem NOTE: comments here are ASCII on purpose. cmd.exe reads .bat files in the OEM
rem codepage (CP949 on Korean Windows); UTF-8 Hangul gets mis-decoded and can
rem produce stray command separators that break even rem lines.
rem
rem Requires the .NET SDK:  winget install Microsoft.DotNet.SDK.8

cd /d "%~dp0"
dotnet test tools\DomainTests\DomainTests.csproj --nologo

echo.
pause
