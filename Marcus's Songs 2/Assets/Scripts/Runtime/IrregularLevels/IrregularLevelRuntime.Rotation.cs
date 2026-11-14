using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class IrregularLevelRuntime
{
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
        if (moving || rotating || levelCompleted || ActiveLayer == null) return;
        if (!CanRotate(quarterTurns, out string reason)) { status = reason; return; }
        StartCoroutine(AnimateRotation(quarterTurns));
    }

    bool CanRotate(int turns, out string reason)
    {
        RuntimeLayer layer = ActiveLayer;
        List<Transform> layerCubes = GetLayerCubes(layer);
        if (layerCubes.Count == 0) { reason = "The active layer has no cubes."; return false; }
        var movingCubes = new HashSet<Transform>(layerCubes);
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
        List<Transform> layerCubes = GetLayerCubes(layer);
        var oldPositions = new Dictionary<Transform, Vector3Int>();
        foreach (Transform cube in layerCubes)
            if (cube != null)
            {
                Vector3Int cell = LocalToCell(cube.localPosition);
                oldPositions[cube] = cell;
            }
        var pivot = new GameObject("Layer Turn Pivot").transform;
        pivot.SetParent(worldRoot, false);
        pivot.localPosition = CellToLocal(layer.axisPoint);
        pivot.localRotation = Quaternion.identity;
        foreach (Transform cube in layerCubes)
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
        foreach (Transform cube in layerCubes)
        {
            if (cube == null) continue;
            cube.SetParent(worldRoot, true);
            Vector3Int destination = LocalToCell(cube.localPosition);
            cube.localPosition = CellToLocal(destination);
            Vector3 angles = cube.localEulerAngles;
            cube.localRotation = Quaternion.Euler(Mathf.Round(angles.x / 90f) * 90f, Mathf.Round(angles.y / 90f) * 90f, Mathf.Round(angles.z / 90f) * 90f);
            cells[destination] = cube;
        }
        foreach (BlockedFaceData face in blockedFaces)
            foreach (KeyValuePair<Transform, Vector3Int> entry in oldPositions)
                if (entry.Value == face.cell)
                {
                    face.cell = LocalToCell(entry.Key.localPosition);
                    break;
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

    List<Transform> GetLayerCubes(RuntimeLayer layer)
    {
        var result = new List<Transform>();
        if (layer == null) return result;
        int slice = AxisCoordinate(layer.axisPoint, layer.axis);
        foreach (KeyValuePair<Vector3Int, Transform> entry in cells)
            if (AxisCoordinate(entry.Key, layer.axis) == slice) result.Add(entry.Value);
        return result;
    }

    static int AxisCoordinate(Vector3Int cell, Axis axis) => axis == Axis.X ? cell.x : axis == Axis.Y ? cell.y : cell.z;
    static void SetAxisCoordinate(ref Vector3Int cell, Axis axis, int value)
    {
        if (axis == Axis.X) cell.x = value;
        else if (axis == Axis.Y) cell.y = value;
        else cell.z = value;
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

}
