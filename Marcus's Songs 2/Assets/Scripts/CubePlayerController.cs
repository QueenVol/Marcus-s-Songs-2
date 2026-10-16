using System.Collections;
using UnityEngine;

/// <summary>Creates the player marker and moves it between adjacent cube-face cells.</summary>
public class CubePlayerController : MonoBehaviour
{
    readonly Vector3 tokenScale = new Vector3(.46f,.025f,.46f);
    RubiksCubeController cube;
    Transform token, currentCubie;
    Vector3 currentFaceNormal = Vector3.up;
    Vector3 mouseDownPosition;

    public bool IsMoving { get; private set; }

    public void Initialize(RubiksCubeController cubeController, Transform startingCubie)
    {
        if (token != null) return;
        cube = cubeController;
        currentCubie = startingCubie;
        currentFaceNormal = Vector3.up;

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Player Placeholder";
        token = marker.transform;
        token.SetParent(currentCubie, false);
        token.localPosition = currentFaceNormal*.54f;
        token.localScale = tokenScale;
        var material = new Material(Shader.Find("Standard"));
        material.color = new Color(.15f,.9f,1f);
        marker.GetComponent<Renderer>().material = material;
        marker.GetComponent<Collider>().enabled = false;
    }

    void Update()
    {
        if (cube == null || token == null) return;
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
        return Vector3.Dot(cubie.localPosition,normalInCube) > 0.5f;
    }

    void TryMovePlayer(Vector3 screenPoint)
    {
        if (IsMoving || cube.IsBusy) return;
        Ray ray = cube.MainCamera.ScreenPointToRay(screenPoint);
        if (!Physics.Raycast(ray,out var hit,100f) || !cube.IsCubie(hit.collider.transform)) return;

        Transform targetCubie = hit.collider.transform;
        Vector3 targetNormal = FaceNormalFromHit(targetCubie,hit.normal);
        if (!IsOuterFace(currentCubie,currentFaceNormal) || !IsOuterFace(targetCubie,targetNormal)) return;

        Vector3 currentNormalWorld = currentCubie.TransformDirection(currentFaceNormal).normalized;
        Vector3 targetNormalWorld = targetCubie.TransformDirection(targetNormal).normalized;
        Vector3 currentFaceCenter = currentCubie.TransformPoint(currentFaceNormal*.5f);
        Vector3 targetFaceCenter = targetCubie.TransformPoint(targetNormal*.5f);
        float distance = Vector3.Distance(currentFaceCenter,targetFaceCenter);
        float normalDot = Vector3.Dot(currentNormalWorld,targetNormalWorld);
        bool sameFaceNeighbor = normalDot > .99f && Mathf.Abs(distance-1.02f) < .12f;
        bool acrossEdgeNeighbor = targetCubie == currentCubie && Mathf.Abs(normalDot) < .01f;
        if (sameFaceNeighbor || acrossEdgeNeighbor)
            StartCoroutine(MovePlayer(targetCubie,targetNormal,acrossEdgeNeighbor));
    }

    IEnumerator MovePlayer(Transform targetCubie, Vector3 targetNormal, bool crossingEdge)
    {
        IsMoving = true;
        Transform pivot = cube.CubePivot;
        Vector3 start = pivot.InverseTransformPoint(token.position);
        Vector3 end = pivot.InverseTransformPoint(targetCubie.TransformPoint(targetNormal*.54f));
        Quaternion startRotation = Quaternion.Inverse(pivot.rotation)*token.rotation;
        Quaternion endRotation = targetCubie.localRotation*Quaternion.FromToRotation(Vector3.up,targetNormal);
        Vector3 control = Vector3.zero;
        if (crossingEdge)
        {
            Vector3 midpoint = pivot.InverseTransformPoint(currentCubie.TransformPoint((currentFaceNormal+targetNormal)*.54f));
            control = midpoint*2f-(start+end)*.5f;
        }

        token.SetParent(pivot,true);
        float elapsed = 0;
        const float duration = .28f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float linearT = Mathf.Clamp01(elapsed/duration);
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
        token.localPosition = targetNormal*.54f;
        token.localRotation = Quaternion.FromToRotation(Vector3.up,targetNormal);
        token.localScale = tokenScale;
        currentCubie = targetCubie;
        currentFaceNormal = targetNormal;
        IsMoving = false;
    }
}
