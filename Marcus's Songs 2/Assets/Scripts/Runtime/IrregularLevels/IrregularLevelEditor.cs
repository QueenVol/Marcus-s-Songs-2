using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A small runtime level-editing prototype for sparse, grid-aligned cube worlds.
/// Click a cube to place beside it, select it for the rotating group, or erase it.
/// </summary>
public partial class IrregularLevelEditor : MonoBehaviour
{
    public enum EditMode { Place, SetSpawnPoint, SetGoalPoint, Erase, PaintBlockedFace }
    public enum Axis { X, Y, Z }

    [System.Serializable]
    class BlockedFaceData
    {
        public Vector3Int cell;
        public Vector3Int normal;
    }

    [System.Serializable]
    class SavedLevel
    {
        public List<Vector3Int> cells = new List<Vector3Int>();
        public List<BlockedFaceData> blockedFaces = new List<BlockedFaceData>();
        // Migration for levels saved when black meant a whole cube.
        public List<Vector3Int> nonWalkableCells = new List<Vector3Int>();
        public List<SavedLayer> layers = new List<SavedLayer>();
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

    [System.Serializable]
    class SavedLayer
    {
        public string name;
        public int axis;
        public Vector3Int axisPoint;
        public List<Vector3Int> cells = new List<Vector3Int>();
    }

    class RotationLayer
    {
        public string name;
        public Axis axis = Axis.X;
        public Vector3Int axisPoint;
    }

    [Header("Grid")]
    [SerializeField, Min(0.1f)] float cellSize = 1f;
    [Header("Default irregular example")]
    [SerializeField] bool loadExampleOnStart = true;
    [SerializeField] float turnDuration = 0.45f;
    [Header("Colors")]
    [SerializeField] Color cubeColor = new Color(0.72f, 0.77f, 0.83f);
    [SerializeField] Color blockedFaceColor = new Color(0.025f, 0.03f, 0.04f);
    [SerializeField] Color selectedColor = new Color(1f, 0.26f, 0.18f);
    [SerializeField] Color axisColor = new Color(0.12f, 0.64f, 1f);
    [SerializeField] Color spawnPointColor = new Color(1f, 0.8f, 0.12f);
    [SerializeField] Color goalPointColor = new Color(0.95f, 0.3f, 0.85f);
    [SerializeField] Color gridColor = new Color(0.4f, 0.52f, 0.65f, 0.25f);

    readonly Dictionary<Vector3Int, Transform> cells = new Dictionary<Vector3Int, Transform>();
    readonly List<RotationLayer> layers = new List<RotationLayer>();
    int activeLayerIndex = -1;
    Camera viewCamera;
    Material cubeMaterial, selectedMaterial, axisMaterial, gridMaterial, blockedFaceMaterial;
    Material spawnPointMaterial, goalPointMaterial;
    LineRenderer axisLine;
    Transform spawnCube;
    Transform spawnMarker;
    Vector3Int spawnFaceNormal = Vector3Int.up;
    Transform goalCube;
    Transform goalMarker;
    Vector3Int goalFaceNormal = Vector3Int.up;
    EditMode mode = EditMode.Place;
    readonly List<BlockedFaceData> blockedFaces = new List<BlockedFaceData>();
    Vector3Int placementCoordinate = new Vector3Int(3, 0, 0);
    string placementX = "3", placementY = "0", placementZ = "0";
    bool rotating;
    float orbitYaw = -34f, orbitPitch = 24f, orbitDistance = 12f;
    Vector2 lastMouse;
    Vector2 panelScroll;
    GUIStyle titleStyle, hintStyle;
    string status = "Choose a grid slice to define a rotating layer.";

    RotationLayer ActiveLayer => activeLayerIndex >= 0 && activeLayerIndex < layers.Count ? layers[activeLayerIndex] : null;

    void Start()
    {
        viewCamera = Camera.main;
        if (viewCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            viewCamera = cameraObject.AddComponent<Camera>();
        }

        cubeMaterial = MakeMaterial(cubeColor);
        blockedFaceMaterial = MakeMaterial(blockedFaceColor);
        selectedMaterial = MakeMaterial(selectedColor);
        axisMaterial = MakeMaterial(axisColor);
        gridMaterial = MakeMaterial(gridColor);
        spawnPointMaterial = MakeMaterial(spawnPointColor);
        goalPointMaterial = MakeMaterial(goalPointColor);
        CreateAxisLine();
        if (loadExampleOnStart)
        {
            string savePath = Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json");
            if (File.Exists(savePath)) LoadLevel();
            else CreateExample();
        }
        UpdateCamera();
        UpdateAxisLine();
    }

    struct GUIEnabledScope : System.IDisposable
    {
        readonly bool old;
        public GUIEnabledScope(bool enabled) { old = GUI.enabled; GUI.enabled = enabled; }
        public void Dispose() { GUI.enabled = old; }
    }
}
