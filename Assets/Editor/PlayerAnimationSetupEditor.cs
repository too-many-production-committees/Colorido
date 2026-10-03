using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 玩家帧动画一键配置工具。
/// 菜单：Tools → Player Animation Setup。
/// - 校验 Assets/Anime/Player/Frames/&lt;动作&gt;/ 下的 PNG 序列帧（数字自然排序）
/// - 统一像素画导入设置（Sprite、Point、无 mipmap、无压缩、统一 PPU、脚底基准 pivot）
/// - 生成五组 AnimationClip 与 Player.controller（重复执行保留 GUID、不产生重复状态）
/// - 为选中的玩家配置 PlayerBillboardVisual 动画模式、AnimatedSprite 子节点、
///   PlayerAnimationDriver、遮挡脚本 playerRenderer 引用与脚本执行顺序。
/// 地面移动只使用一个 Move 状态（走动与奔跑共用同一 Clip，靠 MoveSpeed 参数调速）。
/// </summary>
public class PlayerAnimationSetupEditor : EditorWindow
{
    const string FramesRoot = "Assets/Anime/Player/Frames";
    const string ClipsRoot = "Assets/Anime/Player/Clips";
    const string ControllerPath = "Assets/Anime/Player/Player.controller";

    [Serializable]
    class ActionConfig
    {
        public string name;
        public float fps = 12f;
        public bool loop;
    }

    // Move 的素材来源候选。只使用选中的一组，绝不拼接两组帧。
    static readonly string[] MoveSourceCandidates = { "Move", "Walk", "Run" };

    // 早期版本生成过的动作资源，仅用于提示，不自动删除。
    static readonly string[] LegacyActionNames = { "Walk", "Run" };

    int pixelsPerUnit = 100;
    float apexTolerance = 0.35f;
    float speedThreshold = 0.05f;
    string moveFrameSource = "Move";
    bool autoFootBaseline = true;
    int footBaselinePixels = 0;
    Vector2 scroll;
    readonly List<string> logLines = new List<string>();
    readonly Dictionary<string, int> frameCounts = new Dictionary<string, int>();
    readonly Dictionary<string, FrameMetrics> metricsCache = new Dictionary<string, FrameMetrics>();

    readonly List<ActionConfig> actions = new List<ActionConfig>
    {
        new ActionConfig { name = "Idle", fps = 8f, loop = true },
        new ActionConfig { name = "IdleVariant", fps = 8f, loop = false },
        // Move 只有一个 Clip：走动 1 倍速播放，奔跑由 MoveSpeed 参数加速。
        new ActionConfig { name = "Move", fps = 24f, loop = true },
        new ActionConfig { name = "Jump", fps = 12f, loop = false },
        new ActionConfig { name = "Fall", fps = 12f, loop = false },
    };

    [Serializable]
    class SavedSettings
    {
        public int version = 1;
        public int pixelsPerUnit = 100;
        public float apexTolerance = 0.35f;
        public float speedThreshold = 0.05f;
        public string moveFrameSource = "Move";
        public bool autoFootBaseline = true;
        public int footBaselinePixels;
        public List<ActionConfig> actions = new List<ActionConfig>();
    }

    static string SettingsPath => Path.GetFullPath(
        Path.Combine(Application.dataPath, "../UserSettings/PlayerAnimationSetup.json"));
    bool settingsLoaded;
    string lastSavedSettings;

