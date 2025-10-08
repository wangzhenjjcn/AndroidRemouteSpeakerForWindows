Param(
  [switch]$WindowsOnly,
  [switch]$AndroidOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "[build] 开始构建" -ForegroundColor Cyan

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

function Build-Windows {
  Write-Host "[build] 构建 Windows" -ForegroundColor Cyan
  $sln = Join-Path $root 'windows' 'AudioBridge.Windows.sln'
  if (-not (Test-Path $sln)) {
    Write-Host "[build] 未发现解决方案，将在后续步骤创建。" -ForegroundColor Yellow
    return
  }
  & dotnet build $sln -c Release
  & dotnet publish $sln -c Release -p:PublishSingleFile=true -p:SelfContained=false -o (Join-Path $dist 'Windows')
}

function Build-Android {
  Write-Host "[build] 构建 Android" -ForegroundColor Cyan
  $androidDir = Join-Path $root 'android'
  if (-not (Test-Path (Join-Path $androidDir 'gradlew'))) {
    Write-Host "[build] 未发现 Android Gradle Wrapper，跳过。" -ForegroundColor Yellow
    return
  }
  # Ensure JAVA_HOME for gradle
  if (-not $env:JAVA_HOME) {
    $jdkRoots = @('C:\\Program Files\\Microsoft','C:\\Program Files\\Java')
    $found = $false
    foreach ($r in $jdkRoots) {
      if (Test-Path $r) {
        $cands = Get-ChildItem -Directory -ErrorAction SilentlyContinue $r | Where-Object { $_.Name -like 'jdk-17*' }
        if ($cands) {
          $env:JAVA_HOME = $cands[0].FullName
          $env:Path = (Join-Path $env:JAVA_HOME 'bin') + ";" + $env:Path
          Write-Host "[build] 使用 JAVA_HOME=$env:JAVA_HOME" -ForegroundColor Green
          $found = $true
          break
        }
      }
    }
    if (-not $found) {
      Write-Host "[build] 未找到 JDK 17，无法构建 Android" -ForegroundColor Red
      return
    }
  }
  Push-Location $androidDir
  & .\gradlew assembleDebug --no-daemon
  Pop-Location
  $apk = Get-ChildItem -Recurse -Path (Join-Path $androidDir 'app\build\outputs\apk\debug') -Filter 'app-debug.apk' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($apk) {
    Copy-Item $apk.FullName (Join-Path $dist 'Android\app-debug.apk') -Force
    Write-Host "[build] APK 输出: $(Join-Path $dist 'Android\app-debug.apk')" -ForegroundColor Green
  }
}

if (-not $AndroidOnly) { Build-Windows }
if (-not $WindowsOnly) { Build-Android }

Write-Host "[build] 完成" -ForegroundColor Green


