using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds, turns, and displays an odd-sized Rubik's cube.</summary>
public class RubiksCubeController : MonoBehaviour
{
    [Header("Cube layout")]
    [SerializeField, Min(3)] int size = 3;
    [SerializeField, Min(.5f)] float cubieSpacing = 1.02f;
    [SerializeField, Range(.5f, 1f)] float cubieScale = .96f;
    [SerializeField] Transform cubePivot;
    [Header("Face colors (+X, -X, +Y, -Y, +Z, -Z)")]
    [SerializeField] Color rightColor = new Color(.92f,.12f,.16f);
    [SerializeField] Color leftColor = new Color(1f,.42f,.08f);
    [SerializeField] Color upColor = new Color(.98f,.78f,.12f);
    [SerializeField] Color downColor = new Color(.96f,.96f,.92f);
    [SerializeField] Color frontColor = new Color(.12f,.42f,.95f);
    [SerializeField] Color backColor = new Color(.12f,.72f,.32f);
    [SerializeField] Color gapColor = new Color(.035f,.045f,.065f);
    [Header("Camera and lighting")]
    [SerializeField] Camera cubeCamera;
    [SerializeField] Light keyLight;
    [SerializeField] Vector3 cameraPosition = new Vector3(0, 0, -10);
    [SerializeField, Range(20, 80)] float cameraFieldOfView = 42;
    [SerializeField] Vector2 startingOrbit = new Vector2(-24f, 32f);
    [SerializeField, Min(0)] float dragSensitivity = .45f;
    [SerializeField] Color backgroundColor = new Color(.055f,.075f,.12f);
    [Header("Controls")]
    [SerializeField] bool showMoveButtons = true;
    [SerializeField] bool showShuffleButton = true;
    [SerializeField, Min(1)] int shuffleMoveCount = 18;
    [SerializeField, Min(.01f)] float turnDuration = .2f;

    readonly List<Transform> cubies = new List<Transform>();
    Transform pivot;
    Camera cam;
    CubePlayerController playerController;
    bool turning, dragging;
    Vector3 lastMouse;
    float orbitX = -24f, orbitY = 32f;
    GUIStyle buttonStyle;

    public bool IsTurning => turning;
    public bool IsBusy => turning || (playerController != null && playerController.IsMoving);
    public Transform CubePivot => pivot;
    public Camera MainCamera => cam;
    public IReadOnlyList<Transform> Cubies => cubies;
    public float CellSpacing => cubieSpacing;
    public float FaceExtent => cubieScale*.5f;
    public int OuterLayer => size/2;
    void Start()
    {
        size = Mathf.Max(3, size | 1);
        cam = cubeCamera != null ? cubeCamera : Camera.main;
        if (!cam) { var go = new GameObject("Main Camera"); go.tag = "MainCamera"; cam = go.AddComponent<Camera>(); }
        cam.transform.position = cameraPosition;
        cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = cameraFieldOfView;
        cam.backgroundColor = backgroundColor;
        cam.clearFlags = CameraClearFlags.SolidColor;

        var light = keyLight != null ? keyLight : FindObjectOfType<Light>();
        if (!light)
        {
            var lightObject = new GameObject("Key Light");
            light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(35,-35,0);
        }

        pivot = cubePivot != null ? cubePivot : new GameObject("Rubik's Cube").transform;
        pivot.name = "Rubik's Cube";
        BuildCube();
        playerController = GetComponent<CubePlayerController>();
        if (playerController != null)
        {
            Vector3 face = playerController.StartingFace;
            Transform startingCubie = cubies.Find(c => (c.localPosition - face*(OuterLayer*cubieSpacing)).sqrMagnitude < .001f);
            playerController.Initialize(this, startingCubie);
        }
        orbitX = startingOrbit.x;
        orbitY = startingOrbit.y;
        UpdateOrbit();
    }

    Material MakeMaterial(Color color)
    {
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", .22f);
        return material;
    }

