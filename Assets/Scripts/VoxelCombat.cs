using UnityEngine;

/// <summary>
/// 玩家战斗：
///   · 鼠标左键 —— 普通攻击：朝准星方向挥一下，命中 3 米出头、正前方约 80 度锥形里的目标就扣血 + 击退
///     （目标既可以是野怪，也可以是野生动物 —— 野外的小动物能猎，见 VoxelAnimal）；
///   · 鼠标右键 —— 元素战技：脚下放一圈范围伤害，人物中心 4.5 米内的敌人全吃（进冷却，见角色的 SkillCd）；
///   · F 键 —— 元素爆发：元素能量攒满了才能放，范围更大伤害更高，放完能量清零。
///
/// 三个伤害都走同一套公式：出战角色的攻击力 × 对应倍率（普攻 / 战技 / 爆发的天赋等级），
/// 再按暴击率 / 暴击伤害掷一次暴击。普通攻击命中还会给元素能量（EnergyPerHit），
/// 所以"打普攻攒能量 → 放爆发"是一个完整的循环。没接角色系统时用 Inspector 上的 damage 兜底。
/// </summary>
public class VoxelCombat : MonoBehaviour
{
    public float range       = 3.4f;    // 够得着的距离（从相机算）
    public float damage      = 32f;     // 兜底伤害：没接角色系统时用
    public float interval    = 0.45f;   // 两下之间的间隔
    public float knockback   = 1.4f;    // 击退距离
    public float halfAngle   = 40f;     // 命中锥形的半角

    const float SkillRadius = 4.5f;     // 元素战技的作用半径
    const float BurstRadius = 7.0f;     // 元素爆发的作用半径
    const float SkillCastGap = 0.35f;   // 放技能后短暂的出招停顿

    float _cooldown;
    float _hitFlash;
    bool  _hitCrit;                     // 这一下是不是暴击（决定准星反馈的颜色）
    Transform _camera;
    Material _material;                  // 世界那套顶点色材质（放技能光效要用）
    GUIStyle _mark;

    public void Init(Transform cameraTransform, Material material)
    {
        _camera = cameraTransform;
        _material = material;
    }

    void Update()
    {
        if (_camera == null) return;

        if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        if (_hitFlash > 0f) _hitFlash -= Time.deltaTime;

        // 鼠标没锁、或者有面板占着输入时不打（划面打开时 VoxelPlayer 也停了）
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (VoxelPanels.InputBlocked) return;

        if (Input.GetMouseButtonDown(0) && _cooldown <= 0f) Attack();
        if (Input.GetMouseButtonDown(1) && _cooldown <= 0f) CastSkill();
        if (Input.GetKeyDown(KeyCode.F) && _cooldown <= 0f) CastBurst();
    }

    // ---------- 元素战技 / 元素爆发 ----------

    /// <summary>元素战技（鼠标右键）：以人物为中心打一圈，进冷却</summary>
    void CastSkill()
    {
        var state = VoxelCharacterSheet.ActiveState;
        if (state == null) return;
        if (!state.SkillReady) return;          // 还在冷却：什么都不做（HUD 上有冷却条）

        state.StartSkillCooldown();
        _cooldown = SkillCastGap;

        int hits = AreaDamage(state.SkillDamage, SkillRadius, state.Info.SkillName);
        PlayCastFx(state.Info.ElementColor, SkillRadius, 0.5f, 0.55f);
        VoxelAudio.PlaySfxAt("teleport", transform.position + Vector3.up * 1f, 0.75f, 1.15f);

        VoxelChat.Announce($"{state.Name} · {state.Info.SkillName}" + (hits > 0 ? $"（命中 {hits} 个）" : ""));
    }

    /// <summary>元素爆发（F 键）：能量攒满才能放，范围更大、伤害更高</summary>
    void CastBurst()
    {
        var state = VoxelCharacterSheet.ActiveState;
        if (state == null) return;

        if (!state.BurstReady)
        {
            VoxelChat.Announce($"元素能量不足（{state.Energy:0} / {state.MaxEnergy:0}）—— 打普攻攒能量");
            return;
        }

        state.ConsumeBurst();
        _cooldown = SkillCastGap * 1.6f;

        int hits = AreaDamage(state.BurstDamage, BurstRadius, state.Info.BurstName);
        PlayCastFx(state.Info.ElementColor, BurstRadius, 1.1f, 0.9f);
        VoxelAudio.PlaySfxAt("heal", transform.position + Vector3.up * 1.2f, 1f, 0.92f);

        VoxelChat.Announce($"{state.Name} · {state.Info.BurstName}" + (hits > 0 ? $"（命中 {hits} 个）" : ""));
    }

