# 支架参数化大样（独立诊断模块）

模块版本 v0.4.2，程序集 0.4.2.0；未接入正式工具箱安装包。

模块根据用户输入生成原生 CAD 几何、尺寸和材料表，支持创建、编辑、查看、删除及保存后回读。
节点示意由代码绘制，仅表达构件关系，不作为可直接施工的节点详图。

第三方参考图纸、固定详图库及其提取、嵌入、缓存、加载路径已全部移除。
构建和验证无需补回任何旧图纸。测试种子由 `scripts/New-TestDrawing.ps1` 使用空 CAD 数据库生成。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-support-detail.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-support-detail.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-support-detail-autocad.ps1
```

构建仍需要真实 AutoCAD 2020 SDK。临时 DLL、测试图纸与运行日志只保存在忽略的 `work/` 中。
原图操作使用独立副本；用户自己已有的工程文件不属于仓库资源清理范围。
