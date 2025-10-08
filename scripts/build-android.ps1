Param(
  [string]$JdkHome = "",
  [string]$SdkDir = ""
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "[android-build] Start" -ForegroundColor Cyan

# Resolve project root
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$androidDir = Join-Path $root 'android'
$distDir = Join-Path $root 'dist\Android'

# 1) Ensure JAVA_HOME (JDK 17)
function Use-Jdk([string]$path) {
  if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path (Join-Path $path 'bin'))) {
    $env:JAVA_HOME = $path
    $env:Path = (Join-Path $path 'bin') + ";" + $env:Path
    Write-Host "[android-build] JAVA_HOME=$env:JAVA_HOME" -ForegroundColor Green
    return $true
  }
  return $false
}

if (-not (Use-Jdk $JdkHome)) {
  $candidates = @()
  $msRoot = 'C:\\Program Files\\Microsoft'
  if (Test-Path $msRoot) {
    $candidates += (Get-ChildItem -Directory $msRoot -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'jdk-17*' } | Select-Object -ExpandProperty FullName)
  }
  $javaRoot = 'C:\\Program Files\\Java'
  if (Test-Path $javaRoot) {
    $candidates += (Get-ChildItem -Directory $javaRoot -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'jdk-17*' } | Select-Object -ExpandProperty FullName)
  }
  foreach ($c in $candidates) { if (Use-Jdk $c) { break } }
}

if (-not $env:JAVA_HOME) {
  Write-Host "[android-build] ERROR: JDK 17 not found. Please install JDK 17 and/or pass -JdkHome" -ForegroundColor Red
  exit 1
}

# 2) Ensure Android SDK
if ([string]::IsNullOrWhiteSpace($SdkDir)) {
  $SdkDir = Join-Path $androidDir '.android-sdk'
}

if (-not (Test-Path $SdkDir)) {
  Write-Host "[android-build] WARN: SDK dir not found at $SdkDir" -ForegroundColor Yellow
}

$env:ANDROID_SDK_ROOT = $SdkDir
$env:ANDROID_HOME = $SdkDir

# 3) Ensure local.properties exists with sdk.dir
$localProps = Join-Path $androidDir 'local.properties'
if (-not (Test-Path $localProps)) {
  "sdk.dir=$SdkDir" | Out-File -FilePath $localProps -Encoding ASCII -Force
}

# 4) Build APK
Push-Location $androidDir
& .\gradlew --version
& .\gradlew assembleDebug --no-daemon
Pop-Location

# 5) Copy APK to dist
$apk = Get-ChildItem -Recurse -Path (Join-Path $androidDir 'app\build\outputs\apk\debug') -Filter 'app-debug.apk' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($apk) {
  New-Item -ItemType Directory -Force -Path $distDir | Out-Null
  Copy-Item $apk.FullName (Join-Path $distDir 'app-debug.apk') -Force
  Write-Host "[android-build] APK: $(Join-Path $distDir 'app-debug.apk')" -ForegroundColor Green
} else {
  Write-Host "[android-build] ERROR: APK not found. Check Gradle output." -ForegroundColor Red
  exit 1
}

Write-Host "[android-build] Done" -ForegroundColor Cyan


