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
    [SerializeField, Min(0.01f)] float viewTurnDuration = 0.28f;

    readonly Dictionary<Vector3Int, Transform> cells = new Dictionary<Vector3Int, Transform>();
    readonly List<RuntimeLayer> layers = new List<RuntimeLayer>();
    readonly List<GameObject> reachableHighlights = new List<GameObject>();
    Camera viewCamera;
    Transform worldRoot, playerToken, currentCube;
    Vector3Int currentFace = Vector3Int.up;
    int activeLayerIndex;
    bool moving, rotating, rotatingView;
    float viewQuarterTurns;
    float orbitYaw = 45f, orbitPitch = 30f, orbitDistance = 12f;
    Vector3 cameraCenter;
    Material cubeMaterial, playerMaterial, axisMaterial, outlineMaterial;
    Mesh outlineMesh;
    LineRenderer axisLine;
    readonly List<LineRenderer> layerOutlineEdges = new List<LineRenderer>();
    GUIStyle titleStyle, bodyStyle, rotationButtonStyle;
    Texture2D rotationButtonBackground;
    string status = "Click an adjacent face to move. Use the arrows beside the active layer to turn it.";

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
        SetInitialCameraCenter();
        UpdateAxisLine();
        UpdateLayerOutline();
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
        status = "Click an adjacent face to move. Use the arrows beside the active layer to turn it.";
        RefreshReachableHighlights();
    }

    void RefreshReachableHighlights()
    {
        foreach (GameObject highlight in reachableHighlights)
            if (highlight != null) Destroy(highlight);
        reachableHighlights.Clear();
        if (currentCube == null || moving) return;

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

    void CreateLayerOutline()
    {
        Material outlineLineMaterial = new Material(Shader.Find("Sprites/Default"));
        Color color = new Color(1f, 0.78f, 0.18f, 1f);
        for (int i = 0; i < 12; i++)
        {
            var edgeObject = new GameObject($"Active Layer Outline Edge {i + 1}");
            edgeObject.transform.SetParent(worldRoot, false);
            LineRenderer edge = edgeObject.AddComponent<LineRenderer>();
            edge.useWorldSpace = true;
            edge.positionCount = 2;
            edge.startWidth = cellSize * 0.04f;
            edge.endWidth = cellSize * 0.04f;
            edge.material = outlineLineMaterial;
            edge.startColor = color;
            edge.endColor = color;
            edge.enabled = false;
            layerOutlineEdges.Add(edge);
        }
    }

    void UpdateLayerOutline()
    {
        RuntimeLayer layer = ActiveLayer;
        if (rotating || layer == null || layer.cubes.Count == 0)
        {
            foreach (LineRenderer edge in layerOutlineEdges) edge.enabled = false;
            return;
        }
        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        foreach (Transform cube in layer.cubes)
        {
            if (cube == null) continue;
            Vector3Int cell = LocalToCell(cube.localPosition);
            min = Vector3Int.Min(min, cell);
            max = Vector3Int.Max(max, cell);
        }
        if (min.x == int.MaxValue)
        {
            foreach (LineRenderer edge in layerOutlineEdges) edge.enabled = false;
            return;
        }
        // Grid cubes are scaled to 94% of a cell, so their surfaces end at 0.47 cells.
        // Add only a tiny offset to keep the outline visible without making it float.
        Vector3 padding = Vector3.one * 0.472f;
        Vector3 low = (Vector3)min - padding;
        Vector3 high = (Vector3)max + padding;
        Vector3[] corners =
        {
            new(low.x, low.y, low.z), new(high.x, low.y, low.z),
            new(high.x, high.y, low.z), new(low.x, high.y, low.z),
            new(low.x, low.y, high.z), new(high.x, low.y, high.z),
            new(high.x, high.y, high.z), new(low.x, high.y, high.z)
        };
        int[,] edges =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };
        for (int i = 0; i < layerOutlineEdges.Count; i++)
        {
            LineRenderer edge = layerOutlineEdges[i];
            edge.enabled = true;
            edge.SetPosition(0, worldRoot.TransformPoint(corners[edges[i, 0]] * cellSize));
            edge.SetPosition(1, worldRoot.TransformPoint(corners[edges[i, 1]] * cellSize));
        }
    }

    Rect RotationButtonRect(int direction)
    {
        RuntimeLayer layer = ActiveLayer;
        if (viewCamera == null || layer == null || layer.cubes.Count == 0)
            return new Rect(direction < 0 ? 12f : Screen.width - 56f, Screen.height * 0.5f - 20f, 44f, 40f);

        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        foreach (Transform cube in layer.cubes)
        {
            if (cube == null) continue;
            Vector3Int cell = LocalToCell(cube.localPosition);
            min = Vector3Int.Min(min, cell);
            max = Vector3Int.Max(max, cell);
        }
        if (min.x == int.MaxValue)
            return new Rect(direction < 0 ? 12f : Screen.width - 56f, Screen.height * 0.5f - 20f, 44f, 40f);

        Vector3 low = (Vector3)min - Vector3.one * 0.54f;
        Vector3 high = (Vector3)max + Vector3.one * 0.54f;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 corner = new Vector3(x == 0 ? low.x : high.x, y == 0 ? low.y : high.y, z == 0 ? low.z : high.z);
            Vector3 projected = viewCamera.WorldToScreenPoint(worldRoot.TransformPoint(corner * cellSize));
            if (projected.z <= 0f) continue;
            float guiY = Screen.height - projected.y;
            minX = Mathf.Min(minX, projected.x);
            maxX = Mathf.Max(maxX, projected.x);
            minY = Mathf.Min(minY, guiY);
            maxY = Mathf.Max(maxY, guiY);
        }

        const float buttonWidth = 44f;
        const float buttonHeight = 40f;
        if (float.IsInfinity(minX))
            return new Rect(direction < 0 ? 12f : Screen.width - 56f, Screen.height * 0.5f - 20f, buttonWidth, buttonHeight);

        Vector2[] cornerAnchors =
        {
            new Vector2(minX, minY - buttonHeight * 0.5f - 8f),
            new Vector2(maxX, minY - buttonHeight * 0.5f - 8f),
            new Vector2(maxX, maxY + buttonHeight * 0.5f + 8f),
            new Vector2(minX, maxY + buttonHeight * 0.5f + 8f)
        };
        // At the reference view, clockwise is bottom-right and counterclockwise is top-left.
        // Each 90-degree camera turn moves both anchors with the projected rotation plane.
        float cornerPosition = Mathf.Repeat((direction < 0 ? 0f : 2f) - viewQuarterTurns, 4f);
        int firstCorner = Mathf.FloorToInt(cornerPosition);
        int nextCorner = (firstCorner + 1) % 4;
        Vector2 anchor = Vector2.Lerp(cornerAnchors[firstCorner], cornerAnchors[nextCorner], cornerPosition - firstCorner);
        float left = Mathf.Clamp(anchor.x - buttonWidth * 0.5f, 12f, Screen.width - buttonWidth - 12f);
        float top = Mathf.Clamp(anchor.y - buttonHeight * 0.5f, 12f, Screen.height - buttonHeight - 12f);
        return new Rect(left, top, buttonWidth, buttonHeight);
    }

    void EnsureRotationButtonStyle()
    {
        if (rotationButtonStyle != null) return;
        rotationButtonBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "White Rotation Button Background",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        rotationButtonBackground.SetPixel(0, 0, new Color(0.72f, 0.77f, 0.83f, 1f));
        rotationButtonBackground.Apply();
        Color markColor = new Color(0.12f, 0.16f, 0.22f);
        rotationButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 21,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0),
            border = new RectOffset(0, 0, 0, 0),
            normal = { background = rotationButtonBackground, textColor = markColor },
            hover = { background = rotationButtonBackground, textColor = markColor },
            active = { background = rotationButtonBackground, textColor = markColor },
            focused = { background = rotationButtonBackground, textColor = markColor }
        };
    }

    bool PointerOverRotationButtons(Vector3 screenPoint)
    {
        Vector2 guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        return (CanRotate(1, out _) && RotationButtonRect(-1).Contains(guiPoint))
            || (CanRotate(-1, out _) && RotationButtonRect(1).Contains(guiPoint));
    }

    void Update()
    {
        if (viewCamera == null) return;
        if (!rotatingView && Input.GetKeyDown(KeyCode.Q)) RotateView(90f);
        if (!rotatingView && Input.GetKeyDown(KeyCode.E)) RotateView(-90f);
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f && !PointerOverHud(Input.mousePosition))
        {
            orbitDistance = Mathf.Clamp(orbitDistance - scroll * 0.6f, 4f, 40f);
            UpdateCamera();
        }

        for (int i = 0; i < Mathf.Min(9, layers.Count); i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) SelectLayer(i);

        if (!moving && !rotating && Input.GetMouseButtonDown(0) && !PointerOverHud(Input.mousePosition) && !PointerOverRotationButtons(Input.mousePosition)) TryMovePlayer(Input.mousePosition);
    }

    bool PointerOverHud(Vector3 screenPoint)
    {
        float guiY = Screen.height - screenPoint.y;
        return HudRect.Contains(new Vector2(screenPoint.x, guiY));
    }

    void RotateView(float degrees)
    {
        StartCoroutine(AnimateViewTurn(degrees));
    }

    IEnumerator AnimateViewTurn(float degrees)
    {
        rotatingView = true;
        float startYaw = orbitYaw;
        float targetYaw = startYaw + degrees;
        float startQuarterTurns = viewQuarterTurns;
        float targetQuarterTurns = startQuarterTurns + (degrees > 0f ? 1f : -1f);
        float elapsed = 0f;
        while (elapsed < viewTurnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / viewTurnDuration);
            float smooth = t * t * (3f - 2f * t);
            orbitYaw = Mathf.Lerp(startYaw, targetYaw, smooth);
            viewQuarterTurns = Mathf.Lerp(startQuarterTurns, targetQuarterTurns, smooth);
            UpdateCamera();
            yield return null;
        }
        orbitYaw = Mathf.Repeat(targetYaw, 360f);
        viewQuarterTurns = targetQuarterTurns;
        UpdateCamera();
        rotatingView = false;
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
        RefreshReachableHighlights();
        UpdateLayerOutline();
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
        UpdateLayerOutline();
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
        UpdateLayerOutline();
        status = $"Active layer: {ActiveLayer.name}. Click a nearby arrow to turn it by 90°.";
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
        var occupiedAfterTurn = new HashSet<Vector3Int>();
        foreach (KeyValuePair<Vector3Int, Transform> entry in cells)
        {
            Vector3Int destination = movingCubes.Contains(entry.Value)
                ? RotateCell(entry.Key, layer, turns)
                : entry.Key;
            if (!occupiedAfterTurn.Add(destination))
            {
                reason = "Rotation blocked: two cubes would overlap.";
                return false;
            }
        }

        if (currentCube != null && playerToken != null)
        {
            Vector3Int playerCell = LocalToCell(currentCube.localPosition);
            Vector3Int playerFaceWorld = SnapToFace(worldRoot.InverseTransformDirection(currentCube.TransformDirection(currentFace)));
            if (movingCubes.Contains(currentCube))
            {
                playerCell = RotateCell(playerCell, layer, turns);
                playerFaceWorld = RotateOffset(playerFaceWorld, layer.axis, turns);
            }

            if (occupiedAfterTurn.Contains(playerCell + playerFaceWorld))
            {
                reason = "Rotation blocked: it would cover the player's face and trap them inside a cube.";
                return false;
            }
        }
        reason = "Ready.";
        return true;
    }

    IEnumerator AnimateRotation(int turns)
    {
        rotating = true;
        UpdateLayerOutline();
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
        UpdateLayerOutline();
        UpdateCamera();
        RefreshReachableHighlights();
        status = $"Rotated {layer.name} by {turns * 90}°. Player follows its cube if it moved.";
    }

    Vector3Int RotateCell(Vector3Int cell, RuntimeLayer layer, int turns)
    {
        return RotateOffset(cell - layer.axisPoint, layer.axis, turns) + layer.axisPoint;
    }

    static Vector3Int RotateOffset(Vector3Int offset, Axis axis, int turns)
    {
        Vector3Int p = offset;
        turns = ((turns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
        {
            if (axis == Axis.X) p = new Vector3Int(p.x, -p.z, p.y);
            else if (axis == Axis.Y) p = new Vector3Int(p.z, p.y, -p.x);
            else p = new Vector3Int(-p.y, p.x, p.z);
        }
        return p;
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
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.82f, 0.87f, 0.94f) } };
        }
        GUILayout.BeginArea(HudRect, GUI.skin.box);
        GUILayout.Label("Irregular World — Play", titleStyle);
        GUILayout.Label("Click an adjacent cube face to move. Press Q/E to rotate the view by 90°; wheel to zoom.", bodyStyle);
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
            GUILayout.Label("Keys 1–9 select a layer · click the arrows beside it to rotate ±90°", bodyStyle);
        }
        GUILayout.Space(8);
        GUILayout.Label(status, bodyStyle);
        GUILayout.EndArea();

        if (ActiveLayer != null && !moving && !rotating)
        {
            EnsureRotationButtonStyle();
            if (CanRotate(1, out _) && GUI.Button(RotationButtonRect(-1), "↺", rotationButtonStyle)) RotateActiveLayer(1);
            if (CanRotate(-1, out _) && GUI.Button(RotationButtonRect(1), "↻", rotationButtonStyle)) RotateActiveLayer(-1);
        }
    }
}
