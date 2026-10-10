# 渲染模型与 Canvas

## 依赖树

```text
Inno.Rendering                  中立契约
├─ RenderOutputSession          Host 选择内容、viewport、输入、route
├─ IRenderModel                 插件判断是否适用并构建输出
├─ RenderView                   本帧投影、可见性和 viewport
├─ IViewContentSource           提供世界内容
├─ IViewDrawable                绘制器
├─ IViewPointerTarget           命中与局部输入
└─ RenderOutputRoute            多模型同输出时显式排序及内容源分配
Inno.Rendering.Runtime          插件代际、帧协调、资源退休
Editor / Player                 声明输出 Session

Inno.Rendering2D                Camera2D、相机栈、2D 排序与后处理
Inno.Canvas                     RML、字体、世界画布、UI 绘制与事件
```

插件只通过引擎契约交互，不互相引用。Camera 是渲染模型的类型，不在 Core。Canvas 没有 GameView provider 或独立的 backbuffer 请求；没有可用模型时画面为空并产生诊断。多个模型都接受一个输出时，`RenderOutputRoute` 逐层指定模型及独占的世界内容源；各层独立绘制，再按预乘 Alpha 合成，不按插件加载顺序叠加。

## 一帧中的数据流

1. Editor/Player 提供 `ContentReadScope`、输出 viewport 和输入。Rendering2D 从场景的 `Rendering2DSceneSystem` 与 Camera2D 建立 View。
2. Canvas `IViewContentSource` 读取 Canvas 所在对象的完整父子 Transform。`referenceWidth`、`referenceHeight` 决定 RML 逻辑布局尺寸，除以项目级 `logicalPixelsPerWorldUnit` 得到以原点为中心的局部 XY 平面尺寸，再应用父子 Transform；相机投影只决定输出像素与字形栅格密度。
3. Rendering2D 收集精灵及外部 `ViewContentItem`，应用其 SortingGroup2D 规则统一排序。Canvas 是一个内部顺序固定的透明项，可处于精灵之间。排序后只合并相邻精灵批次。
4. 已捕获指针的内容项优先接收后续输入；其余目标按绘制顺序反向命中。指针按下决定键盘焦点，焦点 Canvas 在指针移出后继续接收按键和文字。Canvas 射线命中自身平面，将交点转换到局部 RML 坐标。所有 View 先路由输入，`IViewContentFrameSource.CompleteFrame` 再统一推进每个文档 Context 一次，并将 DOM 事件放入 Core EventDispatcher；Canvas 的下一次 GameBehavior.Update 在运行会话作用域内派发回调。Canvas 绘制器将 RML 网格和图集直接送入 2D 场景颜色阶段，随后才进行后处理。
5. Canvas 组件拥有 UI Context 及其已发布的中立网格、纹理数据；绘制器只借用这份增量资源状态并拥有当前渲染代际的 GPU 资源。仅重建 ViewContentSource 时释放旧绘制器的 GPU 资源，保留仍存活文档的 CPU 数据，新的绘制器可继续读取后续增量帧。替换文档、UI 服务或销毁组件时清空资源状态并释放 Context；实际插件代际退休继续通过组件销毁完成此流程。RmlUi 原生字体注册由 UI backend 在自身退休时清空。Editor/Player 输出目标归 Host 所有。Shader/Material 预览使用专用请求，不属于场景模型。

## 作者与运行时约束

- Canvas 组件只保存 `document`。字体由 RML 的 `@font-face` 声明，资源在导入时成为依赖；`font-family` 选族，`font-weight` 选面，`font-size` 定大小。文档使用隔离的后端字体名。未知字体与 `none` 不借用其他文档的旧字体。
- Canvas 的 `SetText`、`SetClass` 和 `Listen(handler)` 用于 HUD 更新、点击及 RCSS 动画。内部在渲染输出阶段读取 UI 事件，交给 Core EventDispatcher，在下一次脚本更新中派发；订阅 token 由脚本释放。样例把按钮事件与 `opacity` transition 串起来。世界 HUD 可以作为 Camera 子对象；它仍是受世界排序与遮挡规则控制的画布。
- 默认路径不为每个 Canvas 创建 RenderTexture。局部裁剪后的网格和字形图集直接绘制；纹理只应服务明确的缓存或特殊效果。
- GameView/SceneView 是输出与编辑操作的适配器，不拥有第二套场景合成。SceneView 可以使用插件提供的编辑相机，GameView 使用场景相机。

本轮采用纯世界空间语义。固定屏幕覆盖与 3D 模型可基于上述契约另行实现，不改变 Canvas 与 2D 的依赖方向。跨模型合成是顺序图层，不定义跨模型几何深度交错。
