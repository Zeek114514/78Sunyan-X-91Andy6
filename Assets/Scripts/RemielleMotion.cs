using UnityEngine;

/// <summary>Authored local test motion. Not an animation extracted from ZZZ.</summary>
public sealed class RemielleMotion
{
    sealed class Joint
    {
        public Transform Transform;
        public Quaternion Rest;
        public Vector3 Position;
        public Joint(Transform value)
        {
            Transform = value;
            if (value == null) return;
            Rest = value.localRotation; Position = value.localPosition;
        }
        public void Pose(float x, float y, float z)
        { if (Transform != null) Transform.localRotation = Rest * Quaternion.Euler(x, y, z); }
    }

    sealed class Leg
    {
        public Joint Hip, Knee, Ankle;
        public Vector3 RestFoot, Pin;
        public Quaternion RestFootRotation;
        public bool Planted, HasSwung;
        public Leg(Joint hip, Joint knee, Joint ankle, Transform root)
        {
            Hip = hip; Knee = knee; Ankle = ankle;
            if (!Valid) return;
            RestFoot = root.InverseTransformPoint(ankle.Transform.position);
            RestFootRotation = Quaternion.Inverse(root.rotation) * ankle.Transform.rotation;
        }
        public bool Valid => Hip.Transform != null && Knee.Transform != null && Ankle.Transform != null;
    }

    readonly Transform root;
    readonly Joint pelvis, spine, chest, head, leftArm, rightArm, leftElbow, rightElbow;
    readonly Joint[] hair, skirt;
    readonly Joint leftShoulder, rightShoulder, leftWrist, rightWrist;
    readonly Joint[] leftFingers, rightFingers;
    readonly Joint leftEye, rightEye;
    readonly SkinnedMeshRenderer face;
    readonly int blinkShape = -1, smileShape = -1;
    readonly Leg left, right;
    Vector3 pelvisRest;
    Vector2 follow, followVelocity;
    float movement, phase, lean;
    public float MaximumFootError { get; private set; }
    public int PlantedSamples { get; private set; }
    public float MaximumPlantDrift { get; private set; }

    public RemielleMotion(Transform owner)
    {
        root = owner;
        leftEye = Find("左目"); rightEye = Find("右目");
        face = owner.GetComponentInChildren<SkinnedMeshRenderer>();
        if (face != null && face.sharedMesh != null)
        {
            blinkShape = Shape("まばたき", "瞬き", "blink", "闭眼");
            smileShape = Shape("にこり", "smile", "微笑");
        }
        pelvis = Find("腰"); spine = Find("上半身"); chest = Find("上半身2"); head = Find("頭");
        leftArm = Find("左腕"); rightArm = Find("右腕");
        leftElbow = Find("左ひじ"); rightElbow = Find("右ひじ");
        leftShoulder = Find("左肩"); rightShoulder = Find("右肩");
        leftWrist = Find("左手首"); rightWrist = Find("右手首");
        leftFingers = new[] { Find("左人指１"), Find("左中指１"), Find("左薬指１"), Find("左小指１") };
        rightFingers = new[] { Find("右人指１"), Find("右中指１"), Find("右薬指１"), Find("右小指１") };
        left = new Leg(Find("左足D"), Find("左ひざD"), Find("左足首D"), root);
        right = new Leg(Find("右足D"), Find("右ひざD"), Find("右足首D"), root);
        if (pelvis.Transform != null) pelvisRest = pelvis.Position;
        // A few strand roots only: rotating every descendant compounds the motion.
        hair = new[] { Find("Ctr_B_HairA_01"), Find("Ctr_B_HairB_01"), Find("Ctr_F_Hair_01") };
        skirt = new[] { Find("Skn_B_Skirt_01"), Find("Skn_L_SkirtD_01"), Find("Skn_R_SkirtD_01") };
    }

    Joint Find(string name)
    {
        foreach (var bone in root.GetComponentsInChildren<Transform>(true))
            if (bone.name == name) return new Joint(bone);
        return new Joint(null);
    }

