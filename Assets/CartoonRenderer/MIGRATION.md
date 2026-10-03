# Cartoon Projection Renderer → Colorido (URP) 迁移说明

## 2026-10-01：静止低频刷新

- `Projection Adaptive Refresh`（默认开）：镜头交互时使用既有运动/普通刷新率，停稳后补一次最终视角重绘，随后进入静止刷新率。
- `Projection Idle Updates Per Second`（默认 `0.5`）：每两秒一次场景重建；`0` 则保持色块，直到镜头、参数或显式场景变化通知触发更新。静止只降低重建频率，不降低相机绘制帧率。
- 同尺寸的运动/静止档也会补最终视角；相机重新转动不等待静止截止时间。同步截图工具保持原固定捕获语义。
- 未通知的物体移动、材质变化由非零静止刷新率最终捕获。设为 `0` 时动态内容必须调用 `ProjectedShapePass.HintSceneContentChanged()`；材质内部动画也不自动侦测。通知使用版本号，避免两台相机抢走同一个通知，以及忙碌中的通知丢失。
- 角色重绘的动画帧准备与缓存参数未改动。已有角色位移通知仍可提前触发场景刷新，因此角色走动期间场景可能高于静止刷新率。
- 本轮保留用户当前采样宽度、颜色与角色风格，不实现旋转深度重投影；转动期间的既有回读/计算延迟仍存在。
- 原生 Unity 异步功能验收入口：`CartoonProjectionLatencyValidation.RunIdle`（独立批处理，不加 `-quit`）。验证最终视角、静止冻结、0.5 Hz、双相机、计算中通知、参数变化与旋转唤醒。报告：`Generated/LowLatencyValidation/IdleReport.txt`。

当前合并分支：`codex/merge-colorido-projects`（尚未提交）
原外层整合分支：`codex/cartoon-renderer-integration`
原独立验证分支：`codex/cartoon-renderer-migration`
当前工作目录：`/Volumes/WD 1TB/Unity Hub/GitHub/Colorido/Colorido`（已合并到内层工程，外层仅保留副本）
GitHub 回退点：`1.0.9`，对应 `8913ee419dcd2d191423f8047159f4659edc773a`，远端标签已核对。
验证编辑器：**Unity 6000.3.25f1**（本机仅有的已安装编辑器）
历史版本：外层曾声明 6000.4.6f1；合并保留内层已有的 6000.3.25f1 配置。

2026-09-30 工程合并保留了内层新角色动画、脚本执行顺序与场景布局。详细冲突处理、备份和本轮验收见根目录 `PROJECT_MERGE.md`；以下实测数据保留为迁移阶段记录，本轮新报告位于 `Generated/MergeValidation/Report.txt`。

## 打开方式

1. 用 Unity 6000.3.25f1 打开本工程目录。
2. 实际房间验收打开 `Assets/Scenes/CartoonRoomAcceptance.unity`；原简单对照场景为 `Assets/Scenes/CartoonMigrationPreview.unity`。
3. 选中场景里的 `Main Camera` 或管线资产，效果开关在：
   `Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset`
   → Renderer Features → **Cartoon Projection Renderer**
   - `Enabled`：总开关（关闭后回到正常 URP 画面）
   - `Projected 2D Shapes`：使用二维投影重绘（本轮默认效果）
   - `Projection Show Contours`：轮廓调试开关
   - `Projection Width / Updates Per Second / Color Step / Minimum Area / Contour Tolerance`：精度参数

也可以在 `Window > Rendering > Renderer Features` 或 Inspector 里选中该 Renderer Data 直接调。

## 房间验收

详见 `Assets/CartoonRenderer/Acceptance/README.md`。房间包括木地板、贴图挂画、沙发、书架、桌椅、盆栽，以及项目现有的 2D 人物图片。进入 Play 后：

- `T` 开关效果，`C` 显示轮廓，`R` 重置。
- 按住 `Q/E` 连续绕房间旋转；`Space` 开关自动旋转。
- `WASD` 移动测试人物；`1/2` 对比人物在茶几前、后的遮挡。

