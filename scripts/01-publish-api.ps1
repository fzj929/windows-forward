param(
    [string]$Configuration = "Release",
    [string]$PublishDir = "$PSScriptRoot\..\publish"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$apiProject = Join-Path $root "src\WindowForward.Api\WindowForward.Api.csproj"
$resolvedPublishDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishDir)

Write-Step "发布后台程序"
dotnet publish $apiProject -c $Configuration -o $resolvedPublishDir

Write-Host ""
Write-Host "后台发布完成。" -ForegroundColor Green
Write-Host "发布目录：$resolvedPublishDir"
Write-Host "提示：此脚本不会安装、配置或启动 Windows 服务。"
