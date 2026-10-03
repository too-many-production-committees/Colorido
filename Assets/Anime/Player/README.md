# 玩家帧动画素材放置说明

帧序列尚未加入。把 PNG 序列帧放进下面的目录，然后运行
Unity 菜单 **Tools → Player Animation Setup** → **Full Setup (1 → 2 → 3)**。

## 目录结构（动作名固定，工具按此查找）

    Assets/Anime/Player/Frames/Idle/*.png
    Assets/Anime/Player/Frames/IdleVariant/*.png
    Assets/Anime/Player/Frames/Move/*.png
    Assets/Anime/Player/Frames/Jump/*.png
    Assets/Anime/Player/Frames/Fall/*.png

工具生成（不要手工维护）：

    Assets/Anime/Player/Clips/<动作>.anim
    Assets/Anime/Player/Player.controller

地面移动只有一个 **Move** 状态：走动与奔跑共用同一 Clip，
奔跑不改状态，只提高该状态的播放速度。

待机有两段：**Idle** 与 **IdleVariant**，两者长度可以不同，
进入地面静止时随机选一段，播完一轮再随机选下一段（详见下文「第二待机」）。

## Move 素材来源

工具窗口顶部有 **Move Source** 选择器，列出实际存在的
Move / Walk / Run 目录。Move 的帧**只来自选中的那一组**：

- 不会把 Walk 与 Run 两组帧拼成一段。
- 不会搬运、重命名或删除任何原始素材。
- 若 `Frames/Move/` 不存在而旧目录 `Frames/Walk/` 或 `Frames/Run/` 存在，
  工具会改用其中一组并在日志中说明用了哪一组；可在窗口里改为另一组。
- 旧的 `Clips/Walk.anim`、`Clips/Run.anim` 若存在，会列出提示但不自动删除。

建议把选定的一组复制（而非移动）到 `Frames/Move/` 后再运行，
这样目录名与状态名一致，最不容易混淆。

## 走动 / 奔跑速度

- 走动速度：`PlayerController.moveSpeed`（默认 5）
- 奔跑速度：`PlayerController.runSpeed`（默认 7.5，即 1.5 倍），按住 LeftShift 生效
- Move 动画播放速度：`PlayerAnimationDriver.moveWalkPlaybackSpeed`（默认 1）
  与 `moveRunPlaybackSpeed`（默认 1.5），可在 Inspector 调整

播放速度通过 Animator 的 **MoveSpeed 参数**驱动 Move 状态，
不使用 `Animator.speed`，因此跳跃与待机的播放速度不受影响；
Jump / Fall 状态没有速度参数，空中动画始终保持 1 倍速。

## 步伐节奏（与 Move 动画同步）

`PlayerAnimationDriver.moveStepCurve` 是一条以 **Move 动画归一化周期**为横轴的水平速度倍率曲线：

- 默认取值 0.9 ~ 1.1 的轻微起伏，一个周期的平均值为 1，**不改变平均移动速度**。
- 曲线峰值对应蹬地推进阶段，位置可直接在 Inspector 的曲线上拖动调整；
  一个循环可以有多步（多峰），脚本不做「一圈一步」的假设。
- 走动与奔跑共用这条曲线：相位直接取 Move 动画自身的归一化时间，
  所以奔跑的 1.5 倍播放速度会让节奏自动同步加快，不需要另外设置。
- 只在地面主动移动（处于 Move 状态）时生效；跳跃、坠落、输入暂停时倍率恒为 1。
- 应用方式是缩放喂给现有移动与碰撞逻辑的速度，不逐帧修改位置；
  朝向、动作判定、二段跳、投影吸附都不受影响。
- `moveStepStrength`（0 ~ 1）控制强度：设为 **0 即恢复原来的匀速移动**，
  设为 0.5 则波动减半。
- 上报给 Animator 的 `Speed` 始终是**基础移动速度**（不含节奏波动），
  动画播放速度也只依据走/跑的基础倍率，因此节奏与动画调速不会互相反馈。

Move 的 Clip 建议保持循环（工具默认 Loop），否则归一化时间会停在末尾，节奏也就不再起伏。

## 帧文件命名

- 同一动作目录内的 PNG 按**文件名中的数字自然排序**作为播放顺序，
  因此 `move_1.png, move_2.png, …, move_10.png` 会得到 1,2,…,10 的正确顺序（不是 1,10,2）。
- 同一动作内所有帧的画布尺寸应完全一致；跨动作请保持相同的像素密度与脚底基准。
- 建议每个动作所有帧共用同一个画布尺寸（例如 64×64）与同一脚底高度，
  避免动作切换时人物上下跳动。
- 只放 PNG；目录里的其他文件会被忽略并在校验日志中提示。

## 导入设置（工具自动应用）

- Texture Type = Sprite（Single），Mesh Type = Full Rect
- Filter = Point，Mip Maps 关闭，NPOT = None
- Compression = Uncompressed，Read/Write = On（用于透明边距校验）
- Pivot = 底部中心 (0.5, 0)，使脚底对齐玩家原点
- Pixels Per Unit 由工具窗口中统一指定（默认 100）

## 朝向

默认按**素材朝右**处理（`PlayerAnimationDriver.artFacesRight = true`）。
若素材默认朝左，在玩家物体的 PlayerAnimationDriver 上取消勾选 artFacesRight。

## 校验会报告的内容

每个动作会打印来源目录、帧数、帧序，以及每帧画布尺寸与上/下/左/右透明边距，
并提示画布尺寸不一致或全透明帧，便于在动作切换抖动前发现问题。

## 第二待机（IdleVariant）

两段待机由驱动器**随机**选择，但进入随机切换前有一段缓冲：

- 刚站住（落地或停下）先固定播 **Idle**，不会立刻抽到 IdleVariant。
- 连续**无操作**满 `PlayerAnimationDriver.idleStartDelay` 秒（默认 4 秒）后，
  才开始随机切换：按 `idleVariantChance`（默认 0.5，即各 50%）在 Idle / IdleVariant 之间选择。
- 每次切换都在**当前段完整播放一轮之后**；两段长度可以不同，判定依据是 Animator 的实际播放进度，不是固定秒数。
- 允许连续选中同一段，此时会从头重播。
- 不会每帧随机，也不会在一段未播完时切换。
- 移动、起跳、坠落立即打断待机并**清零无操作计时**；再次站住时重新从 Idle 开始计时。
- **下落过程中不会触发待机**：只有连续贴地若干帧才算站住，所以下落途中擦到地面一帧也不会播待机。
- 镜头旋转、视角切换与第一人称期间沿用现有暂停行为：播放冻结、当前段保持、
  **暂停时间不计入无操作计时**，恢复后继续当前段。

需要把延迟关掉时：`idleStartDelay = 0` 表示站住即随机；`idleVariantChance` 设为 0 或 1 可固定使用其中一段。

## Animator 状态与参数

状态：Idle、IdleVariant、Move、Jump、Fall。

参数：

| 参数 | 类型 | 用途 |
|---|---|---|
| Speed | Float | 水平速度绝对值 |
| VerticalSpeed | Float | 垂直速度（顶点容差用） |
| Grounded | Bool | 是否落地 |
| MoveSpeed | Float | Move 状态的播放速度倍率（1 走动 / 1.5 奔跑） |
| IdleVariantSelected | Bool | 待机段目标：true = IdleVariant，false = Idle |
| IdleSelect | Trigger | 触发一次待机段选择（从头播放） |
| JumpStarted | Trigger | 成功起跳（含二段跳，会重播 Jump） |

待机的两条 AnyState 转换都允许自转换（canTransitionToSelf），因此同一段被再次选中时也会从头重播；
转换条件是「Grounded + Speed 低于阈值 + IdleSelect 触发器 + IdleVariantSelected 取值」，
换段时机由驱动器按播放完成情况决定，Animator 里没有 Exit Time 自动返回。