这是独立视觉验收场景，不改原游戏输入和物理逻辑。切出 Play 时恢复 Renderer Feature 的原开关设置；旋转时近侧墙体自动隐藏以方便观察室内。批处理入口 `CartoonRoomAcceptanceBuilder.BuildAndVerify` 会生成六张房间截图、做人物遮挡差分检查，然后执行同步／异步原色回归。尚未做 Player 构建或交互 Play 全流程验收。

## 复现对照图与统计

菜单 `Cartoon Migration > 0. Run All`，或批处理：

```bash
"/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath "<工程路径>" \
  -executeMethod CartoonMigrationTools.RunAll -logFile /tmp/migration.log
```

输出：
- `Assets/CartoonRenderer/Generated/Captures/01_effect_off.png`（效果关闭）
- `Assets/CartoonRenderer/Generated/Captures/02_effect_on.png`（效果开启）
- `Assets/CartoonRenderer/Generated/Captures/03_contours_debug.png`（轮廓调试）
- `Assets/CartoonRenderer/Generated/Captures/04_camera_rotated.png`（镜头旋转后）
- `Assets/CartoonRenderer/Generated/MigrationReport.txt`（区域数 / 边数 / 三角形 / CPU 耗时）

## 实测数据（640×480 固定机位）

以下为 2026-09-30 修正捕获色彩空间后的一次回归记录，不是独立性能基准。

| 场景 | 区域数 | 轮廓边（简化前→后） | 三角形 | CPU 重建 |
|---|---:|---:|---:|---:|
| 效果开启 | 33 | 4617 → 98 | 158 | 19.0 ms |
| 轮廓调试 | 33 | 4617 → 98 | 158 | 27.7 ms |
| 镜头旋转 28° | 30 | 4577 → 83 | 88 | 25.4 ms |

## 捕获颜色与多场景回归

`Assets/Editor/CartoonProjectionRegression.cs` 是独立批处理入口，运行时返回编辑器更新循环以推进正常异步回调。不要传 `-quit`，验证结束后脚本自行退出，失败返回非零退出码：

```bash
"/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "<Colorido 主项目路径>" \
  -executeMethod CartoonProjectionRegression.Run -logFile /tmp/cartoon-projection-regression.log
```

结果在 `Assets/CartoonRenderer/Generated/RegressionReport.txt`。已通过：

- 两个 Additive 场景、最后一个空场景、`HideFlags.DontSave` 子对象、场景卸载后的枚举。
- 同步颜色与身份目标实际格式均为 `R8G8B8A8_UNorm`，`sRGB=false`，与运行时路径共用格式常量。
- 已知材质底色的同步捕获、同步多边形重绘、正常异步多边形重绘均为 RGB `(128, 64, 32)`。
- 身份缓冲保持精确字节 `(1, 0, 0, 255)`，不会被 sRGB 转换污染。
- 丢弃同步生成的网格后，正常异步回调和 CPU 工作任务在主项目整合验证的一轮中 2 次编辑器更新后生成新网格；不是复用旧截图。具体更新次数不是性能保证。
- 四张迁移对照图已重新生成。尚未进行 Player 构建、连续运动和复杂人物遮挡验收。

## 本轮改动文件

工程配置
- `ProjectSettings/ProjectVersion.txt`：编辑器版本改为当前可用版本
- `Packages/manifest.json`：加入 `com.unity.render-pipelines.universal` / `.core` 17.3.0（编辑器内置包，无需下载）
- `ProjectSettings/GraphicsSettings.asset`：接入 URP 管线资产 + URP 全局设置映射（保留 Linear 色彩空间）
- `ProjectSettings/URPProjectSettings.asset`：随 URP 接入生成
- `ProjectSettings/InputManager.asset`：URP 首次导入自动追加的调试输入轴（非手工改动）

渲染器核心（`Assets/CartoonRenderer/`）
- `Runtime/CartoonRendererFeature.cs`：解耦新旧路径。新投影路径不再依赖旧 v1 着色器，缺失时不再阻止运行
- `Runtime/Projection/ProjectedShapePass.cs`：
  - 渲染器枚举改为场景遍历（`FindObjectsByType` 看不到 `HideFlags.DontSave` 的运行时生成对象，背景盒因此从未被捕获）
  - 每个场景的根对象立即消费，避免 `GetRootGameObjects(List)` 清空列表导致仅捕获最后一个场景
  - 重绘按「身份缓冲 alpha」生成的覆盖遮罩混合，未覆盖区域保留原画面（原先整屏不透明覆盖，天空变黑）
  - 新增可选同步回读路径（仅编辑器批处理使用；运行时仍是原异步路径）
  - 同步与异步目标共用显式 UNorm 格式，防止批处理出现二次 sRGB 编码、颜色变浅和身份字节污染