    void BuildCube()
    {
        var black = MakeMaterial(gapColor);
        Color[] colors = { rightColor, leftColor, upColor, downColor, frontColor, backColor };
        var materials = new Material[colors.Length];
        for (int i = 0; i < materials.Length; i++) materials[i] = MakeMaterial(colors[i]);

        int min = -OuterLayer, max = OuterLayer;
        for (int x=min; x<=max; x++) for (int y=min; y<=max; y++) for (int z=min; z<=max; z++)
        {
            var cubie = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubie.name = $"Cubie {x},{y},{z}";
            cubie.transform.SetParent(pivot, false);
            cubie.transform.localPosition = new Vector3(x,y,z)*cubieSpacing;
            cubie.transform.localScale = Vector3.one*cubieScale;
            cubies.Add(cubie.transform);

            var renderer = cubie.GetComponent<Renderer>();
            cubie.GetComponent<MeshFilter>().sharedMesh = CreateFaceMesh();
            renderer.sharedMaterials = new[]
            {
                x==max ? materials[0] : black, x==min ? materials[1] : black,
                y==max ? materials[2] : black, y==min ? materials[3] : black,
                z==max ? materials[4] : black, z==min ? materials[5] : black
            };
        }
    }

    Mesh CreateFaceMesh()
    {
        var mesh = new Mesh { name = "Six-sided colored cubie" };
        mesh.vertices = new Vector3[]
        {
            // Face order and material slots: +X, -X, +Y, -Y, +Z, -Z.
             new(.5f,-.5f,-.5f), new(.5f,.5f,-.5f), new(.5f,.5f,.5f), new(.5f,-.5f,.5f),
             new(-.5f,-.5f,.5f), new(-.5f,.5f,.5f), new(-.5f,.5f,-.5f), new(-.5f,-.5f,-.5f),
             new(-.5f,.5f,.5f), new(.5f,.5f,.5f), new(.5f,.5f,-.5f), new(-.5f,.5f,-.5f),
             new(-.5f,-.5f,-.5f), new(.5f,-.5f,-.5f), new(.5f,-.5f,.5f), new(-.5f,-.5f,.5f),
             new(-.5f,-.5f,.5f), new(.5f,-.5f,.5f), new(.5f,.5f,.5f), new(-.5f,.5f,.5f),
             new(.5f,-.5f,-.5f), new(-.5f,-.5f,-.5f), new(-.5f,.5f,-.5f), new(.5f,.5f,-.5f)
        };
        mesh.subMeshCount = 6;
        for (int face = 0; face < 6; face++)
        {
            int start = face*4;
            mesh.SetTriangles(new[] { start,start+1,start+2,start,start+2,start+3 }, face);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public bool IsCubie(Transform candidate) => cubies.Contains(candidate);

    public bool PointerOverControls(Vector3 screenPoint)
    {
        float x = screenPoint.x, y = Screen.height-screenPoint.y;
        return (showMoveButtons && y >= Screen.height-72 && x >= 30 && x <= 30+12*(44+7))
            || (y >= Screen.height-72 && x >= Screen.width-164 && x <= Screen.width-28)
            || (y >= 24 && y <= 66 && x >= Screen.width-164 && x <= Screen.width-28);
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) { dragging = true; lastMouse = Input.mousePosition; }
        if (Input.GetMouseButtonUp(0)) dragging = false;
        if (dragging && Input.GetMouseButton(0) && (Input.mousePosition-lastMouse).sqrMagnitude >= 36f)
        {
            Vector3 delta = Input.mousePosition-lastMouse;
            orbitY += delta.x*dragSensitivity;
            orbitX = Mathf.Clamp(orbitX-delta.y*dragSensitivity, -80f, 80f);
            lastMouse = Input.mousePosition;
            UpdateOrbit();
        }
        if (Input.GetKeyDown(KeyCode.R) && !IsBusy) StartCoroutine(Shuffle());
        if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
    }

    IEnumerator Turn(Vector3 axis, int layer, float degrees)
    {
        if (IsBusy) yield break;
        turning = true;
        var selected = new List<Transform>();
        foreach (Transform cubie in cubies)
            if (cubie != null && cubie.parent == pivot && Mathf.RoundToInt(Vector3.Dot(cubie.localPosition,axis)/cubieSpacing) == layer)
                selected.Add(cubie);

        var group = new GameObject("Turning layer").transform;
        if (playerController != null) playerController.ClearHighlights();
        group.SetParent(pivot, false);
        foreach (Transform cubie in selected) cubie.SetParent(group, true);
        Quaternion from = group.localRotation;
        Quaternion to = from*Quaternion.AngleAxis(degrees,axis);
        float elapsed = 0;
        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;
            group.localRotation = Quaternion.Slerp(from,to,elapsed/turnDuration);
            yield return null;
        }
        group.localRotation = to;

        for (int i=selected.Count-1; i>=0; i--)
        {
            Transform cubie = selected[i];
            if (cubie == null) continue;
            cubie.SetParent(pivot, true);
            Snap(cubie);
        }
        Destroy(group.gameObject);
        turning = false;
        if (playerController != null) playerController.RefreshHighlights();
    }

