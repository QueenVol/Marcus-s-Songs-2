using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A small runtime level-editing prototype for sparse, grid-aligned cube worlds.
/// Click a cube to place beside it, select it for the rotating group, or erase it.
/// </summary>
public class IrregularLevelEditor : MonoBehaviour
{
    public enum EditMode { Place, SelectLayer, SetSpawnPoint, Erase }
    public enum Axis { X, Y, Z }

    [System.Serializable]
    class SavedLevel
    {
        public List<Vector3Int> cells = new List<Vector3Int>();
        public List<SavedLayer> layers = new List<SavedLayer>();
        public List<Vector3Int> rotatingCells;
        public int axis;
        public Vector3Int axisPoint;
        public bool hasSpawnPoint;
        public Vector3Int spawnPoint;
        public Vector3Int spawnFaceNormal = Vector3Int.up;
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
        public readonly List<Transform> cubes = new List<Transform>();
    }

    [Header("Grid")]
    [SerializeField, Min(0.1f)] float cellSize = 1f;
    [Header("Default irregular example")]
    [SerializeField] bool loadExampleOnStart = true;
    [SerializeField] float turnDuration = 0.45f;
    [Header("Colors")]
    [SerializeField] Color cubeColor = new Color(0.72f, 0.77f, 0.83f);
    [SerializeField] Color selectedColor = new Color(1f, 0.26f, 0.18f);
    [SerializeField] Color axisColor = new Color(0.12f, 0.64f, 1f);
    [SerializeField] Color spawnPointColor = new Color(1f, 0.8f, 0.12f);
    [SerializeField] Color gridColor = new Color(0.4f, 0.52f, 0.65f, 0.25f);

    readonly Dictionary<Vector3Int, Transform> cells = new Dictionary<Vector3Int, Transform>();
    readonly List<RotationLayer> layers = new List<RotationLayer>();
    int activeLayerIndex = -1;
    Camera viewCamera;
    Material cubeMaterial, selectedMaterial, axisMaterial, gridMaterial;
    Material spawnPointMaterial;
    LineRenderer axisLine;
    Transform spawnCube;
    Transform spawnMarker;
    Vector3Int spawnFaceNormal = Vector3Int.up;
    EditMode mode = EditMode.SelectLayer;
    Vector3Int placementCoordinate = new Vector3Int(3, 0, 0);
    string placementX = "3", placementY = "0", placementZ = "0";
    bool rotating;
    float orbitYaw = -34f, orbitPitch = 24f, orbitDistance = 12f;
    Vector2 lastMouse;
    Vector2 panelScroll;
    GUIStyle titleStyle, hintStyle;
    string status = "Select cubes to define a rotating layer.";

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
        selectedMaterial = MakeMaterial(selectedColor);
        axisMaterial = MakeMaterial(axisColor);
        gridMaterial = MakeMaterial(gridColor);
        spawnPointMaterial = MakeMaterial(spawnPointColor);
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

    Material MakeMaterial(Color color)
    {
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", 0.18f);
        if (color.a < 1f)
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
        return material;
    }

    void CreateExample()
    {
        // A stepped, incomplete structure. The central X slice is preselected.
        Vector3Int[] example =
        {
            new(-1,0,0), new(-1,1,0), new(-1,2,0), new(-1,0,1), new(-1,1,1),
            new(0,0,-1), new(0,1,-1), new(0,2,-1), new(0,0,0), new(0,1,0), new(0,2,0), new(0,0,1), new(0,1,1),
            new(1,0,0), new(1,1,0), new(1,2,0), new(1,1,1),
            new(2,0,0), new(2,1,0)
        };
        foreach (Vector3Int cell in example) AddCell(cell);
        AddLayer("Layer 1");
        foreach (Vector3Int cell in example)
            if (cell.x == 0) SetSelected(cell, true);
        SetSpawnPoint(new Vector3Int(0, 2, 0), Vector3Int.up);
    }

