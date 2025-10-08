@echo off
setlocal enabledelayedexpansion

REM Locate repo root
set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%.." >nul
set "ROOT=%CD%"
set "DIST=%ROOT%\dist"
if not exist "%DIST%" mkdir "%DIST%" >nul 2>&1

echo [build] Windows publish...
dotnet publish "%ROOT%\windows\App\App.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false -o "%DIST%\Windows"
if errorlevel 1 goto :fail

echo [build] Android assembleDebug...
set "ANDROID_DIR=%ROOT%\android"
if exist "%ANDROID_DIR%\gradlew" (
  pushd "%ANDROID_DIR%" >nul
  if "%JAVA_HOME%"=="" (
    if exist "C:\Program Files\Microsoft\jdk-17*" (
      for /d %%D in ("C:\Program Files\Microsoft\jdk-17*") do (
        set "JAVA_HOME=%%D"
        goto :setjavapath
      )
    ) else if exist "C:\Program Files\Java\jdk-17*" (
      for /d %%D in ("C:\Program Files\Java\jdk-17*") do (
        set "JAVA_HOME=%%D"
        goto :setjavapath
      )
    )
  )
:setjavapath
  if not "%JAVA_HOME%"=="" set "PATH=%JAVA_HOME%\bin;%PATH%"
  call gradlew assembleDebug --no-daemon
  if errorlevel 1 (
    popd >nul
    goto :fail
  )
  popd >nul
  if not exist "%DIST%\Android" mkdir "%DIST%\Android" >nul 2>&1
  if exist "%ANDROID_DIR%\app\build\outputs\apk\debug\app-debug.apk" copy /Y "%ANDROID_DIR%\app\build\outputs\apk\debug\app-debug.apk" "%DIST%\Android\app-debug.apk" >nul
) else (
  echo [build] Android gradlew not found, skip.
)

echo [build] Done.
popd >nul
exit /b 0

:fail
echo [build] FAILED
popd >nul
exit /b 1


