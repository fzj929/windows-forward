param(
    [string]$PublishDir = "$PSScriptRoot\..\publish",
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$webDir = Join-Path $root "web"
$webDist = Join-Path $webDir "dist"
$resolvedPublishDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishDir)
$wwwroot = Join-Path $resolvedPublishDir "wwwroot"

Write-Step "安装前端依赖"
Push-Location $webDir
try {
    if (-not $SkipNpmInstall) {
        npm.cmd install
    } else {
        Write-Host "已跳过 npm install。"
    }

    Write-Step "构建前端页面"
    npm.cmd run build
} finally {
    Pop-Location
}

Write-Step "复制前端页面到发布目录 wwwroot"
if (-not (Test-Path $resolvedPublishDir)) {
    New-Item -ItemType Directory -Force -Path $resolvedPublishDir | Out-Null
}

if (Test-Path $wwwroot) {
    Remove-Item $wwwroot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item (Join-Path $webDist "*") $wwwroot -Recurse -Force

Write-Host ""
Write-Host "前端页面发布完成。" -ForegroundColor Green
Write-Host "页面目录：$wwwroot"
Write-Host "提示：此脚本不会安装、配置或启动 Windows 服务。"
