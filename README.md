# Colorido

Colorido 是一个基于 Unity 的 2.5D 视角转换原型，灵感来自《FEZ》。项目重点探索平台视角旋转、投影吸附、第一人称视角切换、图片角色 billboard，以及像素化画面风格。

## Unity 版本

项目使用 Unity `2023.2.20f1c1` 创建。

## 操作方式

- `A / D` 或方向键：左右移动
- `Space`：跳跃
- `Q / E`：旋转平台视角
- `F`：在平台视角和第一人称视角之间切换
- `V`：备用视角切换键

进入第一人称视角时，玩家会被锁定，不能移动或跳跃。

## 当前功能

- 类 FEZ 的 90 度平台视角旋转
- 基于当前视角投影的多平台吸附
- 带镜头运动和弹性缓动的平台旋转
- 平台视角与第一人称视角之间的平滑过渡
- 使用图片替代 3D 玩家模型，并始终朝向摄像机
- 整屏像素化后处理
- 带二分阴影和硬高光的像素化模型 Shader
- 由六个面组成的正立方体背景空间
- 背景每个面的明度略有差异
- 平台视角下镜头会轻微跟随玩家

## 主要脚本

### `FezCameraController.cs`

负责平台视角旋转、第一人称切换、镜头跟随、镜头动态效果，以及背景旋转联动。

### `ProjectionManager.cs`

负责在视角旋转后，根据当前屏幕投影判断平台是否重叠，并在合适时把玩家吸附到目标平台。

### `PlayerController.cs`

负责玩家横向移动、重力、跳跃、跳跃缓冲和土狼时间。

### `PlayerBillboardVisual.cs`

负责把玩家的 3D 外观替换为图片 Quad，并让图片始终朝向摄像机。

### `BackgroundBox.cs`

负责生成包围场景的正立方体背景盒，并在平台视角旋转时提供背景动画。

### `PixelPerfectCameraEffect.cs`

负责整屏低分辨率像素化渲染，并提供轻微的动态像素效果。

## Shader

### `PixelatedModel.shader`

用于 3D 模型的像素化材质。支持：

- 贴图 UV 像素化
- 颜色分阶
- 二分阴影
- 硬高光
- 可选的顶点吸附

### `PixelPerfectPost.shader`

用于整屏后处理。支持：

- 低分辨率放大
- 颜色分阶
- 轻微动态像素噪声

### `BillboardImage.shader`

用于玩家图片 billboard 的透明无光照 Shader。

## 项目结构

主要目录：

- `Assets/Scenes`：场景文件
- `Assets/Scripts`：玩法、镜头和渲染相关脚本
- `Assets/Shaders`：自定义 Shader 和材质
- `Packages`：Unity 包配置
- `ProjectSettings`：Unity 项目设置

以下 Unity 自动生成目录不会提交到 git：

- `Library`
- `Temp`
- `Logs`
- `UserSettings`

## 如何运行

1. 克隆仓库。
2. 使用 Unity `2023.2.20f1c1` 或兼容的 Unity 2023.2 版本打开项目。
3. 打开 `Assets/Scenes/SampleScene.unity`。
4. 点击 Play 运行。

首次打开项目时，Unity 会自动重新生成被 `.gitignore` 忽略的 `Library` 目录。

## 开发状态

这是一个实验性原型，不是完整游戏。当前重点是验证：

- 2.5D 视角转换是否成立
- 平台投影吸附是否稳定
- 第一人称切换是否有表现力
- 像素化画面风格是否适合后续玩法

后续可以继续扩展关卡机关、收集物、门、检查点、掉落重生和更完整的 UI。
