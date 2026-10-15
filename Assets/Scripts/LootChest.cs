using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战利品宝箱（原神味）：散落在世界各处，刻意不写进大地图的地标列表 —— 得自己走出去找。
/// 五个品质（低 → 高）：普通 / 精致 / 史诗 / 高级 / 华丽，品质越高外形越华丽、体积越大、奖励越好、数量越少。
/// 两种开法：
///   · 直接开启：走到跟前准星对上，按 E 就开。
///   · 封印宝箱：箱子外面立着一圈封印石，另外有几只守卫怪看着；把守卫全部清掉封印才破，然后才能开。
/// 外形优先用 Resources/Chests 下 AI 生成的 3D 模型（一个品质一个），模型没导进来时自动退化成
/// 代码拼的顶点色低模，所以模型缺失也不影响玩。
/// 开箱奖励：Andy的币（大量）+ 原石（少量）+ 素材（随机）。
/// </summary>
public class LootChest : MonoBehaviour
{
    /// <summary>品质（低 → 高：普通 / 精致 / 史诗 / 高级 / 华丽）</summary>
    public enum Tier { Common, Exquisite, Epic, Advanced, Luxurious }

    /// <summary>场上所有宝箱</summary>
    public static readonly List<LootChest> All = new List<LootChest>();

    const float UseRadius  = 3.4f;     // 走到这么近才能开
    const float ShowRadius = 12f;      // 多远开始飘名字（近了才看得见，保留"自己找到"的感觉）
    const float FocusDot   = 0.45f;    // 准星大致朝着宝箱才算"看着它"
    const float SlabHeight = 0.07f;    // 箱子底下那块薄石板的厚度（模型底面就贴在它上面）

    // ---------- 品质配置 ----------

    /// <summary>一个品质的外观 + 掉落配置</summary>
    struct Config
    {
        public string  name;                 // 显示名
        public string  model;                // AI 模型在 Resources 里的路径（不带扩展名）
        public Vector3 box;                  // 兜底低模的箱体尺寸（宽 / 高 / 深）
        public float   modelSize;            // AI 模型最长边归一化到多长（米）
        public Color   wood, woodTop;        // 箱体 / 箱盖
        public Color   metal, accent;        // 铁箍 / 锁扣
        public Color   gem;                  // 顶部宝石
        public bool    hasGem;
        public int     coinMin, coinMax;     // Andy的币
        public int     gemMin,  gemMax;      // 原石
        public int     matMin,  matMax;      // 素材份数
        public string[] materials;           // 可能开出来的素材
    }

