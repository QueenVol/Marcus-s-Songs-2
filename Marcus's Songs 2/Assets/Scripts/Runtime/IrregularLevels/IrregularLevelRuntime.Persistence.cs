using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class IrregularLevelRuntime
{
    string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

    bool TryLoadScenePreview(out LevelData data)
    {
        data = null;
        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "IrregularLevelSlots");
            int slot = -1;
            string selectedPath = Path.Combine(directory, "selected_slot.txt");
            if (File.Exists(selectedPath)) int.TryParse(File.ReadAllText(selectedPath), out slot);
            string slotPath = slot >= 0 ? Path.Combine(directory, $"slot_{slot:D4}.json") : null;
            if (string.IsNullOrEmpty(slotPath) || !File.Exists(slotPath))
            {
                if (Directory.Exists(directory))
                    foreach (string candidate in Directory.GetFiles(directory, "slot_*.json"))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(candidate);
                        if (fileName.StartsWith("slot_") && int.TryParse(fileName.Substring(5), out int number))
                            slot = Mathf.Max(slot, number);
                    }
                slotPath = slot >= 0 ? Path.Combine(directory, $"slot_{slot:D4}.json") : SavePath;
            }
            if (!File.Exists(slotPath)) return false;
            data = JsonUtility.FromJson<LevelData>(File.ReadAllText(slotPath));
            return data != null;
        }
        catch { return false; }
    }

    void LoadLevel()
    {
        if (!File.Exists(SavePath))
        {
            status = $"No saved level found. Create and save one in the editor first. Expected: {SavePath}";
            return;
        }

        LevelData data;
        try { data = JsonUtility.FromJson<LevelData>(File.ReadAllText(SavePath)); }
        catch (Exception exception)
        {
            status = $"Could not read level: {exception.Message}";
            return;
        }
        if (data == null || data.cells == null)
        {
            status = "The saved level data is empty or invalid.";
            return;
        }

        foreach (Vector3Int cell in data.cells) AddCell(cell);
        if (data.nonWalkableCells != null)
            foreach (Vector3Int cell in data.nonWalkableCells)
                if (cells.ContainsKey(cell)) AddBlockedFace(cell, Vector3Int.up);
        if (data.blockedFaces != null)
            foreach (BlockedFaceData face in data.blockedFaces)
                if (face != null && cells.ContainsKey(face.cell)) AddBlockedFace(face.cell, SnapToFace(face.normal));
        if (data.hasGoalPoint && cells.TryGetValue(data.goalPoint, out goalCube))
        {
            goalFace = SnapToFace(data.goalFaceNormal);
            if (HasBlockedFace(data.goalPoint, goalFace) || !IsOpenFace(goalCube, goalFace))
            {
                goalCube = null;
                status = "The saved goal face is blocked or covered. Choose a walkable exposed face in the editor and save again.";
            }
            else CreateGoalMarker();
        }
        if (data.layers != null && data.layers.Count > 0)
        {
            foreach (LayerData saved in data.layers)
            {
                var layer = new RuntimeLayer
                {
                    name = string.IsNullOrEmpty(saved.name) ? $"Layer {layers.Count + 1}" : saved.name,
                    axis = (Axis)Mathf.Clamp(saved.axis, 0, 2),
                    axisPoint = saved.axisPoint
                };
                if (saved.cells != null && saved.cells.Count > 0)
                {
                    var counts = new Dictionary<int, int>();
                    foreach (Vector3Int cell in saved.cells)
                    {
                        int coordinate = AxisCoordinate(cell, layer.axis);
                        counts[coordinate] = counts.TryGetValue(coordinate, out int count) ? count + 1 : 1;
                    }
                    int slice = AxisCoordinate(layer.axisPoint, layer.axis);
                    int highestCount = 0;
                    foreach (KeyValuePair<int, int> pair in counts)
                        if (pair.Value > highestCount) { slice = pair.Key; highestCount = pair.Value; }
                    SetAxisCoordinate(ref layer.axisPoint, layer.axis, slice);
                }
                layers.Add(layer);
            }
        }
        else if (data.rotatingCells != null && data.rotatingCells.Count > 0)
        {
            var legacyLayer = new RuntimeLayer { name = "Layer 1", axis = (Axis)Mathf.Clamp(data.axis, 0, 2), axisPoint = data.axisPoint };
            if (data.rotatingCells.Count > 0)
                SetAxisCoordinate(ref legacyLayer.axisPoint, legacyLayer.axis, AxisCoordinate(data.rotatingCells[0], legacyLayer.axis));
            layers.Add(legacyLayer);
        }
        if (layers.Count > 0) activeLayerIndex = 0;
        if (data.hasSpawnPoint && cells.TryGetValue(data.spawnPoint, out currentCube))
        {
            currentFace = SnapToFace(data.spawnFaceNormal);
            if (!HasBlockedFace(data.spawnPoint, currentFace) && IsOpenFace(currentCube, currentFace)) CreatePlayer();
            else
            {
                currentCube = null;
                status = "The saved spawn face is blocked or covered. Choose a walkable exposed face in the editor and save again.";
            }
        }
        else status = "Level loaded, but no valid player spawn face is set. Set one in the editor and save again.";
        SetInitialCameraCenter();
        UpdateAxisLine();
        UpdateLayerOutline();
        UpdateCamera();
    }

}
