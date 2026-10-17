using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 世界上掉落的道具：抛出后落到落点，待机时旋转浮动，走近能捡回物品栏。
/// 原石是代码拼的小八面体；纠缠之缘用 Resources 里的 3D 模型（模型缺失就退化成八面体）；
/// 兽肉（野生动物掉的）是代码拼的一根带肉骨头，捡起来进"素材"，按 R 吃掉回饱食度。
/// 孙琰只认原石（那是他的饭），纠缠之缘和兽肉他都不捡。
/// </summary>
public class VoxelGem : MonoBehaviour
{
    /// <summary>掉落物种类</summary>
    public enum Kind { Primogem, Fate, Meat }

    /// <summary>纠缠之缘的 3D 模型（Resources 下，不带扩展名）</summary>
    const string FateModelPath = "IntertwinedFate";
    const float  FateModelSize = 0.62f;    // 模型最长边缩到这么大（和原石差不多）
    const float  FateHover     = 0.60f;    // 纠缠之缘的悬浮高度（模型比八面体胖，抬高点别扎进地里）

    public static readonly List<VoxelGem> All = new List<VoxelGem>();

    const float SettleTime  = 0.4f;    // 从抛出点到落点的时间
    const float PickupRadius = 1.3f;   // 玩家拾取半径
    const float HoverHeight  = 0.45f;  // 原石待机时离地的高度
    const float MeatHover    = 0.32f;  // 兽肉离地更低一点（一块肉飘在半空很怪）

    Kind _kind = Kind.Primogem;
    float _hover = HoverHeight;

    OpenWorld _world;
    Transform _player;
    VoxelInteraction _inventory;
    Vector3 _startPos;
    Vector3 _restPos;
    float _age;
    float _bobPhase = 0f;

    /// <summary>落点（孙琰朝这里走）</summary>
    public Vector3 RestPosition => _restPos;
    /// <summary>是否已落定（落定后才能被捡走）</summary>
    public bool Settled => _age > SettleTime + 0.5f;

    public static VoxelGem Spawn(OpenWorld world, Transform player, VoxelInteraction inventory,
                                 Vector3 from, Kind kind = Kind.Primogem)
    {
        string goName = kind == Kind.Fate ? "IntertwinedFate" : (kind == Kind.Meat ? "RawMeat" : "Primogem");
        var go = new GameObject(goName);
        var gem = go.AddComponent<VoxelGem>();
        gem.Init(world, player, inventory, from, kind);
        return gem;
    }