    static readonly Config[] Configs =
    {
        // 0 普通：破木板 + 麻绳，最不起眼
        new Config
        {
            name = "普通宝箱", model = "Chests/ChestCommon",
            box = new Vector3(0.52f, 0.34f, 0.38f), modelSize = 0.80f,
            wood = new Color32(0x8a, 0x62, 0x42, 0xff), woodTop = new Color32(0x9c, 0x73, 0x50, 0xff),
            metal = new Color32(0x6d, 0x5a, 0x3f, 0xff), accent = new Color32(0x7a, 0x66, 0x48, 0xff),
            gem = Color.white, hasGem = false,
            coinMin = 18, coinMax = 36, gemMin = 0, gemMax = 1, matMin = 1, matMax = 1,
            materials = new[] { "史莱姆凝液", "风车菊", "蒲公英籽" },
        },
        // 1 精致：奶油木色 + 铁角 + 蓝漆
        new Config
        {
            name = "精致宝箱", model = "Chests/ChestExquisite",
            box = new Vector3(0.58f, 0.38f, 0.42f), modelSize = 0.90f,
            wood = new Color32(0xd9, 0xc9, 0xa3, 0xff), woodTop = new Color32(0xe8, 0xdc, 0xc0, 0xff),
            metal = new Color32(0x4d, 0x4a, 0x52, 0xff), accent = new Color32(0x4d, 0x6f, 0xae, 0xff),
            gem = Color.white, hasGem = false,
            coinMin = 45, coinMax = 80, gemMin = 1, gemMax = 2, matMin = 1, matMax = 2,
            materials = new[] { "史莱姆凝液", "丘丘人面具", "风车菊", "水晶矿" },
        },
        // 2 史诗：深紫檀 + 银箍 + 紫青宝石
        new Config
        {
            name = "史诗宝箱", model = "Chests/ChestEpic",
            box = new Vector3(0.64f, 0.44f, 0.46f), modelSize = 1.00f,
            wood = new Color32(0x4a, 0x3b, 0x58, 0xff), woodTop = new Color32(0x5c, 0x4a, 0x6c, 0xff),
            metal = new Color32(0xb9, 0xbc, 0xc6, 0xff), accent = new Color32(0x8a, 0x5c, 0xd8, 0xff),
            gem = new Color32(0x3f, 0xb8, 0xb0, 0xff), hasGem = true,
            coinMin = 90, coinMax = 150, gemMin = 2, gemMax = 4, matMin = 2, matMax = 3,
            materials = new[] { "丘丘人面具", "水晶矿", "蒲公英籽", "精锻用魔矿" },
        },
        // 3 高级：朱漆 + 厚金箍 + 红绒
        new Config
        {
            name = "高级宝箱", model = "Chests/ChestAdvanced",
            box = new Vector3(0.70f, 0.48f, 0.50f), modelSize = 1.10f,
            wood = new Color32(0x8e, 0x23, 0x23, 0xff), woodTop = new Color32(0xa0, 0x2c, 0x2c, 0xff),
            metal = new Color32(0xd9, 0xa7, 0x2c, 0xff), accent = new Color32(0xf0, 0xd6, 0x8a, 0xff),
            gem = new Color32(0xff, 0xe0, 0x8a, 0xff), hasGem = false,
            coinMin = 160, coinMax = 240, gemMin = 4, gemMax = 7, matMin = 3, matMax = 4,
            materials = new[] { "水晶矿", "精锻用魔矿", "丘丘人面具" },
        },
        // 4 华丽：象牙 + 满身金饰 + 发光宝石，最扎眼
        new Config
        {
            name = "华丽宝箱", model = "Chests/ChestLuxurious",
            box = new Vector3(0.78f, 0.54f, 0.56f), modelSize = 1.25f,
            wood = new Color32(0xf0, 0xe2, 0xc0, 0xff), woodTop = new Color32(0xfb, 0xf1, 0xd8, 0xff),
            metal = new Color32(0xe6, 0xc3, 0x4c, 0xff), accent = new Color32(0xa8, 0x32, 0x3a, 0xff),
            gem = new Color32(0xff, 0xf0, 0x9c, 0xff), hasGem = true,
            coinMin = 300, coinMax = 450, gemMin = 8, gemMax = 12, matMin = 4, matMax = 6,
            materials = new[] { "精锻用魔矿", "水晶矿" },
        },
    };

    /// <summary>各品质成为"封印宝箱"的概率（越高品质越可能要打一架）</summary>
    static readonly float[] SealChances = { 0.25f, 0.45f, 0.70f, 0.85f, 1f };

    /// <summary>各品质的守卫数量</summary>
    static readonly int[] GuardCounts = { 2, 2, 3, 3, 4 };

    public static string TierName(Tier t) => Configs[(int)t].name;
    public static float  SealChance(Tier t) => SealChances[(int)t];
    public static int    GuardCount(Tier t) => GuardCounts[(int)t];

    // ---------- 实例状态 ----------

    public Tier tier = Tier.Common;

    OpenWorld _world;
    Transform _player;
    VoxelInteraction _inventory;
    Material _material;               // 世界的顶点色材质（石板 / 封印石 / 光效都用它）

    Transform _modelRoot;             // 宝箱本体（开箱时弹一下）
    Transform _sealRoot;              // 封印石圈（解封后销毁）
    readonly List<Transform> _sealRunes = new List<Transform>();
    readonly List<Vector3>   _runeHome  = new List<Vector3>();
    readonly List<VoxelMonster> _guards = new List<VoxelMonster>();

    bool _sealed, _opened, _locked;
    float _idleTime;
    GUIStyle _nameStyle, _promptStyle;