    int Shape(params string[] aliases)
    {
        foreach (string alias in aliases)
        for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
        {
            string name = face.sharedMesh.GetBlendShapeName(i);
            int colon = name.IndexOf(':');
            if (colon >= 0) name = name.Substring(colon + 1);
            foreach (string part in name.Split('|'))
                if (string.Equals(part.Trim(), alias, System.StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    public void Sample(float time, float amount, float gaitPhase)
    {
        movement = amount; phase = gaitPhase;
        Pose(time, 0, 0, null, false);
    }

    public void Advance(float time, float dt, float speed, float acceleration, float turning, OpenWorld world)
    {
        movement = Mathf.SmoothDamp(movement, Mathf.Clamp01(speed / .65f), ref blendVelocity, .18f, 10, dt);
        // 60cm per full gait cycle; 60% support travels 36cm, matching root displacement.
        phase += speed * dt / .6f * Mathf.PI * 2;
        lean = Mathf.Lerp(lean, Mathf.Clamp(acceleration * 2, -3, 3), 1 - Mathf.Exp(-dt * 8));
        Vector2 target = new Vector2(-lean + Mathf.Sin(phase - .7f) * movement * 1.2f, turning * -2.5f);
        follow = Vector2.SmoothDamp(follow, target, ref followVelocity, .28f, 30, dt);
        Pose(time, turning, lean, world, true);
    }
    float blendVelocity;

    void Pose(float time, float turning, float accelerationLean, OpenWorld world, bool pinFeet)
    {
        float swing = Mathf.Sin(phase) * movement;
        float breathing = Mathf.Sin(time * 1.8f);
        float idleWeight = Mathf.Sin(time * .7f) * (1 - movement);
        // Local gestures enter and leave with zero velocity; no random pose jumps.
        float idleTime = Mathf.Repeat(time, 18f);
        float glance = Gesture(idleTime, 5f, 10f) * (1 - movement);
        float settle = Gesture(idleTime, 12f, 17f) * (1 - movement);
        // Brief blink, then a longer open-eye interval; no random changes across pause/resume.
        float blinkTime = Mathf.Repeat(time + .8f, 4.7f);
        float blink = Gesture(blinkTime, 0, .24f);
        if (face != null)
        {
            if (blinkShape >= 0) face.SetBlendShapeWeight(blinkShape, blink * 100);
            if (smileShape >= 0) face.SetBlendShapeWeight(smileShape, settle * 28);
        }
        float gaze = Mathf.Sin(time * .43f) * 1.5f + glance * 3;
        leftEye.Pose(Mathf.Sin(time * .31f) * .6f, gaze, 0);
        rightEye.Pose(Mathf.Sin(time * .31f) * .6f, gaze, 0);
        float turnStep = Mathf.Sin(time * Mathf.PI * 3.3f);
        if (pelvis.Transform != null)
        {
            // Shift onto the support leg; keep enough knee flexion for both feet to reach the floor.
            pelvis.Transform.localPosition = pelvisRest + new Vector3(
                idleWeight * .008f + Mathf.Cos(phase) * movement * .012f,
                -.018f + breathing * .002f - movement * (.012f + .008f * (1 - Mathf.Cos(phase * 2))), 0);
            pelvis.Pose(0, -swing * 2, swing * 1.3f);
        }
        spine.Pose(breathing * .7f + movement * 2 + accelerationLean, swing * 2 + turning * 5, -idleWeight);
        chest.Pose(0, swing * 1.5f + turning * 4, -swing * .7f);
        head.Pose(-breathing * .4f + glance * 1.5f, Mathf.Sin(time * .5f) * 3 * (1 - movement) + turning * 12 + glance * 9,
            idleWeight * .6f - glance * 2);
        leftShoulder.Pose(breathing * .35f, -settle * 1.5f, -settle * 1.2f);
        rightShoulder.Pose(-breathing * .35f, settle * 1.5f, settle * 1.2f);
        leftArm.Pose(-swing * 14 + breathing * .5f, 0, -27 + idleWeight);
        rightArm.Pose(swing * 14 - breathing * .5f, 0, 27 + idleWeight);
        leftElbow.Pose(0, -movement * 3, -4 - movement * (3 + Mathf.Max(0, -Mathf.Sin(phase - .35f)) * 5));
        rightElbow.Pose(0, movement * 3, 4 + movement * (3 + Mathf.Max(0, Mathf.Sin(phase - .35f)) * 5));
        float wristLag = Mathf.Sin(phase - .65f) * movement;
        leftWrist.Pose(breathing * .4f, -wristLag * 2, -2 - settle * 2);
        rightWrist.Pose(-breathing * .4f, wristLag * 2, 2 + settle * 2);
        for (int i = 0; i < leftFingers.Length; i++)
        {
            float curl = 4 + i * 1.2f + movement * 2 + settle * 2;
            leftFingers[i].Pose(0, 0, -curl);
            rightFingers[i].Pose(0, 0, curl);
        }
        foreach (var strand in hair) strand.Pose(follow.x, 0, follow.y);
        foreach (var panel in skirt) panel.Pose(follow.x * .65f - movement * 2, 0, follow.y * .5f);
        Foot(left, Mathf.Repeat(phase / (Mathf.PI * 2), 1), turning, turnStep, world, pinFeet);
        Foot(right, Mathf.Repeat(phase / (Mathf.PI * 2) + .5f, 1), turning, -turnStep, world, pinFeet);
    }

    void Foot(Leg leg, float cycle, float turning, float turnStep, OpenWorld world, bool pinFeet)
    {
        if (!leg.Valid) return;
        leg.Hip.Pose(0, 0, 0); leg.Knee.Pose(0, 0, 0); leg.Ankle.Pose(0, 0, 0);
        bool support = cycle < .6f;
        if (!support) leg.HasSwung = true;
        float swingTime = Mathf.Clamp01((cycle - .6f) / .4f);
        float z = support ? Mathf.Lerp(-.18f, .18f, cycle / .6f)
            : Mathf.Lerp(.18f, -.18f, Smooth(swingTime));
        // Zero vertical speed at lift-off/touchdown, with a level ankle through support.
        float lift = Mathf.Pow(Mathf.Sin(swingTime * Mathf.PI), 2) * .065f * movement;
        lift += Mathf.Max(0, turnStep) * Mathf.Abs(turning) * .025f;
        Vector3 local = leg.RestFoot + new Vector3(0, lift, z * movement);
        Vector3 target = root.TransformPoint(local);
        if (world != null) target.y += world.SurfaceY(target.x, target.z) - root.position.y;
        // The first step starts from a neutral stance, not a front contact pose.
        // Wait for its first swing before locking a complete support interval.
        if (movement < .05f || Mathf.Abs(turning) > .05f) leg.HasSwung = false;
        bool planting = pinFeet && leg.HasSwung && support && movement > .2f && Mathf.Abs(turning) < .05f;
        if (planting)
        {
            if (!leg.Planted) leg.Pin = target;
            target = Vector3.Lerp(target, leg.Pin, Mathf.SmoothStep(0, 1, movement / .5f));
        }
        leg.Planted = planting;
        SolveLeg(leg, target);
        // Match the sole to the local terrain, with a conservative slope limit.
        Quaternion sole = root.rotation * leg.RestFootRotation;
        if (world != null)
        {
            const float probe = .12f;
            float dx = (world.SurfaceY(target.x + probe, target.z) - world.SurfaceY(target.x - probe, target.z)) / (2 * probe);
            float dz = (world.SurfaceY(target.x, target.z + probe) - world.SurfaceY(target.x, target.z - probe)) / (2 * probe);
            Vector3 normal = Vector3.RotateTowards(Vector3.up, new Vector3(-dx, 1, -dz).normalized, 20 * Mathf.Deg2Rad, 0);
            sole = Quaternion.FromToRotation(Vector3.up, normal) * sole;
        }
        // Toe clearance is only added in swing; planted ankle targets remain fixed.
        float clearance = Mathf.Sin(swingTime * Mathf.PI) * 5 * movement;
        leg.Ankle.Transform.rotation = sole * Quaternion.AngleAxis(-clearance, Vector3.right);
        float error = Vector3.Distance(leg.Ankle.Transform.position, target);
        MaximumFootError = Mathf.Max(MaximumFootError, error);
        if (planting && movement >= .5f)
        {
            PlantedSamples++;
            MaximumPlantDrift = Mathf.Max(MaximumPlantDrift, Vector3.Distance(leg.Ankle.Transform.position, leg.Pin));
        }
    }

    void SolveLeg(Leg leg, Vector3 target)
    {
        Transform a = leg.Hip.Transform, b = leg.Knee.Transform, c = leg.Ankle.Transform;
        Vector3 start = a.position, delta = target - start;
        float upper = Vector3.Distance(start, b.position), lower = Vector3.Distance(b.position, c.position);
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
        Vector3 direction = delta.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(-root.forward, direction).normalized;
        float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
        Vector3 knee = start + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        a.rotation = Quaternion.FromToRotation(b.position - start, knee - start) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
        c.rotation = root.rotation * leg.RestFootRotation;
    }

    public static float Smooth(float t)
    { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); }

    static float Gesture(float time, float start, float end)
    {
        if (time <= start || time >= end) return 0;
        float t = (time - start) / (end - start);
        return Smooth(Mathf.Min(t, 1 - t) * 2);
    }
}
