using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public partial class IrregularLevelEditor
{
    void Update()
    {
        TrackEditorHistory();
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
        if (Input.GetMouseButtonDown(2))
        {
            panningCamera = !PointerOverEditorPanel();
            lastMouse = Input.mousePosition;
        }
        if (!Input.GetMouseButton(2)) panningCamera = false;
        if (panningCamera && Input.GetMouseButton(2))
        {
            Vector2 current = Input.mousePosition;
            Vector2 delta = current - lastMouse;
            Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            float worldUnitsPerPixel = 2f * orbitDistance * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            cameraPanOffset -= rotation * new Vector3(delta.x, delta.y, 0f) * worldUnitsPerPixel;
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
        if (mode == EditMode.SetSpawnPoint)
        {
            Vector3Int faceNormal = SnapToFace(cube.InverseTransformDirection(hit.normal));
            if (HasBlockedFace(cell, faceNormal) || !IsOpenFace(cube, faceNormal)) { status = "Choose an exposed, walkable face for the player spawn."; return; }
            SetSpawnPoint(cell, faceNormal);
            status = $"Player spawn point set to {cell}, face {faceNormal}.";
        }
        else if (mode == EditMode.SetGoalPoint)
        {
            Vector3Int faceNormal = SnapToFace(cube.InverseTransformDirection(hit.normal));
            if (HasBlockedFace(cell, faceNormal) || !IsOpenFace(cube, faceNormal)) { status = "Choose an exposed, walkable face for the goal."; return; }
            SetGoalPoint(cell, faceNormal);
            status = $"Player goal set to {cell}, face {faceNormal}.";
        }
        else if (mode == EditMode.Erase)
            RemoveCell(cell);
        else if (mode == EditMode.PaintBlockedFace)
        {
            Vector3Int faceNormal = SnapToFace(cube.InverseTransformDirection(hit.normal));
            ToggleBlockedFace(cell, faceNormal);
        }
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
        Vector3 center = MapCenter() + cameraPanOffset;
        Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        viewCamera.transform.position = center + rotation * new Vector3(0f, 0f, -orbitDistance);
        viewCamera.transform.rotation = rotation;
        viewCamera.fieldOfView = 45f;
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = new Color(0.055f, 0.075f, 0.11f);
    }

    void OnDrawGizmos()
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (!Application.isPlaying)
        {
            if (TryGetScenePreview(out SavedLevel savedPreview) && savedPreview.cells != null)
            {
                DrawScenePreview(savedPreview.cells, cellSize, cubeColor);

                if (savedPreview.hasSpawnPoint)
                {
                    Gizmos.color = spawnPointColor;
                    Gizmos.DrawSphere(CellToLocal(savedPreview.spawnPoint) + (Vector3)savedPreview.spawnFaceNormal * (cellSize * 0.55f), cellSize * 0.14f);
                }
                if (savedPreview.hasGoalPoint)
                {
                    Gizmos.color = goalPointColor;
                    Gizmos.DrawSphere(CellToLocal(savedPreview.goalPoint) + (Vector3)savedPreview.goalFaceNormal * (cellSize * 0.55f), cellSize * 0.14f);
                }
            }
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
            return;
        }

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
        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

}
