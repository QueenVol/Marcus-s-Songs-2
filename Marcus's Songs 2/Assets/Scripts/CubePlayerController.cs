using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Creates the player marker and moves it between adjacent cube-face cells.</summary>
public class CubePlayerController : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] Color playerColor = new Color(.15f,.9f,1f);
    [SerializeField] Vector3 playerScale = new Vector3(.46f,.025f,.46f);
    [SerializeField] Vector3 startingFace = Vector3.up;
    [SerializeField, Min(.5f)] float playerHeight = .54f;
    [SerializeField, Min(.01f)] float moveDuration = .28f;
    [Header("Available move outline")]
    [SerializeField] Color outlineColor = Color.yellow;
    [SerializeField] Color outlineEmission = Color.yellow;
    [SerializeField, Min(.001f)] float outlineThickness = .04f;
    [SerializeField, Min(0)] float outlineSurfaceOffset = .002f;
    [Header("Input")]
    [SerializeField] bool allowMouseMovement = true;
    
    readonly Vector3[] faceNormals = { Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back };
    readonly List<GameObject> highlights = new List<GameObject>();
    RubiksCubeController cube;
    Transform token, currentCubie;
    Material borderMaterial;
    Mesh borderMesh;
    Vector3 currentFaceNormal = Vector3.up;
    Vector3 mouseDownPosition;

    public bool IsMoving { get; private set; }
    public Vector3 StartingFace => SnapToFace(startingFace);

    static Vector3 SnapToFace(Vector3 value)
    {
        if (value.sqrMagnitude < .001f) return Vector3.up;
        value.Normalize();
        if (Mathf.Abs(value.x) > Mathf.Abs(value.y) && Mathf.Abs(value.x) > Mathf.Abs(value.z))
            return value.x > 0 ? Vector3.right : Vector3.left;
        if (Mathf.Abs(value.y) > Mathf.Abs(value.z))
            return value.y > 0 ? Vector3.up : Vector3.down;
        return value.z > 0 ? Vector3.forward : Vector3.back;
    }

    public void Initialize(RubiksCubeController cubeController, Transform startingCubie)
    {
        if (token != null) return;
        cube = cubeController;
        currentCubie = startingCubie;
        currentFaceNormal = StartingFace;

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Player Placeholder";
        token = marker.transform;
        token.SetParent(currentCubie, false);
        token.localPosition = currentFaceNormal*playerHeight;
        token.localScale = playerScale;
        var material = new Material(Shader.Find("Standard"));
        material.color = playerColor;
        marker.GetComponent<Renderer>().material = material;
        marker.GetComponent<Collider>().enabled = false;

        borderMaterial = new Material(Shader.Find("Standard"));
        borderMaterial.color = outlineColor;
        borderMaterial.EnableKeyword("_EMISSION");
        borderMaterial.SetColor("_EmissionColor",outlineEmission);
        borderMesh = CreateBorderMesh();
        RefreshHighlights();
    }

    void Update()
    {
        if (!allowMouseMovement || cube == null || token == null) return;
        if (Input.GetMouseButtonDown(0))
            mouseDownPosition = Input.mousePosition;
        if (Input.GetMouseButtonUp(0))
        {
            Vector3 releasedAt = Input.mousePosition;
            if ((releasedAt-mouseDownPosition).sqrMagnitude < 36f && !cube.PointerOverControls(releasedAt))
                TryMovePlayer(releasedAt);
        }
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

    bool IsOuterFace(Transform cubie, Vector3 localNormal)
    {
        Vector3 normalInCube = cube.CubePivot.InverseTransformDirection(cubie.TransformDirection(localNormal)).normalized;
        return Vector3.Dot(cubie.localPosition,normalInCube) > (cube.OuterLayer-.5f)*cube.CellSpacing;
    }

    bool IsAdjacent(Transform targetCubie, Vector3 targetNormal, out bool crossingEdge)
    {
        Vector3 currentNormalWorld = currentCubie.TransformDirection(currentFaceNormal).normalized;
        Vector3 targetNormalWorld = targetCubie.TransformDirection(targetNormal).normalized;
        Vector3 currentFaceCenter = currentCubie.TransformPoint(currentFaceNormal*cube.FaceExtent);
        Vector3 targetFaceCenter = targetCubie.TransformPoint(targetNormal*cube.FaceExtent);
        float distance = Vector3.Distance(currentFaceCenter,targetFaceCenter);
        float normalDot = Vector3.Dot(currentNormalWorld,targetNormalWorld);
        bool sameFaceNeighbor = normalDot > .99f && Mathf.Abs(distance-cube.CellSpacing) < cube.CellSpacing*.12f;
        crossingEdge = targetCubie == currentCubie && Mathf.Abs(normalDot) < .01f;
        return sameFaceNeighbor || crossingEdge;
    }

    public void ClearHighlights()
    {
        foreach (GameObject highlight in highlights)
            if (highlight != null) Destroy(highlight);
        highlights.Clear();
    }

    public void RefreshHighlights()
    {
        ClearHighlights();
        if (cube == null || token == null || IsMoving || cube.IsTurning) return;

        foreach (Transform cubie in cube.Cubies)
        {
            if (cubie == null || cubie.parent != cube.CubePivot) continue;
            foreach (Vector3 normal in faceNormals)
            {
                if (!IsOuterFace(cubie,normal) || !IsAdjacent(cubie,normal,out _)) continue;
                CreateBorder(cubie,normal);
            }
        }
    }

    void CreateBorder(Transform cubie, Vector3 normal)
    {
        Vector3 up = Mathf.Abs(normal.x) > .5f ? Vector3.up
            : Mathf.Abs(normal.y) > .5f ? Vector3.forward : Vector3.up;
        var outline = new GameObject("Movable Cell Outline");
        outline.transform.SetParent(cubie,false);
        outline.transform.localPosition = normal*(.5f+outlineSurfaceOffset);
        outline.transform.localRotation = Quaternion.LookRotation(normal,up);
        outline.AddComponent<MeshFilter>().sharedMesh = borderMesh;
        outline.AddComponent<MeshRenderer>().sharedMaterial = borderMaterial;
        highlights.Add(outline);
    }

    Mesh CreateBorderMesh()
    {
        float outer = .5f+outlineSurfaceOffset*.5f;
        float inner = outer-outlineThickness;
        var mesh = new Mesh { name = "Flat movable-cell outline" };
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

    void TryMovePlayer(Vector3 screenPoint)
    {
        if (IsMoving || cube.IsBusy) return;
        Ray ray = cube.MainCamera.ScreenPointToRay(screenPoint);
        if (!Physics.Raycast(ray,out var hit,100f) || !cube.IsCubie(hit.collider.transform)) return;

        Transform targetCubie = hit.collider.transform;
        Vector3 targetNormal = FaceNormalFromHit(targetCubie,hit.normal);
        if (!IsOuterFace(currentCubie,currentFaceNormal) || !IsOuterFace(targetCubie,targetNormal)) return;

        if (IsAdjacent(targetCubie,targetNormal,out bool acrossEdgeNeighbor))
            StartCoroutine(MovePlayer(targetCubie,targetNormal,acrossEdgeNeighbor));
    }

    IEnumerator MovePlayer(Transform targetCubie, Vector3 targetNormal, bool crossingEdge)
    {
        IsMoving = true;
        ClearHighlights();
        Transform pivot = cube.CubePivot;
        Vector3 start = pivot.InverseTransformPoint(token.position);
        Vector3 end = pivot.InverseTransformPoint(targetCubie.TransformPoint(targetNormal*playerHeight));
        Quaternion startRotation = Quaternion.Inverse(pivot.rotation)*token.rotation;
        Quaternion endRotation = targetCubie.localRotation*Quaternion.FromToRotation(Vector3.up,targetNormal);
        Vector3 control = Vector3.zero;
        if (crossingEdge)
        {
            Vector3 midpoint = pivot.InverseTransformPoint(currentCubie.TransformPoint((currentFaceNormal+targetNormal)*playerHeight));
            control = midpoint*2f-(start+end)*.5f;
        }

        token.SetParent(pivot,true);
        float elapsed = 0;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float linearT = Mathf.Clamp01(elapsed/moveDuration);
            float easedT = linearT*linearT*(3f-2f*linearT);
            if (crossingEdge)
            {
                float oneMinusT = 1f-linearT;
                token.localPosition = oneMinusT*oneMinusT*start + 2f*oneMinusT*linearT*control + linearT*linearT*end;
            }
            else token.localPosition = Vector3.Lerp(start,end,easedT);
            token.localRotation = Quaternion.Slerp(startRotation,endRotation,easedT);
            yield return null;
        }

        token.SetParent(targetCubie,false);
        token.localPosition = targetNormal*playerHeight;
        token.localRotation = Quaternion.FromToRotation(Vector3.up,targetNormal);
        token.localScale = playerScale;
        currentCubie = targetCubie;
        currentFaceNormal = targetNormal;
        IsMoving = false;
        RefreshHighlights();
    }
}
