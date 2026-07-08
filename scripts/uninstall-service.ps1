param(
    [string]$ServiceName = "WindowsForward"
)

$ErrorActionPreference = "Stop"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "服务已删除：$ServiceName"
} else {
    Write-Host "服务不存在：$ServiceName"
}