    void OnEnable()
    {
        settingsLoaded = false;
        lastSavedSettings = null;
        if (File.Exists(SettingsPath))
        {
            try
            {
                string json = File.ReadAllText(SettingsPath);
                SavedSettings saved = JsonUtility.FromJson<SavedSettings>(json);
                if (saved == null || saved.version != 1 || saved.actions == null)
                    throw new InvalidDataException("配置格式或版本无效。");
                pixelsPerUnit = saved.pixelsPerUnit;
                apexTolerance = saved.apexTolerance;
                speedThreshold = saved.speedThreshold;
                moveFrameSource = string.IsNullOrEmpty(saved.moveFrameSource) ? "Move" : saved.moveFrameSource;
                autoFootBaseline = saved.autoFootBaseline;
                footBaselinePixels = saved.footBaselinePixels;
                foreach (ActionConfig action in actions)
                {
                    ActionConfig entry = saved.actions.FirstOrDefault(a => a != null && a.name == action.name);
                    if (entry == null) continue;
                    action.fps = entry.fps;
                    action.loop = entry.loop;
                }
                settingsLoaded = true;
                lastSavedSettings = SerializeSettings();
            }
            catch (Exception exception)
            {
                // 不覆盖损坏的文件，避免丢失用户配置。
                Debug.LogError($"[PlayerAnimationSetup] 无法读取配置 {SettingsPath}: {exception.Message}");
            }
            return;
        }

        // 首次迁移：保留已经生成的 Clip 的实际帧率和循环设置。
        foreach (ActionConfig action in actions)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsRoot}/{action.name}.anim");
            if (clip == null) continue;
            action.fps = clip.frameRate;
            action.loop = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
        }
        ResolveMoveSource();
        string firstFrame = CollectFramePaths(moveFrameSource)?.FirstOrDefault();
        if (!string.IsNullOrEmpty(firstFrame) && AssetImporter.GetAtPath(firstFrame) is TextureImporter importer)
            pixelsPerUnit = Mathf.Max(1, Mathf.RoundToInt(importer.spritePixelsPerUnit));
        settingsLoaded = true;
        SaveSettings();
    }

    void OnDisable()
    {
        SaveSettings();
    }

    string SerializeSettings()
    {
        return JsonUtility.ToJson(new SavedSettings
        {
            pixelsPerUnit = pixelsPerUnit,
            apexTolerance = apexTolerance,
            speedThreshold = speedThreshold,
            moveFrameSource = moveFrameSource,
            autoFootBaseline = autoFootBaseline,
            footBaselinePixels = footBaselinePixels,
            actions = actions
        }, true);
    }

    void SaveSettings()
    {
        if (!settingsLoaded) return;
        string json = SerializeSettings();
        if (json == lastSavedSettings) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(SettingsPath))
                File.Replace(temporaryPath, SettingsPath, null);
            else
                File.Move(temporaryPath, SettingsPath);
            lastSavedSettings = json;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PlayerAnimationSetup] 无法保存配置: {exception.Message}");
        }
    }

    // 单帧几何信息（像素），用于脚底对齐与显示尺寸推导。
    struct FrameMetrics
    {
        public int frameCount;
        public int canvasWidth;
        public int canvasHeight;
        public bool canvasConsistent;
        public int minBottomMargin;
        public int maxBottomMargin;
        public int modeBottomMargin;
        public int maxHeadAboveCanvasBottom;
        public int maxContentHeight;
        public int minContentWidth;
        public int maxContentWidth;
        public int belowBaselineCount;
        public string belowBaselineSample;
        public int fullTransparentCount;
        public bool readable;
    }

    [MenuItem("Tools/Player Animation Setup")]
    static void Open()
    {
        GetWindow<PlayerAnimationSetupEditor>("Player Animation Setup");
    }

    void OnGUI()
    {
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play Mode 下不可用，请退出 Play Mode。", MessageType.Warning);
            return;
        }

        EditorGUI.BeginChangeCheck();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Sprite Import", EditorStyles.boldLabel);
        pixelsPerUnit = EditorGUILayout.IntField("Pixels Per Unit", pixelsPerUnit);
        autoFootBaseline = EditorGUILayout.Toggle("Auto Foot Baseline", autoFootBaseline);
        if (!autoFootBaseline)
            footBaselinePixels = EditorGUILayout.IntField("Foot Baseline (px)", footBaselinePixels);
        EditorGUILayout.HelpBox(
            "脚底基准 = 画布底边到地面线的像素数，同一个值应贯穿所有动作，" +
            "这样切换动作时人物不会上下跳动。自动模式取该动作出现最多的下边距（腾空帧仍自然抬起）。",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        foreach (ActionConfig action in actions)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(action.name, GUILayout.Width(120f));
            action.fps = EditorGUILayout.FloatField("FPS", action.fps);
            action.loop = EditorGUILayout.Toggle("Loop", action.loop);

            frameCounts.TryGetValue(action.name, out int count);
            string countLabel = count > 0
                ? $"{count} 帧 / {count / Mathf.Max(0.01f, action.fps):0.00}s"
                : "no frames";
            GUILayout.Label(countLabel, GUILayout.Width(150f));
            EditorGUILayout.EndHorizontal();

            if (action.name == "Move")
                DrawMoveSourceSelector();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Controller", EditorStyles.boldLabel);
        apexTolerance = EditorGUILayout.FloatField("Apex Speed Tolerance", apexTolerance);
        speedThreshold = EditorGUILayout.FloatField("Move Speed Threshold", speedThreshold);
        EditorGUILayout.HelpBox(
            "Move 状态用 MoveSpeed 参数调速（走动 1、奔跑 1.5，在 PlayerAnimationDriver 上调整），" +
            "不使用 Animator.speed，跳跃与待机播放速度不受影响。\n" +
            "待机两段 Idle / IdleVariant 由驱动器随机选择：进入静止时随机一次，播完一轮再随机，" +
            "每次选择都从头播放（同一段被再次选中也会重播），IdleVariant 建议保持不循环。\n" +
            "Loop 勾选会写入 Clip。Fall 默认不循环：长距离下落时停在最后一帧；" +
            "若素材本身是多帧循环坠落，请勾选 Fall 的 Loop。",
            MessageType.Info);

        if (EditorGUI.EndChangeCheck())
            SaveSettings();
        EditorGUILayout.LabelField("设置自动保存到 UserSettings/PlayerAnimationSetup.json", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        if (GUILayout.Button("1. Validate Frames"))
            ValidateFrames();

        if (GUILayout.Button("2. Build Clips + Controller"))
            BuildClipsAndController();

        if (GUILayout.Button("3. Configure Selected Player"))
            ConfigureSelectedPlayer();

        if (GUILayout.Button("Full Setup (1 → 2 → 3)"))
        {
            ValidateFrames();
            BuildClipsAndController();
            ConfigureSelectedPlayer();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
        foreach (string line in logLines.TakeLast(200))
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.EndScrollView();
        SaveSettings();
    }

    // ------------------------------------------------------------------
    // 1. 校验与导入设置
    // ------------------------------------------------------------------

    void DrawMoveSourceSelector()
    {
        // 只列出真有帧的目录，避免把空的 Walk/ 之类选成来源。
        List<string> available = MoveSourceCandidates
            .Where(candidate => CountFrameFiles(candidate) > 0)
            .ToList();

        if (available.Count == 0)
        {
            EditorGUILayout.LabelField("Move Source", "尚无含帧的 Move / Walk / Run 目录");
            return;
        }

        int current = Mathf.Max(0, available.IndexOf(moveFrameSource));
        int selected = EditorGUILayout.Popup("Move Source", current, available.ToArray());
        if (available[selected] != moveFrameSource)
        {
            moveFrameSource = available[selected];
            Log($"Move 素材来源切换为 Frames/{moveFrameSource}/（{CountFrameFiles(moveFrameSource)} 帧）。");
        }
    }

    // Move 的帧只来自选定的那一组目录；Walk 与 Run 不会自动合并。
    void ResolveMoveSource()
    {
        if (CountFrameFiles(moveFrameSource) > 0)
            return;

        foreach (string candidate in MoveSourceCandidates)
        {
            if (CountFrameFiles(candidate) <= 0)
                continue;

            Log($"警告：Frames/{moveFrameSource}/ 没有帧文件，Move 改用 Frames/{candidate}/（{CountFrameFiles(candidate)} 帧）。" +
                "如需其它来源请在窗口顶部的 Move Source 中选择。");
            moveFrameSource = candidate;
            return;
        }
    }

    int CountFrameFiles(string folderName)
    {
        List<string> paths = CollectFramePaths(folderName);
        return paths?.Count ?? 0;
    }

    string GetFrameFolder(ActionConfig action)
    {
        return action.name == "Move" ? moveFrameSource : action.name;
    }

    string GetFrameFolderByName(string actionName)
    {
        return actionName == "Move" ? moveFrameSource : actionName;
    }

    // 显示尺寸的参考动作：优先 Idle（姿态最接近站立高度），其次 Move，最后任意有帧的动作。
    string PickReferenceAction()
    {
        foreach (string preferred in new[] { "Idle", "Move" })
        {
            if (CountFrameFiles(GetFrameFolderByName(preferred)) > 0)
                return preferred;
        }

        foreach (ActionConfig action in actions)
        {
            if (CountFrameFiles(GetFrameFolder(action)) > 0)
                return action.name;
        }

        return null;
    }

    void ValidateFrames()
    {
        ClearLog();
        metricsCache.Clear();

        if (GraphicsSettings.currentRenderPipeline != null)
            Log("警告：检测到 SRP 渲染管线。本工具按内置管线选择 Sprites/Default 材质，请确认兼容。");
        else
            Log("渲染管线：内置 Built-in，SpriteRenderer 将使用默认 Sprites/Default（透明、无光照）。");

        ResolveMoveSource();
        if (moveFrameSource != "Move")
            Log($"Move 素材来源为 Frames/{moveFrameSource}/：只使用这一组，不与其它目录拼接，" +
                "也不搬运或删除原素材。");

        foreach (ActionConfig action in actions)
        {
            string folder = GetFrameFolder(action);
            List<string> paths = CollectFramePaths(folder);
            frameCounts[action.name] = paths?.Count ?? 0;

            if (paths == null || paths.Count == 0)
            {
                Log($"[{action.name}] 未找到帧文件：{FramesRoot}/{folder}/（跳过）");
                continue;
            }

            if (ListNonPngFiles(folder, out string nonPng))
                Log($"[{action.name}] 警告：{FramesRoot}/{folder}/ 中存在非 PNG 文件将被忽略：{nonPng}");

            Log($"[{action.name}] 来源 Frames/{folder}/，{paths.Count} 帧，播放顺序：" +
                DescribeFrameOrder(paths));

            FrameMetrics metrics = PrepareActionFrames(action.name, folder, paths, logReport: true);
            metricsCache[action.name] = metrics;
        }

        ReportLegacyAssets();
        Log("校验完成。请确认素材默认朝向是否朝右（驱动器的 artFacesRight）。");
    }

    string DescribeFrameOrder(List<string> paths)
    {
        const int preview = 12;
        IEnumerable<string> names = paths.Take(preview).Select(Path.GetFileNameWithoutExtension);
        string joined = string.Join(", ", names);
        return paths.Count > preview ? $"{joined}, …（共 {paths.Count} 帧）" : joined;
    }

    void ReportLegacyAssets()
    {
        foreach (string legacy in LegacyActionNames)
        {
            if (legacy == moveFrameSource)
                continue;

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsRoot}/{legacy}.anim") != null)
                Log($"提示：Clips/{legacy}.anim 已不再被 Player.controller 引用，可按需手动删除" +
                    "（工具不会自动删除素材或已生成的 Clip）。");

            if (AssetDatabase.IsValidFolder($"{FramesRoot}/{legacy}"))
                Log($"提示：Frames/{legacy}/ 保留原样，未被使用、未被修改。");
        }
    }

    List<string> CollectFramePaths(string folderName)
    {
        string folder = $"{FramesRoot}/{folderName}";
        if (!AssetDatabase.IsValidFolder(folder))
            return null;

        string fullFolder = Path.GetFullPath(folder);
        if (!Directory.Exists(fullFolder))
            return null;

        return Directory.GetFiles(fullFolder)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OrderBy(f => f, Comparer<string>.Create(NaturalCompare))
            .Select(f => $"{folder}/{f}")
            .ToList();
    }

    bool ListNonPngFiles(string folderName, out string sample)
    {
        sample = null;
        string folder = Path.GetFullPath($"{FramesRoot}/{folderName}");
        if (!Directory.Exists(folder))
            return false;

        sample = Directory.GetFiles(folder)
            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .FirstOrDefault();

        return sample != null;
    }

    void ApplySpriteImportSettings(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Log($"错误：{assetPath} 不是纹理资产。");
            return;
        }

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureType = TextureImporterType.Sprite;
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.filterMode = FilterMode.Point;
        settings.mipmapEnabled = false;
        settings.npotScale = TextureImporterNPOTScale.None;
        settings.alphaIsTransparency = true;
        settings.wrapMode = TextureWrapMode.Clamp;
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, 0f);
        importer.SetTextureSettings(settings);

        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    void ApplySpriteImportSettings(string assetPath, float pivotY)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Log($"错误：{assetPath} 不是纹理资产。");
            return;
        }

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureType = TextureImporterType.Sprite;
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.filterMode = FilterMode.Point;
        settings.mipmapEnabled = false;
        settings.npotScale = TextureImporterNPOTScale.None;
        settings.alphaIsTransparency = true;
        settings.wrapMode = TextureWrapMode.Clamp;
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, Mathf.Clamp(pivotY, 0f, 1f));
        importer.SetTextureSettings(settings);

        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    // 两遍导入：先设为可读并测量透明边距，再按地面线写入每帧 pivot。
    // pivot 的 X 固定为画布中心，保留素材原有的水平位移，不做逐帧横向对齐。
    FrameMetrics PrepareActionFrames(string action, string folder, List<string> paths, bool logReport)
    {
        foreach (string path in paths)
            ApplySpriteImportSettings(path, 0f);

        FrameMetrics metrics = MeasureFrames(paths);
        int baseline = ResolveBaseline(metrics);

        if (metrics.readable)
        {
            metrics.belowBaselineCount = 0;
            metrics.belowBaselineSample = null;
            foreach (string path in paths)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || !texture.isReadable)
                    continue;

                int bottomMargin = GetBottomMargin(texture);
                if (bottomMargin < 0)
                    continue;

                if (bottomMargin < baseline)
                {
                    metrics.belowBaselineCount++;
                    if (metrics.belowBaselineSample == null)
                        metrics.belowBaselineSample = Path.GetFileName(path);
                }

                ApplySpriteImportSettings(path, baseline / (float)texture.height);
            }
        }

        if (logReport)
            LogFrameReport(action, folder, paths, metrics, baseline);

        return metrics;
    }

    int ResolveBaseline(FrameMetrics metrics)
    {
        if (metrics.maxBottomMargin <= 0)
            return 0;

        if (!autoFootBaseline && footBaselinePixels > 0)
            return footBaselinePixels;

        return metrics.modeBottomMargin;
    }

    FrameMetrics MeasureFrames(List<string> paths)
    {
        FrameMetrics metrics = new FrameMetrics
        {
            frameCount = paths.Count,
            canvasConsistent = true,
            minBottomMargin = int.MaxValue,
            minContentWidth = int.MaxValue,
            readable = true
        };

        Dictionary<int, int> bottomMarginCounts = new Dictionary<int, int>();

        foreach (string path in paths)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || !texture.isReadable)
            {
                metrics.readable = false;
                continue;
            }

            if (metrics.canvasWidth == 0)
            {
                metrics.canvasWidth = texture.width;
                metrics.canvasHeight = texture.height;
            }
            else if (texture.width != metrics.canvasWidth || texture.height != metrics.canvasHeight)
            {
                metrics.canvasConsistent = false;
            }

            Color32[] pixels = texture.GetPixels32();
            int minX = texture.width, maxX = -1, minY = texture.height, maxY = -1;
            for (int y = 0; y < texture.height; y++)
            {
                int row = y * texture.width;
                for (int x = 0; x < texture.width; x++)
                {
                    if (pixels[row + x].a == 0)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0)
            {
                metrics.fullTransparentCount++;
                continue;
            }

            // Unity 纹理像素自下往上排列：minY 即内容下方空行数（下边距）。
            int bottomMargin = minY;
            int contentHeight = maxY - minY + 1;
            int contentWidth = maxX - minX + 1;

            metrics.minBottomMargin = Mathf.Min(metrics.minBottomMargin, bottomMargin);
            metrics.maxBottomMargin = Mathf.Max(metrics.maxBottomMargin, bottomMargin);
            metrics.maxHeadAboveCanvasBottom = Mathf.Max(metrics.maxHeadAboveCanvasBottom, maxY + 1);
            metrics.maxContentHeight = Mathf.Max(metrics.maxContentHeight, contentHeight);
            metrics.minContentWidth = Mathf.Min(metrics.minContentWidth, contentWidth);
            metrics.maxContentWidth = Mathf.Max(metrics.maxContentWidth, contentWidth);

            bottomMarginCounts.TryGetValue(bottomMargin, out int seen);
            bottomMarginCounts[bottomMargin] = seen + 1;
        }

        if (metrics.minBottomMargin == int.MaxValue)
            metrics.minBottomMargin = 0;

        if (metrics.minContentWidth == int.MaxValue)
            metrics.minContentWidth = 0;

        // 取出现最多的下边距作地面线；并列时取较大者，避免把整段动画压到地面以下。
        metrics.modeBottomMargin = bottomMarginCounts.Count == 0
            ? 0
            : bottomMarginCounts
                .OrderByDescending(pair => pair.Value)
                .ThenByDescending(pair => pair.Key)
                .First().Key;

        return metrics;
    }

    int GetBottomMargin(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();
        for (int y = 0; y < texture.height; y++)
        {
            int row = y * texture.width;
            for (int x = 0; x < texture.width; x++)
            {
                if (pixels[row + x].a != 0)
                    return y;
            }
        }

        return -1;
    }

    void LogFrameReport(string action, string folder, List<string> paths, FrameMetrics metrics, int baseline)
    {
        if (!metrics.readable)
        {
            Log($"[{action}] 警告：帧纹理不可读，跳过透明边距分析（无法自动对齐脚底）。");
            return;
        }

        if (metrics.fullTransparentCount > 0)
            Log($"[{action}] 警告：{metrics.fullTransparentCount} 帧全透明，请检查素材。");

        if (!metrics.canvasConsistent)
            Log($"[{action}] 警告：各帧画布尺寸不一致（基准 {metrics.canvasWidth}x{metrics.canvasHeight}），" +
                "动作切换会抖动；请统一画布尺寸后重跑校验。");

        Log($"[{action}] 画布 {metrics.canvasWidth}x{metrics.canvasHeight}，内容 " +
            $"{metrics.minContentWidth}..{metrics.maxContentWidth} x {metrics.maxContentHeight} px（高按最高帧计）");
        Log($"[{action}] 下边距 {metrics.minBottomMargin}..{metrics.maxBottomMargin} px（出现最多 {metrics.modeBottomMargin}），" +
            $"脚底基准取 {baseline} px = {(autoFootBaseline && footBaselinePixels <= 0 ? "自动" : "手动")}");

        float baselineWorld = baseline / (float)Mathf.Max(1, pixelsPerUnit);
        Log($"[{action}] 地面线位于画布底边上方 {baselineWorld:0.###} 世界单位（PPU {pixelsPerUnit}）");

        if (metrics.belowBaselineCount > 0)
            Log($"[{action}] 提示：{metrics.belowBaselineCount} 帧最低点低于地面线（例如 {metrics.belowBaselineSample}）。" +
                "若那是真正的触地帧，请把 Foot Baseline 设为该动作的最小下边距。");
        else if (metrics.minBottomMargin > baseline && baseline > 0)
            Log($"[{action}] 提示：所有帧都悬空 {metrics.minBottomMargin - baseline} px 以上，" +
                "请确认脚底基准是否偏小。");

        int headAboveBaseline = metrics.maxHeadAboveCanvasBottom - baseline;
        if (headAboveBaseline > 0)
            Log($"[{action}] 最高点在地面线上方 {headAboveBaseline} px = " +
                $"{headAboveBaseline / (float)Mathf.Max(1, pixelsPerUnit):0.###} 世界单位" +
                "（Configure 步骤据此写入 PlayerBillboardVisual.animatedSpriteWorldHeight）");

        if (paths.Count <= 12)
        {
            foreach (string path in paths)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || !texture.isReadable)
                    continue;

                Log($"[{action}] {Path.GetFileName(path)}: {texture.width}x{texture.height}，" +
                    $"下边距 {GetBottomMargin(texture)} px");
            }
        }
    }

    // ------------------------------------------------------------------
    // 2. Clips + Controller
    // ------------------------------------------------------------------

    void BuildClipsAndController()
    {
        ClearLog();

        EnsureFolder(ClipsRoot);

        ResolveMoveSource();

        Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        foreach (ActionConfig action in actions)
        {
            string folder = GetFrameFolder(action);
            List<string> paths = CollectFramePaths(folder);
            if (paths == null || paths.Count == 0)
            {
                Log($"[{action.name}] 无帧文件（{FramesRoot}/{folder}/），跳过 Clip 生成。");
                continue;
            }

            // 按地面线基准导入后取精灵，pivot 决定砍脚位置与贴地高度。
            FrameMetrics metrics = metricsCache.TryGetValue(action.name, out FrameMetrics cached)
                ? cached
                : PrepareActionFrames(action.name, folder, paths, logReport: false);
            metricsCache[action.name] = metrics;

            List<Sprite> sprites = new List<Sprite>();
            foreach (string path in paths)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    Log($"[{action.name}] 错误：{path} 未导入为 Sprite，跳过该动作。");
                    sprites.Clear();
                    break;
                }

                sprites.Add(sprite);
            }

            if (sprites.Count == 0)
                continue;

            clips[action.name] = BuildClip(action, sprites);
        }

        if (clips.Count == 0)
        {
            Log("没有生成任何 Clip：请先在 Assets/Anime/Player/Frames/<动作>/ 放入 PNG 序列帧。");
            return;
        }

        BuildController(clips);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Clip 与 Controller 构建完成。");
    }

    AnimationClip BuildClip(ActionConfig action, List<Sprite> sprites)
    {
        string clipPath = $"{ClipsRoot}/{action.name}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        bool isNew = clip == null;

        if (isNew)
            clip = new AnimationClip { name = action.name };
        else
            AnimationUtility.SetObjectReferenceCurve(clip, GetSpriteBinding(), null);

        float fps = Mathf.Max(0.01f, action.fps);
        clip.frameRate = fps;

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[sprites.Count + 1];
        for (int i = 0; i < sprites.Count; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = sprites[i] };

        // 末尾补一帧同精灵，保证最后一帧按时长完整显示。
        keys[sprites.Count] = new ObjectReferenceKeyframe
        {
            time = sprites.Count / fps,
            value = sprites[sprites.Count - 1]
        };

        AnimationUtility.SetObjectReferenceCurve(clip, GetSpriteBinding(), keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0f;
        settings.stopTime = sprites.Count / fps;
        settings.loopTime = action.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (isNew)
            AssetDatabase.CreateAsset(clip, clipPath);
        else
            EditorUtility.SetDirty(clip);

        string sourceNote = action.name == "Move" ? $"，来源 Frames/{GetFrameFolder(action)}/" : string.Empty;
        Log($"[{action.name}] Clip: {clipPath}，{sprites.Count} 帧 @ {fps:0.#}fps，Loop={action.loop}{sourceNote}（{(isNew ? "新建" : "复用现有资源，GUID 不变）")}");
        return clip;
    }

    EditorCurveBinding GetSpriteBinding()
    {
        // Animator 与 SpriteRenderer 同挂在 AnimatedSprite 节点上，路径为空。
        return EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
    }

    void BuildController(Dictionary<string, AnimationClip> clips)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        bool isNew = controller == null;

        if (isNew)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // 清空旧参数与状态，重复执行不产生重复内容（资源文件与 GUID 保持不变）。
        for (int i = controller.parameters.Length - 1; i >= 0; i--)
            controller.RemoveParameter(controller.parameters[i]);

        AddParameter(controller, "Speed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "VerticalSpeed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "Grounded", AnimatorControllerParameterType.Bool);
        // Move 状态的播放速度倍率：走动 1、奔跑 1.5（由 PlayerAnimationDriver 写入）。
        AddParameter(controller, "MoveSpeed", AnimatorControllerParameterType.Float);
        // 待机段随机选择：IdleVariantSelected 指定目标段，IdleSelect 触发一次选择。
        AddParameter(controller, "IdleVariantSelected", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "IdleSelect", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "JumpStarted", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        // 显式清空旧内容，重复执行不产生重复状态或过渡（资源文件与 GUID 保持不变）。
        ChildAnimatorState[] existingStates = stateMachine.states;
        for (int i = 0; i < existingStates.Length; i++)
            stateMachine.RemoveState(existingStates[i].state);

        AnimatorStateTransition[] existingAnyState = stateMachine.anyStateTransitions;
        for (int i = 0; i < existingAnyState.Length; i++)
            stateMachine.RemoveAnyStateTransition(existingAnyState[i]);

        AnimatorTransition[] existingEntries = stateMachine.entryTransitions;
        for (int i = 0; i < existingEntries.Length; i++)
            stateMachine.RemoveEntryTransition(existingEntries[i]);

        ChildAnimatorStateMachine[] existingSubMachines = stateMachine.stateMachines;
        for (int i = 0; i < existingSubMachines.Length; i++)
            stateMachine.RemoveStateMachine(existingSubMachines[i].stateMachine);

        stateMachine.defaultState = null;

        Dictionary<string, AnimatorState> states = new Dictionary<string, AnimatorState>();
        int index = 0;
        foreach (ActionConfig action in actions)
        {
            if (!clips.TryGetValue(action.name, out AnimationClip motion))
                continue;

            Vector2 position = new Vector2(260f * index, 60f);
            AnimatorState state = stateMachine.AddState(action.name, position);
            state.motion = motion;
            state.speed = 1f;
            states[action.name] = state;
            index++;
        }

        if (!states.TryGetValue("Idle", out AnimatorState idle))
        {
            Log("错误：缺少 Idle 状态（Idle 帧缺失），无法构建 Controller。");
            return;
        }

        // AnyState 出边按数组顺序求值，顺序即优先级。
        if (states.TryGetValue("Jump", out AnimatorState jump))
        {
            // 1. 成功起跳：触发器优先，且允许自转换让二段跳重新播放。
            AnimatorStateTransition jumpByTrigger = stateMachine.AddAnyStateTransition(jump);
            ConfigureImmediate(jumpByTrigger);
            jumpByTrigger.canTransitionToSelf = true;
            jumpByTrigger.AddCondition(AnimatorConditionMode.If, 0f, "JumpStarted");

            // 2. 离地且向上（如土狼时间内起跳、走上斜坡等）。
            AnimatorStateTransition jumpRising = stateMachine.AddAnyStateTransition(jump);
            ConfigureImmediate(jumpRising);
            jumpRising.canTransitionToSelf = false;
            jumpRising.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            jumpRising.AddCondition(AnimatorConditionMode.Greater, apexTolerance, "VerticalSpeed");
        }

        if (states.TryGetValue("Move", out AnimatorState move))
        {
            // 3. 落地且有水平移动：走动与奔跑共用这一个状态，只靠 MoveSpeed 参数改播放速度；
            // 不动 Animator.speed，跳跃与待机的播放速度不受影响。
            move.speed = 1f;
            move.speedParameterActive = true;
            move.speedParameter = "MoveSpeed";

            AnimatorStateTransition toMove = stateMachine.AddAnyStateTransition(move);
            ConfigureImmediate(toMove);
            toMove.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            toMove.AddCondition(AnimatorConditionMode.Greater, speedThreshold, "Speed");
        }

        if (states.TryGetValue("Fall", out AnimatorState fall))
        {
            // 5. 离地且向下（顶点容差由驱动器与容差条件共同保证不抖动）。
            AnimatorStateTransition toFall = stateMachine.AddAnyStateTransition(fall);
            ConfigureImmediate(toFall);
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            toFall.AddCondition(AnimatorConditionMode.Less, -apexTolerance, "VerticalSpeed");
        }

        // 待机入口不在这里：Move/Jump/Fall 都不直接连 Idle，
        // 而是由驱动器在落地静止时触发 IdleSelect，随机进入 Idle 或 IdleVariant。

        stateMachine.defaultState = idle;
        // 每次选择都从头播放（canTransitionToSelf 让同一段被再次选中时也能重播），
        // 没有定时触发，也没有自动返回 Idle：换段时机由驱动器按实际播放完成情况决定。
        if (states.TryGetValue("Idle", out AnimatorState idleState))
        {
            AnimatorStateTransition toIdle = stateMachine.AddAnyStateTransition(idleState);
            ConfigureImmediate(toIdle);
            toIdle.canTransitionToSelf = true;
            toIdle.AddCondition(AnimatorConditionMode.If, 0f, "IdleSelect");
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IdleVariantSelected");
            toIdle.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            toIdle.AddCondition(AnimatorConditionMode.Less, speedThreshold, "Speed");
        }

        if (states.TryGetValue("IdleVariant", out AnimatorState idleVariant))
        {
            AnimatorStateTransition toVariant = stateMachine.AddAnyStateTransition(idleVariant);
            ConfigureImmediate(toVariant);
            toVariant.canTransitionToSelf = true;
            toVariant.AddCondition(AnimatorConditionMode.If, 0f, "IdleSelect");
            toVariant.AddCondition(AnimatorConditionMode.If, 0f, "IdleVariantSelected");
            toVariant.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            toVariant.AddCondition(AnimatorConditionMode.Less, speedThreshold, "Speed");
        }

        stateMachine.defaultState = idle;

        EditorUtility.SetDirty(controller);
        Log($"Controller: {ControllerPath}（{(isNew ? "新建" : "复用现有资源，GUID 不变）")}，" +
            $"参数 6 个，状态 {states.Count} 个，默认态 Idle；待机段由 IdleSelect 触发器随机选择");
    }

    void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter parameter = new AnimatorControllerParameter
        {
            name = name,
            type = type
        };

        switch (type)
        {
            case AnimatorControllerParameterType.Bool:
                parameter.defaultBool = false;
                break;
            case AnimatorControllerParameterType.Float:
                parameter.defaultFloat = 0f;
                break;
        }

        controller.AddParameter(parameter);
    }

    void ConfigureImmediate(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.duration = 0f;
        transition.offset = 0f;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.canTransitionToSelf = false;
    }

    // ------------------------------------------------------------------
    // 3. 配置场景中的玩家
    // ------------------------------------------------------------------

    void ConfigureSelectedPlayer()
    {
        ClearLog();

        if (Application.isPlaying)
            return;

        PlayerController playerController = ResolveTargetPlayer();
        if (playerController == null)
        {
            Log("错误：未找到玩家。请选中带 PlayerController 的物体，或确认场景中存在玩家。");
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Log("错误：Player.controller 不存在，请先执行「Build Clips + Controller」。");
            return;
        }

        Undo.IncrementCurrentGroup();
        string undoName = "Configure Player Animation";

        PlayerBillboardVisual visual = playerController.GetComponent<PlayerBillboardVisual>();
        if (visual == null)
            visual = Undo.AddComponent<PlayerBillboardVisual>(playerController.gameObject);

        Undo.RecordObject(visual, undoName);
        visual.animationController = controller;

        // 让 billboard 节点与 AnimatedSprite 子节点就位（幂等）。
        visual.EnsureVisual();
        visual.ApplySettings();

        SpriteRenderer spriteRenderer = visual.AnimatedSpriteRenderer;
        if (spriteRenderer == null)
        {
            Log("错误：AnimatedSprite 子节点创建失败。");
            return;
        }

        // 初始精灵与显示基准取「Idle → Move → 任意有帧的动作」的首帧。
        // 基准高度用素材内容高度（地面线到最高点），不使用含透明边距的画布高度，
        // 否则角色会被大片留白缩小。
        string referenceAction = PickReferenceAction();
        if (referenceAction != null)
        {
            List<string> referencePaths = CollectFramePaths(GetFrameFolderByName(referenceAction));
            Sprite referenceFirst = referencePaths != null && referencePaths.Count > 0
                ? AssetDatabase.LoadAssetAtPath<Sprite>(referencePaths[0])
                : null;

            if (referenceFirst != null)
            {
                FrameMetrics metrics = metricsCache.TryGetValue(referenceAction, out FrameMetrics cached)
                    ? cached
                    : PrepareActionFrames(referenceAction, GetFrameFolderByName(referenceAction), referencePaths, logReport: false);
                metricsCache[referenceAction] = metrics;

                int baseline = ResolveBaseline(metrics);
                int headAboveBaseline = metrics.maxHeadAboveCanvasBottom - baseline;
                float contentWorldHeight = headAboveBaseline / (float)Mathf.Max(1, pixelsPerUnit);

                spriteRenderer.sprite = referenceFirst;
                if (contentWorldHeight > 0.0001f)
                {
                    visual.animatedSpriteWorldHeight = contentWorldHeight;
                    Log($"AnimatedSprite 初始精灵：{referenceFirst.name}（{referenceAction}），" +
                        $"内容高度 {contentWorldHeight:0.###} 世界单位 → 显示缩放 {visual.size.y / contentWorldHeight:0.###}" +
                        $"（显示高度按 PlayerBillboardVisual.size.y = {visual.size.y} 换算）。");
                }
                else
                {
                    Log($"警告：{referenceAction} 的内容高度为 0，未写入显示基准高度。");
                }
            }
        }
        else
        {
            Log("提示：尚无任何动作帧，初始精灵与显示尺寸未设置；放入素材并重新执行步骤 1/2 后再跑本步骤。");
        }

        EditorUtility.SetDirty(visual);

        PlayerAnimationDriver driver = playerController.GetComponent<PlayerAnimationDriver>();
        if (driver == null)
            driver = Undo.AddComponent<PlayerAnimationDriver>(playerController.gameObject);

        Undo.RecordObject(driver, undoName);
        driver.playerController = playerController;
        driver.billboardVisual = visual;
        EditorUtility.SetDirty(driver);

        // 遮挡脚本若在场景中，明确绑定 AnimatedSprite 的 SpriteRenderer。
        ProjectionPlayerOcclusionVisualizer[] occluderVisualizers = FindObjectsByType<ProjectionPlayerOcclusionVisualizer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        ProjectionPlayerOcclusionVisualizer occlusion = occluderVisualizers.Length > 0 ? occluderVisualizers[0] : null;
        if (occlusion != null)
        {
            Undo.RecordObject(occlusion, undoName);
            occlusion.playerRenderer = spriteRenderer;
            EditorUtility.SetDirty(occlusion);
            Log($"遮挡脚本 playerRenderer 已绑定 {spriteRenderer.name}。");
        }
        else
        {
            Log("场景中未找到 ProjectionPlayerOcclusionVisualizer，跳过 playerRenderer 绑定。");
        }

        // 脚本执行顺序：控制器 → 驱动器 → 显示脚本。
        SetExecutionOrder(playerController, -50);
        SetExecutionOrder(driver, -40);
        SetExecutionOrder(visual, -30);

        EditorSceneManager.MarkSceneDirty(playerController.gameObject.scene);
        Log($"玩家 {playerController.name} 配置完成（层级：player / player_billboard_visual / AnimatedSprite）。");
    }

    PlayerController ResolveTargetPlayer()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null)
        {
            PlayerController selectedController = selected.GetComponent<PlayerController>();
            if (selectedController != null)
                return selectedController;
        }

        PlayerController[] players = FindObjectsByType<PlayerController>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return players.Length > 0 ? players[0] : null;
    }

    void SetExecutionOrder(Component component, int order)
    {
        if (component == null)
            return;

        if (!(component is MonoBehaviour behaviour))
            return;

        MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
        if (script == null)
            return;

        if (MonoImporter.GetExecutionOrder(script) != order)
        {
            MonoImporter.SetExecutionOrder(script, order);
            Log($"{script.name} 执行顺序 → {order}");
        }
    }

    // ------------------------------------------------------------------
    // 杂项
    // ------------------------------------------------------------------

    void EnsureFolder(string assetFolder)
    {
        string[] parts = assetFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static int NaturalCompare(string a, string b)
    {
        int ia = 0;
        int ib = 0;
        while (ia < a.Length && ib < b.Length)
        {
            if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
            {
                long na = 0;
                long nb = 0;
                while (ia < a.Length && char.IsDigit(a[ia]))
                {
                    na = na * 10 + (a[ia] - '0');
                    ia++;
                }

                while (ib < b.Length && char.IsDigit(b[ib]))
                {
                    nb = nb * 10 + (b[ib] - '0');
                    ib++;
                }

                if (na != nb)
                    return na.CompareTo(nb);
            }
            else
            {
                int cmp = char.ToUpperInvariant(a[ia]).CompareTo(char.ToUpperInvariant(b[ib]));
                if (cmp != 0)
                    return cmp;

                ia++;
                ib++;
            }
        }

        return (a.Length - ia).CompareTo(b.Length - ib);
    }

    void ClearLog()
    {
        logLines.Clear();
    }

    void Log(string message)
    {
        logLines.Add(message);
        Debug.Log($"[PlayerAnimationSetup] {message}");
    }
}