    public void DebugMove(string move)
    {
        if (!Application.isPlaying || IsBusy || string.IsNullOrEmpty(move)) return;
        char face = char.ToUpperInvariant(move[0]);
        Vector3 axis = (face=='U'||face=='D') ? Vector3.up
            : (face=='L'||face=='R') ? Vector3.right : Vector3.forward;
        int layer = (face=='U'||face=='R'||face=='F') ? OuterLayer : -OuterLayer;
        StartCoroutine(Turn(axis,layer,move.EndsWith("′") || move.EndsWith("'") ? -90f : 90f));
    }

    public void DebugShuffle()
    {
        if (Application.isPlaying && !IsBusy) StartCoroutine(Shuffle());
    }

    public void DebugRefreshPlayer()
    {
        if (Application.isPlaying && playerController != null) playerController.RefreshHighlights();
    }

    void Snap(Transform cubie)
    {
        Vector3 position = cubie.localPosition;
        cubie.localPosition = new Vector3(
            Mathf.Round(position.x/cubieSpacing)*cubieSpacing,
            Mathf.Round(position.y/cubieSpacing)*cubieSpacing,
            Mathf.Round(position.z/cubieSpacing)*cubieSpacing);
        Vector3 angles = cubie.localEulerAngles;
        cubie.localRotation = Quaternion.Euler(
            Mathf.Round(angles.x/90f)*90f,
            Mathf.Round(angles.y/90f)*90f,
            Mathf.Round(angles.z/90f)*90f);
    }

    IEnumerator Shuffle()
    {
        if (IsBusy) yield break;
        for (int i=0; i<shuffleMoveCount; i++)
        {
            Vector3 axis = Random.value < .5f ? Vector3.up : Vector3.right;
            int layer = Random.Range(-OuterLayer,OuterLayer+1);
            yield return Turn(axis,layer,Random.value < .5f ? 90f : -90f);
        }
    }

    void UpdateOrbit()
    {
        if (pivot != null) pivot.rotation = Quaternion.Euler(orbitX,orbitY,0);
    }

    void OnGUI()
    {
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 17;
        }
        GUI.color = new Color(1,1,1,.92f);
        if (showMoveButtons)
        {
            float width = 44, gap = 7, start = 30, y = Screen.height-72;
            string[] moves = { "U","U′","D","D′","L","L′","R","R′","F","F′","B","B′" };
            for (int i=0; i<moves.Length; i++)
            {
                if (!GUI.Button(new Rect(start+i*(width+gap),y,width,40),moves[i],buttonStyle) || IsBusy) continue;
                DebugMove(moves[i]);
            }
        }
        if (showShuffleButton && GUI.Button(new Rect(Screen.width-164,24,136,42),"打乱魔方",buttonStyle) && !IsBusy)
            StartCoroutine(Shuffle());
    }
}
