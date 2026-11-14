using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class IrregularLevelRuntime
{
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

    void AddBlockedFace(Vector3Int cell, Vector3Int normal)
    {
        if (HasBlockedFace(cell, normal)) return;
        blockedFaces.Add(new BlockedFaceData { cell = cell, normal = normal });
        if (cells.TryGetValue(cell, out Transform cube)) CreateBlockedFaceVisual(cube, normal);
    }

    bool HasBlockedFace(Vector3Int cell, Vector3Int normal)
    {
        foreach (BlockedFaceData face in blockedFaces)
            if (face.cell == cell && face.normal == normal) return true;
        return false;
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

    void CreateGoalMarker()
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Player Goal Face Marker";
        goalMarker = marker.transform;
        goalMarker.SetParent(goalCube, false);
        goalMarker.localPosition = (Vector3)goalFace * 0.515f;
        goalMarker.localRotation = Quaternion.FromToRotation(Vector3.up, goalFace);
        goalMarker.localScale = new Vector3(0.5f, 0.025f, 0.5f);
        marker.GetComponent<Renderer>().sharedMaterial = goalPointMaterial;
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null) markerCollider.enabled = false;
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
        CheckGoalReached();
    }

    void RefreshReachableHighlights()
    {
        foreach (GameObject highlight in reachableHighlights)
            if (highlight != null) Destroy(highlight);
        reachableHighlights.Clear();
        if (currentCube == null || moving || levelCompleted) return;

        Vector3Int[] faceNormals =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };
        foreach (Transform cube in cells.Values)
        {
            foreach (Vector3Int face in faceNormals)
            {
                if (HasBlockedFace(LocalToCell(cube.localPosition), face) || !IsOpenFace(cube, face) || !IsAdjacent(cube, face)) continue;
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
        List<Transform> layerCubes = GetLayerCubes(layer);
        if (rotating || layerCubes.Count == 0)
        {
            foreach (LineRenderer edge in layerOutlineEdges) edge.enabled = false;
            return;
        }
        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        foreach (Transform cube in layerCubes)
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
        List<Transform> layerCubes = GetLayerCubes(layer);
        if (viewCamera == null || layerCubes.Count == 0)
            return new Rect(direction < 0 ? 12f : Screen.width - 56f, Screen.height * 0.5f - 20f, 44f, 40f);

        const float buttonWidth = 44f;
        const float buttonHeight = 40f;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (Transform cube in layerCubes)
        {
            if (cube == null) continue;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 localCorner = new Vector3(x == 0 ? -0.5f : 0.5f, y == 0 ? -0.5f : 0.5f, z == 0 ? -0.5f : 0.5f);
                Vector3 projected = viewCamera.WorldToScreenPoint(cube.TransformPoint(localCorner));
                if (projected.z <= 0f) continue;
                float guiY = Screen.height - projected.y;
                minX = Mathf.Min(minX, projected.x);
                maxX = Mathf.Max(maxX, projected.x);
                minY = Mathf.Min(minY, guiY);
                maxY = Mathf.Max(maxY, guiY);
            }
        }
        if (float.IsInfinity(minX))
            return new Rect(direction < 0 ? 12f : Screen.width - 56f, Screen.height * 0.5f - 20f, buttonWidth, buttonHeight);

        // Horizontal rotation planes (Y axis) use their left/right edges.
        // Vertical planes keep the existing four-corner layout. The active
        // layer's own projected bounds determine its exact positions.
        if (layer.axis == Axis.Y)
        {
            const float minimumSideSpan = 72f;
            if (maxX - minX < minimumSideSpan)
            {
                float centerX = (minX + maxX) * 0.5f;
                minX = centerX - minimumSideSpan * 0.5f;
                maxX = centerX + minimumSideSpan * 0.5f;
            }
            float gap = 8f;
            float centerY = (minY + maxY) * 0.5f;
            float sideLeft = direction < 0 ? minX - buttonWidth - gap : maxX + gap;
            sideLeft = Mathf.Clamp(sideLeft, 12f, Screen.width - buttonWidth - 12f);
            float sideTop = Mathf.Clamp(centerY - buttonHeight * 0.5f, 12f, Screen.height - buttonHeight - 12f);
            return new Rect(sideLeft, sideTop, buttonWidth, buttonHeight);
        }

        // Preserve the four-corner mapping for slices whose projection is
        // taller than it is wide, including a minimum span for edge-on views.
        const float minimumSpan = 104f;
        if (maxX - minX < minimumSpan)
        {
            float centerX = (minX + maxX) * 0.5f;
            minX = centerX - minimumSpan * 0.5f;
            maxX = centerX + minimumSpan * 0.5f;
        }
        if (maxY - minY < minimumSpan)
        {
            float centerY = (minY + maxY) * 0.5f;
            minY = centerY - minimumSpan * 0.5f;
            maxY = centerY + minimumSpan * 0.5f;
        }
        Vector2[] corners =
        {
            new Vector2(minX, minY - buttonHeight * 0.5f - 8f),
            new Vector2(maxX, minY - buttonHeight * 0.5f - 8f),
            new Vector2(maxX, maxY + buttonHeight * 0.5f + 8f),
            new Vector2(minX, maxY + buttonHeight * 0.5f + 8f)
        };
        float position = Mathf.Repeat((direction < 0 ? 0f : 2f) - viewQuarterTurns, 4f);
        int first = Mathf.FloorToInt(position), second = (first + 1) % 4;
        Vector2 anchor = Vector2.Lerp(corners[first], corners[second], position - first);
        float left = Mathf.Clamp(anchor.x - buttonWidth * 0.5f, 12f, Screen.width - buttonWidth - 12f);
        float top = Mathf.Clamp(anchor.y - buttonHeight * 0.5f, 12f, Screen.height - buttonHeight - 12f);
        return new Rect(left, top, buttonWidth, buttonHeight);
    }

    int GetClockwiseTurnSign(RuntimeLayer layer)
    {
        if (layer == null || viewCamera == null) return -1;
        List<Transform> layerCubes = GetLayerCubes(layer);
        Vector3 axis = worldRoot.TransformDirection(AxisVector(layer.axis)).normalized;
        Vector3 axisOrigin = worldRoot.TransformPoint(CellToLocal(layer.axisPoint));
        Vector3 radial = Vector3.zero;
        float farthest = 0f;
        foreach (Transform cube in layerCubes)
        {
            if (cube == null) continue;
            Vector3 candidate = Vector3.ProjectOnPlane(cube.position - axisOrigin, axis);
            if (candidate.sqrMagnitude > farthest) { farthest = candidate.sqrMagnitude; radial = candidate; }
        }
        if (farthest > 0.0001f)
        {
            Vector3 center = viewCamera.WorldToScreenPoint(axisOrigin);
            Vector3 start = viewCamera.WorldToScreenPoint(axisOrigin + radial);
            Vector3 turned = viewCamera.WorldToScreenPoint(axisOrigin + Quaternion.AngleAxis(10f, axis) * radial);
            Vector2 a = new Vector2(start.x - center.x, start.y - center.y);
            Vector2 b = new Vector2(turned.x - center.x, turned.y - center.y);
            float signedArea = a.x * b.y - a.y * b.x;
            if (Mathf.Abs(signedArea) > 0.001f) return signedArea < 0f ? 1 : -1;
        }
        return Vector3.Dot(axis, viewCamera.transform.position - axisOrigin) >= 0f ? -1 : 1;
    }

}
