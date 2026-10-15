using UnityEngine;

/// <summary>
/// 玩家普攻（鼠标左键）：朝准星方向挥一下，命中 3 米出头、正前方约 80 度锥形里的野外怪就扣血 + 击退，
/// 并给一个命中的准星反馈。打起来就是"一下一下敲"，连招 / 重击 / 元素技能留给后面的战斗系统。
///
/// 伤害不是写死的：接了角色养成之后 = 出战角色的攻击力 × 普通攻击天赋倍率，
/// 再按角色的暴击率 / 暴击伤害掷一次暴击（暴击时准星反馈是金红色）。没接角色系统就用 Inspector 上的 damage。
/// </summary>
public class VoxelCombat : MonoBehaviour
{
    public float range       = 3.4f;    // 够得着的距离（从相机算）
    public float damage      = 32f;     // 兜底伤害：没接角色系统时用
    public float interval    = 0.45f;   // 两下之间的间隔
    public float knockback   = 1.4f;    // 击退距离
    public float halfAngle   = 40f;     // 命中锥形的半角

    float _cooldown;
    float _hitFlash;
    bool  _hitCrit;                     // 这一下是不是暴击（决定准星反馈的颜色）
    Transform _camera;
    GUIStyle _mark;

    public void Init(Transform cameraTransform)
    {
        _camera = cameraTransform;
    }

    void Update()
    {
        if (_camera == null) return;

        if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        if (_hitFlash > 0f) _hitFlash -= Time.deltaTime;

        // 鼠标没锁（开始面板 / 对话框 / 地图 / 开幕动画开着）时不打；划面打开时 VoxelPlayer 也停了
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (VoxelChat.IsOpen || VoxelGacha.IsOpen || WorldMap.IsOpen || VoxelInventory.IsOpen ||
            VoxelCharacterScreen.IsOpen || TitleScreen.IsOpen || VoxelMenu.IsOpen) return;

        if (Input.GetMouseButtonDown(0) && _cooldown <= 0f) Attack();
    }

    void Attack()
    {
        _cooldown = interval;

        Vector3 origin  = _camera.position;
        Vector3 forward = _camera.forward;

        VoxelMonster best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < VoxelMonster.All.Count; i++)
        {
            var m = VoxelMonster.All[i];
            if (m == null || m.Dead) continue;

            Vector3 to = m.transform.position + Vector3.up * m.HitHeight - origin;
            float dist = to.magnitude;
            if (dist > range) continue;                                   // 够不着
            if (Vector3.Angle(forward, to) > halfAngle) continue;          // 不在正前方锥形里

            float score = dist - Vector3.Dot(forward, to.normalized) * 0.8f;   // 越正前、越近越优先
            if (score < bestScore) { bestScore = score; best = m; }
        }

        if (best == null) return;

        Vector3 dir = best.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = forward;
        dir.Normalize();

        bool crit;
        float amount = HitDamage(out crit);

        best.TakeDamage(amount, crit ? "你的暴击" : "你的攻击");
        best.Knockback(dir, knockback);
        _hitFlash = 0.16f;
        _hitCrit = crit;
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