    static readonly Dictionary<Tier, GameObject> Templates = new Dictionary<Tier, GameObject>();
    static readonly HashSet<Tier> ModelMissing = new HashSet<Tier>();   // 模型确实没导进来（免得每次开箱都刷一遍警告）
    static readonly Dictionary<Tier, Mesh> FallbackMeshes = new Dictionary<Tier, Mesh>();
    static Mesh _slabMesh;

    /// <summary>开过了没有</summary>
    public bool Opened => _opened;
    /// <summary>是不是封印宝箱（守卫没清光之前打不开）</summary>
    public bool Sealed => _sealed;
    /// <summary>是不是被机关锁着（解密场地用）</summary>
    public bool Locked => _locked;

    /// <summary>机关锁：锁着的时候箱子照样摆在那儿、能看到，但按 E 打不开。
    /// 解密场地用它实现"宝箱先摆在中央机关台上，点亮方碑之后才能开"</summary>
    public void SetLocked(bool locked) => _locked = locked;

    // ---------- 初始化 ----------

    public void Init(OpenWorld world, Transform player, VoxelInteraction inventory, Material material,
                     Tier t, float yaw)
    {
        tier = t;
        _world = world;
        _player = player;
        _inventory = inventory;
        _material = material;

        // ★ 只往上抬、不往下压：野外铺的宝箱传进来的 y 是 0，会被抬到地面；
        //   而解密场地把宝箱生成在中央机关台上（比地面高 1.2 米），
        //   如果这里无条件吸到地面，宝箱就会被整个塞进机关台里 —— 看不见。
        float x = transform.position.x, z = transform.position.z;
        float surf = world.SurfaceY(x, z);
        transform.position = new Vector3(x, Mathf.Max(transform.position.y, surf), z);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        BuildSlab();
        BuildVisual();

        // 箱子挡人：给个粗略的盒碰撞（不用模型自带的碎网格，免得卡住脚下）
        var box = Configs[(int)tier].box;
        var col = gameObject.AddComponent<BoxCollider>();
        col.size   = new Vector3(box.x, box.y * 1.45f + SlabHeight, box.z);
        col.center = new Vector3(0f, col.size.y * 0.5f, 0f);
    }

    void OnEnable()  => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>登记一名守卫（LootChestSpawner 刷出守卫后调用）</summary>
    public void AddGuard(VoxelMonster guard)
    {
        if (guard != null) _guards.Add(guard);
    }

    /// <summary>把宝箱变成封印宝箱：立起封印石圈，守卫没清光之前打不开</summary>
    public void Seal()
    {
        if (_sealed) return;
        _sealed = true;
        BuildSealRing();
    }

    /// <summary>还剩几只活着的守卫</summary>
    public int RemainingGuards()
    {
        int n = 0;
        for (int i = 0; i < _guards.Count; i++)
            if (_guards[i] != null && !_guards[i].Dead) n++;
        return n;
    }

    // ---------- 主循环 ----------

    void Update()
    {
        if (_world == null) return;

        float dt = Time.deltaTime;
        _idleTime += dt;

        if (_sealed)
        {
            if (RemainingGuards() == 0) BreakSeal();
            else AnimateSeal(dt);
        }

        // 机关锁着的时候打不开（解密场地：宝箱先摆在那儿，点亮方碑才解锁）
        if (!_opened && !_sealed && !_locked && CanUseInput() && Focused(out _) && Input.GetKeyDown(KeyCode.E))
            Open();
    }

    /// <summary>封印石圈待机：整体慢慢转，符文一上一下地浮</summary>
    void AnimateSeal(float dt)
    {
        if (_sealRoot == null) return;
        _sealRoot.Rotate(0f, 12f * dt, 0f, Space.Self);

        for (int i = 0; i < _sealRunes.Count; i++)
        {
            var r = _sealRunes[i];
            if (r == null) continue;
            r.localPosition = _runeHome[i] + Vector3.up * (Mathf.Sin(_idleTime * 1.8f + i * 0.9f) * 0.06f);
        }
    }

