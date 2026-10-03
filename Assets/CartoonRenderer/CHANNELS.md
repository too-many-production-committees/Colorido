# 场景与角色独立控制的卡通渲染

在 `CartoonRenderer` 内新增 Scene / Character 两个独立通道：开关、参数、缓存、异步任务与重建状态互相隔离，
并保留一个 Master 总开关。二维角色使用**局部空间帧缓存重绘**，不复制整屏 GPU 回读管线。

工作工程：`/Volumes/WD 1TB/Unity Hub/GitHub/Colorido/Colorido`（Unity 6000.3.25f1 / URP 17.3.0 / Linear）。

## 初始状态

初始配置刻意保持改动前的画面：**场景通道开启、角色通道关闭**，二维人物继续显示原图。

`CartoonRenderSettings` 原有字段全部保留并继续作为**场景通道**的风格参数，因此已有资产（`CartoonUniversalRenderer.asset`）
升级后不会丢失风格、精度或开关：缺少的新字段由字段初始值补齐，GUID 未变。
角色通道使用独立的 `CartoonCharacterSettings`，不与环境共享同一个可变配置对象。

## Play 模式崩溃修复（2026-10-01）

两份用户崩溃记录和 `Editor-prev.log` 均指向原生
`SendShadowCullingCallbacks → PrepareDrawShadowsCommandStep1 → ScriptableRenderContext.Submit`。
原实现从 `AddRenderPasses` 驱动角色系统，但 URP 17.3 在此之前已经完成相机及阴影剔除；
角色更新会创建 MeshRenderer、替换网格、切换原 SpriteRenderer，不能在这里更改剔除所依据的对象。

- 将 `PumpChannels()` 移到 `OnCameraPreCull`，`AddRenderPasses` 仅负责设置和入队渲染 Pass。
- 即使 Master 关闭也执行剔除前更新，让角色恢复原图并隐藏已有重绘节点。
- 没有关闭场景阴影、降低角色参数、修改动画脚本或保存用户场景。

新增验收入口 `CartoonCharacterPlayModeValidation.Run`，只能在隔离批处理工程运行，
不加 `-quit` 或 `-nographics`。它打开实际 `CartoonMigrationPreview` 场景，
进入真实 Play 模式（包括域重载和玩家 Animator），保留阴影，使用正常异步路径，
验证 Master、场景/角色开关、形状参数及轮廓切换，并连续执行三轮 Play/Stop。
报告：`Assets/CartoonRenderer/Generated/PlayModeValidation/Report.txt`。

修复版三轮均 PASS，分别手动渲染 165 / 128 / 115 帧，角色构建失败计数均为 0。
既有 `CartoonChannelAcceptance.BuildAndRun` 十组检查全部 PASS；
`ColoridoProjectMergeValidation.Run` EXIT=0，主场景、房间、遮挡及同步/异步原色回归均通过。
`CartoonProjectionLatencyValidation.Run` EXIT=0，正交平移/缩放缓存对齐及停稳 A/B 差异均为 0 像素，
报告另存为 `Generated/PlayModeValidation/LatencyRegression.txt`。
对应报告另存于 `Generated/PlayModeValidation/ChannelRegression.txt`、
`MergeRegression.txt` 和 `ColorRegression.txt`，不覆盖之前的验收报告。
隔离副本与工作工程的 `CartoonRendererFeature.cs` SHA-256 一致。
修复前同一验收确认 Master 关闭不恢复角色，但批处理副本没有复现用户 GUI 的原生崩溃；
因此上述时机问题是依据日志和调用链定位并修正的风险，仍需在用户实际编辑器窗口确认。
此测试也不代表不可读 Sprite 的异步 GPU 回读已经验收。

## 开关与参数

### 角色动画换帧优化（2026-10-01）

实际配置曾将 `Character.minimumRebuildInterval` 调到 0.221 秒，未缓存帧最多约每秒启动
4.5 次构建，低于 Move 动画的换帧速度。已将工程配置改为 **0**；
保留用户的 colorStep=107、minimumArea=148、contourTolerance=3.19、maximumSampleSize=336、
颜色参数、细节选项及缓存预算，不通过降低画质加快换帧。
该间隔只限制新构建，已缓存帧始终直接切换；同时每角色仍只有一个待处理任务，
保留全局每帧启动预算，不无限提交 worker。

