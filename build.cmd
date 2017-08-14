@echo off
cls

.paket\paket.bootstrapper.exe
if errorlevel 1 (
  exit /b %errorlevel%
)

.paket\paket.exe restore -g build-tools
if errorlevel 1 (
  exit /b %errorlevel%
)

packages\build-tools\FAKE\tools\FAKE.exe build.fsx %*
