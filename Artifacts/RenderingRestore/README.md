# GitHub 1.0.9 基础上的渲染与动画恢复

恢复基线：`origin/main` / `1.0.9`，提交 `8913ee419dcd2d191423f8047159f4659edc773a`。
保留本地渲染、人物动画及原 Animator 状态机，发布到 `main`，标签 `1.0.10`。

## 本地备份

完整备份目录：`/Volumes/WD 1TB/Unity Hub/GitHub/Colorido-local-backup-20261004-070639/`。
其中 `local-backup.bundle` 包含原提交、本地修改及未跟踪资源。
Git stash：`f195caed9b96471d7143b49a3c9eee3df7e3727a`。
备份分支：`codex/backup-before-github-restore-20261004-070639`。

## 移植内容

- 本地 CartoonRenderer 包及其样式参数、URP 管线与三个自定义 Shader。
- Player 帧图、动画片段、Player.controller、PlayerAnimationDriver，以及 AnimatedSprite 显示脚本。
- 原控制器实际包含 Idle、IdleVariant、Move 三个状态、七个参数；Jump/Fall 目录未提供片段，保持备份原状。Move 的原片段为 15 fps、40 个精灵帧，保留原片段的循环设置。
- 主场景只接入动画显示组件、动画子节点和 URP 材质；角色与平台的位置、碰撞体、移动配置、镜头及投影组件保持 GitHub 场景配置。
- PlayerController 仅增加供动画读取的速度、落地、朝向、暂停状态及起跳通知。移动计算、范围约束、瞬移仍使用 GitHub 实现。Shift 调节动画播放速度；移动速度与 GitHub 一致。动画驱动的步伐倍率接口保留，未重新接入物理移动。
- 未恢复此前的平台同步瞬移、加强活动区域限制和遮挡旋转实验。
- 修复两处渲染验证工具兼容问题：移除对已弃用项目合并验证器的依赖；URP 首次初始化后重新读取有效的人物渲染通道。

## 本次验证

在独立项目 `/tmp/colorido-render-restoration-validation` 中运行，未通过验证器修改主项目场景或渲染参数。
使用本机已安装的 Unity **6000.3.25f1**；项目保留 GitHub 的 **6000.4.6f1** 声明，未在该版本运行验证。

- `ChannelReport.txt`：场景/人物四种通道组合、参数隔离、动画帧缓存、遮挡、镜头变动、生成与卸载检查全部 PASS。
- `PlayModeReport.txt`：三个真实 Play/Stop 周期；异步渲染、阴影、主开关与人物通道切换 PASS。日志中另有 Unity Editor Search 索引器的异常，未发生游戏代码异常或渲染崩溃。
- `AnimationReport.txt`：主场景无丢失脚本、三个原状态及七参数、移动 40 帧、播放速度、朝向、暂停恢复、停止待机、待机变体选择、起跳通知和主场景渲染 PASS。
- `SampleScene.png`：恢复后主场景的实际渲染截图。
- `SourceAudit.json`：GitHub 核心玩法脚本与本地状态机/驱动的哈希核对。

`Assets/CartoonRenderer/Generated` 内随备份恢复的其他报告属于历史验证，不能作为本次结果。
`EditorGeneratedImporterChanges.patch` 保存了本机编辑器自动产生的无关 NPC/图片导入器变更；发布提交保留这些文件的 GitHub 内容；本机编辑器可能在工作区继续自动生成相同变更。

复现：将主项目的 Assets、Packages、ProjectSettings 复制到独立目录，复制 SampleScene 为 CartoonMigrationPreview，并为后者生成不同 GUID 的 .meta；将本目录的 RestoredAnimationValidation.cs 放入独立目录的 Assets/Editor。依次在 batch editor 执行：

```text
-executeMethod CartoonChannelAcceptance.BuildAndRun
-executeMethod CartoonCharacterPlayModeValidation.Run
-executeMethod RestoredAnimationValidation.Run
```

这些入口自行退出，不添加 `-quit` 或 `-nographics`。验证工具会切换测试场景或测试参数，须在独立副本运行。