    /// <summary>对话框 / 祈愿 / 大地图 / 开幕动画开着、或者鼠标没锁的时候，不响应 E 键和提示</summary>
    static bool CanUseInput() =>
        Cursor.lockState == CursorLockMode.Locked && !VoxelChat.IsOpen && !VoxelGacha.IsOpen && !VoxelMenu.IsOpen &&
        !WorldMap.IsOpen && !TitleScreen.IsOpen;

    /// <summary>玩家是不是站在跟前盯着它</summary>
    bool Focused(out float distance)
    {
        distance = 999f;
        if (_player == null || _opened) return false;

        Vector3 to = transform.position - _player.position;
        to.y = 0f;
        distance = to.magnitude;
        if (distance > UseRadius) return false;

        var cam = Camera.main;
        if (cam == null) return true;

        Vector3 dir = (transform.position + Vector3.up * 0.45f) - cam.transform.position;
        if (dir.magnitude < 0.7f) return true;                              // 几乎踩在箱子上，不用对准
        return Vector3.Dot(cam.transform.forward, dir.normalized) > FocusDot;
    }

    // ---------- 开箱 ----------

    void Open()
    {
        _opened = true;

        var cfg = Configs[(int)tier];
        int cx = Mathf.RoundToInt(transform.position.x);
        int cz = Mathf.RoundToInt(transform.position.z);

        // 用"位置 + 品质"当种子：同一个宝箱每次开出来的东西一样（和世界其它生成一样是确定性的）
        var rng = new System.Random(Hash(cx, cz, (int)tier));

        var lines = new List<string>();

        int coins = rng.Next(cfg.coinMin, cfg.coinMax + 1);
        if (_inventory != null) _inventory.AddAndyCoin(coins);
        lines.Add("Andy的币 ×" + coins);

        int gems = rng.Next(cfg.gemMin, cfg.gemMax + 1);
        if (gems > 0)
        {
            if (_inventory != null) _inventory.AddPrimogem(gems);
            lines.Add("原石 ×" + gems);
        }

        int kinds = rng.Next(cfg.matMin, cfg.matMax + 1);
        var gained = new Dictionary<string, int>();
        var order  = new List<string>();

        for (int i = 0; i < kinds; i++)
        {
            string mat = cfg.materials[rng.Next(cfg.materials.Length)];
            int amount = rng.Next(1, 4);

            if (!gained.ContainsKey(mat)) { gained[mat] = 0; order.Add(mat); }
            gained[mat] += amount;

            if (_inventory != null) _inventory.AddMaterial(mat, amount);
        }
        foreach (var mat in order) lines.Add(mat + " ×" + gained[mat]);

        VoxelHud.ShowLoot("开启 " + cfg.name, lines);
        VoxelChat.Announce("开启了" + cfg.name);

        DimChest();
        StartCoroutine(PlayFx(NewFx(true, new Color32(0xff, 0xe2, 0x8a, 0xff), 1.7f, 0.8f),
                              NewFx(false, new Color32(0xff, 0xd0, 0x6a, 0xff), SlabHeight + 0.06f, 1.2f),
                              true));
    }

    /// <summary>封印破除：紫光冲一下，然后箱子就能开了</summary>
    void BreakSeal()
    {
        _sealed = false;

        if (_sealRoot != null) { Destroy(_sealRoot.gameObject); _sealRoot = null; }
        _sealRunes.Clear();
        _runeHome.Clear();

        VoxelChat.Announce("封印解除：" + Configs[(int)tier].name + " 可以打开了");

        StartCoroutine(PlayFx(NewFx(true, new Color32(0xc8, 0xa8, 0xff, 0xff), 1.5f, 0.8f),
                              NewFx(false, new Color32(0xb9, 0x8c, 0xff, 0xff), 0.09f, 1.2f),
                              false));
    }

