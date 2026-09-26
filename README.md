# InnoEngine.Canvas

Canvas 是独立 UI 插件：RML 文档、字体依赖、DOM 事件和世界空间网格由本插件负责。它不引用 Rendering2D，也不提供 Camera 或 GameView 渲染模型。只有安装了能接受世界内容的渲染模型，GameView 和 Player 才会显示 Canvas。

## 运行样例

本仓库独立安装和构建，不在 `Plugins/` 中引用 Rendering2D。`Assets/~Samples/SampleScene.iscene` 只包含默认 800 × 450 逻辑像素、8 × 4.5 世界单位的 Canvas 和按钮控制脚本；点击按钮会更新计数并切换文字的 RCSS 淡出效果。在同时安装 Canvas 与 Rendering2D 的 `TestProject` 中配置 Camera2D 和 Rendering2DSceneSystem 后进行可视测试。安装态样例可只读打开，修改时先使用 **Import Sample**。Canvas 单独安装时文档 API 仍可工作，但没有渲染模型，GameView 不出图。

```bash
/Users/aaronliao/.dotnet/dotnet run \
  --project ../InnoEngine/src/composition/editor/host/Inno.Editor.Application -- .
```

Canvas 的 `referenceWidth`、`referenceHeight` 默认是 800 × 450，始终决定 RML 百分比布局尺寸。项目级 `CanvasProjectSettings.logicalPixelsPerWorldUnit` 默认是 100；局部平面因此为 8 × 4.5 世界单位，再由自身和父级 Transform 缩放、旋转和定位。相机决定投影和最终屏幕像素密度。Canvas 不存字体、材质、Pipeline、排序或独立密度字段；材质由插件内部管理，2D 排序由 Rendering2D 的 SortingGroup2D 决定。

字体在 RML 中用 `@font-face` 声明，正文通过 `font-family`、`font-weight` 和 `font-size` 选择字体族、字形和大小。`font-family: none` 不绘制文字。字体文件会成为文档的资产依赖，换文档时重建 UI Context，避免旧字体或字形残留。文档依然可以使用 `Canvas.SetText`、`SetClass`、`SetAttribute`、`SetContent` 更新 HUD，用 `DrainEvents()` 消费点击等 DOM 事件。

`Assets/Runtime/Rendering` 将 UI 作为中立 `IViewContentSource` 发布；Rendering2D 收集后与精灵统一排序，在场景颜色阶段和后处理之前绘制。输入先按 View 与同一绘制顺序命中，再逆变换成 Canvas 局部 RML 坐标。默认直接绘制网格和字形图集，不为每个 Canvas 固定创建纹理。

## 验证与导出

```bash
/Users/aaronliao/.dotnet/dotnet test Tests/InnoEngine.Canvas.Tests/InnoEngine.Canvas.Tests.csproj
/Users/aaronliao/.dotnet/dotnet run \
  --project ../InnoEngine/src/composition/editor/host/Inno.Editor.Build.Cli -- \
  plugin --project . --output Builds/InnoEngine.Canvas.iplugin \
  --display-name InnoEngine.Canvas
```

构建目录、`Library/`、`Logs/` 和根目录生成的 IDE 工程都是派生文件。具体模块边界见 [架构文档](docs/ARCHITECTURE.md)。
