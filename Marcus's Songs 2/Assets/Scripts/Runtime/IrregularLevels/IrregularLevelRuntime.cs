using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Loads an editor-authored irregular level and provides basic face-grid movement and turns.</summary>
public partial class IrregularLevelRuntime : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Serializable]
    class BlockedFaceData
    {
        public Vector3Int cell;
        public Vector3Int normal;
    }

    [Serializable]
    class LevelData
    {
        public List<Vector3Int> cells = new List<Vector3Int>();
        public List<BlockedFaceData> blockedFaces = new List<BlockedFaceData>();
        public List<Vector3Int> nonWalkableCells = new List<Vector3Int>();
        public List<LayerData> layers = new List<LayerData>();
        public List<Vector3Int> rotatingCells;
        public int axis;
        public Vector3Int axisPoint;
        public bool hasSpawnPoint;
        public Vector3Int spawnPoint;
        public Vector3Int spawnFaceNormal = Vector3Int.up;
        public bool hasGoalPoint;
        public Vector3Int goalPoint;
        public Vector3Int goalFaceNormal = Vector3Int.up;
    }

    [Serializable]
    class LayerData
    {
        public string name;
        public int axis;
        public Vector3Int axisPoint;
        public List<Vector3Int> cells = new List<Vector3Int>();
    }

    class RuntimeLayer
    {
        public string name;
        public Axis axis;
        public Vector3Int axisPoint;
    }

    [Header("Level data")]
    [SerializeField] string saveFileName = "MarcusSongs_IrregularLevel.json";
    [SerializeField, Min(0.1f)] float cellSize = 1f;
    [Header("Player")]
    [SerializeField] Color playerColor = new Color(0.15f, 0.9f, 1f);
    [SerializeField] Color blockedFaceColor = new Color(0.025f, 0.03f, 0.04f);
    [SerializeField] Color goalPointColor = new Color(0.95f, 0.3f, 0.85f);
    [SerializeField, Min(0.1f)] float playerHeight = 0.54f;
    [SerializeField, Min(0.01f)] float moveDuration = 0.24f;
    [Header("Reachable face outline")]
    [SerializeField] Color outlineColor = Color.yellow;
    [SerializeField] Color outlineEmission = Color.yellow;
    [SerializeField, Min(0.001f)] float outlineThickness = 0.04f;
    [SerializeField, Min(0f)] float outlineSurfaceOffset = 0.002f;
    [Header("View")]
    [SerializeField] Color backgroundColor = new Color(0.055f, 0.075f, 0.12f);
    [SerializeField] Color axisColor = new Color(0.12f, 0.64f, 1f);
    [SerializeField] float turnDuration = 0.4f;
    [SerializeField, Min(0.01f)] float viewTurnDuration = 0.28f;
    [SerializeField, Min(0.1f)] float cameraPanSpeed = 7f;

    readonly Dictionary<Vector3Int, Transform> cells = new Dictionary<Vector3Int, Transform>();
    readonly List<BlockedFaceData> blockedFaces = new List<BlockedFaceData>();
    readonly List<RuntimeLayer> layers = new List<RuntimeLayer>();
    readonly List<GameObject> reachableHighlights = new List<GameObject>();
    Camera viewCamera;
    Transform worldRoot, playerToken, currentCube, goalCube, goalMarker;
    Vector3Int currentFace = Vector3Int.up;
    Vector3Int goalFace = Vector3Int.up;
    int activeLayerIndex;
    bool moving, rotating, rotatingView, levelCompleted;
    float viewQuarterTurns;
    float orbitYaw = 45f, orbitPitch = 30f, orbitDistance = 12f;
    Vector3 cameraCenter;
    Material cubeMaterial, playerMaterial, axisMaterial, outlineMaterial, blockedFaceMaterial, goalPointMaterial;
    Mesh outlineMesh;
    LineRenderer axisLine;
    readonly List<LineRenderer> layerOutlineEdges = new List<LineRenderer>();
    GUIStyle bodyStyle, rotationButtonStyle;
    Texture2D rotationButtonBackground;
    string status = "Click an adjacent face to move. Use the arrows beside the active layer to turn it.";

    RuntimeLayer ActiveLayer => activeLayerIndex >= 0 && activeLayerIndex < layers.Count ? layers[activeLayerIndex] : null;
    static Rect HudRect => new Rect(18f, 18f, 315f, 210f);

    void Start()
    {
        viewCamera = Camera.main;
        if (viewCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            viewCamera = cameraObject.AddComponent<Camera>();
        }
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = backgroundColor;
        viewCamera.farClipPlane = 500f;
        cubeMaterial = MakeMaterial(new Color(0.72f, 0.77f, 0.83f));
        blockedFaceMaterial = MakeMaterial(blockedFaceColor);
        goalPointMaterial = MakeMaterial(goalPointColor);
        playerMaterial = MakeMaterial(playerColor);
        axisMaterial = MakeMaterial(axisColor);
        outlineMaterial = MakeMaterial(outlineColor);
        outlineMaterial.EnableKeyword("_EMISSION");
        outlineMaterial.SetColor("_EmissionColor", outlineEmission);
        outlineMesh = CreateOutlineMesh();
        worldRoot = new GameObject("Loaded Irregular Level").transform;
        worldRoot.SetParent(transform, false);
        CreateAxisLine();
        CreateLayerOutline();
        LoadLevel();
    }

    Material MakeMaterial(Color color)
    {
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", 0.18f);
        return material;
    }

    void UpdateCamera()
    {
        Quaternion orbit = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        viewCamera.transform.position = cameraCenter + orbit * new Vector3(0f, 0f, -orbitDistance);
        viewCamera.transform.rotation = orbit;
        viewCamera.fieldOfView = 45f;
    }

    void SetInitialCameraCenter()
    {
        if (cells.Count == 0)
        {
            cameraCenter = Vector3.zero;
            return;
        }
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3Int cell in cells.Keys)
        {
            Vector3 position = CellToLocal(cell);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        cameraCenter = (min + max) * 0.5f;
    }

    void OnGUI()
    {
        if (bodyStyle == null)
        {
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.82f, 0.87f, 0.94f) } };
        }
        GUILayout.BeginArea(HudRect, GUI.skin.box);
        GUILayout.Label("Click an adjacent cube face to move. Reach the pink goal marker to finish. Press R to restart; WASD move the view; Q/E rotate it; wheel to zoom.", bodyStyle);
        GUILayout.Label("Yellow outlines show the faces you can move to next.", bodyStyle);
        GUILayout.Label("Keys 1–9 select a layer · click the arrows beside it to rotate ±90°", bodyStyle);
        if (GUILayout.Button("Back to level editor")) SceneManager.LoadScene("IrregularLevelEditor");
        GUILayout.EndArea();

        if (ActiveLayer != null && !moving && !rotating && !levelCompleted)
        {
            EnsureRotationButtonStyle();
            int clockwiseTurns = GetClockwiseTurnSign(ActiveLayer);
            if (CanRotate(-clockwiseTurns, out _) && GUI.Button(RotationButtonRect(-1), "↺", rotationButtonStyle)) RotateActiveLayer(-clockwiseTurns);
            if (CanRotate(clockwiseTurns, out _) && GUI.Button(RotationButtonRect(1), "↻", rotationButtonStyle)) RotateActiveLayer(clockwiseTurns);
        }
    }
}
