param(
    [string]$ServiceName = "WindowsForward",
    [string]$DisplayName = "Windows Forward Manager",
    [string]$Configuration = "Release",
    [string]$PublishDir = "$PSScriptRoot\..\publish\api",
    [int]$Port = 5000,
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "请使用管理员身份运行 PowerShell，否则无法安装/重启 Windows 服务，也无法启用部分转发命令。"
    }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$apiProject = Join-Path $root "src\WindowForward.Api\WindowForward.Api.csproj"
$webDir = Join-Path $root "web"
$webDist = Join-Path $webDir "dist"
$resolvedPublishDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishDir)
$wwwroot = Join-Path $resolvedPublishDir "wwwroot"
$exe = Join-Path $resolvedPublishDir "WindowForward.Api.exe"

Assert-Admin

Write-Step "检查构建工具"
dotnet --version | Out-Host
node --version | Out-Host
npm.cmd --version | Out-Host

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

Write-Step "发布后台服务"
dotnet publish $apiProject -c $Configuration -o $resolvedPublishDir

Write-Step "复制前端页面到后台 wwwroot"
if (Test-Path $wwwroot) {
    Remove-Item $wwwroot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item (Join-Path $webDist "*") $wwwroot -Recurse -Force

Write-Step "配置 Windows 服务"
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus("Stopped", "00:00:30")
    }
    sc.exe config $ServiceName binPath= "`"$exe`" --urls http://0.0.0.0:$Port" DisplayName= "`"$DisplayName`"" | Out-Host
} else {
    New-Service `
        -Name $ServiceName `
        -DisplayName $DisplayName `
        -BinaryPathName "`"$exe`" --urls http://0.0.0.0:$Port" `
        -StartupType Automatic
}

Write-Step "启动 Windows 服务"
Start-Service -Name $ServiceName

Write-Host ""
Write-Host "部署完成。" -ForegroundColor Green
Write-Host "服务名称：$ServiceName"
Write-Host "访问地址：http://localhost:$Port"
Write-Host "发布目录：$resolvedPublishDir"
