# 低延迟二维投影重绘

工作工程：`/Volumes/WD 1TB/Unity Hub/GitHub/Colorido/Colorido`。
仍然捕获原材质底色，再提取二维区域/轮廓并重绘多边形，不把人物或场景换成白模。

## 开关与默认参数

选中 `Settings/CartoonUniversalRenderer.asset` 中的 Cartoon Projection Renderer：

- `Projection Low Latency`：默认开启；关闭后回到固定分辨率/固定频率路径。
- `Projection Motion Width`：320，镜头移动时的捕获宽度。
- `Projection Motion Updates Per Second`：30，请求频率上限，不保证每秒完成 30 次。
- `Projection Settle Seconds`：0.2，镜头停止变化后恢复完整宽度。
- `Projection Width`：640，稳定镜头的细节宽度。
- `Projection Updates Per Second`：8，稳定状态周期性刷新，保留动态场景更新能力。
- `Projection Maximum Result Age`：0.35 秒，过期新结果不再覆盖当前画面。

冷启动先生成交互精度结果。每个相机最多一个 GPU 回读/CPU 工作任务，
不排队积压；运动时提前使用运动频率的截止时间，停稳后立即安排高精度重建。
低精度的最小区域面积和轮廓容差按分辨率缩放，颜色量化步长不变。
同步截图工具始终使用完整分辨率，不自动降档，便于复现原画面与比较。

## 镜头对齐与过期处理

标准正交相机保持方向不变时，已有多边形和覆盖遮罩按当前平移/缩放逐帧对齐。
遮罩采样仍使用原捕获 UV；每相机单独绑定材质参数，避免共享材质污染其他相机。
这种对齐不重新生成区域，尚未捕获的新露出区域暂时保留当前 URP 原画面。

旋转和透视相机不进行这种无深度的平面变换，避免错误遮挡和拉伸。
切换投影类型、超过 25 度的镜头突变或大幅位置跳变时，不覆盖不匹配的旧多边形，
新结果到达前保留当前原画面。超龄结果只在完成时丢弃，不阻塞等待 GPU，
也没有强行取消正在执行的 GPU 请求。连续旋转仍存在回读/重建时延。

## 计时含义

`ProjectedShapePass.ProjectionCompleted` 为每次首次提交的新重绘提供：
相机 ID、捕获序号、宽度、运动档位、回读回调耗时、CPU Build 耗时、
捕获记录到首次重绘命令提交的耗时。房间 Game HUD 显示最近一次宽度及提交耗时。

最后一项包含 CPU 调度和等待回调，但不包含显示器呈现，也不是 GPU 精确计时；
不能把它当成真实输入到屏幕端到端延迟。CPU Build 从函数内部 Stopwatch 获取，
不包含 Task 的排队等待。多个相机的 HUD 静态统计表示最近完成的相机。

## 原生 Unity 验证

本工程没有 `game-dev` 密封性能适配器，使用原生 Unity 批处理 A/B 冒烟测试，
数据仅用于本机检查，不能证明正式性能预算或硬件上的稳定提升。

```sh
"/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "<关闭编辑器的测试工程路径>" \
  -executeMethod CartoonProjectionLatencyValidation.Run \
  -logFile /tmp/colorido-lowlatency-validation.log
```

不要加 `-quit`。测试顺序：策略断言 → 缓存 X/Y 平移与新生成画面的 GPU 像素比较 →
同一房间/640×480/60 Hz 请求节奏/相同镜头轨迹的旧模式与低延迟模式 A/B →
稳定画面比较 → 原工程合并、人物、房间遮挡和同步/异步原色回归。

报告、逐样本 CSV 和图片位于 `Generated/LowLatencyValidation/`。
本轮在独立临时工程执行，没有切换或保存用户当前打开的场景。

## 本次对照记录（2026-09-30）

Unity 6000.3.25f1 / Metal / Linear，同一进程里顺序测试，最终完整回归退出码 0。
测试副本源码 SHA-256 与工作工程六个相关脚本/Shader 一致。
Unity 原生测试未使用 game-dev 密封运行包，也没有控制整机负载，不能据此承诺游戏帧率。

| 指标 | 原固定 640px / 8Hz | 运动 320px / 30Hz 请求 |
|---|---:|---:|
| 运动期完成样本 | 12 | 32 |
| 捕获到重绘提交 p50 | 179.4 ms | 70.2 ms |
| 捕获到重绘提交 p95 | 409.0 ms | 219.3 ms |
| CPU Build p50 | 161.2 ms | 58.8 ms |
| 新结果提交间隔 p50 | 179.0 ms | 69.6 ms |

运动期新模式所有样本宽度均为 320；停稳后恢复 640。
横向/纵向缓存平移、正交缩放与新生成画面的差异均为 0 像素。
停稳后的新旧模式画面差异也是 0 像素。实际玩家 Sprite 的两个 Move 帧、
房间前后遮挡、多场景枚举及同步/正常异步 RGB(128,64,32) 原色检查均通过。

新模式仍出现过一次约 343 ms 的提交尖峰，并未消除 GC、线程调度、重建算法或复杂视角的长尾。
这次首先解决刷新策略和正交平移对齐，不是最终性能版本。
报告与原始 CSV 已复制回本工程的 `Generated/LowLatencyValidation/`，
旁边保留此次人物、房间、异步原色回归的文本报告。
本轮渲染器改动前备份：`/Volumes/WD 1TB/Unity Hub/GitHub/colorido-lowlatency-backup.PVzIUf/renderer-before.tgz`。