    void AddCell(Vector3Int cell)
    {
        if (cells.ContainsKey(cell)) return;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"Cube {cell.x},{cell.y},{cell.z}";
        cube.transform.SetParent(transform, false);
        cube.transform.localPosition = CellToLocal(cell);
        cube.transform.localScale = Vector3.one * (cellSize * 0.94f);
        cube.GetComponent<Renderer>().sharedMaterial = cubeMaterial;
        cells.Add(cell, cube.transform);
        UpdateCellColor(cell);
        if (viewCamera != null) UpdateCamera();
    }

    void RemoveCell(Vector3Int cell)
    {
        if (!cells.TryGetValue(cell, out Transform cube)) return;
        cells.Remove(cell);
        if (spawnCube == cube) SetSpawnPoint(null);
        foreach (RotationLayer layer in layers) layer.cubes.Remove(cube);
        Destroy(cube.gameObject);
        if (viewCamera != null) UpdateCamera();
    }

    Vector3 CellToLocal(Vector3Int cell) => new Vector3(cell.x, cell.y, cell.z) * cellSize;
    Vector3Int LocalToCell(Vector3 local) => new Vector3Int(
        Mathf.RoundToInt(local.x / cellSize),
        Mathf.RoundToInt(local.y / cellSize),
        Mathf.RoundToInt(local.z / cellSize));

    void SetSelected(Vector3Int cell, bool selected)
    {
        if (ActiveLayer == null || !cells.TryGetValue(cell, out Transform cube)) return;
        if (selected)
        {
            if (!ActiveLayer.cubes.Contains(cube)) ActiveLayer.cubes.Add(cube);
        }
        else
        {
            ActiveLayer.cubes.Remove(cube);
        }
        UpdateCellColor(cell);
    }

    void AddLayer(string layerName)
    {
        layers.Add(new RotationLayer { name = layerName, axisPoint = Vector3Int.zero });
        activeLayerIndex = layers.Count - 1;
        RefreshCellColors();
        UpdateAxisLine();
    }

    void RefreshCellColors()
    {
        foreach (Vector3Int cell in cells.Keys) UpdateCellColor(cell);
    }

