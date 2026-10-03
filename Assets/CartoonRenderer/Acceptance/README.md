# Colorido 房间验收场景

用 Unity **6000.3.25f1** 打开 `/Volumes/WD 1TB/Unity Hub/GitHub/Colorido/Colorido`，再打开 `Assets/Scenes/CartoonRoomAcceptance.unity`，点击 Play。两个工程已合并到内层；外层仅保留合并前副本。

这里使用实际的室内组合模型：木地板、色块地毯、沙发、靠枕、书架与书本、桌椅、显示器、盆栽、窗框和贴图挂画。材质为 URP Lit，投影读取其原纹理和颜色，不是中性材质替换。2D 人物直接使用原项目的 `Assets/madoka.png`，不参加二维区域重建。

## 操作

| 操作 | 快捷键 |
|---|---|
| 原 URP / 二维重绘对照 | T |
| 简化轮廓调试 | C |
| 连续旋转镜头 | 按住 Q / E |
| 自动旋转开关 | Space |
| 移动 2D 人物 | WASD |
| 人物站在茶几前 / 后 | 1 / 2 |
| 重置镜头与人物 | R |

Game 窗口左上也有按钮。键盘操作前先点击 Game 窗口。

这只是视觉验收：人物移动不受碰撞和导航限制，不是游戏角色控制器。按 1/2 会切到固定正面机位，让遮挡对照保持可比。近侧墙体会随镜头位置自动隐藏，这是剖面展示，不是捕获缺失。退出 Play 会恢复效果开关与轮廓开关的原值。

## 重点检查

1. 开关效果后，墙面、织物、书本和挂画应保留原配色；不应泛白、变粉或被光照重新染色。
2. 地板与桌面纹理应形成平涂区域，C 模式可看到区域的共享简化轮廓。
3. 连续旋转时检查闪烁、旧轮廓滞后、丢失物体和白边。低延迟模式默认运动 320px / 30Hz 请求，停稳后恢复 640px / 8Hz；实际完成频率受回读与 CPU 限制，快速旋转仍可能暴露时延。HUD 显示最近捕获到重绘提交的耗时（不是屏幕显示延迟），详见 `../LOW_LATENCY.md`。
4. 人物应保持原图。按 2 后茶几应该挡住下半身，按 1 后应恢复；不要把二维人物当成始终置顶的 UI。
5. 旋转到房间侧后方时确认自动剖面墙体切换符合预期。

## 复现与边界

菜单 `Cartoon Migration > 4. Build Room Acceptance Scene` 可重建本场景及专用材质。生成资产只放在本目录，重建会替换本工具生成的房间场景；编辑后若要保留布局，请另存为其他场景。

```bash
"/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "/Volumes/WD 1TB/Unity Hub/GitHub/Colorido/Colorido" \
  -executeMethod CartoonRoomAcceptanceBuilder.BuildAndVerify \
  -logFile /tmp/colorido-room-integration.log
```

不要加 `-quit`：验证包含返回编辑器更新循环的异步测试，完成后自行退出。六张图片位于 `Captures/`，房间统计与人物遮挡差分在 `RoomValidation.txt`；同步／异步原色回归在 `Assets/CartoonRenderer/Generated/RegressionReport.txt`。

截图验收使用编辑器同步回读，运行时仍为异步；已单独验证正常异步原色重绘，但还不能替代这间房的连续交互验收或 Player 构建验收。房间最终一轮为 324 个区域、48.6 ms CPU 重建，旋转后 333 个区域、42.6 ms。首次运行曾达到 81–85 ms；这里只证明路径可用，并未证明满足游戏性能预算。人物可见差分像素从茶几前的 1692 降至茶几后的 1356，且人物仍有可见部分，遮挡比较通过。