角色绘制热路径复用每个目标的 `MaterialPropertyBlock`；轮廓关闭时不创建/上传轮廓网格，
开启后从当前缓存形状补建网格，再次换帧仍同步到最新轮廓。形状算法和原色采样不变。

限速参数对照使用**同一优化版代码**和同一风格，在隔离工程中以 24 Hz 驱动实际 Move 的
40 张 Sprite，冷缓存各运行 8 秒，场景通道保持开启：

| 重建间隔 | 检查次数 | 当前帧重绘命中 | 命中占比 | 实际显示的重绘帧 | 完成构建 |
|---|---:|---:|---:|---:|---:|
| 0.221 秒 | 156 | 53 | 34.0% | 26/40 | 32 |
| 0 秒 | 179 | 140 | 78.2% | 40/40 | 40 |

没有显示旧动画帧或同时显示原图和重绘。首轮未缓存帧仍使用原图，不能保证首次播放即
100% 重绘；缓存命中后不受重建间隔限制。接着以同一缓存回放 4 秒：84/84 次检查
均显示当前重绘帧，命中占比 100%，新增构建 0。检查比例是 CPU 相机渲染后的状态检查，
**不是显示器呈现帧率或密封硬件性能基准**；不据此宣称材质参数块/轮廓改动的独立加速倍数。
批处理不推进 frameCount，测试明确标记每个模拟动画帧，避免重复相机保护污染结果。

验收入口：`CartoonCharacterRefreshValidation.Run`（隔离批处理工程，不加 `-quit/-nographics`）。
报告：`Generated/CharacterRefresh/Report.txt`。
优化版使用实际零间隔配置复跑 `CartoonChannelAcceptance.BuildAndRun`：十组全 PASS，
缓存回放新增构建 0；真实 Play/Stop 三轮全 PASS，并验证关闭轮廓后再开启能补建当前帧轮廓网格。
报告分别另存为 `Generated/CharacterRefresh/ChannelRegression.txt` 和 `PlayRegression.txt`。

选中 `Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset` → Cartoon Projection Renderer：

| 位置 | 作用 |
|---|---|
| `Enabled` | Master 总开关 |
| `Scene Channel Enabled` | 场景通道（下面所有 Projection* 字段都是它的风格） |
| `Character → Enabled` | 角色通道总开关；关闭时所有角色保持原图 |
| `Character → Color Step / Minimum Area / Contour Tolerance / Maximum Sample Size / Preserve Detail Regions` | 角色形状参数（改变才使角色缓存失效） |
| `Character → Palette Saturation / Brightness / Show Contours` | 仅绘制期生效，不触发重建 |
| `Character → Minimum Rebuild Interval / Prepares Per Frame` | 角色更新策略 |
| `Character → Cache Capacity / Cache Pixel Budget` | 角色缓存预算 |

## 组件

**`CartoonRenderClassifier`**（任意对象，可选）— 显式通道覆盖：`Inherit / Scene / Character / Exclude`。
就近生效并向子节点继承，所以子节点可以覆盖父节点。它不改变 Unity Layer、碰撞体或任何玩法标记。

**`CartoonCharacterRenderController`**（角色根节点）— 角色通道标记 + 独立模式：
`Inherit`（跟随通道）、`Original`（保持原材质/原图）、`ProjectedShapes`（本角色二维重绘）。
可选 `Style Override` 资产单独调整某个角色。

规则：
- 角色通道总开关优先级最高，单个角色**无法**把它重新打开；通道关闭时所有角色都按 `Original` 处理。
- 通道开启时 `Original` 仍可作为单个角色的排除选项。
- 未标记的可处理三维网格归场景；未标记的 Sprite 保持原行为（不参与场景重绘）。
- 同一个 Renderer 只属于一个通道（就近标记决定）。

## 二维角色的实现

不复制整屏 GPU 回读＋CPU 重建管线，流程是：

