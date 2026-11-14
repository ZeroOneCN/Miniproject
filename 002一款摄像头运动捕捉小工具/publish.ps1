# 打包为自包含单文件 exe（目标机器无需安装 .NET）
# 用法：pwsh ./publish.ps1            → dist/摄像头监控台.exe
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root 'src/CameraMotionCapture.App/CameraMotionCapture.App.csproj') `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out

$exe = Get-ChildItem $out -Filter *.exe | Select-Object -First 1
if (-not $exe) { throw '发布失败：dist 下没有 exe' }

$extra = (Get-ChildItem $out -File | Where-Object { $_.Name -ne $exe.Name }).Name
if ($extra) { Write-Host "附加文件：$($extra -join ', ')" }

Write-Host ("产物：{0}（{1} MB）" -f $exe.FullName, [math]::Round($exe.Length / 1MB, 1))
