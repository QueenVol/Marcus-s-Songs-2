using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Loads an editor-authored irregular level and provides basic face-grid movement and turns.</summary>
public class IrregularLevelRuntime : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Serializable]
    class LevelData
    {
        public List<Vector3Int> cells = new List<Vector3Int>();
        public List<LayerData> layers = new List<LayerData>();
        public List<Vector3Int> rotatingCells;
        public int axis;
        public Vector3Int axisPoint;
        public bool hasSpawnPoint;
        public Vector3Int spawnPoint;
        public Vector3Int spawnFaceNormal = Vector3Int.up;
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
        public readonly List<Transform> cubes = new List<Transform>();
    }

    [Header("Level data")]
    [SerializeField] string saveFileName = "MarcusSongs_IrregularLevel.json";
    [SerializeField, Min(0.1f)] float cellSize = 1f;
    [Header("Player")]
    [SerializeField] Color playerColor = new Color(0.15f, 0.9f, 1f);
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

    readonly Dictionary<Vector3Int, Transform> cells = new Dictionary<Vector3Int, Transform>();
    readonly List<RuntimeLayer> layers = new List<RuntimeLayer>();
    readonly List<GameObject> reachableHighlights = new List<GameObject>();
    Camera viewCamera;
    Transform worldRoot, playerToken, currentCube;
    Vector3Int currentFace = Vector3Int.up;
    int activeLayerIndex;
    bool moving, rotating;
    float orbitYaw = -34f, orbitPitch = 24f, orbitDistance = 12f;
    Vector2 lastMouse;
    Material cubeMaterial, playerMaterial, axisMaterial, outlineMaterial;
    Mesh outlineMesh;
    LineRenderer axisLine;
    GUIStyle titleStyle, bodyStyle;
    string status = "Click an adjacent face to move. Q/E rotates the active layer.";

    RuntimeLayer ActiveLayer => activeLayerIndex >= 0 && activeLayerIndex < layers.Count ? layers[activeLayerIndex] : null;
    static Rect HudRect => new Rect(18f, 18f, 315f, 280f);

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
        playerMaterial = MakeMaterial(playerColor);
        axisMaterial = MakeMaterial(axisColor);
        outlineMaterial = MakeMaterial(outlineColor);
        outlineMaterial.EnableKeyword("_EMISSION");
        outlineMaterial.SetColor("_EmissionColor", outlineEmission);
        outlineMesh = CreateOutlineMesh();
        worldRoot = new GameObject("Loaded Irregular Level").transform;
        worldRoot.SetParent(transform, false);
        CreateAxisLine();
        LoadLevel();
    }

    Material MakeMaterial(Color color)
    {
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", 0.18f);
        return material;
    }

    string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

    void LoadLevel()
    {
        if (!File.Exists(SavePath))
        {
            status = $"No saved level found. Create and save one in the editor first. Expected: {SavePath}";
            return;
        }

        LevelData data;
        try { data = JsonUtility.FromJson<LevelData>(File.ReadAllText(SavePath)); }
        catch (Exception exception)
        {
            status = $"Could not read level: {exception.Message}";
            return;
        }
        if (data == null || data.cells == null)
        {
            status = "The saved level data is empty or invalid.";
            return;
        }

        foreach (Vector3Int cell in data.cells) AddCell(cell);
        if (data.layers != null && data.layers.Count > 0)
        {
            foreach (LayerData saved in data.layers)
            {
                var layer = new RuntimeLayer
                {
                    name = string.IsNullOrEmpty(saved.name) ? $"Layer {layers.Count + 1}" : saved.name,
                    axis = (Axis)Mathf.Clamp(saved.axis, 0, 2),
                    axisPoint = saved.axisPoint
                };
                if (saved.cells != null)
                    foreach (Vector3Int cell in saved.cells)
                        if (cells.TryGetValue(cell, out Transform cube) && !layer.cubes.Contains(cube)) layer.cubes.Add(cube);
                layers.Add(layer);
            }
        }
        else if (data.rotatingCells != null && data.rotatingCells.Count > 0)
        {
            var legacyLayer = new RuntimeLayer { name = "Layer 1", axis = (Axis)Mathf.Clamp(data.axis, 0, 2), axisPoint = data.axisPoint };
            foreach (Vector3Int cell in data.rotatingCells)
                if (cells.TryGetValue(cell, out Transform cube)) legacyLayer.cubes.Add(cube);
            layers.Add(legacyLayer);
        }
        if (layers.Count > 0) activeLayerIndex = 0;
        if (data.hasSpawnPoint && cells.TryGetValue(data.spawnPoint, out currentCube))
        {
            currentFace = SnapToFace(data.spawnFaceNormal);
            if (IsOpenFace(currentCube, currentFace)) CreatePlayer();
            else
            {
                currentCube = null;
                status = "The saved spawn face is blocked by another cube. Choose an exposed face in the editor and save again.";
            }
        }
        else status = "Level loaded, but no valid player spawn face is set. Set one in the editor and save again.";
        UpdateAxisLine();
        UpdateCamera();
    }

    void AddCell(Vector3Int cell)
    {
        if (cells.ContainsKey(cell)) return;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"Cube {cell.x},{cell.y},{cell.z}";
        cube.transform.SetParent(worldRoot, false);
        cube.transform.localPosition = CellToLocal(cell);
        cube.transform.localScale = Vector3.one * (cellSize * 0.94f);
        cube.GetComponent<Renderer>().sharedMaterial = cubeMaterial;
        cells.Add(cell, cube.transform);
    }

    void CreatePlayer()
    {
        var tokenObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tokenObject.name = "Player";
        playerToken = tokenObject.transform;
        playerToken.SetParent(currentCube, false);
        playerToken.localPosition = (Vector3)currentFace * playerHeight;
        playerToken.localRotation = Quaternion.FromToRotation(Vector3.up, currentFace);
        playerToken.localScale = new Vector3(0.46f, 0.025f, 0.46f);
        tokenObject.GetComponent<Renderer>().sharedMaterial = playerMaterial;
        Collider collider = tokenObject.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        status = "Click an adjacent face to move. Q/E rotates the active layer.";
        RefreshReachableHighlights();
    }

    void RefreshReachableHighlights()
    {
        foreach (GameObject highlight in reachableHighlights)
            if (highlight != null) Destroy(highlight);
        reachableHighlights.Clear();
        if (currentCube == null || moving || rotating) return;

        Vector3Int[] faceNormals =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };
        foreach (Transform cube in cells.Values)
        {
            foreach (Vector3Int face in faceNormals)
            {
                if (!IsOpenFace(cube, face) || !IsAdjacent(cube, face)) continue;
                var outline = new GameObject("Reachable Face Outline");
                outline.transform.SetParent(cube, false);
                outline.transform.localPosition = (Vector3)face * (0.5f + outlineSurfaceOffset);
                Vector3 up = Mathf.Abs(face.x) > 0.5f ? Vector3.up
                    : Mathf.Abs(face.y) > 0.5f ? Vector3.forward : Vector3.up;
                outline.transform.localRotation = Quaternion.LookRotation(face, up);
                outline.AddComponent<MeshFilter>().sharedMesh = outlineMesh;
                outline.AddComponent<MeshRenderer>().sharedMaterial = outlineMaterial;
                reachableHighlights.Add(outline);
            }
        }
    }

    Mesh CreateOutlineMesh()
    {
        float outer = 0.5f + outlineSurfaceOffset * 0.5f;
        float inner = outer - outlineThickness;
        var mesh = new Mesh { name = "Reachable Face Yellow Outline" };
        mesh.vertices = new Vector3[]
        {
            new(-outer,inner,0), new(outer,inner,0), new(outer,outer,0), new(-outer,outer,0),
            new(inner,-inner,0), new(outer,-inner,0), new(outer,inner,0), new(inner,inner,0),
            new(-outer,-inner,0), new(-inner,-inner,0), new(-inner,inner,0), new(-outer,inner,0),
            new(-outer,-outer,0), new(outer,-outer,0), new(outer,-inner,0), new(-outer,-inner,0)
        };
        mesh.triangles = new[]
        {
            0,1,2, 0,2,3,
            4,5,6, 4,6,7,
            8,9,10, 8,10,11,
            12,13,14, 12,14,15
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void CreateAxisLine()
    {
        var axisObject = new GameObject("Active Layer Axis");
        axisObject.transform.SetParent(worldRoot, false);
        axisLine = axisObject.AddComponent<LineRenderer>();
        axisLine.useWorldSpace = false;
        axisLine.positionCount = 2;
        axisLine.startWidth = cellSize * 0.065f;
        axisLine.endWidth = cellSize * 0.065f;
        axisLine.material = axisMaterial;
        axisLine.numCapVertices = 4;
    }

    void UpdateAxisLine()
    {
        if (axisLine == null) return;
        RuntimeLayer layer = ActiveLayer;
        axisLine.enabled = layer != null;
        if (layer == null) return;
        Vector3 direction = AxisVector(layer.axis);
        Vector3 center = CellToLocal(layer.axisPoint);
        float length = cellSize * 2f;
        foreach (Vector3Int cell in cells.Keys)
            length = Mathf.Max(length, Vector3.Distance(CellToLocal(cell), center) + cellSize * 2f);
        Vector3 offset = direction * length;
        axisLine.SetPosition(0, center - offset);
        axisLine.SetPosition(1, center + offset);
    }

    void Update()
    {
        if (viewCamera == null) return;
        if (Input.GetMouseButtonDown(1) && !PointerOverHud(Input.mousePosition)) lastMouse = Input.mousePosition;
        if (Input.GetMouseButton(1) && !PointerOverHud(Input.mousePosition))
        {
            Vector2 mouse = Input.mousePosition;
            Vector2 delta = mouse - lastMouse;
            orbitYaw += delta.x * 0.35f;
            orbitPitch = Mathf.Clamp(orbitPitch - delta.y * 0.25f, -10f, 70f);
            lastMouse = mouse;
            UpdateCamera();
        }
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f && !PointerOverHud(Input.mousePosition))
        {
            orbitDistance = Mathf.Clamp(orbitDistance - scroll * 0.6f, 4f, 40f);
            UpdateCamera();
        }

        if (Input.GetKeyDown(KeyCode.Q)) RotateActiveLayer(-1);
        if (Input.GetKeyDown(KeyCode.E)) RotateActiveLayer(1);
        for (int i = 0; i < Mathf.Min(9, layers.Count); i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) SelectLayer(i);

        if (!moving && !rotating && Input.GetMouseButtonDown(0) && !PointerOverHud(Input.mousePosition)) TryMovePlayer(Input.mousePosition);
    }

    bool PointerOverHud(Vector3 screenPoint)
    {
        float guiY = Screen.height - screenPoint.y;
        return HudRect.Contains(new Vector2(screenPoint.x, guiY));
    }

    void TryMovePlayer(Vector3 screenPoint)
    {
        if (playerToken == null || currentCube == null)
        {
            status = "Player is not on a valid spawn face.";
            return;
        }
        Ray ray = viewCamera.ScreenPointToRay(screenPoint);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, viewCamera.cullingMask))
        {
            status = "Click did not hit a grid face.";
            return;
        }
        Transform targetCube = hit.collider.transform;
        if (targetCube.parent != worldRoot)
        {
            status = "The clicked object is not a grid cube.";
            return;
        }
        Vector3Int targetFace = SnapToFace(targetCube.InverseTransformDirection(hit.normal));
        if (!IsOpenFace(targetCube, targetFace))
        {
            status = "That face is blocked by another cube.";
            return;
        }
        if (!IsAdjacent(targetCube, targetFace))
        {
            status = "That face is open, but it does not share an edge with the player's face.";
            return;
        }
        StartCoroutine(MovePlayer(targetCube, targetFace));
    }

    bool IsOpenFace(Transform cube, Vector3Int normal)
    {
        Vector3Int coordinate = LocalToCell(cube.localPosition);
        Vector3Int outward = SnapToFace(worldRoot.InverseTransformDirection(cube.TransformDirection(normal)));
        return !cells.ContainsKey(coordinate + outward);
    }

    bool IsAdjacent(Transform targetCube, Vector3Int targetFace)
    {
        Vector3Int currentWorldNormal = SnapToFace(worldRoot.InverseTransformDirection(currentCube.TransformDirection(currentFace)));
        Vector3Int targetWorldNormal = SnapToFace(worldRoot.InverseTransformDirection(targetCube.TransformDirection(targetFace)));
        if (targetCube == currentCube)
            return DotInt(currentWorldNormal, targetWorldNormal) == 0;

        Vector3Int cellDelta = LocalToCell(targetCube.localPosition) - LocalToCell(currentCube.localPosition);
        if (currentWorldNormal == targetWorldNormal)
        {
            int gridDistance = Mathf.Abs(cellDelta.x) + Mathf.Abs(cellDelta.y) + Mathf.Abs(cellDelta.z);
            return gridDistance == 1 && DotInt(cellDelta, currentWorldNormal) == 0;
        }

        if (DotInt(currentWorldNormal, targetWorldNormal) != 0) return false;

        // Perpendicular face cells can share an edge across a level change too.
        // Doubled face-center coordinates keep this check exact on the integer grid.
        Vector3Int doubledFaceCenterDelta = cellDelta * 2 + targetWorldNormal - currentWorldNormal;
        return DotInt(doubledFaceCenterDelta, doubledFaceCenterDelta) == 2;
    }

    static int DotInt(Vector3Int a, Vector3Int b) => a.x * b.x + a.y * b.y + a.z * b.z;

    IEnumerator MovePlayer(Transform targetCube, Vector3Int targetFace)
    {
        moving = true;
        Vector3 start = playerToken.position;
        Vector3 end = targetCube.TransformPoint((Vector3)targetFace * playerHeight);
        Vector3 control = Vector3.zero;
        bool crossingEdge = targetCube == currentCube;
        if (crossingEdge)
        {
            Vector3 midpoint = currentCube.TransformPoint(((Vector3)currentFace + (Vector3)targetFace) * playerHeight);
            control = midpoint * 2f - (start + end) * 0.5f;
        }
        playerToken.SetParent(worldRoot, true);
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float smooth = t * t * (3f - 2f * t);
            if (crossingEdge)
            {
                float oneMinus = 1f - t;
                playerToken.position = oneMinus * oneMinus * start + 2f * oneMinus * t * control + t * t * end;
            }
            else playerToken.position = Vector3.Lerp(start, end, smooth);
            yield return null;
        }
        playerToken.SetParent(targetCube, false);
        playerToken.localPosition = (Vector3)targetFace * playerHeight;
        playerToken.localRotation = Quaternion.FromToRotation(Vector3.up, targetFace);
        currentCube = targetCube;
        currentFace = targetFace;
        moving = false;
        RefreshReachableHighlights();
        status = $"Moved to {LocalToCell(currentCube.localPosition)}, face {currentFace}.";
    }

    static Vector3Int SnapToFace(Vector3 value)
    {
        if (value.sqrMagnitude < 0.001f) return Vector3Int.up;
        value.Normalize();
        if (Mathf.Abs(value.x) > Mathf.Abs(value.y) && Mathf.Abs(value.x) > Mathf.Abs(value.z)) return value.x > 0 ? Vector3Int.right : Vector3Int.left;
        if (Mathf.Abs(value.y) > Mathf.Abs(value.z)) return value.y > 0 ? Vector3Int.up : Vector3Int.down;
        return value.z > 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
    }

    void SelectLayer(int index)
    {
        if (index < 0 || index >= layers.Count) return;
        activeLayerIndex = index;
        UpdateAxisLine();
        status = $"Active layer: {ActiveLayer.name}. Q/E rotates it by 90°.";
    }

    void RotateActiveLayer(int quarterTurns)
    {
        if (moving || rotating || ActiveLayer == null) return;
        if (!CanRotate(quarterTurns, out string reason)) { status = reason; return; }
        StartCoroutine(AnimateRotation(quarterTurns));
    }

    bool CanRotate(int turns, out string reason)
    {
        RuntimeLayer layer = ActiveLayer;
        if (layer == null || layer.cubes.Count == 0) { reason = "The active layer has no cubes."; return false; }
        var movingCubes = new HashSet<Transform>(layer.cubes);
        var destinations = new HashSet<Vector3Int>();
        foreach (Transform cube in layer.cubes)
        {
            if (cube == null) continue;
            Vector3Int destination = RotateCell(LocalToCell(cube.localPosition), layer, turns);
            if (!destinations.Add(destination)) { reason = "Rotation blocked: two cubes would overlap."; return false; }
            if (cells.TryGetValue(destination, out Transform occupant) && !movingCubes.Contains(occupant))
            { reason = $"Rotation blocked by cube at {destination}."; return false; }
        }
        reason = "Ready.";
        return true;
    }

    IEnumerator AnimateRotation(int turns)
    {
        rotating = true;
        RuntimeLayer layer = ActiveLayer;
        var oldPositions = new Dictionary<Transform, Vector3Int>();
        foreach (Transform cube in layer.cubes)
            if (cube != null) oldPositions[cube] = LocalToCell(cube.localPosition);
        var pivot = new GameObject("Layer Turn Pivot").transform;
        pivot.SetParent(worldRoot, false);
        pivot.localPosition = CellToLocal(layer.axisPoint);
        pivot.localRotation = Quaternion.identity;
        foreach (Transform cube in layer.cubes)
            if (cube != null) cube.SetParent(pivot, true);
        Vector3 direction = AxisVector(layer.axis);
        Quaternion from = pivot.localRotation;
        Quaternion to = from * Quaternion.AngleAxis(turns * 90f, direction);
        float elapsed = 0f;
        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;
            pivot.localRotation = Quaternion.Slerp(from, to, Mathf.Clamp01(elapsed / turnDuration));
            yield return null;
        }
        pivot.localRotation = to;
        foreach (KeyValuePair<Transform, Vector3Int> entry in oldPositions) cells.Remove(entry.Value);
        foreach (Transform cube in layer.cubes)
        {
            if (cube == null) continue;
            cube.SetParent(worldRoot, true);
            Vector3Int destination = LocalToCell(cube.localPosition);
            cube.localPosition = CellToLocal(destination);
            Vector3 angles = cube.localEulerAngles;
            cube.localRotation = Quaternion.Euler(Mathf.Round(angles.x / 90f) * 90f, Mathf.Round(angles.y / 90f) * 90f, Mathf.Round(angles.z / 90f) * 90f);
            cells[destination] = cube;
        }
        Destroy(pivot.gameObject);
        rotating = false;
        UpdateAxisLine();
        UpdateCamera();
        RefreshReachableHighlights();
        status = $"Rotated {layer.name} by {turns * 90}°. Player follows its cube if it moved.";
    }

    Vector3Int RotateCell(Vector3Int cell, RuntimeLayer layer, int turns)
    {
        Vector3Int p = cell - layer.axisPoint;
        turns = ((turns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
        {
            if (layer.axis == Axis.X) p = new Vector3Int(p.x, -p.z, p.y);
            else if (layer.axis == Axis.Y) p = new Vector3Int(p.z, p.y, -p.x);
            else p = new Vector3Int(-p.y, p.x, p.z);
        }
        return p + layer.axisPoint;
    }

    static Vector3 AxisVector(Axis axis)
    {
        if (axis == Axis.X) return Vector3.right;
        if (axis == Axis.Y) return Vector3.up;
        return Vector3.forward;
    }

    Vector3Int LocalToCell(Vector3 local) => new Vector3Int(Mathf.RoundToInt(local.x / cellSize), Mathf.RoundToInt(local.y / cellSize), Mathf.RoundToInt(local.z / cellSize));
    Vector3 CellToLocal(Vector3Int cell) => new Vector3(cell.x, cell.y, cell.z) * cellSize;

    void UpdateCamera()
    {
        Vector3 center = Vector3.zero;
        if (cells.Count > 0)
        {
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            foreach (Vector3Int cell in cells.Keys) { Vector3 p = CellToLocal(cell); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            center = (min + max) * 0.5f;
        }
        Quaternion orbit = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        viewCamera.transform.position = center + orbit * new Vector3(0f, 0f, -orbitDistance);
        viewCamera.transform.rotation = orbit;
        viewCamera.fieldOfView = 45f;
    }

    void OnGUI()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.82f, 0.87f, 0.94f) } };
        }
        GUILayout.BeginArea(HudRect, GUI.skin.box);
        GUILayout.Label("Irregular World — Play", titleStyle);
        GUILayout.Label("Click an adjacent cube face to move. Right-drag to orbit; wheel to zoom.", bodyStyle);
        GUILayout.Label("Yellow outlines show the faces you can move to next.", bodyStyle);
        GUILayout.Space(8);
        GUILayout.Label($"Player: {(currentCube == null ? "not spawned" : $"{LocalToCell(currentCube.localPosition)} · face {currentFace}")}", bodyStyle);
        GUILayout.Label($"Active layer: {(ActiveLayer == null ? "none" : ActiveLayer.name)}", bodyStyle);
        if (GUILayout.Button("Back to level editor")) SceneManager.LoadScene("IrregularLevelEditor");
        if (layers.Count > 0)
        {
            string[] names = new string[layers.Count];
            for (int i = 0; i < layers.Count; i++) names[i] = $"{i + 1}: {layers[i].name}";
            int picked = GUILayout.SelectionGrid(activeLayerIndex, names, 2);
            if (picked != activeLayerIndex) SelectLayer(picked);
            GUILayout.Label("Keys 1–9 select a layer · Q/E rotate it ±90°", bodyStyle);
        }
        GUILayout.Space(8);
        GUILayout.Label(status, bodyStyle);
        GUILayout.EndArea();
    }
}