    /// <summary>开过的箱子压暗一点，一眼能看出"这个已经开过了"</summary>
    void DimChest()
    {
        var dim = new Color(0.58f, 0.56f, 0.53f, 1f);

        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            var src = r.sharedMaterial;
            if (src == null) continue;

            var mat = new Material(src);            // 每个箱子一份，染暗不会波及同品质的其它箱子
            if (mat.HasProperty("_BaseColor"))      mat.SetColor("_BaseColor", src.GetColor("_BaseColor") * dim);
            else if (mat.HasProperty("_Color"))     mat.SetColor("_Color", src.GetColor("_Color") * dim);
            else if (mat.HasProperty("_Tint"))      mat.SetColor("_Tint", new Color(dim.r, dim.g, dim.b, 1f));
            r.sharedMaterial = mat;
        }
    }

    /// <summary>造一个光效件：竖直的是光柱（往上抽），压扁的是光环（往外扩）</summary>
    Transform NewFx(bool column, Color color, float y, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = column ? "FxColumn" : "FxRing";

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.SetParent(transform, true);
        go.transform.position = transform.position + Vector3.up * y;
        go.transform.rotation = Quaternion.identity;

        var mesh = go.GetComponent<MeshFilter>().mesh;
        var colors = new Color[mesh.vertexCount];
        for (int i = 0; i < colors.Length; i++) colors[i] = color.linear;
        mesh.SetColors(colors);
        go.GetComponent<MeshRenderer>().sharedMaterial = _material;

        go.transform.localScale = column
            ? new Vector3(radius, 1.5f, radius)
            : new Vector3(radius * 1.5f, 0.09f, radius * 1.5f);

        return go.transform;
    }

    /// <summary>光效表演：光柱往上抽细、光环往外扩薄；开箱时顺带让箱子弹一下</summary>
    IEnumerator PlayFx(Transform column, Transform ring, bool popChest)
    {
        Vector3 baseScale = Vector3.one;
        if (popChest && _modelRoot != null) baseScale = _modelRoot.localScale;

        const float life = 1.15f;

        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / life);

            if (popChest && _modelRoot != null)
                _modelRoot.localScale = baseScale * (1f + Mathf.Sin(Mathf.Clamp01(k / 0.3f) * Mathf.PI) * 0.09f);

            if (column != null)
            {
                column.position += Vector3.up * (Time.deltaTime * 1.4f);
                float w = Mathf.Lerp(column.localScale.x, 0.06f, k);
                column.localScale = new Vector3(w, Mathf.Lerp(1.5f, 2.6f, k), w);
            }

            if (ring != null)
            {
                float r = Mathf.Lerp(1.2f, 3.6f, k);
                ring.localScale = new Vector3(r, Mathf.Lerp(0.09f, 0.02f, k), r);
            }

            yield return null;
        }

        if (popChest && _modelRoot != null) _modelRoot.localScale = baseScale;
        if (column != null) Destroy(column.gameObject);
        if (ring != null) Destroy(ring.gameObject);
    }

    // ---------- 外形 ----------

    /// <summary>箱子底下垫一块薄石板：坡地上不显悬空，也和地形有个分隔</summary>
    void BuildSlab()
    {
        if (_slabMesh == null)
        {
            var m = new LowPoly();
            m.Box(Vector3.zero, Vector3.one,
                  new Color32(0xa9, 0xa3, 0x99, 0xff), new Color32(0xbb, 0xb5, 0xaa, 0xff));
            _slabMesh = m.Build("ChestSlab");
        }

        var box = Configs[(int)tier].box;

        var go = new GameObject("Slab");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, SlabHeight * 0.5f, 0f);
        go.transform.localScale = new Vector3(box.x * 1.32f, SlabHeight, box.z * 1.38f);

        go.AddComponent<MeshFilter>().sharedMesh = _slabMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _material;
    }

    /// <summary>宝箱本体：有 AI 模型用模型，没有就用代码拼的低模</summary>
    void BuildVisual()
    {
        var template = Template(tier);

        if (template != null)
        {
            var model = Instantiate(template, transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            model.SetActive(true);
            _modelRoot = model.transform;
            return;
        }

        var go = new GameObject("Model");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = FallbackMesh(tier);
        go.AddComponent<MeshRenderer>().sharedMaterial = _material;
        _modelRoot = go.transform;
    }

    /// <summary>取（第一次会构建）某个品质的模型模板：归一化尺寸 + 材质换成 URP/Lit。
    /// 之后同品质的宝箱都从这个模板 Instantiate，缩放和材质只算一次。</summary>
    static GameObject Template(Tier tier)
    {
        if (ModelMissing.Contains(tier)) return null;

        if (Templates.TryGetValue(tier, out var cached))
        {
            if (cached != null) return cached;
            Templates.Remove(tier);          // 上一局结束时被销毁的空壳（不重载域时会遇到），重建
        }

        var cfg = Configs[(int)tier];
        var prefab = Resources.Load<GameObject>(cfg.model);

        if (prefab == null)
        {
            Debug.LogWarning($"[LootChest] 没找到模型 Resources/{cfg.model}，{cfg.name} 先用代码拼的低模顶替。");
            ModelMissing.Add(tier);
            return null;
        }

        // 量包围盒时模板要处于激活状态（GetComponentsInChildren 默认不找未激活的子物体）
        var root = new GameObject("ChestTemplate_" + tier);
        var model = Instantiate(prefab, root.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[LootChest] Resources/{cfg.model} 里没有可渲染的东西，{cfg.name} 先用低模顶替。");
            Destroy(root);
            ModelMissing.Add(tier);
            return null;
        }

        SwapToLitMaterial(renderers, Resources.Load<Texture2D>(cfg.model + "_Albedo"));

        // 生成模型的原始尺寸和原点都不固定：先按最长边缩到目标大小，再把中心对到原点、底面贴到石板上
        Bounds bounds = BoundsOf(renderers);
        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longest > 0.0001f) model.transform.localScale = Vector3.one * (cfg.modelSize / longest);

        bounds = BoundsOf(renderers);
        model.transform.position += new Vector3(root.transform.position.x - bounds.center.x,
                                                SlabHeight - bounds.min.y,
                                                root.transform.position.z - bounds.center.z);

        root.SetActive(false);            // 模板只用来复制，不参与显示
        Templates[tier] = root;
        return root;
    }

    static Bounds BoundsOf(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    /// <summary>生成的 PBR 在 URP 下会渲染成抛光金属，换成 URP/Lit 只保留底色贴图（和掉落物 / NPC 同一套处理）。
    /// 注意：生成器给的 FBX 里不带贴图，彩色贴图是单独一张 PNG，约定叫 "模型名_Albedo"，这里从 Resources 里取。</summary>
    static void SwapToLitMaterial(Renderer[] renderers, Texture2D albedo)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return;

        foreach (var r in renderers)
        {
            var src = r.sharedMaterial;
            var mat = new Material(lit) { name = (src != null ? src.name : "Chest") + "_Lit" };

            var tex = albedo != null ? albedo : (src != null ? src.mainTexture : null);
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);      // 贴图自带颜色，底色不再叠
            }
            else if (src != null && src.HasProperty("_Color"))
            {
                mat.SetColor("_BaseColor", src.color);
            }

            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.35f);

            r.sharedMaterial = mat;
        }
    }

    /// <summary>兜底低模：箱体 + 箱盖 + 两道铁箍 + 锁扣（高品质再镶一颗宝石）</summary>
    static Mesh FallbackMesh(Tier tier)
    {
        if (FallbackMeshes.TryGetValue(tier, out var cached) && cached != null) return cached;

        var cfg = Configs[(int)tier];
        Vector3 b = cfg.box;
        float lidH = b.y * 0.44f;

        var m = new LowPoly();

        // 箱体 + 箱盖 + 盖顶一条窄的（做出倒角的感觉）
        m.Box(new Vector3(0f, SlabHeight + b.y * 0.5f, 0f), new Vector3(b.x, b.y, b.z), cfg.wood, cfg.woodTop);
        m.Box(new Vector3(0f, SlabHeight + b.y + lidH * 0.5f, 0f), new Vector3(b.x * 1.05f, lidH, b.z * 1.05f), cfg.wood, cfg.woodTop);
        m.Box(new Vector3(0f, SlabHeight + b.y + lidH, 0f), new Vector3(b.x * 0.92f, lidH * 0.6f, b.z * 0.92f), cfg.woodTop, cfg.woodTop);

        // 两道竖箍：从盖顶一直包到箱底
        float bandH = b.y + lidH * 1.3f;
        for (int s = -1; s <= 1; s += 2)
            m.Box(new Vector3(s * b.x * 0.28f, SlabHeight + bandH * 0.5f, 0f),
                  new Vector3(b.x * 0.09f, bandH, b.z * 1.08f), cfg.metal, cfg.metal);

        // 锁扣 + 高品质的宝石
        m.Box(new Vector3(0f, SlabHeight + b.y * 0.78f, b.z * 0.54f),
              new Vector3(b.x * 0.22f, b.y * 0.42f, b.z * 0.12f), cfg.accent, cfg.metal);

        if (cfg.hasGem)
            m.Octa(new Vector3(0f, SlabHeight + b.y + lidH * 0.55f, b.z * 0.52f), b.x * 0.16f, cfg.gem);

        var mesh = m.Build("ChestFallback_" + tier);
        FallbackMeshes[tier] = mesh;
        return mesh;
    }

    // ---------- 封印石圈 ----------

    /// <summary>封印外观：地上一圈暗紫法阵 + 六块封印石，每块顶上一颗浮空符文</summary>
    void BuildSealRing()
    {
        _sealRoot = new GameObject("SealRing").transform;
        _sealRoot.SetParent(transform, false);

        var cfg = Configs[(int)tier];
        float r = cfg.box.x * 2.6f;                                  // 圈的大小跟着箱子走
        Color rune  = new Color32(0xc2, 0x92, 0xff, 0xff);
        Color stone = new Color32(0x4a, 0x40, 0x5c, 0xff);

        // 法阵圆盘（Cylinder 是 1 米直径 2 米高，压扁就是圆盘）
        AddSealPart(PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f),
                    new Vector3(r, 0.035f, r), new Color32(0x6a, 0x4a, 0x9c, 0xff));

        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            float px = Mathf.Cos(a) * r * 0.52f;
            float pz = Mathf.Sin(a) * r * 0.52f;

            // 每块石头贴着自己脚下那一点的地形高度，坡地上也不会悬空
            float ground = _world.SurfaceY(transform.position.x + px, transform.position.z + pz) - transform.position.y;

            AddSealPart(PrimitiveType.Cube, new Vector3(px, ground + 0.16f, pz),
                        new Vector3(0.24f, 0.34f, 0.24f), stone);

            var runeT = AddSealPart(PrimitiveType.Cube, new Vector3(px, ground + 0.62f, pz),
                                    Vector3.one * 0.1f, rune);
            if (runeT != null)
            {
                runeT.localRotation = Quaternion.Euler(45f, i * 53f, 45f);
                _sealRunes.Add(runeT);
                _runeHome.Add(runeT.localPosition);
            }
        }
    }

    Transform AddSealPart(PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "SealPart";

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);                 // 封印石不挡路，别把玩家卡在箱子外面

        go.transform.SetParent(_sealRoot, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        var mesh = go.GetComponent<MeshFilter>().mesh;
        var colors = new Color[mesh.vertexCount];
        for (int i = 0; i < colors.Length; i++) colors[i] = color.linear;
        mesh.SetColors(colors);
        go.GetComponent<MeshRenderer>().sharedMaterial = _material;

        return go.transform;
    }

    // ---------- 提示（名字 + 按键） ----------

    void OnGUI()
    {
        if (_opened || _player == null || !CanUseInput()) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 top = transform.position + Vector3.up * 0.95f;
        float dist = Vector3.Distance(top, cam.transform.position);
        if (dist > ShowRadius) return;

        Vector3 screen = cam.WorldToScreenPoint(top);
        if (screen.z <= 0f) return;

        EnsureStyles();
        float alpha = Mathf.Clamp01((ShowRadius - dist) / 4f);

        _nameStyle.normal.textColor = new Color(1f, 0.94f, 0.78f, alpha);
        VoxelHud.LabelShadow(new Rect(screen.x - 110f, Screen.height - screen.y - 12f, 220f, 22f),
                             Configs[(int)tier].name, _nameStyle);

        if (!Focused(out _)) return;

        // 准星下方一点的操作提示
        string hint;
        Color hintColor;

        if (_sealed)
        {
            hint = "封印中 · 击败附近的守卫（还剩 " + RemainingGuards() + " 只）";
            hintColor = new Color(0.86f, 0.74f, 1f, 0.96f);
        }
        else if (_locked)
        {
            // 解密场地先摆着宝箱、机关锁着：提示里不写"方碑" —— 璃月那边是石灯 / 石鼓，写死了就对不上
            hint = "机关未启动 · 先点亮场地里的机关";
            hintColor = new Color(1f, 0.86f, 0.55f, 0.96f);
        }
        else
        {
            hint = "[E] 开启";
            hintColor = new Color(1f, 1f, 1f, 0.96f);
        }

        _promptStyle.normal.textColor = hintColor;

        VoxelHud.LabelShadow(new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.5f + 54f, 480f, 26f),
                             hint, _promptStyle);
    }

    void EnsureStyles()
    {
        if (_nameStyle != null) return;

        _nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        _promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
    }

    // ---------- 小工具 ----------

    /// <summary>位置 + 品质的确定性哈希（同一个宝箱掉落固定）</summary>
    static int Hash(int a, int b, int salt)
    {
        unchecked
        {
            int n = a * 73856093 ^ b * 19349663 ^ salt * 83492791;
            n = (n ^ (n >> 13)) * 1274126177;
            return n & 0x7fffffff;
        }
    }

    /// <summary>拼顶点色低模的小工具（盒子 / 八面体），绕序和世界里其它低模一致</summary>
    sealed class LowPoly
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Color>   _c = new List<Color>();
        readonly List<int>     _t = new List<int>();

        /// <summary>一个盒子：底面和侧面用 side 色，顶面用 top 色</summary>
        public void Box(Vector3 center, Vector3 size, Color side, Color top)
        {
            int baseIndex = _v.Count;
            Vector3 h = size * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? -h.x : h.x,
                                             (i & 2) == 0 ? -h.y : h.y,
                                             (i & 4) == 0 ? -h.z : h.z);
                _v.Add(center + corner);
                _c.Add(((i & 2) == 0 ? side : top).linear);
            }

            int[] faces =
            {
                0, 2, 3, 0, 3, 1,
                4, 5, 7, 4, 7, 6,
                0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3,
                1, 3, 7, 1, 7, 5,
                0, 4, 6, 0, 6, 2,
            };
            for (int i = 0; i < faces.Length; i++) _t.Add(baseIndex + faces[i]);
        }

        /// <summary>一颗八面体（宝石）：朝上的面提亮一点</summary>
        public void Octa(Vector3 center, float radius, Color color)
        {
            float h = radius * 0.9f;
            Vector3[] p =
            {
                center + new Vector3(0f, h, 0f),
                center + new Vector3(radius, 0f, 0f),
                center + new Vector3(0f, 0f, radius),
                center + new Vector3(-radius, 0f, 0f),
                center + new Vector3(0f, 0f, -radius),
                center + new Vector3(0f, -h, 0f),
            };
            int[][] faces =
            {
                new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 0, 4, 3 }, new[] { 0, 1, 4 },
                new[] { 5, 1, 2 }, new[] { 5, 2, 3 }, new[] { 5, 3, 4 }, new[] { 5, 4, 1 },
            };

            foreach (var f in faces)
            {
                Vector3 a = p[f[0]], b = p[f[1]], c = p[f[2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;

                Color col = color * (0.78f + 0.22f * Mathf.Max(0f, n.y));
                col.a = 1f;

                int baseIndex = _v.Count;
                _v.Add(a);  _c.Add(col.linear);
                _v.Add(b);  _c.Add(col.linear);
                _v.Add(c);  _c.Add(col.linear);
                _t.Add(baseIndex); _t.Add(baseIndex + 1); _t.Add(baseIndex + 2);
            }
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(_v);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
