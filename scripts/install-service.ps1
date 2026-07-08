param(
    [string]$ServiceName = "WindowsForward",
    [string]$DisplayName = "Windows Forward Manager",
    [string]$PublishDir = "$PSScriptRoot\..\publish\api"
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $PublishDir "WindowForward.Api.exe"

if (-not (Test-Path $exe)) {
    throw "未找到 $exe。请先运行：dotnet publish .\src\WindowForward.Api\WindowForward.Api.csproj -c Release -o .\publish\api"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
}

New-Service -Name $ServiceName -DisplayName $DisplayName -BinaryPathName "`"$exe`"" -StartupType Automatic
Start-Service -Name $ServiceName
Write-Host "服务已安装并启动：$ServiceName"
