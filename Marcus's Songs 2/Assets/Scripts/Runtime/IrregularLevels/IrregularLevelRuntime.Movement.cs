using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class IrregularLevelRuntime
{
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
        int clockwiseTurns = GetClockwiseTurnSign(ActiveLayer);
        return (CanRotate(-clockwiseTurns, out _) && RotationButtonRect(-1).Contains(guiPoint))
            || (CanRotate(clockwiseTurns, out _) && RotationButtonRect(1).Contains(guiPoint));
    }

    void Update()
    {
        if (viewCamera == null) return;
        if (Input.GetKeyDown(KeyCode.R))
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }
        if (!rotatingView && Input.GetKeyDown(KeyCode.Q)) RotateView(90f);
        if (!rotatingView && Input.GetKeyDown(KeyCode.E)) RotateView(-90f);
        Vector2 panInput = new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
        if (panInput.sqrMagnitude > 0f)
        {
            panInput = Vector2.ClampMagnitude(panInput, 1f);
            Quaternion yaw = Quaternion.Euler(0f, orbitYaw, 0f);
            float speed = cameraPanSpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 2f : 1f);
            Vector3 panDirection = yaw * new Vector3(panInput.x, 0f, panInput.y);
            cameraCenter += panDirection * speed * Time.deltaTime;
            UpdateCamera();
        }
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f && !PointerOverHud(Input.mousePosition))
        {
            orbitDistance = Mathf.Clamp(orbitDistance - scroll * 0.6f, 4f, 40f);
            UpdateCamera();
        }

        for (int i = 0; i < Mathf.Min(9, layers.Count); i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) SelectLayer(i);

        if (!moving && !rotating && !levelCompleted && Input.GetMouseButtonDown(0) && !PointerOverHud(Input.mousePosition) && !PointerOverRotationButtons(Input.mousePosition)) TryMovePlayer(Input.mousePosition);
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
        if (HasBlockedFace(LocalToCell(targetCube.localPosition), targetFace))
        {
            status = "That black face cannot be stood on.";
            return;
        }
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
        CheckGoalReached();
        RefreshReachableHighlights();
        UpdateLayerOutline();
        if (!levelCompleted) status = $"Moved to {LocalToCell(currentCube.localPosition)}, face {currentFace}.";
    }

    void CheckGoalReached()
    {
        if (goalCube == null || currentCube != goalCube || currentFace != goalFace) return;
        levelCompleted = true;
        status = "Goal reached! Level complete.";
        RefreshReachableHighlights();
    }

    static Vector3Int SnapToFace(Vector3 value)
    {
        if (value.sqrMagnitude < 0.001f) return Vector3Int.up;
        value.Normalize();
        if (Mathf.Abs(value.x) > Mathf.Abs(value.y) && Mathf.Abs(value.x) > Mathf.Abs(value.z)) return value.x > 0 ? Vector3Int.right : Vector3Int.left;
        if (Mathf.Abs(value.y) > Mathf.Abs(value.z)) return value.y > 0 ? Vector3Int.up : Vector3Int.down;
        return value.z > 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
    }

}
