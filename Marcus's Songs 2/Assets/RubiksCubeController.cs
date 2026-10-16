using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Creates and controls a mouse-orbitable 3x3 Rubik's cube.</summary>
public class RubiksCubeController : MonoBehaviour
{
    const float Spacing = 1.02f;
    readonly Color[] faceColors = {
        new Color(.92f,.12f,.16f), new Color(1f,.42f,.08f),
        new Color(.98f,.78f,.12f), new Color(.96f,.96f,.92f),
        new Color(.12f,.42f,.95f), new Color(.12f,.72f,.32f)
    };
    Transform pivot;
    Transform playerToken, playerCubie;
    Vector3 playerFaceNormal = Vector3.up;
    Camera cam;
    bool turning, dragging, moving;
    Vector3 lastMouse, mouseDownPosition;
    readonly Vector3 playerTokenScale = new Vector3(.46f,.025f,.46f);
    readonly List<Transform> cubies = new List<Transform>();
    float orbitX = -24f, orbitY = 32f;
    GUIStyle labelStyle, buttonStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var camera = Camera.main;
        var host = camera ? camera.gameObject : new GameObject("Rubiks Cube Controller");
        if (!host.GetComponent<RubiksCubeController>()) host.AddComponent<RubiksCubeController>();
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
        if (!light) { var l = new GameObject("Key Light"); light = l.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.25f; l.transform.rotation=Quaternion.Euler(35,-35,0); }
        pivot = new GameObject("Rubik's Cube").transform;
        BuildCube();
        BuildPlayer();
        UpdateOrbit();
    }

    Material MakeMaterial(Color color)
    {
        var shader = Shader.Find("Standard");
        var m = new Material(shader);
        m.color = color;
        m.SetFloat("_Glossiness", .22f);
        return m;
    }

    void BuildCube()
    {
        var black = MakeMaterial(new Color(.035f,.045f,.065f));
        var mats = new Material[6];
        for(int i=0;i<6;i++) mats[i]=MakeMaterial(faceColors[i]);
        for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++) for(int z=-1;z<=1;z++)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name=$"Cubie {x},{y},{z}";
            go.transform.SetParent(pivot,false);
            go.transform.localPosition=new Vector3(x,y,z)*Spacing;
            go.transform.localScale=Vector3.one*.96f;
            cubies.Add(go.transform);
            var renderer=go.GetComponent<Renderer>();
            go.GetComponent<MeshFilter>().sharedMesh=CreateFaceMesh();
            renderer.sharedMaterials=new[]
            {
                x==1 ? mats[0] : black, x==-1 ? mats[1] : black,
                y==1 ? mats[2] : black, y==-1 ? mats[3] : black,
                z==1 ? mats[4] : black, z==-1 ? mats[5] : black
            };
        }
    }

    Mesh CreateFaceMesh()
    {
        var mesh = new Mesh { name = "Six-sided colored cubie" };
        var vertices = new Vector3[]
        {
            // Face order and material slots: +X, -X, +Y, -Y, +Z, -Z.
             new(.5f,-.5f,-.5f), new(.5f,.5f,-.5f), new(.5f,.5f,.5f), new(.5f,-.5f,.5f),
             new(-.5f,-.5f,.5f), new(-.5f,.5f,.5f), new(-.5f,.5f,-.5f), new(-.5f,-.5f,-.5f),
             new(-.5f,.5f,.5f), new(.5f,.5f,.5f), new(.5f,.5f,-.5f), new(-.5f,.5f,-.5f),
             new(-.5f,-.5f,-.5f), new(.5f,-.5f,-.5f), new(.5f,-.5f,.5f), new(-.5f,-.5f,.5f),
             new(-.5f,-.5f,.5f), new(.5f,-.5f,.5f), new(.5f,.5f,.5f), new(-.5f,.5f,.5f),
             new(.5f,-.5f,-.5f), new(-.5f,-.5f,-.5f), new(-.5f,.5f,-.5f), new(.5f,.5f,-.5f)
        };
        mesh.vertices = vertices;
        mesh.subMeshCount = 6;
        for (int face = 0; face < 6; face++)
        {
            int start = face * 4;
            mesh.SetTriangles(new[] { start, start+1, start+2, start, start+2, start+3 }, face);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void BuildPlayer()
    {
        playerCubie = cubies.Find(c => (c.localPosition - Vector3.up*Spacing).sqrMagnitude < .001f);
        playerFaceNormal = Vector3.up;
        var token = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        token.name = "Player Placeholder";
        playerToken = token.transform;
        playerToken.SetParent(playerCubie, false);
        playerToken.localPosition = playerFaceNormal*.54f;
        playerToken.localRotation = Quaternion.identity;
        playerToken.localScale = playerTokenScale;
        token.GetComponent<Renderer>().material = MakeMaterial(new Color(.15f,.9f,1f));
        token.GetComponent<Collider>().enabled = false;
    }

    Vector3 FaceNormalFromHit(Transform cubie, Vector3 worldNormal)
    {
        Vector3 local = cubie.InverseTransformDirection(worldNormal);
        if (Mathf.Abs(local.x) > Mathf.Abs(local.y) && Mathf.Abs(local.x) > Mathf.Abs(local.z))
            return local.x >= 0 ? Vector3.right : Vector3.left;
        if (Mathf.Abs(local.y) > Mathf.Abs(local.z))
            return local.y >= 0 ? Vector3.up : Vector3.down;
        return local.z >= 0 ? Vector3.forward : Vector3.back;
    }

    void TryMovePlayer(Vector3 screenPoint)
    {
        Ray ray = cam.ScreenPointToRay(screenPoint);
        if (!Physics.Raycast(ray, out var hit, 100f) || !cubies.Contains(hit.collider.transform)) return;
        Transform targetCubie = hit.collider.transform;
        Vector3 targetNormal = FaceNormalFromHit(targetCubie, hit.normal);
        Vector3 currentNormalWorld = playerCubie.TransformDirection(playerFaceNormal).normalized;
        Vector3 targetNormalWorld = targetCubie.TransformDirection(targetNormal).normalized;
        Vector3 currentFaceCenter = playerCubie.TransformPoint(playerFaceNormal*.5f);
        Vector3 targetFaceCenter = targetCubie.TransformPoint(targetNormal*.5f);
        float distance = Vector3.Distance(currentFaceCenter,targetFaceCenter);
        float normalDot = Vector3.Dot(currentNormalWorld,targetNormalWorld);
        bool sameFaceNeighbor = normalDot > .99f && Mathf.Abs(distance-Spacing) < .12f;
        bool acrossEdgeNeighbor = targetCubie == playerCubie && Mathf.Abs(normalDot) < .01f;
        if (!moving && !turning && (sameFaceNeighbor || acrossEdgeNeighbor))
            StartCoroutine(MovePlayer(targetCubie,targetNormal,acrossEdgeNeighbor));
    }

    IEnumerator MovePlayer(Transform targetCubie, Vector3 targetNormal, bool crossingEdge)
    {
        moving = true;
        Vector3 start = pivot.InverseTransformPoint(playerToken.position);
        Vector3 end = pivot.InverseTransformPoint(targetCubie.TransformPoint(targetNormal*.54f));
        Quaternion startRotation = Quaternion.Inverse(pivot.rotation)*playerToken.rotation;
        Quaternion endRotation = targetCubie.localRotation*Quaternion.FromToRotation(Vector3.up,targetNormal);
        Vector3 control = Vector3.zero;
        if (crossingEdge)
        {
            Vector3 edgeMidpoint = pivot.InverseTransformPoint(playerCubie.TransformPoint((playerFaceNormal+targetNormal)*.54f));
            control = edgeMidpoint*2f-(start+end)*.5f;
        }

        playerToken.SetParent(pivot,true);
        float elapsed = 0;
        const float duration = .28f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float linearT = Mathf.Clamp01(elapsed/duration);
            float t = linearT*linearT*(3f-2f*linearT);
            if (crossingEdge)
            {
                float oneMinusT = 1f-linearT;
                playerToken.localPosition = oneMinusT*oneMinusT*start + 2f*oneMinusT*linearT*control + linearT*linearT*end;
            }
            else playerToken.localPosition = Vector3.Lerp(start,end,t);
            playerToken.localRotation = Quaternion.Slerp(startRotation,endRotation,t);
            yield return null;
        }

        playerToken.SetParent(targetCubie,false);
        playerToken.localPosition = targetNormal*.54f;
        playerToken.localRotation = Quaternion.FromToRotation(Vector3.up,targetNormal);
        playerToken.localScale = playerTokenScale;
        playerCubie = targetCubie;
        playerFaceNormal = targetNormal;
        moving = false;
    }

    void Update()
    {
        if(Input.GetMouseButtonDown(0)) { dragging=true; lastMouse=mouseDownPosition=Input.mousePosition; }
        if(Input.GetMouseButtonUp(0))
        {
            Vector3 releasedAt=Input.mousePosition;
            if((releasedAt-mouseDownPosition).sqrMagnitude<36f && !PointerOverControls(releasedAt)) TryMovePlayer(releasedAt);
            dragging=false;
        }
        if(dragging && Input.GetMouseButton(0) && (Input.mousePosition-lastMouse).sqrMagnitude>=36f)
        {
            Vector3 delta=Input.mousePosition-lastMouse;
            orbitY+=delta.x*.45f; orbitX-=delta.y*.45f; orbitX=Mathf.Clamp(orbitX,-80,80);
            lastMouse=Input.mousePosition; UpdateOrbit();
        }
        if(Input.GetKeyDown(KeyCode.R)) StartCoroutine(Shuffle());
        if(Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
    }

    bool PointerOverControls(Vector3 screenPoint)
    {
        float x=screenPoint.x, y=Screen.height-screenPoint.y;
        return (y>=Screen.height-72 && x>=30 && x<=30+12*(44+7))
            || (y>=Screen.height-72 && x>=Screen.width-164 && x<=Screen.width-28)
            || (y>=24 && y<=66 && x>=Screen.width-164 && x<=Screen.width-28);
    }

    IEnumerator Turn(Vector3 axis,int layer,float degrees)
    {
        if(turning || moving) yield break;
        turning=true;
        var selected=new List<Transform>();
        foreach(Transform child in cubies)
            if(child != null && child.parent == pivot && Mathf.RoundToInt(Vector3.Dot(child.localPosition,axis)/Spacing)==layer) selected.Add(child);
        var group=new GameObject("Turning layer").transform;
        group.SetParent(pivot,false); group.localPosition=Vector3.zero;
        foreach(var item in selected) item.SetParent(group,true);
        Quaternion from=group.localRotation, to=from*Quaternion.AngleAxis(degrees,axis);
        float elapsed=0;
        while(elapsed<.2f) { elapsed+=Time.deltaTime; group.localRotation=Quaternion.Slerp(from,to,elapsed/.2f); yield return null; }
        group.localRotation=to;
        for(int i=selected.Count-1;i>=0;i--)
        {
            Transform cubie = selected[i];
            if(cubie == null) continue;
            cubie.SetParent(pivot,true);
            if(cubie != null) Snap(cubie);
        }
        Destroy(group.gameObject); turning=false;
    }

    void Snap(Transform t)
    {
        Vector3 p=t.localPosition;
        t.localPosition=new Vector3(Mathf.Round(p.x/Spacing)*Spacing,Mathf.Round(p.y/Spacing)*Spacing,Mathf.Round(p.z/Spacing)*Spacing);
        t.localRotation=Quaternion.Euler(Mathf.Round(t.localEulerAngles.x/90)*90,Mathf.Round(t.localEulerAngles.y/90)*90,Mathf.Round(t.localEulerAngles.z/90)*90);
    }

    IEnumerator Shuffle()
    {
        if(turning || moving) yield break;
        for(int i=0;i<18;i++) { Vector3 axis=Random.value<.5f?Vector3.up:Vector3.right; int layer=Random.Range(-1,2); yield return Turn(axis,layer,Random.value<.5f?90:-90); }
    }

    void UpdateOrbit() => pivot.rotation=Quaternion.Euler(orbitX,orbitY,0);

    void OnGUI()
    {
        if(labelStyle==null) { labelStyle=new GUIStyle(GUI.skin.label); labelStyle.fontSize=18; labelStyle.normal.textColor=Color.white; buttonStyle=new GUIStyle(GUI.skin.button); buttonStyle.fontSize=17; }
        GUI.color=new Color(1,1,1,.92f);
        float w=44, gap=7, start=30, y=Screen.height-72;
        string[] moves={"U","U′","D","D′","L","L′","R","R′","F","F′","B","B′"};
        for(int i=0;i<moves.Length;i++) if(GUI.Button(new Rect(start+i*(w+gap),y,w,40),moves[i],buttonStyle))
        {
            if(!turning && !moving) { string m=moves[i]; Vector3 a=(m[0]=='U'||m[0]=='D')?Vector3.up:(m[0]=='L'||m[0]=='R')?Vector3.right:Vector3.forward; int l=(m[0]=='U'||m[0]=='R'||m[0]=='F')?1:-1; float d=m.EndsWith("′")?-90:90; StartCoroutine(Turn(a,l,d)); }
        }
        if(GUI.Button(new Rect(Screen.width-164,24,136,42),"打乱魔方",buttonStyle)) StartCoroutine(Shuffle());
    }
}