    void SetSpawnPoint(Vector3Int? cell, Vector3Int faceNormal = default)
    {
        if (spawnMarker != null) Destroy(spawnMarker.gameObject);
        spawnMarker = null;
        spawnCube = null;
        if (faceNormal == Vector3Int.zero) faceNormal = Vector3Int.up;
        if (!cell.HasValue || !cells.TryGetValue(cell.Value, out spawnCube)) return;
        spawnFaceNormal = SnapToFace(faceNormal);

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "Player Spawn Face Marker";
        marker.transform.SetParent(spawnCube, false);
        marker.transform.localPosition = (Vector3)spawnFaceNormal * 0.515f;
        marker.transform.localRotation = Quaternion.FromToRotation(Vector3.up, spawnFaceNormal);
        marker.transform.localScale = new Vector3(0.72f, 0.025f, 0.72f);
        marker.GetComponent<Renderer>().sharedMaterial = spawnPointMaterial;
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null) Destroy(markerCollider);
        spawnMarker = marker.transform;
    }

    static Vector3Int SnapToFace(Vector3 value)
    {
        if (value.sqrMagnitude < 0.001f) return Vector3Int.up;
        value.Normalize();
        if (Mathf.Abs(value.x) > Mathf.Abs(value.y) && Mathf.Abs(value.x) > Mathf.Abs(value.z))
            return value.x > 0f ? Vector3Int.right : Vector3Int.left;
        if (Mathf.Abs(value.y) > Mathf.Abs(value.z))
            return value.y > 0f ? Vector3Int.up : Vector3Int.down;
        return value.z > 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
    }

    void UpdateCellColor(Vector3Int cell)
    {
        if (!cells.TryGetValue(cell, out Transform cube)) return;
        bool selected = ActiveLayer != null && ActiveLayer.cubes.Contains(cube);
        cube.GetComponent<Renderer>().sharedMaterial = selected ? selectedMaterial : cubeMaterial;
    }

    void CreateAxisLine()
    {
        var axisObject = new GameObject("Rotation Axis (blue)");
        axisObject.transform.SetParent(transform, false);
        axisLine = axisObject.AddComponent<LineRenderer>();
        axisLine.useWorldSpace = false;
        axisLine.positionCount = 2;
        axisLine.startWidth = cellSize * 0.075f;
        axisLine.endWidth = cellSize * 0.075f;
        axisLine.material = axisMaterial;
        axisLine.numCapVertices = 4;
    }

    void UpdateAxisLine()
    {
        if (axisLine == null) return;
        if (ActiveLayer == null) { axisLine.enabled = false; return; }
        axisLine.enabled = true;
        Vector3Int direction = AxisVector(ActiveLayer.axis);
        Vector3 center = CellToLocal(ActiveLayer.axisPoint);
        float halfLength = cellSize * 2f;
        foreach (Vector3Int cell in cells.Keys)
            halfLength = Mathf.Max(halfLength, Vector3.Distance(CellToLocal(cell), center) + cellSize * 2f);
        Vector3 offset = (Vector3)direction * halfLength;
        axisLine.SetPosition(0, center - offset);
        axisLine.SetPosition(1, center + offset);
    }

    static Vector3Int AxisVector(Axis value)
    {
        if (value == Axis.X) return Vector3Int.right;
        if (value == Axis.Y) return Vector3Int.up;
        return new Vector3Int(0, 0, 1);
    }

    Vector3Int RotateCell(Vector3Int cell, int quarterTurns)
    {
        RotationLayer layer = ActiveLayer;
        Vector3Int p = cell - layer.axisPoint;
        quarterTurns = ((quarterTurns % 4) + 4) % 4;
        for (int i = 0; i < quarterTurns; i++)
        {
            if (layer.axis == Axis.X) p = new Vector3Int(p.x, -p.z, p.y);
            else if (layer.axis == Axis.Y) p = new Vector3Int(p.z, p.y, -p.x);
            else p = new Vector3Int(-p.y, p.x, p.z);
        }
        return p + layer.axisPoint;
    }

    bool CanRotate(int quarterTurns, out string reason)
    {
        RotationLayer layer = ActiveLayer;
        if (layer == null || layer.cubes.Count == 0)
        {
            reason = "Select at least one cube for the rotating layer.";
            return false;
        }

        var destinations = new HashSet<Vector3Int>();
        var movingCubes = new HashSet<Transform>(layer.cubes);
        foreach (Transform cube in layer.cubes)
        {
            if (cube == null) continue;
            Vector3Int source = LocalToCell(cube.localPosition);
            Vector3Int destination = RotateCell(source, quarterTurns);
            if (!destinations.Add(destination))
            {
                reason = "Two cubes in the layer would land in the same grid cell.";
                return false;
            }
            if (cells.TryGetValue(destination, out Transform occupant) && !movingCubes.Contains(occupant))
            {
                reason = $"Rotation blocked: destination {destination} is occupied by a stationary cube.";
                return false;
            }
        }

        reason = "Ready to rotate.";
        return true;
    }

    void RotateLayer(int quarterTurns)
    {
        if (rotating)
        {
            status = "Wait for the current rotation to finish.";
            return;
        }
        if (!CanRotate(quarterTurns, out string reason))
        {
            status = reason;
            return;
        }
        StartCoroutine(AnimateRotation(quarterTurns));
    }

    System.Collections.IEnumerator AnimateRotation(int quarterTurns)
    {
        rotating = true;
        RotationLayer layer = ActiveLayer;
        var originalPositions = new Dictionary<Transform, Vector3Int>();
        foreach (Transform cube in layer.cubes)
            if (cube != null) originalPositions[cube] = LocalToCell(cube.localPosition);

        var pivot = new GameObject("Temporary Layer Pivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = CellToLocal(layer.axisPoint);
        pivot.localRotation = Quaternion.identity;
        foreach (Transform cube in layer.cubes)
            if (cube != null) cube.SetParent(pivot, true);

        Vector3 axisDirection = AxisVector(layer.axis);
        Quaternion from = pivot.localRotation;
        Quaternion to = from * Quaternion.AngleAxis(quarterTurns * 90f, axisDirection);
        float elapsed = 0f;
        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;
            pivot.localRotation = Quaternion.Slerp(from, to, Mathf.Clamp01(elapsed / turnDuration));
            yield return null;
        }
        pivot.localRotation = to;

        foreach (KeyValuePair<Transform, Vector3Int> entry in originalPositions)
            cells.Remove(entry.Value);
        foreach (Transform cube in layer.cubes)
        {
            if (cube == null) continue;
            cube.SetParent(transform, true);
            Vector3Int newCell = LocalToCell(cube.localPosition);
            cube.localPosition = CellToLocal(newCell);
            cube.localRotation = Quaternion.Euler(
                Mathf.Round(cube.localEulerAngles.x / 90f) * 90f,
                Mathf.Round(cube.localEulerAngles.y / 90f) * 90f,
                Mathf.Round(cube.localEulerAngles.z / 90f) * 90f);
            cells[newCell] = cube;
        }

        Destroy(pivot.gameObject);
        rotating = false;
        UpdateCamera();
        status = $"Rotated {layer.name}: {layer.cubes.Count} cubes by {quarterTurns * 90}°.";
    }

    void Update()
    {
        if (viewCamera == null) return;
        if (Input.GetMouseButtonDown(1)) lastMouse = Input.mousePosition;
        if (Input.GetMouseButton(1))
        {
            Vector2 current = Input.mousePosition;
            Vector2 delta = current - lastMouse;
            orbitYaw += delta.x * 0.35f;
            orbitPitch = Mathf.Clamp(orbitPitch - delta.y * 0.25f, -10f, 70f);
            lastMouse = current;
            UpdateCamera();
        }
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f && !PointerOverEditorPanel())
        {
            orbitDistance = Mathf.Clamp(orbitDistance - scroll * 0.6f, 5f, 24f);
            UpdateCamera();
        }

        if (rotating || EventSystemIsOverGUI()) return;
        if (Input.GetMouseButtonDown(0)) HandleSceneClick();
    }

    bool EventSystemIsOverGUI()
    {
        return PointerOverEditorPanel();
    }

    bool PointerOverEditorPanel()
    {
        Vector3 mouse = Input.mousePosition;
        float guiY = Screen.height - mouse.y;
        return mouse.x < 326f && guiY < Screen.height - 18f;
    }

    void HandleSceneClick()
    {
        Ray ray = viewCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;
        Transform cube = hit.collider.transform;
        if (cube.parent != transform) return;
        Vector3Int cell = LocalToCell(cube.localPosition);
        if (mode == EditMode.SelectLayer)
            SetSelected(cell, ActiveLayer != null && ActiveLayer.cubes.Contains(cube) == false);
        else if (mode == EditMode.SetSpawnPoint)
        {
            Vector3Int faceNormal = SnapToFace(cube.InverseTransformDirection(hit.normal));
            SetSpawnPoint(cell, faceNormal);
            status = $"Player spawn point set to {cell}, face {faceNormal}.";
        }
        else if (mode == EditMode.Erase)
            RemoveCell(cell);
        else
        {
            Vector3 localNormal = transform.InverseTransformDirection(hit.normal).normalized;
            Vector3Int neighbor = cell + new Vector3Int(
                Mathf.RoundToInt(localNormal.x),
                Mathf.RoundToInt(localNormal.y),
                Mathf.RoundToInt(localNormal.z));
            if (cells.ContainsKey(neighbor))
            {
                status = "That grid cell is already occupied.";
                return;
            }
            AddCell(neighbor);
            status = $"Placed cube at {neighbor}.";
        }
    }

    void UpdateCamera()
    {
        Vector3 center = MapCenter();
        Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        viewCamera.transform.position = center + rotation * new Vector3(0f, 0f, -orbitDistance);
        viewCamera.transform.rotation = rotation;
        viewCamera.fieldOfView = 45f;
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = new Color(0.055f, 0.075f, 0.11f);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = gridColor;
        var preview = new HashSet<Vector3Int>();
        if (Application.isPlaying && cells.Count > 0)
        {
            foreach (Vector3Int occupied in cells.Keys)
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                            preview.Add(occupied + new Vector3Int(x, y, z));
        }
        else
        {
            for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                    for (int z = -2; z <= 2; z++)
                        preview.Add(new Vector3Int(x, y, z));
        }
        foreach (Vector3Int cell in preview)
        {
            if (Application.isPlaying && cells.ContainsKey(cell)) continue;
            Gizmos.DrawWireCube(CellToLocal(cell), Vector3.one * cellSize * 0.9f);
        }
    }

    void OnGUI()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.82f, 0.87f, 0.94f) } };
        }

        GUILayout.BeginArea(new Rect(18, 18, 310, Screen.height - 36), GUI.skin.box);
        panelScroll = GUILayout.BeginScrollView(panelScroll, GUILayout.Width(290), GUILayout.Height(Screen.height - 56));
        GUILayout.Label("Irregular Cube Level Editor", titleStyle);
        GUILayout.Label("Unlimited grid · selected cubes rotate 90° around the blue axis", hintStyle);
        GUILayout.Space(8);
        GUILayout.Label($"Mode: {mode}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(mode == EditMode.Place, "Place", GUI.skin.button)) mode = EditMode.Place;
        if (GUILayout.Toggle(mode == EditMode.SelectLayer, "Select layer", GUI.skin.button)) mode = EditMode.SelectLayer;
        if (GUILayout.Toggle(mode == EditMode.SetSpawnPoint, "Set spawn", GUI.skin.button)) mode = EditMode.SetSpawnPoint;
        if (GUILayout.Toggle(mode == EditMode.Erase, "Erase", GUI.skin.button)) mode = EditMode.Erase;
        GUILayout.EndHorizontal();
        GUILayout.Label(mode == EditMode.SetSpawnPoint
            ? "Click a cube face to set the player start. The gold tile marks that face."
            : "Click a cube to use the selected mode.", hintStyle);
        GUILayout.Label(spawnCube == null
            ? "Player spawn: not set"
            : $"Player spawn: {LocalToCell(spawnCube.localPosition)}, face {spawnFaceNormal}", hintStyle);
        GUILayout.Space(8);

        GUILayout.Label("Rotation layers");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ New layer")) AddLayer($"Layer {layers.Count + 1}");
        using (new GUIEnabledScope(ActiveLayer != null))
            if (GUILayout.Button("Delete"))
            {
                layers.RemoveAt(activeLayerIndex);
                activeLayerIndex = layers.Count == 0 ? -1 : Mathf.Clamp(activeLayerIndex, 0, layers.Count - 1);
                RefreshCellColors(); UpdateAxisLine(); status = "Deleted the active layer.";
            }
        GUILayout.EndHorizontal();
        if (layers.Count > 0)
        {
            string[] layerNames = layers.ConvertAll(layer => layer.name).ToArray();
            int selectedLayer = GUILayout.SelectionGrid(activeLayerIndex, layerNames, Mathf.Min(3, layerNames.Length));
            if (selectedLayer != activeLayerIndex)
            {
                activeLayerIndex = selectedLayer;
                RefreshCellColors(); UpdateAxisLine();
            }
        }
        else GUILayout.Label("Create a layer to select cubes and rotate them.", hintStyle);

        RotationLayer active = ActiveLayer;
        using (new GUIEnabledScope(active != null))
        {
        GUILayout.Label(active == null ? "Rotation axis: —" : $"{active.name} axis: {active.axis}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(active != null && active.axis == Axis.X, "X axis", GUI.skin.button)) active.axis = Axis.X;
        if (GUILayout.Toggle(active != null && active.axis == Axis.Y, "Y axis", GUI.skin.button)) active.axis = Axis.Y;
        if (GUILayout.Toggle(active != null && active.axis == Axis.Z, "Z axis", GUI.skin.button)) active.axis = Axis.Z;
        GUILayout.EndHorizontal();
        if (active != null && active.axis == Axis.X)
        {
            active.axisPoint.y = AxisCoordinateRow("Axis Y", active.axisPoint.y);
            active.axisPoint.z = AxisCoordinateRow("Axis Z", active.axisPoint.z);
        }
        else if (active != null && active.axis == Axis.Y)
        {
            active.axisPoint.x = AxisCoordinateRow("Axis X", active.axisPoint.x);
            active.axisPoint.z = AxisCoordinateRow("Axis Z", active.axisPoint.z);
        }
        else if (active != null)
        {
            active.axisPoint.x = AxisCoordinateRow("Axis X", active.axisPoint.x);
            active.axisPoint.y = AxisCoordinateRow("Axis Y", active.axisPoint.y);
        }
        UpdateAxisLine();
        }

        GUILayout.Space(8);
        GUILayout.Label("Place at any grid coordinate");
        GUILayout.BeginHorizontal();
        GUILayout.Label("X", GUILayout.Width(14));
        placementX = GUILayout.TextField(placementX, GUILayout.Width(48));
        GUILayout.Label("Y", GUILayout.Width(14));
        placementY = GUILayout.TextField(placementY, GUILayout.Width(48));
        GUILayout.Label("Z", GUILayout.Width(14));
        placementZ = GUILayout.TextField(placementZ, GUILayout.Width(48));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Place cube at coordinate"))
        {
            if (!int.TryParse(placementX, out int x) || !int.TryParse(placementY, out int y) || !int.TryParse(placementZ, out int z))
                status = "Enter whole numbers for X, Y, and Z.";
            else
            {
                placementCoordinate = new Vector3Int(x, y, z);
                if (cells.ContainsKey(placementCoordinate)) status = $"Cell {placementCoordinate} is already occupied.";
                else
                {
                    AddCell(placementCoordinate);
                    status = $"Placed cube at {placementCoordinate}.";
                }
            }
        }

        GUILayout.BeginHorizontal();
        using (new GUIEnabledScope(!rotating && active != null && CanRotate(1, out _)))
            if (GUILayout.Button("Rotate +90°", GUILayout.Height(34))) RotateLayer(1);
        using (new GUIEnabledScope(!rotating && active != null && CanRotate(-1, out _)))
            if (GUILayout.Button("Rotate -90°", GUILayout.Height(34))) RotateLayer(-1);
        GUILayout.EndHorizontal();
        GUILayout.Label($"Active layer cubes: {(active == null ? 0 : active.cubes.Count)}", hintStyle);

        GUILayout.Space(8);
        if (GUILayout.Button("Clear selection"))
        {
            if (active != null) active.cubes.Clear();
            RefreshCellColors();
            status = "Layer selection cleared.";
        }
        if (GUILayout.Button("Reset example"))
        {
            foreach (Transform cube in new List<Transform>(cells.Values)) Destroy(cube.gameObject);
            cells.Clear(); layers.Clear(); activeLayerIndex = -1;
            CreateExample();
            status = "Restored the irregular example.";
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save level")) SaveLevel();
        if (GUILayout.Button("Load level")) LoadLevel();
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Play saved level"))
        {
            SaveLevel();
            SceneManager.LoadScene("IrregularLevelPlay");
        }

        GUILayout.Space(8);
        GUILayout.Label(status, hintStyle);
        GUILayout.Label("Right-drag: orbit camera   ·   Mouse wheel: zoom", hintStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    int AxisCoordinateRow(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(68));
        if (GUILayout.Button("−", GUILayout.Width(28))) value--;
        GUILayout.Label(value.ToString(), GUILayout.Width(40));
        if (GUILayout.Button("+", GUILayout.Width(28))) value++;
        GUILayout.EndHorizontal();
        return value;
    }

    Vector3 MapCenter()
    {
        if (cells.Count == 0) return Vector3.zero;
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3Int cell in cells.Keys)
        {
            Vector3 position = CellToLocal(cell);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        return (min + max) * 0.5f;
    }

    void SaveLevel()
    {
        var data = new SavedLevel();
        foreach (Vector3Int cell in cells.Keys) data.cells.Add(cell);
        if (spawnCube != null)
        {
            data.hasSpawnPoint = true;
            data.spawnPoint = LocalToCell(spawnCube.localPosition);
            Vector3 faceInLevelSpace = transform.InverseTransformDirection(spawnCube.TransformDirection(spawnFaceNormal));
            data.spawnFaceNormal = SnapToFace(faceInLevelSpace);
        }
        foreach (RotationLayer layer in layers)
        {
            var saved = new SavedLayer { name = layer.name, axis = (int)layer.axis, axisPoint = layer.axisPoint };
            foreach (Transform cube in layer.cubes)
                if (cube != null) saved.cells.Add(LocalToCell(cube.localPosition));
            data.layers.Add(saved);
        }
        string path = Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json");
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        status = $"Level saved to {path}";
    }

    void LoadLevel()
    {
        string path = Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json");
        if (!File.Exists(path))
        {
            status = "No saved level found yet. Use Save level first.";
            return;
        }

        SavedLevel data = JsonUtility.FromJson<SavedLevel>(File.ReadAllText(path));
        if (data == null)
        {
            status = "Saved level could not be read.";
            return;
        }
        foreach (Transform cube in new List<Transform>(cells.Values)) Destroy(cube.gameObject);
        SetSpawnPoint(null);
        cells.Clear();
        layers.Clear(); activeLayerIndex = -1;
        foreach (Vector3Int cell in data.cells) AddCell(cell);
        if (data.layers != null && data.layers.Count > 0)
            foreach (SavedLayer saved in data.layers)
            {
                AddLayer(string.IsNullOrEmpty(saved.name) ? $"Layer {layers.Count + 1}" : saved.name);
                ActiveLayer.axis = (Axis)Mathf.Clamp(saved.axis, 0, 2);
                ActiveLayer.axisPoint = saved.axisPoint;
                if (saved.cells != null)
                    foreach (Vector3Int cell in saved.cells)
                        if (cells.TryGetValue(cell, out Transform cube) && !ActiveLayer.cubes.Contains(cube)) ActiveLayer.cubes.Add(cube);
            }
        else if (data.rotatingCells != null && data.rotatingCells.Count > 0)
        {
            AddLayer("Layer 1");
            ActiveLayer.axis = (Axis)Mathf.Clamp(data.axis, 0, 2);
            ActiveLayer.axisPoint = data.axisPoint;
            foreach (Vector3Int cell in data.rotatingCells)
                if (cells.TryGetValue(cell, out Transform cube)) ActiveLayer.cubes.Add(cube);
        }
        if (data.hasSpawnPoint && cells.ContainsKey(data.spawnPoint)) SetSpawnPoint(data.spawnPoint, data.spawnFaceNormal);
        RefreshCellColors();
        UpdateAxisLine();
        status = "Loaded the saved irregular level.";
    }

    struct GUIEnabledScope : System.IDisposable
    {
        readonly bool old;
        public GUIEnabledScope(bool enabled) { old = GUI.enabled; GUI.enabled = enabled; }
        public void Dispose() { GUI.enabled = old; }
    }
}
