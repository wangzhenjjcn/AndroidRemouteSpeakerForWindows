Param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "[setup] Initialize environment" -ForegroundColor Cyan

# Check .NET 8 SDK
function Ensure-Dotnet {
  $required = '8.0'
  try {
    $ver = (& dotnet --version) 2>$null
  } catch {
    $ver = ''
  }
  if (-not $ver -or -not ($ver.StartsWith($required))) {
    Write-Host "[setup] .NET $required not found. Please install .NET SDK 8.x: https://dotnet.microsoft.com/" -ForegroundColor Yellow
    throw ".NET 8 SDK missing"
  }
  Write-Host "[setup] .NET SDK $ver" -ForegroundColor Green
}

# Ensure JDK 17
function Ensure-Java {
  try { $javaVer = (& java -version) 2>&1 | Select-String 'version' | ForEach-Object { $_.ToString() } } catch { $javaVer = '' }
  if ($javaVer -notmatch '"17\.') {
    Write-Host "[setup] JDK 17 not found. Install Microsoft OpenJDK 17: https://learn.microsoft.com/java/openjdk/download" -ForegroundColor Yellow
    throw "JDK 17 missing"
  }
  Write-Host "[setup] Java OK: $javaVer" -ForegroundColor Green
}

# Try set JAVA_HOME if missing
function Ensure-JavaHome {
  if (-not $env:JAVA_HOME) {
    $candidates = @(
      'C:\\Program Files\\Microsoft\\jdk-17',
      'C:\\Program Files\\Microsoft\\jdk-17.0.16',
      'C:\\Program Files\\Microsoft\\JDK\\17'
    )
    foreach ($c in $candidates) {
      if (Test-Path $c) { $env:JAVA_HOME = $c; break }
      $dirs = Get-ChildItem -Directory -ErrorAction SilentlyContinue (Split-Path $c) | Where-Object { $_.Name -like 'jdk-17*' }
      if ($dirs -and -not $env:JAVA_HOME) { $env:JAVA_HOME = $dirs[0].FullName }
    }
    if ($env:JAVA_HOME) {
      $javaBin = Join-Path $env:JAVA_HOME 'bin'
      $env:PATH = "$javaBin;$env:PATH"
      Write-Host "[setup] JAVA_HOME=$env:JAVA_HOME" -ForegroundColor Green
    } else {
      Write-Host "[setup] WARN: JAVA_HOME not set; sdkmanager may fail" -ForegroundColor Yellow
    }
  }
}

# Ensure Android SDK environment or install local one
function Ensure-AndroidSDK {
  if (-not $env:ANDROID_HOME -and -not $env:ANDROID_SDK_ROOT) {
    Write-Host "[setup] ANDROID_HOME/ANDROID_SDK_ROOT not set" -ForegroundColor Yellow
    throw "Android SDK missing"
  }
  Write-Host "[setup] Android SDK OK" -ForegroundColor Green
}

# Generate Gradle Wrapper
function Ensure-GradleWrapper {
  $androidDir = Join-Path (Join-Path $PSScriptRoot '..') 'android'
  if (Test-Path $androidDir) {
    Push-Location $androidDir
    if (-not (Test-Path 'gradlew')) {
      Write-Host "[setup] Generate Gradle Wrapper" -ForegroundColor Cyan
      $distUrl = 'https://mirrors.cloud.tencent.com/gradle/gradle-8.9-bin.zip'
      $gradleExe = $null
      if (Get-Command gradle -ErrorAction SilentlyContinue) {
        $gradleExe = 'gradle'
      } else {
        $gradleHome = Download-Gradle '8.9'
        if (-not (Validate-GradleHome $gradleHome)) {
          Write-Host "[setup] Detected corrupt gradle home, redownload" -ForegroundColor Yellow
          Remove-Item -Recurse -Force $gradleHome
          $gradleHome = Download-Gradle '8.9'
        }
        $gradleExe = (Join-Path $gradleHome 'bin\\gradle.bat')
      }
      & $gradleExe wrapper --gradle-version 8.9 --no-daemon --gradle-distribution-url $distUrl
    }
    Pop-Location
  }
}

# Install Gradle via winget if not present
function Ensure-Gradle {
  try { $gradleVer = (& gradle -v) 2>&1 | Select-String 'Gradle ' } catch { $gradleVer = $null }
  if (-not $gradleVer) {
    Write-Host "[setup] Gradle not found. Try installing via winget" -ForegroundColor Cyan
    try { winget install --id Gradle.Gradle -e --source winget --accept-source-agreements --accept-package-agreements } catch {}
  }
}

