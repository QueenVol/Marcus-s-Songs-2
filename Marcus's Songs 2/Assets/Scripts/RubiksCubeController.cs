using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds, turns, and displays the 3x3 Rubik's cube.</summary>
public class RubiksCubeController : MonoBehaviour
{
    const float Spacing = 1.02f;
    readonly Color[] faceColors = {
        new Color(.92f,.12f,.16f), new Color(1f,.42f,.08f),
        new Color(.98f,.78f,.12f), new Color(.96f,.96f,.92f),
        new Color(.12f,.42f,.95f), new Color(.12f,.72f,.32f)
    };

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var camera = Camera.main;
        var host = camera ? camera.gameObject : new GameObject("Rubiks Cube Controller");
        if (!host.GetComponent<RubiksCubeController>()) host.AddComponent<RubiksCubeController>();
        if (!host.GetComponent<CubePlayerController>()) host.AddComponent<CubePlayerController>();
    }

    void Start()
    {
        cam = Camera.main;
        if (!cam) { var go = new GameObject("Main Camera"); go.tag = "MainCamera"; cam = go.AddComponent<Camera>(); }
        cam.transform.position = new Vector3(0, 0, -10);
        cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = 42;
        cam.backgroundColor = new Color(.055f,.075f,.12f);
        cam.clearFlags = CameraClearFlags.SolidColor;

        var light = FindObjectOfType<Light>();
        if (!light)
        {
            var lightObject = new GameObject("Key Light");
            light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(35,-35,0);
        }

        pivot = new GameObject("Rubik's Cube").transform;
        BuildCube();
        playerController = GetComponent<CubePlayerController>();
        if (!playerController) playerController = gameObject.AddComponent<CubePlayerController>();
        Transform startingCubie = cubies.Find(c => (c.localPosition - Vector3.up*Spacing).sqrMagnitude < .001f);
        playerController.Initialize(this, startingCubie);
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
        var black = MakeMaterial(new Color(.035f,.045f,.065f));
        var materials = new Material[6];
        for (int i = 0; i < materials.Length; i++) materials[i] = MakeMaterial(faceColors[i]);

        for (int x=-1; x<=1; x++) for (int y=-1; y<=1; y++) for (int z=-1; z<=1; z++)
        {
            var cubie = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubie.name = $"Cubie {x},{y},{z}";
            cubie.transform.SetParent(pivot, false);
            cubie.transform.localPosition = new Vector3(x,y,z)*Spacing;
            cubie.transform.localScale = Vector3.one*.96f;
            cubies.Add(cubie.transform);

            var renderer = cubie.GetComponent<Renderer>();
            cubie.GetComponent<MeshFilter>().sharedMesh = CreateFaceMesh();
            renderer.sharedMaterials = new[]
            {
                x==1 ? materials[0] : black, x==-1 ? materials[1] : black,
                y==1 ? materials[2] : black, y==-1 ? materials[3] : black,
                z==1 ? materials[4] : black, z==-1 ? materials[5] : black
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
        return (y >= Screen.height-72 && x >= 30 && x <= 30+12*(44+7))
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
            orbitY += delta.x*.45f;
            orbitX = Mathf.Clamp(orbitX-delta.y*.45f, -80f, 80f);
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
            if (cubie != null && cubie.parent == pivot && Mathf.RoundToInt(Vector3.Dot(cubie.localPosition,axis)/Spacing) == layer)
                selected.Add(cubie);

        var group = new GameObject("Turning layer").transform;
        group.SetParent(pivot, false);
        foreach (Transform cubie in selected) cubie.SetParent(group, true);
        Quaternion from = group.localRotation;
        Quaternion to = from*Quaternion.AngleAxis(degrees,axis);
        float elapsed = 0;
        while (elapsed < .2f)
        {
            elapsed += Time.deltaTime;
            group.localRotation = Quaternion.Slerp(from,to,elapsed/.2f);
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
    }

    void Snap(Transform cubie)
    {
        Vector3 position = cubie.localPosition;
        cubie.localPosition = new Vector3(
            Mathf.Round(position.x/Spacing)*Spacing,
            Mathf.Round(position.y/Spacing)*Spacing,
            Mathf.Round(position.z/Spacing)*Spacing);
        Vector3 angles = cubie.localEulerAngles;
        cubie.localRotation = Quaternion.Euler(
            Mathf.Round(angles.x/90f)*90f,
            Mathf.Round(angles.y/90f)*90f,
            Mathf.Round(angles.z/90f)*90f);
    }

    IEnumerator Shuffle()
    {
        if (IsBusy) yield break;
        for (int i=0; i<18; i++)
        {
            Vector3 axis = Random.value < .5f ? Vector3.up : Vector3.right;
            int layer = Random.Range(-1,2);
            yield return Turn(axis,layer,Random.value < .5f ? 90f : -90f);
        }
    }

    void UpdateOrbit() => pivot.rotation = Quaternion.Euler(orbitX,orbitY,0);

    void OnGUI()
    {
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 17;
        }
        GUI.color = new Color(1,1,1,.92f);
        float width = 44, gap = 7, start = 30, y = Screen.height-72;
        string[] moves = { "U","U′","D","D′","L","L′","R","R′","F","F′","B","B′" };
        for (int i=0; i<moves.Length; i++)
        {
            if (!GUI.Button(new Rect(start+i*(width+gap),y,width,40),moves[i],buttonStyle) || IsBusy) continue;
            string move = moves[i];
            Vector3 axis = (move[0]=='U'||move[0]=='D') ? Vector3.up
                : (move[0]=='L'||move[0]=='R') ? Vector3.right : Vector3.forward;
            int layer = (move[0]=='U'||move[0]=='R'||move[0]=='F') ? 1 : -1;
            float degrees = move.EndsWith("′") ? -90f : 90f;
            StartCoroutine(Turn(axis,layer,degrees));
        }
        if (GUI.Button(new Rect(Screen.width-164,24,136,42),"打乱魔方",buttonStyle) && !IsBusy)
            StartCoroutine(Shuffle());
    }
}
