using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public partial class IrregularLevelEditor
{
    string SlotDirectory => Path.Combine(Application.persistentDataPath, "IrregularLevelSlots");
    string SlotPath(int slot) => Path.Combine(SlotDirectory, $"slot_{slot:D4}.json");
    string SlotNamePath(int slot) => Path.Combine(SlotDirectory, $"slot_{slot:D4}.name");
    string SelectedSlotPath => Path.Combine(SlotDirectory, "selected_slot.txt");
    void RememberSelectedSlot(int slot)
    {
        Directory.CreateDirectory(SlotDirectory);
        File.WriteAllText(SelectedSlotPath, slot.ToString());
    }

    bool TryGetScenePreview(out SavedLevel data)
    {
        data = null;
        try
        {
            int slot = -1;
            if (File.Exists(SelectedSlotPath)) int.TryParse(File.ReadAllText(SelectedSlotPath), out slot);
            if (slot < 0 || !File.Exists(SlotPath(slot)))
            {
                if (!Directory.Exists(SlotDirectory)) return false;
                foreach (string candidate in Directory.GetFiles(SlotDirectory, "slot_*.json"))
                {
                    string fileName = Path.GetFileNameWithoutExtension(candidate);
                    if (fileName.StartsWith("slot_") && int.TryParse(fileName.Substring(5), out int number))
                        slot = Mathf.Max(slot, number);
                }
            }
            if (slot < 0) return false;
            string path = SlotPath(slot);
            if (!File.Exists(path)) return false;
            data = JsonUtility.FromJson<SavedLevel>(File.ReadAllText(path));
            return data != null;
        }
        catch { return false; }
    }

    string GetSlotName(int slot)
    {
        string path = SlotNamePath(slot);
        if (!File.Exists(path)) return $"Slot {slot}";
        string name = File.ReadAllText(path).Trim();
        return string.IsNullOrEmpty(name) ? $"Slot {slot}" : name;
    }

    void SaveSlotName(int slot, string name)
    {
        string cleanName = string.IsNullOrWhiteSpace(name) ? $"Slot {slot}" : name.Trim();
        File.WriteAllText(SlotNamePath(slot), cleanName);
    }

    void RefreshSaveSlots()
    {
        int selectedSlot = selectedSaveSlotIndex >= 0 && selectedSaveSlotIndex < saveSlots.Count
            ? saveSlots[selectedSaveSlotIndex] : -1;
        saveSlots.Clear();
        if (Directory.Exists(SlotDirectory))
            foreach (string path in Directory.GetFiles(SlotDirectory, "slot_*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("slot_") && int.TryParse(name.Substring(5), out int number)) saveSlots.Add(number);
            }
        saveSlots.Sort();
        selectedSaveSlotIndex = selectedSlot >= 0 ? saveSlots.IndexOf(selectedSlot) : (saveSlots.Count > 0 ? 0 : -1);
    }

    void SaveAsNewSlot()
    {
        RefreshSaveSlots();
        int slot = saveSlots.Count == 0 ? 1 : saveSlots[saveSlots.Count - 1] + 1;
        SaveLevel();
        Directory.CreateDirectory(SlotDirectory);
        string legacyPath = Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json");
        File.Copy(legacyPath, SlotPath(slot), true);
        SaveSlotName(slot, slotNameInput);
        RefreshSaveSlots();
        selectedSaveSlotIndex = saveSlots.IndexOf(slot);
        RememberSelectedSlot(slot);
        slotNameInput = GetSlotName(slot);
        status = $"Saved as Slot {slot}.";
    }

    void OverwriteSelectedSlot()
    {
        RefreshSaveSlots();
        if (selectedSaveSlotIndex < 0 || selectedSaveSlotIndex >= saveSlots.Count) return;
        int slot = saveSlots[selectedSaveSlotIndex];
        SaveLevel();
        File.Copy(Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json"), SlotPath(slot), true);
        status = $"Overwrote Slot {slot}.";
    }

    void RenameSelectedSlot()
    {
        RefreshSaveSlots();
        if (selectedSaveSlotIndex < 0 || selectedSaveSlotIndex >= saveSlots.Count) return;
        int slot = saveSlots[selectedSaveSlotIndex];
        SaveSlotName(slot, slotNameInput);
        RememberSelectedSlot(slot);
        slotNameInput = GetSlotName(slot);
        status = $"Renamed Slot {slot} to \"{slotNameInput}\".";
    }

    void LoadSelectedSlot()
    {
        RefreshSaveSlots();
        if (selectedSaveSlotIndex < 0 || selectedSaveSlotIndex >= saveSlots.Count) return;
        LoadSlot(saveSlots[selectedSaveSlotIndex]);
    }

    void DeleteSlot(int slot)
    {
        string path = SlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
        string namePath = SlotNamePath(slot);
        if (File.Exists(namePath)) File.Delete(namePath);
        pendingDeleteSlot = -1;
        RefreshSaveSlots();
        slotNameInput = selectedSaveSlotIndex >= 0 ? GetSlotName(saveSlots[selectedSaveSlotIndex]) : "New Level";
        if (selectedSaveSlotIndex >= 0) RememberSelectedSlot(saveSlots[selectedSaveSlotIndex]);
        else if (File.Exists(SelectedSlotPath)) File.Delete(SelectedSlotPath);
        status = $"Deleted Slot {slot}.";
    }

    void LoadSlot(int slot)
    {
        string path = SlotPath(slot);
        if (!File.Exists(path)) { status = $"Slot {slot} could not be found."; RefreshSaveSlots(); return; }
        string legacyPath = Path.Combine(Application.persistentDataPath, "MarcusSongs_IrregularLevel.json");
        File.Copy(path, legacyPath, true);
        LoadLevel();
        selectedSaveSlotIndex = saveSlots.IndexOf(slot);
        RememberSelectedSlot(slot);
        slotNameInput = GetSlotName(slot);
        status = $"Loaded Slot {slot}.";
    }

    string CaptureEditorState()
    {
        var data = new SavedLevel();
        foreach (Vector3Int cell in cells.Keys) data.cells.Add(cell);
        foreach (BlockedFaceData face in blockedFaces)
            data.blockedFaces.Add(new BlockedFaceData { cell = face.cell, normal = face.normal });
        if (spawnCube != null)
        {
            data.hasSpawnPoint = true;
            data.spawnPoint = LocalToCell(spawnCube.localPosition);
            data.spawnFaceNormal = SnapToFace(transform.InverseTransformDirection(spawnCube.TransformDirection(spawnFaceNormal)));
        }
        if (goalCube != null)
        {
            data.hasGoalPoint = true;
            data.goalPoint = LocalToCell(goalCube.localPosition);
            data.goalFaceNormal = SnapToFace(transform.InverseTransformDirection(goalCube.TransformDirection(goalFaceNormal)));
        }
        foreach (RotationLayer layer in layers)
            data.layers.Add(new SavedLayer { name = layer.name, axis = (int)layer.axis, axisPoint = layer.axisPoint });
        data.activeLayerIndex = activeLayerIndex;
        return JsonUtility.ToJson(data);
    }

    void TrackEditorHistory()
    {
        if (applyingHistory || lastHistoryState == null || rotating) return;
        string current = CaptureEditorState();
        if (current == lastHistoryState) return;
        undoHistory.Add(lastHistoryState);
        if (undoHistory.Count > 100) undoHistory.RemoveAt(0);
        redoHistory.Clear();
        lastHistoryState = current;
    }

    void UndoEditorAction()
    {
        if (undoHistory.Count == 0) return;
        redoHistory.Add(CaptureEditorState());
        string previous = undoHistory[undoHistory.Count - 1];
        undoHistory.RemoveAt(undoHistory.Count - 1);
        RestoreEditorState(previous);
        status = "Undid the last level edit.";
    }

    void RedoEditorAction()
    {
        if (redoHistory.Count == 0) return;
        undoHistory.Add(CaptureEditorState());
        string next = redoHistory[redoHistory.Count - 1];
        redoHistory.RemoveAt(redoHistory.Count - 1);
        RestoreEditorState(next);
        status = "Redid the level edit.";
    }

    void RestoreEditorState(string json)
    {
        SavedLevel data = JsonUtility.FromJson<SavedLevel>(json);
        if (data == null) return;
        applyingHistory = true;
        foreach (Transform cube in new List<Transform>(cells.Values)) if (cube != null) Destroy(cube.gameObject);
        SetSpawnPoint(null);
        SetGoalPoint(null);
        cells.Clear(); blockedFaces.Clear(); layers.Clear(); activeLayerIndex = -1;
        foreach (Vector3Int cell in data.cells) AddCell(cell);
        foreach (BlockedFaceData face in data.blockedFaces)
            if (face != null && cells.ContainsKey(face.cell)) AddBlockedFaceFromSave(face.cell, SnapToFace(face.normal));
        foreach (SavedLayer saved in data.layers)
        {
            AddLayer(saved.name);
            ActiveLayer.axis = (Axis)Mathf.Clamp(saved.axis, 0, 2);
            ActiveLayer.axisPoint = saved.axisPoint;
        }
        activeLayerIndex = layers.Count == 0 ? -1 : Mathf.Clamp(data.activeLayerIndex, 0, layers.Count - 1);
        if (data.hasSpawnPoint && cells.ContainsKey(data.spawnPoint)) SetSpawnPoint(data.spawnPoint, data.spawnFaceNormal);
        if (data.hasGoalPoint && cells.ContainsKey(data.goalPoint)) SetGoalPoint(data.goalPoint, data.goalFaceNormal);
        RefreshCellColors(); UpdateAxisLine();
        if (viewCamera != null) UpdateCamera();
        lastHistoryState = CaptureEditorState();
        applyingHistory = false;
    }

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
        LoadLevelFromPath(path);
    }

    void LoadBundledLevel()
    {
        string path = Path.Combine(Application.dataPath, "Levels", "Level01_Skybridge.json");
        status = string.Empty;
        LoadLevelFromPath(path);
        if (status.StartsWith("Loaded irregular level from "))
        {
            slotNameInput = "Skybridge";
            status = "Loaded Skybridge from Assets/Levels/Level01_Skybridge.json. Save it as a slot to play it.";
        }
    }

    void LoadLevelFromPath(string path)
    {
        if (!File.Exists(path))
        {
            status = $"Level JSON not found: {path}";
            return;
        }

        SavedLevel data;
        try { data = JsonUtility.FromJson<SavedLevel>(File.ReadAllText(path)); }
        catch (Exception exception)
        {
            status = $"Level JSON could not be read: {exception.Message}";
            return;
        }
        if (data == null || data.cells == null)
        {
            status = "Level JSON is empty or has no cells.";
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
        status = $"Loaded irregular level from {Path.GetFileName(path)}.";
        lastHistoryState = CaptureEditorState();
        undoHistory.Clear();
        redoHistory.Clear();
    }

    void AddBlockedFaceFromSave(Vector3Int cell, Vector3Int normal)
    {
        if (HasBlockedFace(cell, normal)) return;
        blockedFaces.Add(new BlockedFaceData { cell = cell, normal = normal });
        if (cells.TryGetValue(cell, out Transform cube)) CreateBlockedFaceVisual(cube, normal);
    }

}
