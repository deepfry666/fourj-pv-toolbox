# 第三方声明

根目录 MIT 许可仅适用于有权以该许可提供的项目代码，不改变第三方内容的许可。

## Inno Setup 与简体中文翻译

- 安装器由 [Inno Setup](https://jrsoftware.org/) 工具构建；本仓库不再分发其编译器。
- `scripts/installer/languages/ChineseSimplified.isl` 保留了官方来源和译者署名，不能归为本项目原创。
- Inno Setup 版权及许可见同目录 `LICENSE-Inno-Setup.txt`，原文来自 [jrsoftware/issrc](https://github.com/jrsoftware/issrc/blob/main/license.txt)。
- 译者 `kirakira` 的 MIT 许可见同目录 `LICENSE-ChineseSimplified.txt`，原文来自 [简体中文翻译项目](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation/blob/main/LICENSE)。

## Autodesk

AutoCAD、托管 API 和相关商标属于其权利人。构建需要使用者自行取得并安装适用产品和开发资源；本项目 MIT 许可不向使用者提供 Autodesk 产品或 SDK 的再分发授权。

## 不随公开源码提供的资源

第三方参考图纸、固定节点详图库及相关提取、嵌入和缓存逻辑已移除；无需也不应重新补入这些资源。测试使用代码生成的样例。历史包的资源检查见 `REPOSITORY_MIGRATION.md`；软件依赖的第三方许可继续分别适用。
