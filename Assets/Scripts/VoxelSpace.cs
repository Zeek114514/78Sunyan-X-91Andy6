using UnityEngine;

/// <summary>
/// 「这儿站得下人吗」「哪儿站得下」—— 传送落点和"被卡住自救"共用这一套判断。
///
/// 用和玩家 CharacterController 相同尺寸的胶囊（半径 0.3 / 高 1.8）去查，
/// 但把胶囊整体抬起来一点、半径缩到 0.28：正常站在地面上不会被判成"撞上了"，
/// 真的埋进墙里、或者身边一圈全是墙（被关在屋子里 / 神像壳子里）就会判不通。
///
/// 玩家自己的碰撞体要传 ignore 排除掉，否则永远算"撞到自己"。
/// </summary>
public static class VoxelSpace
{
    /// <summary>玩家胶囊的尺寸（和 VoxelBootstrap 里搭的那套一致）</summary>
    public const float Radius = 0.3f, Height = 1.8f;

    const float ProbeRadius = 0.28f;    // 探测用的半径：略小于玩家，贴着地 / 蹭着墙皮不算撞
    const float ProbeLift   = 0.12f;    // 胶囊底部离脚底的高度：站在地面上不算撞

    static readonly Collider[] _hits = new Collider[16];

    /// <summary>脚底在 feet 的位置站不站得下人</summary>
    public static bool Fits(Vector3 feet, Transform ignore = null)
    {
        Vector3 p0 = feet + Vector3.up * (ProbeLift + ProbeRadius);
        Vector3 p1 = feet + Vector3.up * (Height - ProbeRadius);

        // 用带缓冲区的版本：这函数每 0.5 秒要被问十来次，别每次都新分配一个数组
        int n = Physics.OverlapCapsuleNonAlloc(p0, p1, ProbeRadius, _hits, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < n; i++)
        {
            var col = _hits[i];
            if (col == null || col.isTrigger) continue;
            if (ignore != null && col.transform.IsChildOf(ignore)) continue;    // 玩家自己的碰撞体

            return false;
        }

        return true;
    }

    /// <summary>身边是不是"怎么走都出不去"（被关在屋顶 / 屋子 / 神像壳子里了）。
    /// 近处（1.7 米）和远处（3.2 米）两圈都得走不通才算 —— 站在沟里 / 坡上时远处总有方向是通的，
    /// 所以不会在野外误判</summary>
    public static bool Enclosed(Vector3 feet, Transform ignore) =>
        RingBlocked(feet, ignore, 1.7f) && RingBlocked(feet, ignore, 3.2f);

    static bool RingBlocked(Vector3 feet, Transform ignore, float radius)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 0.25f;
            var candidate = new Vector3(feet.x + Mathf.Cos(a) * radius, feet.y, feet.z + Mathf.Sin(a) * radius);

            if (Fits(candidate, ignore)) return false;      // 有一条路能走 → 没被困住
        }

        return true;
    }

    /// <summary>绕开卡住的地方找一块站得下人的位置：由近到远一圈圈试，找到就返回 true。
    /// 找不到（比如整个人被埋在一整块实体里）由调用方自己决定退路 —— 一般就贴回地形表面</summary>
    public static bool TryFindSpot(Vector3 feet, Transform ignore, out Vector3 spot)
    {
        float[] radii = { 1.6f, 2.6f, 4f, 6f, 9f, 13f };

        foreach (float radius in radii)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                var candidate = new Vector3(feet.x + Mathf.Cos(a) * radius, feet.y, feet.z + Mathf.Sin(a) * radius);

                if (!Fits(candidate, ignore)) continue;

                spot = candidate;
                return true;
            }
        }

        spot = feet;
        return false;
    }
}
