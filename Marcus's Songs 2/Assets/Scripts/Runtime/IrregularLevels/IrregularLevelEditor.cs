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
        public int activeLayerIndex = -1;
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
    Vector3 cameraPanOffset;
    Vector2 lastMouse;
    bool panningCamera;
    Vector2 panelScroll;
    GUIStyle titleStyle, hintStyle;
    string status = "Choose a grid slice to define a rotating layer.";
    readonly List<string> undoHistory = new List<string>();
    readonly List<string> redoHistory = new List<string>();
    readonly List<int> saveSlots = new List<int>();
    int selectedSaveSlotIndex = -1;
    int pendingDeleteSlot = -1;
    string slotNameInput = "New Level";
    string lastHistoryState;
    bool applyingHistory;
    static Mesh scenePreviewMesh;
    static string scenePreviewMeshKey;
    static readonly List<Vector3> scenePreviewLines = new List<Vector3>();

    RotationLayer ActiveLayer => activeLayerIndex >= 0 && activeLayerIndex < layers.Count ? layers[activeLayerIndex] : null;

    internal static void DrawScenePreview(IList<Vector3Int> cells, float size, Color color)
    {
        if (cells == null || cells.Count == 0) return;
        var ordered = new List<Vector3Int>(cells);
        ordered.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
        string key = size.ToString("R") + ":" + string.Join(";", ordered.ConvertAll(cell => $"{cell.x},{cell.y},{cell.z}"));
        if (scenePreviewMesh == null) scenePreviewMesh = new Mesh { name = "Irregular Level Scene Preview" };
        if (key != scenePreviewMeshKey)
        {
            var occupied = new HashSet<Vector3Int>(ordered);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var lineSegments = new List<Vector3>();
            // Use full cell faces for the preview shell so adjacent boundary faces meet
            // without transparent gaps when Unity draws the combined gizmo mesh.
            float half = size * 0.5f;
            Vector3Int[] normals = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            Vector3Int[] uAxes = { Vector3Int.up, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 1), Vector3Int.right, Vector3Int.right, Vector3Int.up };
            Vector3Int[] vAxes = { new Vector3Int(0, 0, 1), Vector3Int.up, Vector3Int.right, new Vector3Int(0, 0, 1), Vector3Int.up, Vector3Int.right };
            foreach (Vector3Int cell in ordered)
                for (int face = 0; face < normals.Length; face++)
                {
                    if (occupied.Contains(cell + normals[face])) continue;
                    Vector3 center = (Vector3)cell * size;
                    Vector3 normal = normals[face];
                    Vector3 u = uAxes[face];
                    Vector3 v = vAxes[face];
                    Vector3 faceCenter = center + normal * half;
                    Vector3 a = faceCenter - u * half - v * half;
                    Vector3 b = faceCenter + u * half - v * half;
                    Vector3 c = faceCenter + u * half + v * half;
                    Vector3 d = faceCenter - u * half + v * half;
                    int first = vertices.Count;
                    vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                    triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                    triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
                    lineSegments.Add(a); lineSegments.Add(b); lineSegments.Add(b); lineSegments.Add(c);
                    lineSegments.Add(c); lineSegments.Add(d); lineSegments.Add(d); lineSegments.Add(a);
                }

            scenePreviewMesh.Clear();
            if (vertices.Count > 65535) scenePreviewMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            scenePreviewMesh.SetVertices(vertices);
            scenePreviewMesh.SetTriangles(triangles, 0);
            scenePreviewMesh.RecalculateNormals();
            scenePreviewMesh.RecalculateBounds();
            scenePreviewMeshKey = key;
            scenePreviewMesh.hideFlags = HideFlags.HideAndDontSave;
            scenePreviewLines.Clear();
            scenePreviewLines.AddRange(lineSegments);
        }

        Gizmos.color = color;
        Gizmos.DrawMesh(scenePreviewMesh);
        Gizmos.color = Color.Lerp(color, Color.black, 0.16f);
        for (int i = 0; i < scenePreviewLines.Count; i += 2)
            Gizmos.DrawLine(scenePreviewLines[i], scenePreviewLines[i + 1]);
    }

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
        RefreshSaveSlots();
        if (loadExampleOnStart)
        {
            if (saveSlots.Count > 0) LoadSlot(saveSlots[saveSlots.Count - 1]);
            else if (File.Exists(Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json"))) LoadLevel();
            else CreateExample();
        }
        UpdateCamera();
        UpdateAxisLine();
        lastHistoryState = CaptureEditorState();
    }

    struct GUIEnabledScope : System.IDisposable
    {
        readonly bool old;
        public GUIEnabledScope(bool enabled) { old = GUI.enabled; GUI.enabled = enabled; }
        public void Dispose() { GUI.enabled = old; }
    }
}