1. 读取当前 Sprite 的原颜色与透明轮廓（可读贴图直接取像素，不可读贴图走非阻塞 GPU 回读；
   **批量修改贴图 Read/Write 导入设置的做法没有被采用**）。
2. 采样精度按 Sprite 实际像素尺寸取上限，**从不放大**低分辨率帧。
3. 复用 `ProjectedShapeBuilder` 在**角色局部空间**做颜色分区与共享轮廓简化。
4. 按 (Sprite, 形状参数哈希, 贴图版本) 缓存；换帧命中缓存即直接切换，移动/旋转/缩放/翻转使用当前 Transform。
5. 未缓存帧先显示原图，后台 `Task` 完成后再切换；任何时刻只有原图或重绘其中之一可见，不会叠加。

因此**移动角色和移动镜头不会让已缓存的 Sprite 帧重新做整屏重建**。

透明边缘由原 Sprite 的 alpha 提供（重绘 Shader 直接采样原贴图），所以轮廓是真实轮廓而不是矩形；
脚底基准使用 Sprite 自身的 pivot，翻转使用 `flipX/flipY` 并同步到重绘节点。

### 遮挡与合成

- 场景重绘：`ZTest Always`、不写深度、在透明物体之前绘制，因此**保留当前帧的不透明深度缓冲**。
- 角色重绘：透明队列、`ZTest LEqual`、`ZWrite Off`，深度来自**当前帧**不透明 pass 的深度缓冲，
  排序层与排序序号从原 `SpriteRenderer` 复制。所以桌子、墙体照常遮挡角色，角色不会穿墙或始终置顶。
- 场景通道在捕获时按通道剔除角色对象，因此场景重绘不会涂掉仍使用原材质的角色。
- 角色移动会让下一次场景捕获提前到配置的节奏（`HintSceneContentChanged`），旧位置不会长期留洞。
- 未覆盖区域继续显示原画面；不出现黑屏或全屏不透明覆盖。

本版覆盖不透明场景、透明裁切和二维人物；复杂玻璃、半透明特效和 UI 仍走原 URP 路径。

## 验收

专用验收场景（不动 `SampleScene`）：`Assets/Scenes/CartoonChannelAcceptance.unity`，
可通过菜单 `Cartoon Migration > 5. Build Channel Acceptance Scene` 重建。

```sh
"/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "/Volumes/WD 1TB/Unity Hub/GitHub/Colorido/Colorido" \
  -executeMethod CartoonChannelAcceptance.BuildAndRun \
  -logFile /tmp/colorido-channel-acceptance.log     # 不加 -quit
```

报告 `Assets/CartoonRenderer/Generated/ChannelAcceptance/Report.txt`，四种组合截图与遮挡/翻转对照图在同目录。

已有回归入口继续可用（都保持原语义）：

```sh
... -executeMethod ColoridoProjectMergeValidation.Run        # 合并 + 房间验收
... -executeMethod CartoonProjectionLatencyValidation.Run     # 低延迟 A/B 与异步原色
```

## 实测（2026-10-01，Unity 6000.3.25f1 / Metal / Linear，本机单次采样）

通道验收 `CartoonChannelAcceptance.BuildAndRun`：**RESULT: PASS**（10 组检查）

| 项目 | 数值 |
|---|---|
| 四种组合 | 均正确渲染；场景开关改变 263065 像素，角色开关改变 1851/1902 像素，两通道不同时显示同一个角色 |
| 参数隔离 | 改角色 colorStep：场景 15 区域 / 87 边不变；改场景 contourTolerance：角色构建次数 0，场景边 87→72 |
| 仅颜色参数 | 角色构建次数 0（复用缓存几何） |
| 场景繁忙时动画 | 连续显示 8 个不同帧 |
| 缓存回放 | 40 帧回放两轮：新构建 0，命中 80，缓存 40 条 / 983040 像素，命中率 ~70%，无淘汰、未超预算 |
| 角色形状 | 采样 128×192，572 区域，2904→1395 边，1802 三角形，调色板 296 色，近白占比 0.3% |
| Original 模式 | 恢复原图，重绘节点隐藏 |
| 遮挡 | 桌前 2550 像素 / 桌后 1128 像素；隐藏桌子后 2550，即桌子确实挡住 1422 像素 |
| 镜头平移/缩放/旋转 | 2524 / 5157 / 2430 像素，重绘角色数量始终为 1 |
| 翻转与轮廓 | flipX 改变 2763 像素；重绘轮廓 2550 vs 原图轮廓 2548（比例 1.00） |
| 开关/换装/对象池/卸载 | 12 次切换生成节点 1→1；重生后跟踪 2 / 投影 2；卸载场景后存活投影 0 |
| 通道计时 | 场景 220 区域 / 19790→744 边 / 1302 三角形 / 29.9 ms；角色构建 11.5 ms；缓存命中率 66–75% |

