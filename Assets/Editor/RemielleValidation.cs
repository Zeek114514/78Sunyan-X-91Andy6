using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RemielleValidation
{
    const string Request = "Artifacts/Remielle/validate.request";
    static double nextCheck;
    static RemielleValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request); Run();
    }

    [MenuItem("VoxelDemo/Diagnostics/Validate local Remielle character")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Remielle validation waits for edit mode; the running game was preserved.");
            return;
        }
        Directory.CreateDirectory("Artifacts/Remielle");
        var report = new StringBuilder("utc=" + DateTime.UtcNow.ToString("O") + "\nunity=" + Application.unityVersion + "\n");
        try
        {
            AssetDatabase.ImportAsset(LocalRemielle.ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(LocalRemielle.ModelPath);
            if (template == null) throw new Exception("PMX imported model missing.");
            var renderer = template.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null) throw new Exception("Skinned mesh missing.");
            var mesh = renderer.sharedMesh;
            if (mesh.vertexCount != 61822 || mesh.triangles.Length / 3 != 77986) throw new Exception("Geometry differs from checked source PMX.");
            if (renderer.bones.Any(b => b == null)) throw new Exception("Missing skin bone.");
            if (mesh.bindposes.Length != renderer.bones.Length) throw new Exception("Invalid bind poses.");
            foreach (var w in mesh.boneWeights)
            {
                if (Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1) > .001f) throw new Exception("Unnormalized bone weight.");
                if (w.boneIndex0 >= renderer.bones.Length || w.boneIndex1 >= renderer.bones.Length ||
                    w.boneIndex2 >= renderer.bones.Length || w.boneIndex3 >= renderer.bones.Length) throw new Exception("Invalid skin joint index.");
            }
            report.AppendLine("vertices=" + mesh.vertexCount + "\ntriangles=" + mesh.triangles.Length / 3 + "\nskinBones=" + renderer.bones.Length);
            report.AppendLine("materials=" + renderer.sharedMaterials.Length);
            File.WriteAllLines("Artifacts/Remielle/rig.txt", renderer.bones.Select((bone, index) =>
                index + ": " + bone.name + " parent=" + (bone.parent != null ? bone.parent.name : "-") +
                " pos=" + bone.position + " vertices=" + mesh.boneWeights.Count(w =>
                    w.boneIndex0 == index && w.weight0 > .01f || w.boneIndex1 == index && w.weight1 > .01f ||
                    w.boneIndex2 == index && w.weight2 > .01f || w.boneIndex3 == index && w.weight3 > .01f)));
            File.WriteAllLines("Artifacts/Remielle/materials.txt", renderer.sharedMaterials.Select(m =>
                m.name + " color=" + m.GetColor("_BaseColor")));
            var metadata = AssetDatabase.LoadAllAssetsAtPath(LocalRemielle.ModelPath).OfType<TextAsset>()
                .First(a => a.name == "Textured material indices");
            var texturedMaterials = metadata.text.Split(',').Select(int.Parse).ToArray();
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                var material = renderer.sharedMaterials[i];
                if (material == null || material.shader == null || ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Broken character material.");
                if (texturedMaterials.Contains(i) && material.GetTexture("_BaseMap") == null) throw new Exception("Missing diffuse map: " + material.name);
            }
            report.AppendLine("texturedMaterials=" + texturedMaterials.Length + "; all source diffuse maps resolved");
            CheckRoster(report);
            Capture(template, report);
            CaptureMotion(template, report);
            report.AppendLine("result=PASS (native importer, skin, textures, roster and posed render; no gameplay input simulation)");
        }
        catch (Exception e) { report.AppendLine("result=FAIL\n" + e); Debug.LogException(e); }
        finally { File.WriteAllText("Artifacts/Remielle/editor-validation.txt", report.ToString()); }
    }

    static void CheckRoster(StringBuilder report)
    {
        var owner = new GameObject("Local character roster check") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var inventory = owner.AddComponent<VoxelInteraction>();
            var sheet = owner.AddComponent<VoxelCharacterSheet>();
            sheet.InitTraining(inventory);
            var state = sheet.Roster.FirstOrDefault(s => s.Info.Id == LocalRemielle.CharacterId);
            if (state == null) throw new Exception("Test character missing from roster.");
            sheet.SetActive(state); sheet.Rebuild();
            if (sheet.Active != state) throw new Exception("Test character selection did not survive reopening the list.");
            report.AppendLine("roster=available; selection=retained; saveWrites=disabled for fixture");
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    static void Capture(GameObject template, StringBuilder report)
    {
        // Fixture singletons would disturb a running session, so validation is scheduled for edit mode.
        var preview = new PreviewRenderUtility();
        GameObject instance = null; Mesh baked = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(template);
            preview.AddSingleGO(instance);
            var actor = instance.AddComponent<LocalRemielle>();
            var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            baked = new Mesh();
            actor.SamplePose(0, false); renderer.BakeMesh(baked);
            var idle = baked.vertices;
            actor.SamplePose(.25f, true); renderer.BakeMesh(baked);
            var walking = baked.vertices;
            int moved = idle.Where((v, i) => (v - walking[i]).sqrMagnitude > .000001f).Count();
            if (moved < 100) throw new Exception("Procedural walking did not deform the skin.");
            report.AppendLine("walkingDeformedVertices=" + moved + "\nanimation=procedural test motion (not original game clips)");
            preview.camera.orthographic = true; preview.camera.orthographicSize = 1;
            preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 10;
            preview.camera.transform.position = new Vector3(0, .9f, -3);
            preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.14f, .18f, .24f);
            // Edit-mode skin updates are not scheduled like the player loop. Render the
            // exact baked pose measured above instead of a stale GPU skin buffer.
            var surface = new GameObject("Posed validation surface");
            surface.transform.SetParent(instance.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = baked;
            surface.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            renderer.enabled = false;
            foreach (bool walk in new[] { false, true })
            {
                actor.SamplePose(walk ? .25f : 0, walk);
                renderer.BakeMesh(baked);
                // Warm the texture uploads before saving the first frame.
                preview.BeginStaticPreview(new Rect(0, 0, 1400, 900));
                preview.Render(true);
                UnityEngine.Object.DestroyImmediate(preview.EndStaticPreview());
                preview.BeginStaticPreview(new Rect(0, 0, 1400, 900));
                preview.Render(true);
                var image = preview.EndStaticPreview();
                File.WriteAllBytes("Artifacts/Remielle/" + (walk ? "walk" : "idle") + ".png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
            preview.Cleanup();
        }
    }

    static void CaptureMotion(GameObject template, StringBuilder report)
    {
        var preview = new PreviewRenderUtility();
        GameObject instance = null;
        Mesh mesh = null, floorMesh = null;
        GameObject floor = null;
        Material floorMaterial = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(template);
            preview.AddSingleGO(instance);
            var actor = instance.AddComponent<LocalRemielle>();
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            mesh = new Mesh();
            var surface = new GameObject("Motion baked surface");
            surface.transform.SetParent(instance.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            skin.enabled = false;
            // A fixed world grid makes support-foot drift visible in the moving-camera preview.
            floor = new GameObject("Motion preview grid");
            floorMesh = new Mesh();
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var colors = new System.Collections.Generic.List<Color>();
            for (int x = -6; x < 6; x++) for (int z = -12; z < 6; z++)
            {
                int n = vertices.Count;
                vertices.Add(new Vector3(x * .5f, -.006f, z * .5f));
                vertices.Add(new Vector3(x * .5f, -.006f, (z + 1) * .5f));
                vertices.Add(new Vector3((x + 1) * .5f, -.006f, (z + 1) * .5f));
                vertices.Add(new Vector3((x + 1) * .5f, -.006f, z * .5f));
                triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
                Color color = (x + z) % 2 == 0 ? new Color(.23f, .27f, .32f) : new Color(.20f, .24f, .29f);
                for (int v = 0; v < 4; v++) colors.Add(color);
            }
            floorMesh.SetVertices(vertices); floorMesh.SetTriangles(triangles, 0); floorMesh.SetColors(colors);
            floor.AddComponent<MeshFilter>().sharedMesh = floorMesh;
            floorMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
            floorMaterial.SetInt("_Cull", 0); floorMaterial.SetInt("_ZWrite", 1);
            floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            preview.AddSingleGO(floor);
            var camera = preview.camera;
            camera.orthographic = true; camera.orthographicSize = 1.05f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 20;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.14f, .18f, .24f);
            Directory.CreateDirectory("Artifacts/Remielle/MotionFrames");
            float maxStep = 0, maxRotation = 0;
            // Sample the actual live motion driver at 60Hz for a complete 20 second cycle.
            for (int step = 0; step <= 1200; step++)
            {
                Vector3 before = instance.transform.position;
                Quaternion rotation = instance.transform.rotation;
                if (step > 0) actor.AdvanceDemo(1f / 60);
                maxStep = Mathf.Max(maxStep, Vector3.Distance(before, instance.transform.position));
                maxRotation = Mathf.Max(maxRotation, Quaternion.Angle(rotation, instance.transform.rotation));
                if (step % 2 != 0) continue;
                skin.BakeMesh(mesh);
                foreach (var vertex in mesh.vertices)
                    if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z))
                        throw new Exception("Non-finite motion vertex.");
                Vector3 focus = instance.transform.position + Vector3.up * .86f;
                camera.transform.position = focus + new Vector3(2, .55f, -3.3f);
                camera.transform.LookAt(focus);
                preview.BeginStaticPreview(new Rect(0, 0, 640, 400)); preview.Render(true);
                var image = preview.EndStaticPreview();
                File.WriteAllBytes("Artifacts/Remielle/MotionFrames/" + (step / 2).ToString("D3") + ".png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            report.AppendLine("motionSeconds=20; simulationHz=60; renderedFrames=601; previewFps=30");
            report.AppendLine("maxRootStep=" + maxStep + "; maxTurnDegreesPerStep=" + maxRotation);
            report.AppendLine("maxFootTargetError=" + actor.Motion.MaximumFootError +
                "; plantedSamples=" + actor.Motion.PlantedSamples + "; maxPlantedFootDrift=" + actor.Motion.MaximumPlantDrift);
            if (maxStep > .02f || maxRotation > 4) throw new Exception("Locomotion has a discontinuous root transition.");
            if (actor.Motion.MaximumFootError > .04f) throw new Exception("Foot target exceeds reachable leg range.");
            if (actor.Motion.PlantedSamples < 50 || actor.Motion.MaximumPlantDrift > .02f)
                throw new Exception("Planted foot did not remain stable during support.");
            if (instance.transform.position.magnitude > .002f || Quaternion.Angle(instance.transform.rotation, Quaternion.identity) > .1f)
                throw new Exception("Demo cycle did not return to its starting pose location.");
        }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
            if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
            if (floorMesh != null) UnityEngine.Object.DestroyImmediate(floorMesh);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            preview.Cleanup();
        }
    }
}
