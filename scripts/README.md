# 脚本入口

日常开发只需要记住三个入口：

| 目的 | 命令 |
|---|---|
| 完整回归 | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1` |
| 源码/构建体检 | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-ReleaseReadiness.ps1 -Version <版本> -Phase Source` |
| 候选构建 | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-ReleaseCandidate.ps1 -Version <版本> -AutoCADDir "D:\cad\AutoCAD 2020"` |

## 脚本分组

- `build*.ps1`：编译单个模块或完整六模块 Bundle；
- `test*.ps1`：单模块测试、图纸探针和完整回归；
- `verify*.ps1`：AutoCAD 2020/CoreConsole/真实图纸及在线更新渠道验证；
- `package*.ps1`：独立模块或安装器打包；
- `Build-ReleaseCandidate.ps1`：候选构建总入口，只写入 `work/`；
- `Publish-TestRelease.ps1`：验收后发布到 `outputs/`；
- `Test-ReleaseReadiness.ps1`：版本和交付文件体检；
- `Clear-ProjectWork.ps1`：只清理 `work/` 中未保护内容，默认先预览。

旧版 `package.ps1`、`install.ps1`、`uninstall.ps1` 和中文 `.cmd` 入口只用于安全拒绝和引导，不是开发入口。

所有脚本使用 UTF-8 BOM，以兼容 Windows PowerShell 5。脚本输出和构建临时物必须留在 `work/`，不要在根目录创建测试副本。

CAD 测试无需外部模板：`New-TestDrawing.ps1` 从空数据库生成种子；各测试自行添加样例实体。历史 VPS 站点脚本不参与当前发行，唯一发布入口为 `update-service/New-UpdateRelease.ps1` 和 `Publish-UpdateRelease.ps1`。

`verify-github-update-channel.ps1` 在 `build.ps1` 后单独执行：匿名读取公开 latest Release 和新旧 `stable.json`，比对四个资产、版本、大小和 SHA-256；分别用当前构建及本机已安装的更新 DLL 在独立 .NET Framework 进程中下载校验安装包。输出在 `work/github-update-channel-*/`，不会运行安装器或发布资产。此检查需要联网，不加入离线回归。`-InstalledUpdateDll <路径>` 可指定其他安装位置。
