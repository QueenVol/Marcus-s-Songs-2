using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public partial class IrregularLevelEditor
{
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
        SetSpawnPoint(new Vector3Int(0, 2, 0), Vector3Int.up);
        SetGoalPoint(new Vector3Int(2, 1, 0), Vector3Int.up);
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
        blockedFaces.RemoveAll(face => face.cell == cell);
        if (spawnCube == cube) SetSpawnPoint(null);
        if (goalCube == cube) SetGoalPoint(null);
        Destroy(cube.gameObject);
        if (viewCamera != null) UpdateCamera();
    }

    Vector3 CellToLocal(Vector3Int cell) => new Vector3(cell.x, cell.y, cell.z) * cellSize;
    Vector3Int LocalToCell(Vector3 local) => new Vector3Int(
        Mathf.RoundToInt(local.x / cellSize),
        Mathf.RoundToInt(local.y / cellSize),
        Mathf.RoundToInt(local.z / cellSize));

    List<Transform> GetLayerCubes(RotationLayer layer)
    {
        var result = new List<Transform>();
        if (layer == null) return result;
        foreach (KeyValuePair<Vector3Int, Transform> entry in cells)
            if (AxisCoordinate(entry.Key, layer.axis) == AxisCoordinate(layer.axisPoint, layer.axis)) result.Add(entry.Value);
        return result;
    }

    static int AxisCoordinate(Vector3Int cell, Axis axis) => axis == Axis.X ? cell.x : axis == Axis.Y ? cell.y : cell.z;

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
        if (HasBlockedFace(cell.Value, spawnFaceNormal) || !IsOpenFace(spawnCube, spawnFaceNormal))
        {
            spawnCube = null;
            return;
        }

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

    void SetGoalPoint(Vector3Int? cell, Vector3Int faceNormal = default)
    {
        if (goalMarker != null) Destroy(goalMarker.gameObject);
        goalMarker = null;
        goalCube = null;
        if (faceNormal == Vector3Int.zero) faceNormal = Vector3Int.up;
        if (!cell.HasValue || !cells.TryGetValue(cell.Value, out goalCube)) return;
        goalFaceNormal = SnapToFace(faceNormal);
        if (HasBlockedFace(cell.Value, goalFaceNormal) || !IsOpenFace(goalCube, goalFaceNormal))
        {
            goalCube = null;
            return;
        }

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Player Goal Face Marker";
        marker.transform.SetParent(goalCube, false);
        marker.transform.localPosition = (Vector3)goalFaceNormal * 0.515f;
        marker.transform.localRotation = Quaternion.FromToRotation(Vector3.up, goalFaceNormal);
        marker.transform.localScale = new Vector3(0.5f, 0.025f, 0.5f);
        marker.GetComponent<Renderer>().sharedMaterial = goalPointMaterial;
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null) markerCollider.enabled = false;
        goalMarker = marker.transform;
    }

    bool IsOpenFace(Transform cube, Vector3Int normal)
    {
        Vector3Int cell = LocalToCell(cube.localPosition);
        Vector3Int outward = SnapToFace(transform.InverseTransformDirection(cube.TransformDirection(normal)));
        return !cells.ContainsKey(cell + outward);
    }

    bool HasBlockedFace(Vector3Int cell, Vector3Int normal)
    {
        foreach (BlockedFaceData face in blockedFaces)
            if (face.cell == cell && face.normal == normal) return true;
        return false;
    }

    void ToggleBlockedFace(Vector3Int cell, Vector3Int normal)
    {
        if (HasBlockedFace(cell, normal))
        {
            blockedFaces.RemoveAll(face => face.cell == cell && face.normal == normal);
            if (cells.TryGetValue(cell, out Transform existingCube))
            {
                Transform patch = existingCube.Find($"Blocked Face {normal}");
                if (patch != null) Destroy(patch.gameObject);
            }
            if (spawnCube != null && LocalToCell(spawnCube.localPosition) == cell && spawnFaceNormal == normal)
                SetSpawnPoint(null);
            status = $"Removed black face at {cell}, {normal}.";
            return;
        }

        var face = new BlockedFaceData { cell = cell, normal = normal };
        blockedFaces.Add(face);
        if (cells.TryGetValue(cell, out Transform cube)) CreateBlockedFaceVisual(cube, normal);
        if (spawnCube != null && LocalToCell(spawnCube.localPosition) == cell && spawnFaceNormal == normal)
            SetSpawnPoint(null);
        if (goalCube != null && LocalToCell(goalCube.localPosition) == cell && goalFaceNormal == normal)
            SetGoalPoint(null);
        status = $"Painted a non-walkable black face at {cell}, {normal}. Other faces remain walkable.";
    }

    void CreateBlockedFaceVisual(Transform cube, Vector3Int normal)
    {
        var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        patch.name = $"Blocked Face {normal}";
        patch.transform.SetParent(cube, false);
        patch.transform.localPosition = (Vector3)normal * 0.508f;
        patch.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
        patch.transform.localScale = new Vector3(1f, 0.016f, 1f);
        patch.GetComponent<Renderer>().sharedMaterial = blockedFaceMaterial;
        Collider patchCollider = patch.GetComponent<Collider>();
        if (patchCollider != null) patchCollider.enabled = false;
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
        bool selected = ActiveLayer != null && AxisCoordinate(LocalToCell(cube.localPosition), ActiveLayer.axis) == AxisCoordinate(ActiveLayer.axisPoint, ActiveLayer.axis);
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
        List<Transform> layerCubes = GetLayerCubes(layer);
        if (layerCubes.Count == 0)
        {
            reason = "There are no cubes on the active layer slice.";
            return false;
        }

        var destinations = new HashSet<Vector3Int>();
        var movingCubes = new HashSet<Transform>(layerCubes);
        foreach (Transform cube in layerCubes)
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
        List<Transform> layerCubes = GetLayerCubes(layer);
        var originalPositions = new Dictionary<Transform, Vector3Int>();
        foreach (Transform cube in layerCubes)
            if (cube != null)
            {
                Vector3Int cell = LocalToCell(cube.localPosition);
                originalPositions[cube] = cell;
            }

        var pivot = new GameObject("Temporary Layer Pivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = CellToLocal(layer.axisPoint);
        pivot.localRotation = Quaternion.identity;
        foreach (Transform cube in layerCubes)
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
        foreach (Transform cube in layerCubes)
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
        foreach (BlockedFaceData face in blockedFaces)
            foreach (KeyValuePair<Transform, Vector3Int> entry in originalPositions)
                if (entry.Value == face.cell)
                {
                    face.cell = LocalToCell(entry.Key.localPosition);
                    break;
                }

        Destroy(pivot.gameObject);
        rotating = false;
        UpdateCamera();
        RefreshCellColors();
        UpdateAxisLine();
        status = $"Rotated {layer.name}: {layerCubes.Count} cubes by {quarterTurns * 90}°.";
    }

}
