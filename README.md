# Windows Forward

Windows 转发规则管理服务，后端使用 C# / ASP.NET Core / EF Core SQLite，前端使用 Vue 3 + Element Plus。

## 功能

- 端口转发：`netsh interface portproxy`
- 防火墙入站放行：`New-NetFirewallRule`
- NAT 静态映射：`New-NetNat` / `Add-NetNatStaticMapping`
- 静态路由：`route add/delete`
- IP 转发开关：`IPEnableRouter`
- SSH 本地、远程、动态 SOCKS 转发
- 规则保存到 SQLite，支持启用、禁用、编辑、删除
- 前后端均有规则校验和友好错误提示

## 开发运行

后端：

```powershell
dotnet run --project .\src\WindowForward.Api\WindowForward.Api.csproj
```

前端：

```powershell
cd .\web
npm.cmd install
npm.cmd run dev
```

访问 `http://localhost:5173`。Vite 已代理 `/api` 到 `http://localhost:5000`。

## 构建验证

```powershell
dotnet build .\Windows-Forward.sln
cd .\web
npm.cmd run build
```

## 一键部署运行

脚本会自动安装前端依赖、构建 Vue 页面、发布后台、把前端 `dist` 复制到后台 `wwwroot`，并安装或重启 Windows 服务。

请使用管理员 PowerShell 执行：

```powershell
.\scripts\deploy.ps1
```

默认访问地址：

```text
http://localhost:5000
```

常用参数：

```powershell
.\scripts\deploy.ps1 -Port 8088
.\scripts\deploy.ps1 -SkipNpmInstall
.\scripts\deploy.ps1 -PublishDir D:\apps\WindowsForward
```

## 分步发布

只发布后台程序到 `.\publish`，不配置、不启动 Windows 服务：

```powershell
.\scripts\01-publish-api.ps1
```

只发布前端页面到 `.\publish\wwwroot`：

```powershell
.\scripts\02-publish-web.ps1
```

按顺序执行这两个脚本即可更新发布目录里的后台和页面文件。

## 安装为 Windows 服务

转发命令通常需要管理员权限。请在管理员 PowerShell 中执行：

```powershell
dotnet publish .\src\WindowForward.Api\WindowForward.Api.csproj -c Release -o .\publish\api
.\scripts\install-service.ps1
```

卸载服务：

```powershell
.\scripts\uninstall-service.ps1
```

SQLite 数据库默认位于后台程序所在目录的 `App_Data/window-forward.db`。