- `Shaders/ProjectedCapture.shader`：保持原有线性→sRGB 编码约定
- `Shaders/ProjectedRedraw.shader`：新增覆盖遮罩混合、恢复原有几何朝向

场景与材质
- `Assets/Scenes/SampleScene.unity`：5 个可见网格的 Built-in 默认材质替换为 URP Lit（原为粉色/白模源）
- `Assets/Materials/URP_Migration_Gray.mat`：替换用中性 URP 材质
- `Assets/Scenes/CartoonMigrationPreview.unity`：迁移预览场景（含测试模型，演示色块分区）
- `Assets/Shaders/BillboardImage.shader`：URP 化，保留 `_MainTex`/`_Color`/UV 变换、透明混合、双面
- `Assets/Shaders/UnlitTransparentDoubleSided.shader`：同上
- `Assets/Shaders/PixelatedModel.shader`：由 Built-in CG surface shader 改写为 URP HLSL，属性名与默认值不变

工具
- `Assets/Editor/CartoonMigrationTools.cs`：材质修复、预览场景生成、对照截图与统计
- `Assets/Editor/CartoonShaderInclusion.cs`：把 `Shader.Find` 用到的着色器加入 Always Included Shaders，避免构建裁剪
- `Assets/Editor/CartoonProjectionRegression.cs`：多场景枚举、格式、原色像素及正常异步重绘的针对性回归

## 已验证

- 编译与运行：新增代码零编译错误、零 Shader 错误、零持续异常
- 颜色：场景保留原配色（背景盒深蓝、地面灰、测试块彩色），无粉色材质、无白模
  - 修复前批处理目标会重复 sRGB 编码，导致颜色变浅；旧截图不能作为原材质颜色准确性的证明
  - 修复后背景盒深蓝与地面灰分别保留；原色准确性由已知底色的像素回归确认，而非仅比较光照开关差异
- 图像化：输出为平涂色块 + 简化轮廓，保留彩色纹理分区
- 几何：区域合并、共享轮廓简化、孔洞处理均由原 `ProjectedShapeBuilder` 承担，未重写
- 角色：人物保留原图，朝向相机，镜头旋转 28° 后仍正确；绘制在场景重绘之上
- 开关：`Enabled` 关闭即回到正常 URP 画面，不黑屏
- 构建资源：关键着色器已加入 Always Included Shaders

## 未解决 / 需要后续确认

1. **`Streaming` 生成对象**：本轮修复了背景盒这类 `HideFlags.DontSave` 对象的捕获枚举，但若后续新增同类生成物，需确认其渲染器仍被纳入。
2. **编辑器版本**：本轮在 6000.3.25f1 验证。回到 6000.4.6f1 时需重新确认 URP 包版本与渲染器序列化，不能据此假定兼容。
3. **人物遮挡精度**：人物使用原 3D 深度参与遮挡，站在障碍物后方时轮廓边缘可能有 1 像素级差异。本轮未观察到明显穿插，未做深度特判。
4. **同步回读仅限编辑器**：原截图工具在阻塞入口内循环，不返回编辑器更新循环，异步回调无法推进，故使用同步回读。非阻塞批处理回归已确认正常异步路径可完成。默认更新频率为 8 次/秒（周期 125 ms）；GPU 回读和 CPU 重建还会增加延迟，不能把 125 ms 当作端到端延迟上限。
5. **测试模型观感**：预览场景新增的测试球/块使用程序化棋盘贴图，其 UV 覆盖方式使得未贴图区域显示为灰色。如需更直观的色块演示可替换为带完整 UV 的模型。
6. **未做性能优化**：本轮未追求每帧重建。最终一轮简单场景约 19–28 ms；带纹理的房间约 43–49 ms（640×480），首次运行曾达到 81–85 ms。需单独做受控性能基准；这些值不包含完整 GPU 与回读耗时。
7. **InputManager.asset / 纹理 .meta**：URP 首次导入的自动副作用，非破坏性改动。
