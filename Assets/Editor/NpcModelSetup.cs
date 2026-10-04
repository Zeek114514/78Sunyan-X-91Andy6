using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 给每个 NPC 的角色模型准备运行时素材：
/// 把模型里需要的剪辑（站立 / 走 / 跑 / 倒地）抽出来，拼成带 Motion 参数的动画状态机，存到 Resources 下供 VoxelNpc 加载。
/// 导入的剪辑若是 Legacy（进不了状态机）会顺手修正；站立 / 走 / 跑 顺手勾上"循环播放"（默认只播一次，走两步就僵住）。
/// 模型换新或导入设置变化后：菜单手点一次，或脚本重载时自动补一次。
///
/// 注意：状态机里保留 Downed（倒地）与 Hit（挨打）两个状态，但 VoxelNpc 目前只驱动 0 站立 / 1 走 / 2 跑 / 3 倒地：
/// 挨打动作会打断走 / 跑循环，暂时停用；倒地播模型自带的倒地剪辑（单次播完停在最后一帧）。
/// 控制器文件被删掉 / 名字对不上时，这里会在脚本重载时自动重建；重建好之前 VoxelNpc 会用方块小人顶着，不会报错。
/// </summary>
public static class NpcModelSetup
{
    /// <summary>一个 NPC 的模型资源</summary>
    sealed class NpcModel
    {
        public string name;                                   // 只用于日志
        public string modelKey;                               // Resources 下的模型（不带扩展名）
        public string controllerKey;                          // 生成的控制器放哪（Resources 下）
        public string[][] states;                             // 状态名 → 候选剪辑名，顺序 = Motion 值
    }

    static readonly NpcModel[] Models =
    {
        new NpcModel
        {
            name = "孙琰",
            modelKey      = "sy/tripo_convert_b7aafa07-ebc1-458c-af70-3fe007883286",
            controllerKey = "NpcSunYan/SunYanController",
            states = new[]
            {
                new[] { "Idle",   "standing_relax", "idle" },
                new[] { "Walk",   "walk" },
                new[] { "Run",    "run" },
                new[] { "Downed", "defeat", "fall" },      // 血量掉到 0 时播的倒下动作
                new[] { "Hit",    "hit_to_body", "hit" },  // 挨打反应（模型没有就自动跳过）
            },
        },
        new NpcModel
        {
            name = "Andy",
            modelKey      = "NpcAndy/Andy",
            controllerKey = "NpcAndy/AndyController",
            states = new[]
            {
                new[] { "Idle",   "idle", "standing_relax" },
                new[] { "Walk",   "walk" },
                new[] { "Run",    "run" },
                new[] { "Downed", "defeat", "fall" },
                new[] { "Hit",    "hit_to_body", "hit" },
            },
        },
    };

    const string AttemptsKey = "NpcModelSetup.Attempts.v2";   // 换过 key：旧控制器要重建成"每个状态的剪辑名都对得上"的版本
    const string ReportedKey = "NpcModelSetup.ClipLoopsReported";

    static string ModelPath(NpcModel m)      => "Assets/Resources/" + m.modelKey + ".fbx";
    static string ControllerPath(NpcModel m) => "Assets/Resources/" + m.controllerKey + ".controller";

    [MenuItem("VoxelDemo/生成 NPC 动画控制器")]
    public static void BuildAll()
    {
        foreach (var model in Models) Build(model);
    }