    /// <summary>以人物为中心给半径内的怪结算一次伤害，返回打中几个（会掷暴击）</summary>
    int AreaDamage(float amount, float radius, string skillName)
    {
        int hits = 0;
        Vector3 center = transform.position;

        for (int i = 0; i < VoxelMonster.All.Count; i++)
        {
            var m = VoxelMonster.All[i];
            if (m == null || m.Dead) continue;

            Vector3 to = m.transform.position - center;
            to.y = 0f;
            if (to.sqrMagnitude > radius * radius) continue;

            bool crit = Random.value < VoxelCharacterSheet.ActiveCritRate;
            float dealt = crit ? amount * (1f + VoxelCharacterSheet.ActiveCritDamage) : amount;

            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;
            m.TakeDamage(dealt, crit ? skillName + "（暴击）" : skillName);
            m.Knockback(dir, knockback * 1.6f);
            hits++;
        }

        // 野生动物也吃范围伤害：顺手放个战技，脚边刚好有只兔子也会被吓跑 / 打中
        for (int i = 0; i < VoxelAnimal.All.Count; i++)
        {
            var a = VoxelAnimal.All[i];
            if (a == null || a.Dead) continue;

            Vector3 to = a.transform.position - center;
            to.y = 0f;
            if (to.sqrMagnitude > radius * radius) continue;

            bool crit = Random.value < VoxelCharacterSheet.ActiveCritRate;
            float dealt = crit ? amount * (1f + VoxelCharacterSheet.ActiveCritDamage) : amount;

            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;
            a.TakeDamage(dealt, crit ? skillName + "（暴击）" : skillName);
            a.Knockback(dir, knockback * 1.6f);
            hits++;
        }

        return hits;
    }

    /// <summary>技能光效：贴地一圈扩散的光环 + 一圈上冲的光柱（顶点色，和世界里那套一致）</summary>
    void PlayCastFx(Color color, float radius, float columnHeight, float life)
    {
        if (_material == null) return;

        var root = new GameObject("CastFx");
        root.transform.position = transform.position;

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(ring.GetComponent<Collider>());
        Destroy(column.GetComponent<Collider>());

        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = Vector3.up * 0.12f;
        column.transform.SetParent(root.transform, false);
        column.transform.localPosition = Vector3.up * columnHeight * 0.5f;

        Paint(ring, color);
        Paint(column, color * 1.15f);

        StartCoroutine(AnimateFx(root.transform, ring.transform, column.transform, radius, columnHeight, life));
    }

    void Paint(GameObject go, Color color)
    {
        var mesh = go.GetComponent<MeshFilter>().mesh;
        var colors = new Color[mesh.vertexCount];
        for (int i = 0; i < colors.Length; i++) colors[i] = color.linear;
        mesh.SetColors(colors);
        go.GetComponent<MeshRenderer>().sharedMaterial = _material;
    }

    System.Collections.IEnumerator AnimateFx(Transform root, Transform ring, Transform column,
                                             float radius, float columnHeight, float life)
    {
        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / life);

            float r = Mathf.Lerp(radius * 0.35f, radius, k);
            ring.localScale = new Vector3(r, 0.06f, r);

            float h = Mathf.Lerp(columnHeight * 0.4f, columnHeight * 1.5f, k);
            column.localScale = new Vector3(Mathf.Lerp(radius * 0.5f, radius * 0.2f, k), h * 0.5f,
                                            Mathf.Lerp(radius * 0.5f, radius * 0.2f, k));

