# InnoEngine.Canvas

`InnoEngine.Canvas` 是一个可直接由 Inno Editor 打开的独立 InnoProject，同时也是可导出为 `.iplugin` 的源码型 Canvas 插件。它通过引擎内建的 Text/UI Service 与 RmlUi 适配器完成布局、文字和 DOM 交互，再通过公开 Rendering API 合成到最终画面；插件本身不携带或直接绑定 RmlUi、FreeType、HarfBuzz 等原生库。

> [!IMPORTANT]
> InnoEngine 与该插件仍在开发中。请使用相互匹配的 revision；序列化格式和公开脚本 API 当前不承诺向后兼容。

## 目录关系

推荐将引擎和插件并排放置：

```text
GameEngineDev/
├── InnoEngine/
└── InnoEngine.Canvas/
```

从本仓库启动 Editor：

```bash
/Users/aaronliao/.dotnet/dotnet run \
  --project ../InnoEngine/src/composition/editor/host/Inno.Editor.Application -- .
```

Editor 会生成 `Library/`、`Logs/`、`Inno.GameScripts.csproj`、`Inno.EditorScripts.csproj` 与 `InnoProject.sln`。它们都是本地派生物，不是插件源码，也不应提交。

## 内容

| 路径 | 用途 |
| --- | --- |
| `Assets/Runtime/Components/Canvas.cs` | 可添加到 GameObject 的 Canvas 组件与最小 DOM API |
| `Assets/Runtime/Rendering/` | UI Context、请求生命周期和后端中立的 GPU 合成 Pipeline |
| `Assets/Editor/` | Shader Graph 模板与 Pipeline 资产创建菜单 |
| `Assets/Shaders/` | Canvas 顶点/片段函数与 Shader Graph |
| `Assets/Materials/Canvas.imaterial` | 预乘 Alpha 材质 |
| `Assets/Pipelines/Canvas.irenderpipeline` | Canvas Render Pipeline 资产 |
| `Assets/~Samples/` | 示例 RML、Lato Latin 字体与许可证 |
| `Tests/InnoEngine.Canvas.Tests/` | 插件源码、资源导入、生命周期和原生 Shader 编译回归测试 |

## 使用

在场景 GameObject 上添加 `Canvas`，将 `~Samples/CanvasDemo.rml` 指定给 `document`，按需指定示例字体并把 `fontFamily` 设为 `Interface`。新组件的 `Reset` 会加载当前插件源中的默认 Pipeline 和 Material；旧场景或手工构造的组件如果字段为空，需要显式选择 `Assets/Pipelines/Canvas.irenderpipeline` 与 `Assets/Materials/Canvas.imaterial`。

每个组件拥有独立 UI Context 和整屏 viewport。布局随 presentation 尺寸与 `density` 更新；`order` 决定相对其他 presentation request 的顺序。禁用或删除组件会关闭 Context 并退休相关纹理。当前实现是 screen-space full-presentation Canvas，不是 world-space UI，也不替换 Editor 的 ImGui。

脚本 API 使用 `Inno.Canvas` 命名空间，这与 `InnoEngine.Rendering2D` 仓库使用 `Inno.Rendering2D` 的约定一致。`inno.canvas.*` 是资产和渲染扩展的稳定协议 ID，不随仓库展示名改变。Project/Plugin ID 固定为 `innoengine.canvas`，用户可见名称为 `InnoEngine.Canvas`。

## 构建与验证

```bash
/Users/aaronliao/.dotnet/dotnet test Tests/InnoEngine.Canvas.Tests/InnoEngine.Canvas.Tests.csproj

/Users/aaronliao/.dotnet/dotnet run \
  --project ../InnoEngine/build/pipeline/Inno.Build.Cli -- \
  plugin --project . \
  --output Builds/InnoEngine.Canvas.iplugin \
  --display-name InnoEngine.Canvas
```

导出的 `Builds/InnoEngine.Canvas.iplugin` 可复制到目标 InnoProject 的 `Plugins/`。安装后的内容是只读 mount；继续开发应修改本仓库的 `Assets/` 后重新导出。

## 版本控制

`Assets/`、所有 `.imeta`、`Settings.Project.inno` 和 `Settings.Build.inno` 是源码。`Library/`、`Logs/`、`Temp/`、`Builds/`、根目录 IDE 投影及所有 `bin/obj` 均可重建，已由 `.gitignore` 排除。
