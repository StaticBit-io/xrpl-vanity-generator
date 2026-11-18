@echo off
setlocal EnableDelayedExpansion
rem Builds the Windows desktop application, registers it for the current user and starts it.
rem Double-click this file, or run it from any directory. No administrator rights are needed:
rem the package is registered in developer mode instead of being installed from the .msix.
cd /d "%~dp0"

set "PROJECT=src\XrplVanity.App\XrplVanity.App.csproj"
set "TFM=net10.0-windows10.0.22621.0"
set "LAYOUT=src\XrplVanity.App\bin\publish"
set "MANIFEST=src\XrplVanity.App\bin\Release\%TFM%\win-x64\AppxManifest.xml"
set "INTERMEDIATE=src\XrplVanity.App\obj\Release\%TFM%\win-x64"
set "SUBJECT=CN=StaticBit Vanity Dev"

echo ===============================================
echo  XRPL Vanity - build, register and run
echo ===============================================
echo.

rem --- 1. the .NET SDK -------------------------------------------------------
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: dotnet was not found on PATH. Install the .NET 10 SDK from
  echo        https://dotnet.microsoft.com/download and run this file again.
  goto :fail
)

rem --- 2. developer mode -----------------------------------------------------
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense"`) do set "DEVMODE=%%i"
if not "!DEVMODE!"=="1" (
  echo ERROR: Developer Mode is off, so a package cannot be registered from a folder.
  echo        Turn it on in Settings ^> System ^> For developers, then run this file again.
  echo        Alternatively install the signed .msix from an elevated prompt; see
  echo        docs\desktop-app.md.
  goto :fail
)

rem --- 3. the signing certificate -------------------------------------------
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq '%SUBJECT%' } | Select-Object -First 1).Thumbprint"`) do set "THUMB=%%i"
if "!THUMB!"=="" (
  echo No signing certificate found. Creating one...
  powershell -NoProfile -ExecutionPolicy Bypass -File "app\make-cert.ps1"
  if errorlevel 1 goto :fail
  for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq '%SUBJECT%' } | Select-Object -First 1).Thumbprint"`) do set "THUMB=%%i"
)
if "!THUMB!"=="" (
  echo ERROR: the certificate "%SUBJECT%" could not be created or found.
  goto :fail
)
echo Certificate: !THUMB!

rem --- 4. stop a running instance -------------------------------------------
tasklist /fi "imagename eq XrplVanity.App.exe" 2>nul | find /i "XrplVanity.App.exe" >nul
if not errorlevel 1 (
  echo Closing the running application...
  taskkill /im XrplVanity.App.exe /f >nul 2>&1
  rem ping, not timeout: timeout refuses to run when stdin is redirected.
  ping -n 3 127.0.0.1 >nul
)

rem --- 5. publish ------------------------------------------------------------
rem Resizetizer can keep the images of an earlier logo while refreshing their dates, and the
rem package then shows the old icon. Its stamps go too, or it would skip the empty folder.
if exist "%INTERMEDIATE%\resizetizer" rmdir /s /q "%INTERMEDIATE%\resizetizer"
del /q "%INTERMEDIATE%\maui*.stamp" 2>nul
rem Developer mode refuses to register a package again under the version it already has once
rem the manifest differs (0x80073CFB, for instance after a Windows App SDK update), and the only
rem other way out, removing the package, deletes results.dat. So every run registers a higher
rem version: VanityVersion plus a revision of whole hours since 2026, which an update accepts and
rem which keeps the package's data. The revision field holds up to 65535, that is until 2033.
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$v = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.VanityVersion | Where-Object { $_ } | Select-Object -First 1; $r = [int][math]::Floor(([DateTimeOffset]::UtcNow - [DateTimeOffset]'2026-01-01T00:00:00Z').TotalHours); '{0}.{1}' -f $v, $r"`) do set "PKGVER=%%i"
if "!PKGVER!"=="" (
  echo ERROR: could not read VanityVersion from Directory.Build.props.
  goto :fail
)

echo.
echo Building version !PKGVER!... this takes a few minutes the first time.
dotnet publish "%PROJECT%" -c Release -f %TFM% -p:PublishProfile=MSIX-win-x64 -p:PackageCertificateThumbprint=!THUMB! -p:VanityPackageVersion=!PKGVER! --nologo -v quiet
if errorlevel 1 (
  echo.
  echo ERROR: the build failed. Scroll up for the first error line.
  goto :fail
)

if not exist "%MANIFEST%" (
  echo ERROR: %MANIFEST% was not produced by the build.
  goto :fail
)
if not exist "%LAYOUT%\wwwroot\index.html" (
  echo ERROR: %LAYOUT% has no wwwroot; the publish layout is incomplete.
  goto :fail
)

rem --- 6. register -----------------------------------------------------------
copy /y "%MANIFEST%" "%LAYOUT%\" >nul
if errorlevel 1 (
  echo ERROR: could not copy the manifest into %LAYOUT%.
  goto :fail
)

echo Registering the package for the current user...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Add-AppxPackage -Register '%CD%\%LAYOUT%\AppxManifest.xml' -ErrorAction Stop; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
if errorlevel 1 (
  echo.
  echo ERROR: registration failed. If a previous registration points at a folder that
  echo        no longer exists, remove it and run this file again:
  echo.
  echo            powershell -Command "Remove-AppxPackage (Get-AppxPackage io.staticbit.vanity).PackageFullName"
  echo.
  echo        That also deletes the package's data, including results.dat with its
  echo        seeds - export anything you want to keep from the History page first.
  goto :fail
)

rem --- 7. run ----------------------------------------------------------------
rem PowerShell composes the launch target and writes the separator as [char]33: a literal
rem "!" here would be swallowed by this file's delayed expansion.
echo Starting XRPL Vanity...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $f = (Get-AppxPackage -Name 'io.staticbit.vanity').PackageFamilyName; if (-not $f) { Write-Host 'the package is not registered'; exit 1 }; Start-Process ('shell:appsFolder\' + $f + [char]33 + 'App') -ErrorAction Stop; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
if errorlevel 1 (
  echo ERROR: the application could not be started.
  goto :fail
)

echo.
echo Done. The application is also in the Start menu as "XRPL Vanity".
echo.
pause
exit /b 0

:fail
echo.
pause
exit /b 1