    static void Build(NpcModel model)
    {
        string modelPath = ModelPath(model), controllerPath = ControllerPath(model);

        if (!File.Exists(modelPath))
        {
            Debug.LogError($"[NpcModelSetup] 找不到 {model.name} 的模型 {modelPath}，确认它已放进 Resources。");
            return;
        }

        EnsureAnimationImportable(model);

        var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().ToList();
        if (clips.Count == 0)
        {
            Debug.LogError($"[NpcModelSetup] {modelPath} 里没有动画剪辑，检查导入设置里动画是否被排除。");
            return;
        }

        var legacy = clips.Where(c => c.legacy).Select(c => c.name).ToList();
        if (legacy.Count > 0)
            Debug.LogWarning($"[NpcModelSetup] {model.name} 的这些剪辑仍是 Legacy，放进状态机不会生效：{string.Join(", ", legacy)}。");

        EnsureClipLoops(model);

        EnsureFolder(Path.GetDirectoryName(controllerPath).Replace('\\', '/'));
        if (File.Exists(controllerPath)) AssetDatabase.DeleteAsset(controllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("Motion", AnimatorControllerParameterType.Int);

        var machine = controller.layers[0].stateMachine;
        var states = new List<AnimatorState>();

        foreach (var pair in model.states)
        {
            // pair[1..] 是候选剪辑名：按顺序找第一个有的（比如倒地优先用 defeat，退而用 fall）
            AnimationClip clip = null;
            for (int i = 1; i < pair.Length && clip == null; i++)
                clip = FindClip(clips, pair[i]);

            if (clip == null)
            {
                Debug.LogWarning($"[NpcModelSetup] {model.name} 没找到剪辑 {string.Join(" / ", pair.Skip(1))}，跳过状态 {pair[0]}。");
                continue;
            }

            var state = machine.AddState(pair[0]);
            state.motion = clip;
            states.Add(state);

            // ★ 站立 / 走 / 跑再补一条"自己转回自己"的过渡（播到片尾就重新开始）：
            //   就算剪辑的"循环播放"没勾上、或者导入设置被改回去，动作也一定是循环的，
            //   不会播一次就僵在原地
            if (LoopsByDefault(pair[0]))
            {
                var loop = state.AddTransition(state);
                loop.hasExitTime   = true;
                loop.exitTime      = 0.99f;
                loop.hasFixedDuration = true;
                loop.duration      = 0f;
            }
        }

        if (states.Count == 0)
        {
            Debug.LogError($"[NpcModelSetup] {model.name} 需要的剪辑一个都没匹配上，控制器没生成。" +
                           $"模型里的剪辑：{string.Join(", ", clips.Select(c => c.name))}");
            return;
        }

        machine.defaultState = states[0];

        // Motion 值一改就切到对应状态：站立 / 走 / 跑 / 倒地
        for (int from = 0; from < states.Count; from++)
        {
            for (int to = 0; to < states.Count; to++)
            {
                if (from == to) continue;

                var transition = states[from].AddTransition(states[to]);
                transition.hasExitTime = false;
                transition.duration    = 0.15f;
                transition.AddCondition(AnimatorConditionMode.Equals, to, "Motion");
            }
        }

        AssetDatabase.SaveAssets();

        // 把"状态 → 实际绑的剪辑 → 是否循环"打进日志，下次直接在 Editor.log 里核对
        var mapping = states.Select(s =>
        {
            var c = s.motion as AnimationClip;
            string loops = LoopsByDefault(s.name) ? "循环" : "单次";
            return $"{s.name} → {(c != null ? c.name : "(空)")}[{loops}]";
        });
        Debug.Log($"[NpcModelSetup] {model.name}：已生成 {controllerPath}；状态映射：{string.Join("，", mapping)}；" +
                  $"模型里的剪辑：{string.Join(", ", clips.Select(c => c.name))}");

        VerifyRuntimeAssets(model);
    }

    /// <summary>把站立 / 走 / 跑 的剪辑勾成"循环播放"，倒地动作保持播一次；
    /// 只在设置真的不一样时才重新导入，免得每次脚本重载都白导一遍几 MB 的模型</summary>
    static void EnsureClipLoops(NpcModel model)
    {
        var importer = AssetImporter.GetAtPath(ModelPath(model)) as ModelImporter;
        if (importer == null) return;

        // meta 里没写过剪辑设置时 clipAnimations 是空的，改用导入器生成的默认列表
        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning($"[NpcModelSetup] 读不到 {model.name} 的剪辑列表，循环播放没法自动设置。");
            return;
        }

        bool changed = false;
        var summary = new List<string>();

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].name.StartsWith("__", System.StringComparison.Ordinal)) continue;   // 导入器的单帧预览剪辑，别管

            int state = StateOf(model, clips[i].name);
            if (state < 0) continue;                                  // 表里没提到的剪辑（jump 等）保持原样

            bool loop = LoopsByDefault(model.states[state][0]);        // 只有 站立 / 走 / 跑 循环，其余播一次
            summary.Add(clips[i].name + (loop ? "=循环" : "=单次"));

            if (clips[i].loopTime == loop) continue;

            clips[i].loopTime = loop;
            changed = true;
        }

        if (!changed)
        {
            // 设置已经对得上时不重复刷日志：每个编辑器会话报一次
            if (SessionState.GetBool(ReportedKey, false)) return;
            SessionState.SetBool(ReportedKey, true);
            Debug.Log($"[NpcModelSetup] {model.name} 的剪辑循环设置已是最新：{string.Join("，", summary)}");
            return;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[NpcModelSetup] {model.name}：已把 站立 / 走 / 跑 改成循环播放，倒地动作保持播一次：" +
                  $"{string.Join("，", summary)}");
    }

    /// <summary>这个剪辑名命中哪个状态（-1 = 表里没有）</summary>
    static int StateOf(NpcModel model, string clipName)
    {
        for (int s = 0; s < model.states.Length; s++)
        {
            if (MatchesName(clipName, model.states[s].Skip(1).ToArray())) return s;
        }
        return -1;
    }

    /// <summary>这个状态的剪辑该不该循环：站立 / 走 / 跑 循环，倒地 / 挨打这些播一次</summary>
    static bool LoopsByDefault(string stateName) =>
        stateName == "Idle" || stateName == "Walk" || stateName == "Run";

    /// <summary>这张表里实际能配上的状态数（模型缺某个动作时会少一个）</summary>
    static int ResolvableStateCount(NpcModel model)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath(model)).OfType<AnimationClip>().ToList();
        int count = 0;

        foreach (var pair in model.states)
        {
            for (int i = 1; i < pair.Length; i++)
            {
                if (FindClip(clips, pair[i]) != null) { count++; break; }
            }
        }
        return count;
    }

    /// <summary>剪辑名命中候选名里的任意一个（和 FindClip 一样的宽松规则）</summary>
    static bool MatchesName(string clipName, string[] candidates)
    {
        string name = clipName.ToLowerInvariant();

        foreach (var candidate in candidates)
        {
            string want = candidate.ToLowerInvariant();
            if (name == want || name.EndsWith(want) || name.Contains(want)) return true;
        }
        return false;
    }

    /// <summary>按运行时的方式加载一遍，确认 VoxelNpc 能拿到模型 / 控制器 / 动画</summary>
    [MenuItem("VoxelDemo/验证 NPC 模型资源")]
    public static void VerifyAll()
    {
        foreach (var model in Models) VerifyRuntimeAssets(model);
    }

    static void VerifyRuntimeAssets(NpcModel model)
    {
        var go = Resources.Load<GameObject>(model.modelKey);
        if (go == null)
        {
            Debug.LogError($"[NpcModelSetup] {model.name} 运行时取不到模型：Resources.Load<GameObject>(\"{model.modelKey}\") 返回空。");
            return;
        }

        var controller = Resources.Load<RuntimeAnimatorController>(model.controllerKey);

        float height = 0f;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            height = bounds.size.y;
        }

        Debug.Log($"[NpcModelSetup] {model.name} 运行时校验：模型=OK（{go.name}），控制器={(controller != null ? "OK" : "缺失")}，" +
                  $"渲染器={renderers.Length}，原始身高={height:0.###}");

        if (controller == null) Debug.LogError($"[NpcModelSetup] {model.name} 取不到动画控制器，会退回方块小人。");
        if (height <= 0f) Debug.LogWarning($"[NpcModelSetup] {model.name} 的模型没有可渲染内容。");
    }

    /// <summary>脚本重载时自动补一次：循环播放没勾上就补勾，控制器缺失、状态对不上、动作是 Legacy/空的就重建</summary>
    [InitializeOnLoadMethod]
    static void AutoBuildIfMissing()
    {
        EditorApplication.delayCall += () =>
        {
            foreach (var model in Models)
            {
                string modelPath = ModelPath(model), controllerPath = ControllerPath(model);
                if (!File.Exists(modelPath)) continue;

                EnsureClipLoops(model);                                        // 设置本来就对时它自己会提前返回
                if (File.Exists(controllerPath) && IsControllerUsable(model)) continue;

                // 每次脚本重载都会走到这里：一次修不好也别反复重建
                string key = AttemptsKey + "." + model.name;
                int attempts = SessionState.GetInt(key, 0);
                if (attempts >= 3) continue;

                SessionState.SetInt(key, attempts + 1);
                Build(model);

                if (IsControllerUsable(model)) SessionState.SetInt(key, 0);
            }
        };
    }

    /// <summary>控制器存在、状态数对得上、每个状态绑的剪辑名字也对得上，才算能用。
    /// 状态数要按"模型里实际找得到的动作"算，缺动作的 NPC（比如孙琰没有挨打动作）才不会反复重建。
    /// ★ 剪辑名必须逐个核对：模型重新导入后剪辑子资源的引用有可能错位，症状就是
    ///   "走路只播一次然后僵住"（其实播的是别的单次剪辑），这时必须重建控制器。</summary>
    static bool IsControllerUsable(NpcModel model)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath(model));
        if (controller == null || controller.layers.Length == 0) return false;

        var machine = controller.layers[0].stateMachine;
        if (machine.states.Length != ResolvableStateCount(model)) return false;

        if (!StatesMatchClips(model, machine)) return false;

        var motion = machine.defaultState != null ? machine.defaultState.motion as AnimationClip : null;
        return motion != null && !motion.legacy;
    }

    /// <summary>每个状态上挂的剪辑名，必须和表里那组候选名对得上</summary>
    static bool StatesMatchClips(NpcModel model, AnimatorStateMachine machine)
    {
        foreach (var child in machine.states)
        {
            var state = child.state;
            var clip = state.motion as AnimationClip;
            if (clip == null) return false;

            int index = StateIndexOf(model, state.name);
            if (index < 0) return false;

            if (!MatchesName(clip.name, model.states[index].Skip(1).ToArray()))
            {
                Debug.LogWarning($"[NpcModelSetup] {model.name} 的状态 {state.name} 绑到了「{clip.name}」，" +
                                 $"期望的是 {string.Join(" / ", model.states[index].Skip(1))} —— 需要重建控制器。");
                return false;
            }
        }
        return true;
    }

    static int StateIndexOf(NpcModel model, string stateName)
    {
        for (int i = 0; i < model.states.Length; i++)
            if (model.states[i][0] == stateName) return i;
        return -1;
    }

    /// <summary>模型的动画必须是 Mecanim 导入方式（Legacy 剪辑进不了 AnimatorController）</summary>
    static void EnsureAnimationImportable(NpcModel model)
    {
        string modelPath = ModelPath(model);
        var importer = AssetImporter.GetAtPath(modelPath);

        try
        {
            // 原生模型导入器（fbx 等）
            if (importer is ModelImporter modelImporter)
            {
                if (modelImporter.animationType == ModelImporterAnimationType.None ||
                    modelImporter.animationType == ModelImporterAnimationType.Legacy)
                {
                    modelImporter.animationType   = ModelImporterAnimationType.Generic;
                    modelImporter.importAnimation = true;
                    modelImporter.SaveAndReimport();
                    Debug.Log($"[NpcModelSetup] {model.name}：已把模型动画类型改成 Generic 并重新导入。");
                }
                return;
            }

            // glTFast 的 ScriptedImporter：类名是 internal，只能通过 SerializedObject 改它序列化的设置
            if (importer != null && importer.GetType().FullName == "GLTFast.Editor.GltfImporter")
                TrySetGlTFastAnimationToMecanim(model, importer);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[NpcModelSetup] {model.name} 调整导入设置时出错（可手动在 Inspector 里改）：{e.Message}");
        }
    }

    /// <summary>把 glTFast 导入设置里的 AnimationMethod 从 Legacy 改成 Mecanim 并重新导入</summary>
    static void TrySetGlTFastAnimationToMecanim(NpcModel model, AssetImporter importer)
    {
        var serialized = new SerializedObject(importer);
        var settings = serialized.FindProperty("importSettings");
        var method   = settings?.FindPropertyRelative("animationMethod");

        if (method == null)
        {
            Debug.LogWarning($"[NpcModelSetup] 没读到 {model.name} 的 glTFast animationMethod 设置，请在 Inspector 里手动改成 Mecanim。");
            return;
        }

        int index = System.Array.IndexOf(method.enumNames, "Mecanim");
        if (index < 0)
        {
            Debug.LogWarning($"[NpcModelSetup] 导入设置里没有 Mecanim 选项：{string.Join("/", method.enumNames)}");
            return;
        }

        if (method.enumValueIndex == index) return;

        method.enumValueIndex = index;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        importer.SaveAndReimport();
        Debug.Log($"[NpcModelSetup] {model.name}：已把 glTFast 的动画方式改成 Mecanim 并重新导入。");
    }

    /// <summary>先精确匹配，再容忍导入器给剪辑加的前后缀。
    /// ★ 必须先把 "__preview__*" 这类预览剪辑排除掉：那是导入器生成的单帧预览，
    ///   一旦被模糊匹配选中，动作就会"播一次然后僵住"——走 / 跑不循环的真正原因就是这个。
    ///   同时优先选名字最短的那个，避免后缀更长的干扰剪辑被选中。</summary>
    static AnimationClip FindClip(List<AnimationClip> clips, string name)
    {
        string want = name.ToLowerInvariant();

        var real = clips.Where(c => !c.name.StartsWith("__", System.StringComparison.Ordinal))
                        .OrderBy(c => c.name.Length)
                        .ToList();
        if (real.Count == 0) real = clips.ToList();      // 万一整个模型只有预览剪辑，退回原列表

        var clip = real.FirstOrDefault(c => c.name == name)
                ?? real.FirstOrDefault(c => string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase));
        if (clip != null) return clip;

        return real.FirstOrDefault(c => c.name.ToLowerInvariant().EndsWith(want))
            ?? real.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(want));
    }

    static void EnsureFolder(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

        string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
    }
}