回归：`ColoridoProjectMergeValidation.Run` EXIT=0（SampleScene 4 区域、房间 324 区域、旋转 333 区域、人物前后遮挡 1692/1356 全部 PASS）；
`CartoonProjectionLatencyValidation.Run` 全 PASS（正交平移/缩放缓存对齐差异 **0 像素**，停稳 A/B **0 像素**，
运动档 p50 143.7→60.6 ms、p95 420.4→125.6 ms、CPU 构建 p50 133.6→39.5 ms），同步与异步原色 RGB(128,64,32) 均未污染。

计时口径：上述"捕获到重绘提交"包含 CPU 调度与回读等待，**不含显示器呈现**，不是端到端屏幕延迟；
CPU 构建耗时取自算法内部 Stopwatch，不含 Task 排队。

## 已完成范围

- 通道分类、配置拆分、独立开关与状态隔离。
- 二维角色局部帧缓存重绘（分区、简化轮廓、透明边缘、翻转、脚底基准、Tint、排序层/序号）。
- 场景与角色各自的缓存/异步任务/序号/过期判断/统计/错误信息。
- 深度测试合成，四种开关组合，主场景与房间验收无回归。

## 未完成与已知限制

1. **三维角色未实现**。`SkinnedMeshRenderer` / `MeshRenderer` 角色会被识别为角色通道并保持原材质，
   运行时会打印一次明确说明，**没有**骨骼动画重建通道。第一阶段只完成了二维角色。
2. **图集旋转的 Sprite 不支持**，会回退为原图并给出一次警告（`packingRotation != None`）。
3. **非可读贴图的 GPU 回读路径已实现但未被验收覆盖**：本工程的角色帧是可读贴图，验收实际走的是 CPU 直读路径。
4. **编辑模式下不切换角色显示**。角色通道会替换活动的 `SpriteRenderer`，为避免弄脏并保存用户场景，
   该切换只在 Play 模式或工具显式开启时发生（`CartoonCharacterSystem.AllowVisualChangesOverride`）。
   场景通道不受此限制，编辑模式即可预览。
5. **资源竞争是存在的**：两个通道各自有界，但不是互不竞争 CPU/GPU；角色重建在 worker 线程，
   场景重建同样在 worker 线程，二者共享线程池。
6. **性能数字只代表本机单次采样**，不是帧率承诺；命令提交耗时不是屏幕显示延迟。

## 本轮修复的真实缺陷（供后续参考）

这些是在集成与回归中发现并修掉的，不是重构：

- `FindObjectsByType` 看不到 `HideFlags.DontSave` 的运行时对象，导致生成的背景盒从未被捕获。
- **instanceID 复用**：场景卸载后 id 会被复用，按 id 缓存的相机状态与通道判定会命中已销毁对象的旧结果，
  造成房间场景只捕获到 1 个区域。相机状态与通道判定都改为校验对象身份。
- **捕获渲染器列表的 1 秒缓存会跨场景存活**，场景切换后可能整帧绘制已销毁对象。
- 捕获材质按渲染器池化但从不回收，已补上随渲染器列表刷新的清理。
- 角色构建在 worker 线程读取 `Sprite.bounds/textureRect`（仅主线程 API）会整批抛异常，
  已改为在主线程读好几何再传给后台。
- 两个新 Shader 漏写 `Pass { }` 导致 ShaderLab 解析失败、`Shader.Find` 返回 null，材质为空、角色不渲染。
- 批处理下 `Time.frameCount` 不推进，导致每帧准备预算从不重置，只构建了 1 帧。