            yield return null;
        }

        if (root != null) Destroy(root.gameObject);
    }

    void Attack()
    {
        _cooldown = interval;

        Vector3 origin  = _camera.position;
        Vector3 forward = _camera.forward;

        // 目标可以是野怪，也可以是野生动物（能猎）：同一套锥形判定，挑"最正前、最近"的那一只
        MonoBehaviour target = null;
        float bestScore = float.MaxValue;
        float hitHeight = 0.8f;

        for (int i = 0; i < VoxelMonster.All.Count; i++)
        {
            var m = VoxelMonster.All[i];
            if (m == null || m.Dead) continue;
            if (!InAttackCone(m.transform.position, m.HitHeight, origin, forward, out float score)) continue;
            if (score < bestScore) { bestScore = score; target = m; hitHeight = m.HitHeight; }
        }

        for (int i = 0; i < VoxelAnimal.All.Count; i++)
        {
            var a = VoxelAnimal.All[i];
            if (a == null || a.Dead) continue;
            if (!InAttackCone(a.transform.position, a.HitHeight, origin, forward, out float score)) continue;
            if (score < bestScore) { bestScore = score; target = a; hitHeight = a.HitHeight; }
        }

        if (target == null) return;

        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = forward;
        dir.Normalize();

        bool crit;
        float amount = HitDamage(out crit);
        string cause = crit ? "你的暴击" : "你的攻击";

        var monster = target as VoxelMonster;
        if (monster != null)
        {
            monster.TakeDamage(amount, cause);
            monster.Knockback(dir, knockback);
        }
        else
        {
            var animal = (VoxelAnimal)target;
            animal.TakeDamage(amount, cause);
            animal.Knockback(dir, knockback);
        }

        // 命中音效：打中的位置出声，暴击时音更高更亮一点，耳朵也能听出"这下疼"
        VoxelAudio.PlaySfxAt("attack", target.transform.position + Vector3.up * hitHeight,
                             crit ? 1f : 0.85f, crit ? 1.22f : Random.Range(0.94f, 1.06f));

        // 命中攒元素能量（"打普攻攒能量 → 放爆发"的循环）
        var state = VoxelCharacterSheet.ActiveState;
        if (state != null) state.GainEnergy(VoxelCharacterSheet.CharacterState.EnergyPerHit);

        _hitFlash = 0.16f;
        _hitCrit = crit;
    }

    /// <summary>这一点在不在"正前方的攻击锥形"里；在的话给出优先级分数（越正前、越近越优先）</summary>
    bool InAttackCone(Vector3 position, float hitHeight, Vector3 origin, Vector3 forward, out float score)
    {
        score = float.MaxValue;

        Vector3 to = position + Vector3.up * hitHeight - origin;
        float dist = to.magnitude;
        if (dist > range) return false;                                   // 够不着
        if (Vector3.Angle(forward, to) > halfAngle) return false;          // 不在正前方锥形里

        score = dist - Vector3.Dot(forward, to.normalized) * 0.8f;
        return true;
    }

    /// <summary>这一下打多少：出战角色的攻击力 × 普通攻击倍率，再按暴击率掷一次暴击。
    /// 没接角色系统（比如单独开某个场景）就退回 Inspector 上的 damage</summary>
    float HitDamage(out bool crit)
    {
        crit = false;

        float amount = VoxelCharacterSheet.ActiveNormalAttackDamage;
        if (amount <= 0f) return damage;

        float chance = VoxelCharacterSheet.ActiveCritRate;
        if (chance > 0f && Random.value < chance)
        {
            crit = true;
            amount *= 1f + VoxelCharacterSheet.ActiveCritDamage;
        }

        return amount;
    }

    /// <summary>命中反馈：准星上闪一个小叉，打中了心里有底（暴击时是金红色，一眼就看得出来）</summary>
    void OnGUI()
    {
        if (_hitFlash <= 0f) return;

        if (_mark == null)
            _mark = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };

        float a = Mathf.Clamp01(_hitFlash / 0.16f);
        Color color = _hitCrit ? new Color(1f, 0.74f, 0.32f, a) : new Color(1f, 0.92f, 0.86f, a);
        float arm = _hitCrit ? 14f : 11f;

        _mark.normal.textColor = color;

        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        VoxelHud.FillRect(new Rect(cx - arm, cy - arm, arm * 2f, 2f), new Color(color.r, color.g, color.b, a * 0.9f));
        VoxelHud.FillRect(new Rect(cx - arm, cy + arm - 2f, arm * 2f, 2f), new Color(color.r, color.g, color.b, a * 0.9f));
        VoxelHud.FillRect(new Rect(cx - arm, cy - arm, 2f, arm * 2f), new Color(color.r, color.g, color.b, a * 0.9f));
        VoxelHud.FillRect(new Rect(cx + arm - 2f, cy - arm, 2f, arm * 2f), new Color(color.r, color.g, color.b, a * 0.9f));

        if (_hitCrit)
            VoxelHud.LabelShadow(new Rect(cx - 60f, cy - 44f, 120f, 22f), "暴击", _mark);
    }
}
