using UnityEngine;

/// <summary>
/// 玩家普攻（鼠标左键）：朝准星方向挥一下，命中 3 米出头、正前方约 80 度锥形里的野外怪就扣血 + 击退，
/// 并给一个命中的准星反馈。打起来就是"一下一下敲"，连招 / 重击 / 元素技能留给后面的战斗系统。
/// </summary>
public class VoxelCombat : MonoBehaviour
{
    public float range       = 3.4f;    // 够得着的距离（从相机算）
    public float damage      = 32f;     // 每下伤害
    public float interval    = 0.45f;   // 两下之间的间隔
    public float knockback   = 1.4f;    // 击退距离
    public float halfAngle   = 40f;     // 命中锥形的半角

    float _cooldown;
    float _hitFlash;
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
        if (VoxelChat.IsOpen || VoxelGacha.IsOpen || WorldMap.IsOpen || TitleScreen.IsOpen || VoxelMenu.IsOpen) return;

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

        best.TakeDamage(damage, "你的攻击");
        best.Knockback(dir, knockback);
        _hitFlash = 0.16f;
    }

    /// <summary>命中反馈：准星上闪一个小叉，打中了心里有底</summary>
    void OnGUI()
    {
        if (_hitFlash <= 0f) return;

        if (_mark == null)
            _mark = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };

        float a = Mathf.Clamp01(_hitFlash / 0.16f);
        _mark.normal.textColor = new Color(1f, 0.92f, 0.86f, a);

        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        VoxelHud.FillRect(new Rect(cx - 11f, cy - 11f, 22f, 2f), new Color(1f, 0.9f, 0.85f, a * 0.9f));
        VoxelHud.FillRect(new Rect(cx - 11f, cy + 9f, 22f, 2f), new Color(1f, 0.9f, 0.85f, a * 0.9f));
        VoxelHud.FillRect(new Rect(cx - 11f, cy - 11f, 2f, 22f), new Color(1f, 0.9f, 0.85f, a * 0.9f));
        VoxelHud.FillRect(new Rect(cx + 9f, cy - 11f, 2f, 22f), new Color(1f, 0.9f, 0.85f, a * 0.9f));
    }
}
