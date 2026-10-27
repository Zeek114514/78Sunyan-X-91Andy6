using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional training visit. Suspends the existing world instead of rebuilding it.</summary>
public sealed class TutorialPortal : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    readonly List<GameObject> suspended = new List<GameObject>();
    GameObject player;
    Camera view;
    VoxelCharacterSheet roster;
    Mesh mesh;
    bool visiting;
    string error;
    Material savedSky;
    GUIStyle label, button;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { IsOpen = false; }

    public static void Create(OpenWorld world, GameObject player, Camera camera)
    {
        var owner = new GameObject("Optional tutorial portal");
        var portal = owner.AddComponent<TutorialPortal>();
        portal.player = player; portal.view = camera;
        portal.roster = player.GetComponent<VoxelCharacterSheet>();
        Vector3 position = world.SpawnPoint + new Vector3(-6, 0, 6);
        position.y = world.SurfaceY(position.x, position.z);
        owner.transform.position = position;
        var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
        LowPoly.Cylinder(vertices, colors, triangles, Vector3.zero, .16f, 2f, 1.85f, 24, new Color(.7f, .7f, .62f), true);
        LowPoly.Ring(vertices, colors, triangles, Vector3.up * 2.15f, 1.5f, 1.76f, 40,
            Quaternion.Euler(90, 0, 0), new Color(.2f, .8f, .88f));
        LowPoly.Ring(vertices, colors, triangles, Vector3.up * 2.15f, 1.5f, 1.76f, 40,
            Quaternion.Euler(-90, 0, 0), new Color(.2f, .8f, .88f));
        LowPoly.Ring(vertices, colors, triangles, Vector3.up * 2.15f + Vector3.forward * .08f, 1.64f, 1.84f, 40,
            Quaternion.Euler(90, 0, 0), new Color(.85f, .75f, .4f));
        LowPoly.Ring(vertices, colors, triangles, Vector3.up * 2.15f + Vector3.forward * .08f, 1.64f, 1.84f, 40,
            Quaternion.Euler(-90, 0, 0), new Color(.85f, .75f, .4f));
        portal.mesh = LowPoly.BuildMesh("Tutorial portal frame", vertices, colors, triangles);
        owner.AddComponent<MeshFilter>().sharedMesh = portal.mesh;
        owner.AddComponent<MeshRenderer>().sharedMaterial = world.Material;
    }

    bool Nearby => player != null && Vector3.Distance(player.transform.position, transform.position) < 4f;
    void Update()
    {
        if (visiting) return;
        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
            return;
        }
        if (!Nearby || !VoxelPanels.CanUseInput || view == null) return;
        Vector3 direction = transform.position + Vector3.up * 2 - view.transform.position;
        if (Vector3.Dot(view.transform.forward, direction.normalized) > .75f && Input.GetKeyDown(KeyCode.E))
        {
            IsOpen = true; error = null;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
    }
    void Close()
    {
        IsOpen = false;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }
    void Enter()
    {
        IsOpen = false; visiting = true;
        GetComponent<Renderer>().enabled = false;
        savedSky = RenderSettings.skybox;
        suspended.Clear();
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (root == gameObject || !root.activeSelf) continue;
            suspended.Add(root);
            root.SetActive(false);
        }
        try { VoxelTutorial.Begin(Return, true); }
        catch (Exception exception)
        {
            var partial = FindObjectOfType<VoxelTutorial>();
            if (partial != null) Destroy(partial.gameObject);
            Return(); error = "训练场未能打开：" + exception.Message;
            IsOpen = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Debug.LogException(exception);
        }
    }
    void Return()
    {
        foreach (var root in suspended) if (root != null) root.SetActive(true);
        suspended.Clear();
        if (roster != null) roster.ResumeAfterTraining();
        RenderSettings.skybox = savedSky;
        visiting = false;
        GetComponent<Renderer>().enabled = true;
        // Tutorial owns the final camera handover and restores player input afterwards.
    }
    void OnGUI()
    {
        if (visiting || view == null) return;
        VoxelTypography.Apply();
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 18 };
            label.normal.textColor = Color.white;
            button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
        }
        if (IsOpen)
        {
            int depth = GUI.depth; GUI.depth = -500;
            try
            {
                VoxelHud.FillRect(new Rect(0, 0, Screen.width, Screen.height), new Color(.03f, .07f, .1f, .94f));
                float width = Mathf.Min(520, Screen.width - 32), x = (Screen.width - width) * .5f, y = Mathf.Max(12, (Screen.height - 250) * .5f);
                GUI.Label(new Rect(x, y, width, 105), error ?? "新手训练传送门\n进入可选教程练习基础操作。\n原世界角色、背包与当前世界会保留。", label);
                if (GUI.Button(new Rect(x, y + 125, width, 44), "进入新手教程", button)) Enter();
                if (GUI.Button(new Rect(x, y + 181, width, 40), "取消", button)) Close();
            }
            finally { GUI.depth = depth; }
        }
        else if (!VoxelPanels.InputBlocked)
        {
            Vector3 point = view.WorldToScreenPoint(transform.position + Vector3.up * 4.25f);
            if (point.z > 0 && point.z < 22)
                GUI.Label(new Rect(point.x - 130, Screen.height - point.y, 260, 54), Nearby ? "新手训练传送门\n对准传送门，按 E 选择进入" : "新手训练传送门", label);
        }
    }
    void OnDestroy()
    {
        IsOpen = false;
        if (mesh != null) Destroy(mesh);
    }
}
