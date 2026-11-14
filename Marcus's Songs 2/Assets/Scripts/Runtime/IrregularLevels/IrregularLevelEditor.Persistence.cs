using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public partial class IrregularLevelEditor
{
    void SaveLevel()
    {
        var data = new SavedLevel();
        foreach (Vector3Int cell in cells.Keys) data.cells.Add(cell);
        foreach (BlockedFaceData face in blockedFaces)
        {
            Vector3 worldNormal = cells.TryGetValue(face.cell, out Transform cube)
                ? transform.InverseTransformDirection(cube.TransformDirection(face.normal))
                : face.normal;
            data.blockedFaces.Add(new BlockedFaceData { cell = face.cell, normal = SnapToFace(worldNormal) });
        }
        if (spawnCube != null)
        {
            data.hasSpawnPoint = true;
            data.spawnPoint = LocalToCell(spawnCube.localPosition);
            Vector3 faceInLevelSpace = transform.InverseTransformDirection(spawnCube.TransformDirection(spawnFaceNormal));
            data.spawnFaceNormal = SnapToFace(faceInLevelSpace);
        }
        if (goalCube != null)
        {
            data.hasGoalPoint = true;
            data.goalPoint = LocalToCell(goalCube.localPosition);
            Vector3 faceInLevelSpace = transform.InverseTransformDirection(goalCube.TransformDirection(goalFaceNormal));
            data.goalFaceNormal = SnapToFace(faceInLevelSpace);
        }
        foreach (RotationLayer layer in layers)
        {
            var saved = new SavedLayer { name = layer.name, axis = (int)layer.axis, axisPoint = layer.axisPoint };
            data.layers.Add(saved);
        }
        string path = Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json");
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        status = $"Level saved to {path}";
    }

    void LoadLevel()
    {
        string path = Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json");
        if (!File.Exists(path))
        {
            status = "No saved level found yet. Use Save level first.";
            return;
        }

        SavedLevel data = JsonUtility.FromJson<SavedLevel>(File.ReadAllText(path));
        if (data == null)
        {
            status = "Saved level could not be read.";
            return;
        }
        foreach (Transform cube in new List<Transform>(cells.Values)) Destroy(cube.gameObject);
        SetSpawnPoint(null);
        SetGoalPoint(null);
        cells.Clear();
        blockedFaces.Clear();
        layers.Clear(); activeLayerIndex = -1;
        foreach (Vector3Int cell in data.cells) AddCell(cell);
        if (data.nonWalkableCells != null)
            foreach (Vector3Int cell in data.nonWalkableCells)
                if (cells.ContainsKey(cell)) AddBlockedFaceFromSave(cell, Vector3Int.up);
        if (data.blockedFaces != null)
            foreach (BlockedFaceData face in data.blockedFaces)
                if (face != null && cells.ContainsKey(face.cell)) AddBlockedFaceFromSave(face.cell, SnapToFace(face.normal));
        if (data.layers != null && data.layers.Count > 0)
            foreach (SavedLayer saved in data.layers)
            {
                AddLayer(string.IsNullOrEmpty(saved.name) ? $"Layer {layers.Count + 1}" : saved.name);
                ActiveLayer.axis = (Axis)Mathf.Clamp(saved.axis, 0, 2);
                ActiveLayer.axisPoint = saved.axisPoint;
                // Older saves stored a manually selected group. Migrate it to the most common
                // coordinate along the layer axis so it becomes a spatial slice.
                if (saved.cells != null && saved.cells.Count > 0)
                {
                    var counts = new Dictionary<int, int>();
                    foreach (Vector3Int cell in saved.cells)
                    {
                        int coordinate = AxisCoordinate(cell, ActiveLayer.axis);
                        counts[coordinate] = counts.TryGetValue(coordinate, out int count) ? count + 1 : 1;
                    }
                    int slice = ActiveLayer.axis == Axis.X ? ActiveLayer.axisPoint.x : ActiveLayer.axis == Axis.Y ? ActiveLayer.axisPoint.y : ActiveLayer.axisPoint.z;
                    int highestCount = 0;
                    foreach (KeyValuePair<int, int> pair in counts)
                        if (pair.Value > highestCount) { slice = pair.Key; highestCount = pair.Value; }
                    if (ActiveLayer.axis == Axis.X) ActiveLayer.axisPoint.x = slice;
                    else if (ActiveLayer.axis == Axis.Y) ActiveLayer.axisPoint.y = slice;
                    else ActiveLayer.axisPoint.z = slice;
                }
            }
        else if (data.rotatingCells != null && data.rotatingCells.Count > 0)
        {
            AddLayer("Layer 1");
            ActiveLayer.axis = (Axis)Mathf.Clamp(data.axis, 0, 2);
            ActiveLayer.axisPoint = data.axisPoint;
            if (data.rotatingCells.Count > 0)
            {
                int slice = AxisCoordinate(data.rotatingCells[0], ActiveLayer.axis);
                if (ActiveLayer.axis == Axis.X) ActiveLayer.axisPoint.x = slice;
                else if (ActiveLayer.axis == Axis.Y) ActiveLayer.axisPoint.y = slice;
                else ActiveLayer.axisPoint.z = slice;
            }
        }
        if (data.hasSpawnPoint && cells.ContainsKey(data.spawnPoint) && !HasBlockedFace(data.spawnPoint, SnapToFace(data.spawnFaceNormal))) SetSpawnPoint(data.spawnPoint, data.spawnFaceNormal);
        if (data.hasGoalPoint && cells.ContainsKey(data.goalPoint) && !HasBlockedFace(data.goalPoint, SnapToFace(data.goalFaceNormal))) SetGoalPoint(data.goalPoint, data.goalFaceNormal);
        RefreshCellColors();
        UpdateAxisLine();
        status = "Loaded the saved irregular level.";
    }

    void AddBlockedFaceFromSave(Vector3Int cell, Vector3Int normal)
    {
        if (HasBlockedFace(cell, normal)) return;
        blockedFaces.Add(new BlockedFaceData { cell = cell, normal = normal });
        if (cells.TryGetValue(cell, out Transform cube)) CreateBlockedFaceVisual(cube, normal);
    }

}