    void Init(OpenWorld world, Transform player, VoxelInteraction inventory, Vector3 from, Kind kind)
    {
        _kind = kind;

        switch (kind)
        {
            case Kind.Fate: _hover = FateHover; break;
            case Kind.Meat: _hover = MeatHover; break;
            default:        _hover = HoverHeight; break;
        }

        _world = world;
        _player = player;
        _inventory = inventory;
        _startPos = from;
        _bobPhase = Random.value * 6.28f;

        // 落点 = 抛出点正下方的地表（夹在活动范围内）
        float rx = Mathf.Clamp(from.x, 1f, OpenWorld.Size - 1f);
        float rz = Mathf.Clamp(from.z, 1f, OpenWorld.Size - 1f);
        _restPos = new Vector3(rx, world.SurfaceY(rx, rz) + _hover, rz);

        transform.position = from;
        BuildVisual(world.Material);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>找离 origin 最近的掉落物（默认只找原石：孙琰把原石当饭，纠缠之缘他不管）</summary>
    public static VoxelGem Nearest(Vector3 origin, float maxDistance, Kind kind = Kind.Primogem)
    {
        VoxelGem best = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < All.Count; i++)
        {
            if (All[i]._kind != kind) continue;

            Vector3 to = All[i]._restPos - origin;
            to.y = 0f;
            float d = to.sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = All[i];
            }
        }
        return best;
    }

    /// <summary>被孙琰捡走（只有原石会走到这里）</summary>
    public void CollectByNpc()
    {
        if (_kind != Kind.Primogem) return;
        Destroy(gameObject);
    }

    void Update()
    {
        _age += Time.deltaTime;

        if (_age < SettleTime)
        {
            // 抛掷：带一点上抛的弧线飞到落点
            float t = _age / SettleTime;
            Vector3 p = Vector3.Lerp(_startPos, _restPos, t);
            p.y += Mathf.Sin(t * Mathf.PI) * 0.8f;
            transform.position = p;
            transform.Rotate(0f, Spin * 4.5f * Time.deltaTime, 0f, Space.Self);
            return;
        }

        // 待机：贴着地表悬浮 + 自转（地形起伏会跟着升降）
        _restPos.y = _world.SurfaceY(_restPos.x, _restPos.z) + _hover;
        transform.position = _restPos + Vector3.up * (Mathf.Sin(_bobPhase + _age * 2.4f) * 0.08f);
        transform.Rotate(0f, Spin * Time.deltaTime, 0f, Space.Self);

        // 玩家走近 → 捡回物品栏（原石进原石格，纠缠之缘进纠缠之缘格，兽肉进素材）
        if (Settled && _player != null && _inventory != null)
        {
            Vector3 to = _player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < PickupRadius * PickupRadius)
            {
                if (_kind == Kind.Fate)      _inventory.AddFate(1);
                else if (_kind == Kind.Meat) _inventory.AddMaterial(VoxelInteraction.MeatName, 1);
                else                         _inventory.AddPrimogem(1);

                VoxelAudio.PlaySfx("coin", 0.9f, Random.Range(0.98f, 1.08f));   // 捡东西的清脆声
                Destroy(gameObject);
            }
        }
    }

    /// <summary>自转速度（度 / 秒）：兽肉转慢点，它是一块肉不是一颗宝石</summary>
    float Spin => _kind == Kind.Meat ? 50f : 90f;

    // ---------- 外形 ----------

    /// <summary>原石 = 代码拼的八面体；兽肉 = 代码拼的一根带肉骨头；纠缠之缘 = Resources 里的 3D 模型</summary>
    void BuildVisual(Material material)
    {
        if (_kind == Kind.Fate && BuildModelVisual()) return;

        var filter = gameObject.AddComponent<MeshFilter>();
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        filter.sharedMesh = _kind == Kind.Meat ? BuildMeatMesh() : BuildGemMesh();

        // 没有碰撞体：谁都能走进去捡
    }

    /// <summary>纠缠之缘的立体外形：Resources 里的模型缩到掌心大小、中心对到掉落物原点。
    /// 模型缺失时返回 false，调用方退化成八面体</summary>
    bool BuildModelVisual()
    {
        var prefab = Resources.Load<GameObject>(FateModelPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[VoxelGem] 没找到纠缠之缘模型 Resources/{FateModelPath}，先用八面体顶替。");
            return false;
        }

        var model = Instantiate(prefab, transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[VoxelGem] Resources/{FateModelPath} 里没有可渲染的东西，先用八面体顶替。");
            Destroy(model);
            return false;
        }

        // 最长边缩到 FateModelSize（生成模型的原始尺寸不固定，得自己量）
        Bounds bounds = BoundsOf(renderers);
        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longest > 0.0001f) model.transform.localScale = Vector3.one * (FateModelSize / longest);

        // 缩放后再量一次，把模型中心挪到掉落物原点上（掉出来正好悬在中间）
        bounds = BoundsOf(renderers);
        model.transform.position += transform.position - bounds.center;

        SwapToLitMaterial(renderers);
        return true;
    }

    static Bounds BoundsOf(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    /// <summary>生成模型的 PBR 在 URP 下会渲染成抛光金属，换成 URP/Lit 只保留底色贴图（和 NPC 模型同一套处理）</summary>
    static void SwapToLitMaterial(Renderer[] renderers)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return;

        foreach (var r in renderers)
        {
            var src = r.sharedMaterial;
            var mat = new Material(lit);
            if (src != null && src.mainTexture != null) mat.SetTexture("_BaseMap", src.mainTexture);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.45f);
            r.sharedMaterial = mat;
        }
    }

    /// <summary>兽肉：一根横穿的骨头 + 压在骨头上的一大一小两团肉（代码拼，和原石同一套做法）</summary>
    static Mesh BuildMeatMesh()
    {
        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        var meat      = new Color32(0xb5, 0x4a, 0x37, 255);   // 红肉
        var meatLight = new Color32(0xd8, 0x7a, 0x54, 255);   // 受光那一块
        var bone      = new Color32(0xf0, 0xe8, 0xd2, 255);

        // 骨头：横着穿过肉的一根 + 两端的小圆头
        LowPoly.Box(verts, cols, tris, Vector3.zero, new Vector3(0.055f, 0.055f, 0.40f),
                    Quaternion.identity, bone, bone);
        LowPoly.Sphere(verts, cols, tris, new Vector3(0f, 0f,  0.21f), 0.055f, bone, 6);
        LowPoly.Sphere(verts, cols, tris, new Vector3(0f, 0f, -0.21f), 0.055f, bone, 6);

        // 肉：两团略微不规则（jitter 让它不像两个标准球）
        LowPoly.Sphere(verts, cols, tris, new Vector3(0f, 0.055f,  0.02f), 0.155f, meat,      9, 0.90f, 0.10f, 3f);
        LowPoly.Sphere(verts, cols, tris, new Vector3(0f, 0.065f, -0.11f), 0.125f, meatLight, 9, 0.90f, 0.10f, 7f);

        return LowPoly.BuildMesh("MeatMesh", verts, cols, tris);
    }

    static Mesh BuildGemMesh()
    {
        const float h = 0.42f, r = 0.30f;

        // 顶点：上尖、下尖、四颗"腰"点
        Vector3[] v =
        {
            new Vector3( 0,  h, 0), new Vector3( r, 0, 0), new Vector3( 0, 0,  r),
            new Vector3(-r, 0, 0), new Vector3( 0, 0, -r), new Vector3( 0, -h, 0),
        };

        // 八个面（从外面看逆时针，和 VoxelWorld 的面同一约定）
        int[][] faces =
        {
            new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 0, 4, 3 }, new[] { 0, 1, 4 },
            new[] { 5, 1, 2 }, new[] { 5, 2, 3 }, new[] { 5, 3, 4 }, new[] { 5, 4, 1 },
        };

        var gem = new Color32(0x59, 0xd6, 0xff, 0xff);   // 原石青蓝
        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        foreach (var f in faces)
        {
            Vector3 a = v[f[0]], b = v[f[1]], c = v[f[2]];
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;

            // 朝上的面提亮，做出白芯的感觉；侧面按朝向微微拉开层次
            Color col = n.y > 0.3f ? Color.Lerp((Color)gem, Color.white, 0.45f) : gem;
            col *= 0.72f + 0.28f * Mathf.Max(0f, Vector3.Dot(n, new Vector3(0.25f, 0.9f, -0.25f).normalized));
            col.a = 1f;
            col = col.linear;

            int baseIndex = verts.Count;
            verts.Add(a); cols.Add(col);
            verts.Add(b); cols.Add(col);
            verts.Add(c); cols.Add(col);
            tris.Add(baseIndex); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
        }

        var mesh = new Mesh { name = "PrimogemMesh" };
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