# Download Gradle distribution if winget not available
function Download-Gradle([string]$version) {
  $tools = Join-Path (Join-Path $PSScriptRoot '..') 'tools'
  $dest = Join-Path $tools ("gradle-" + $version)
  if (Test-Path (Join-Path $dest 'bin')) { return $dest }
  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  $zipUrl = "https://mirrors.cloud.tencent.com/gradle/gradle-$version-bin.zip"
  $zipPath = Join-Path $tools ("gradle-" + $version + "-bin.zip")
  Write-Host "[setup] Download Gradle $version" -ForegroundColor Cyan
  $maxRetry = 5
  for ($i=1; $i -le $maxRetry; $i++) {
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    # Get expected size from HEAD
    $expected = 0
    try {
      $head = Invoke-WebRequest -Uri $zipUrl -Method Head -UseBasicParsing
      if ($head.Headers.'Content-Length') { [int64]::TryParse($head.Headers.'Content-Length', [ref]$expected) | Out-Null }
    } catch {}
    # Download with curl resume and retries
    & curl.exe -L -C - --retry 10 --retry-all-errors --connect-timeout 30 --max-time 1800 -o $zipPath $zipUrl
    # Validate header and size
    if ((Test-Path $zipPath)) {
      $len = (Get-Item $zipPath).Length
      $fs = [System.IO.File]::OpenRead($zipPath)
      try { $b0 = $fs.ReadByte(); $b1 = $fs.ReadByte(); }
      finally { $fs.Close() }
      $headerOk = ($b0 -eq 0x50 -and $b1 -eq 0x4B)
      $sizeOk = $false
      if ($expected -gt 0) {
        $sizeOk = ($len -eq $expected)
      } else {
        $sizeOk = ($len -gt 120000000)
      }
      if ($headerOk -and $sizeOk) { break }
    }
    if ($i -eq $maxRetry) { throw "Failed to download Gradle distribution" }
    Start-Sleep -Seconds 3
  }
  $extractRoot = Join-Path $tools "gradle-extract-$version"
  if (Test-Path $extractRoot) { Remove-Item -Recurse -Force $extractRoot }
  try {
    Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force
  } catch {
    Write-Host "[setup] Expand-Archive failed, fallback to tar" -ForegroundColor Yellow
    if (-not (Test-Path $extractRoot)) { New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null }
    & tar -xf $zipPath -C $extractRoot
  }
  $unzipped = Join-Path $extractRoot ("gradle-" + $version)
  Copy-Item -Recurse -Force $unzipped\* $dest
  Remove-Item -Recurse -Force $extractRoot
  Remove-Item -Force $zipPath
  return $dest
}

function Validate-GradleHome([string]$gradHome) {
  $bat = Join-Path $gradHome 'bin\\gradle.bat'
  $jar = Join-Path $gradHome 'lib\\gradle-launcher-8.9.jar'
  $agent = Join-Path $gradHome 'lib\\agents\\gradle-instrumentation-agent-8.9.jar'
  return (Test-Path $bat) -and (Test-Path $jar) -and (Test-Path $agent)
}

# Install local Android SDK commandline-tools if env not set
function Ensure-LocalAndroidSdk {
  if ($env:ANDROID_HOME -or $env:ANDROID_SDK_ROOT) { return }
  $root = Resolve-Path (Join-Path $PSScriptRoot '..')
  $sdkDir = Join-Path $root 'android' | Join-Path -ChildPath '.android-sdk'
  $toolsDir = Join-Path $sdkDir 'cmdline-tools' | Join-Path -ChildPath 'latest'
  if (-not (Test-Path $toolsDir)) {
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $zipUrl = 'https://dl.google.com/android/repository/commandlinetools-win-9477386_latest.zip'
    $zipPath = Join-Path $sdkDir 'cmdline-tools.zip'
    Write-Host "[setup] Download Android cmdline-tools" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
    $tmpExtract = Join-Path $sdkDir 'cmdline-tools-temp'
    if (Test-Path $tmpExtract) { Remove-Item -Recurse -Force $tmpExtract }
    Expand-Archive -Path $zipPath -DestinationPath $tmpExtract -Force
    Copy-Item -Recurse -Force (Join-Path $tmpExtract 'cmdline-tools\*') $toolsDir
    Remove-Item -Force $zipPath
    Remove-Item -Recurse -Force $tmpExtract
  }
  $env:ANDROID_SDK_ROOT = $sdkDir
  $env:PATH = (Join-Path $toolsDir 'bin') + ";" + (Join-Path $sdkDir 'platform-tools') + ";" + $env:PATH
  Write-Host "[setup] Local ANDROID_SDK_ROOT: $sdkDir" -ForegroundColor Green
  Write-Host "[setup] Install required SDK packages" -ForegroundColor Cyan
  $sdkBin = Join-Path $toolsDir 'bin'
  $sdkman = Join-Path $sdkBin 'sdkmanager.bat'
  $packages = @('platform-tools','platforms;android-34','build-tools;34.0.0')
  if (Test-Path $sdkman) {
    $yesFile = Join-Path $sdkDir 'yes.txt'
    Set-Content -Path $yesFile -Value ("y`r`n" * 200)
    foreach ($p in $packages) {
      $cmd = "type `"$yesFile`" | `"$sdkman`" --sdk_root=`"$sdkDir`" `"$p`""
      cmd /c $cmd
    }
    $cmdLic = "type `"$yesFile`" | `"$sdkman`" --sdk_root=`"$sdkDir`" --licenses"
    cmd /c $cmdLic
  } else {
    Write-Host "[setup] sdkmanager.bat not found at $sdkman" -ForegroundColor Yellow
  }

  # Export ANDROID_HOME for compatibility
  $env:ANDROID_HOME = $sdkDir
}

try {
  Ensure-Dotnet
} catch {
  Write-Host "[setup] Try installing .NET 8 SDK via winget" -ForegroundColor Cyan
  try { winget install --id Microsoft.DotNet.SDK.8 -e --source winget --accept-source-agreements --accept-package-agreements } catch {}
}

try {
  Ensure-Java
} catch {
  Write-Host "[setup] Try installing Microsoft OpenJDK 17 via winget" -ForegroundColor Cyan
  try { winget install --id Microsoft.OpenJDK.17 -e --source winget --accept-source-agreements --accept-package-agreements } catch {}
}
Ensure-JavaHome

try {
  Ensure-AndroidSDK
} catch {
  Write-Host "[setup] Setup local Android SDK (cmdline-tools)" -ForegroundColor Cyan
  Ensure-LocalAndroidSdk
}

Ensure-Gradle
Ensure-GradleWrapper

Write-Host "[setup] Done" -ForegroundColor Green


