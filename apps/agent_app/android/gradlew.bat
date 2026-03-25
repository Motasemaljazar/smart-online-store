@ECHO OFF
setlocal EnableDelayedExpansion

REM Custom Gradle launcher used because gradle-wrapper.jar is not shipped in this repository.
REM It bootstraps Gradle from the distributionUrl in gradle/wrapper/gradle-wrapper.properties
REM and then runs: gradle %*

set "DIR=%~dp0"
set "PROP_FILE=%DIR%gradle\wrapper\gradle-wrapper.properties"

if not exist "%PROP_FILE%" (
  echo [gradlew] Missing %PROP_FILE%
  exit /b 1
)

for /f "usebackq tokens=1,* delims==" %%A in (`findstr /b /c:"distributionUrl=" "%PROP_FILE%"`) do (
  set "RAW_URL=%%B"
)

if not defined RAW_URL (
  echo [gradlew] Could not read distributionUrl from %PROP_FILE%
  exit /b 1
)

set "URL=!RAW_URL:\= !"
set "URL=!URL: =!"
set "URL=!URL:https\://=https://!"

for %%F in (!URL!) do set "ZIP_NAME=%%~nxF"
if not defined ZIP_NAME (
  echo [gradlew] Could not parse ZIP name from URL: !URL!
  exit /b 1
)

set "CACHE_ROOT=%USERPROFILE%\.gradle\wrapper\custom-dists"
set "ZIP_PATH=%CACHE_ROOT%\!ZIP_NAME!"

set "BASE_NAME=!ZIP_NAME:-all.zip=!"
set "BASE_NAME=!BASE_NAME:-bin.zip=!"
set "GRADLE_HOME=%CACHE_ROOT%\!BASE_NAME!"

if not exist "%CACHE_ROOT%" mkdir "%CACHE_ROOT%" >NUL 2>&1

if not exist "%ZIP_PATH%" (
  echo [gradlew] Downloading !URL!
  powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-WebRequest -Uri '!URL!' -OutFile '!ZIP_PATH!' -UseBasicParsing } catch { Write-Host $_.Exception.Message; exit 1 }"
  if errorlevel 1 exit /b 1
)

if not exist "%GRADLE_HOME%\bin\gradle.bat" (
  echo [gradlew] Extracting !ZIP_NAME! ...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Expand-Archive -Force '!ZIP_PATH!' '%CACHE_ROOT%' } catch { Write-Host $_.Exception.Message; exit 1 }"
  if errorlevel 1 exit /b 1
)

if not exist "%GRADLE_HOME%\bin\gradle.bat" (
  echo [gradlew] Gradle not found after extraction at: %GRADLE_HOME%
  exit /b 1
)

call "%GRADLE_HOME%\bin\gradle.bat" %*
endlocal
