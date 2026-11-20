using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public partial class IrregularLevelEditor
{
    void OnGUI()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.82f, 0.87f, 0.94f) } };
        }

        GUILayout.BeginArea(new Rect(18, 18, 310, Screen.height - 36), GUI.skin.box);
        panelScroll = GUILayout.BeginScrollView(panelScroll, GUILayout.Width(290), GUILayout.Height(Screen.height - 56));
        GUILayout.Label("Irregular Cube Level Editor", titleStyle);
        GUILayout.Label("Unlimited grid · the active grid slice rotates 90° around the blue axis", hintStyle);
        GUILayout.Space(8);
        GUILayout.Label($"Mode: {mode}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(mode == EditMode.Place, "Place", GUI.skin.button)) mode = EditMode.Place;
        if (GUILayout.Toggle(mode == EditMode.PaintBlockedFace, "Black face", GUI.skin.button)) mode = EditMode.PaintBlockedFace;
        if (GUILayout.Toggle(mode == EditMode.SetSpawnPoint, "Set spawn", GUI.skin.button)) mode = EditMode.SetSpawnPoint;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(mode == EditMode.SetGoalPoint, "Set goal", GUI.skin.button)) mode = EditMode.SetGoalPoint;
        if (GUILayout.Toggle(mode == EditMode.Erase, "Erase", GUI.skin.button)) mode = EditMode.Erase;
        GUILayout.EndHorizontal();
        string modeHint = mode == EditMode.SetSpawnPoint
            ? "Click a walkable cube face to set the player start."
            : mode == EditMode.SetGoalPoint
                ? "Click a walkable cube face to set the level goal. The goal moves with its cube when a layer turns."
            : mode == EditMode.PaintBlockedFace
                ? "Click one cube face to paint or erase a black, non-walkable face. Other faces stay usable."
                : "Click a cube to use the selected mode.";
        GUILayout.Label(modeHint, hintStyle);
        GUILayout.Label(spawnCube == null
            ? "Player spawn: not set"
            : $"Player spawn: {LocalToCell(spawnCube.localPosition)}, face {spawnFaceNormal}", hintStyle);
        GUILayout.Label(goalCube == null
            ? "Player goal: not set"
            : $"Player goal: {LocalToCell(goalCube.localPosition)}, face {goalFaceNormal}", hintStyle);
        GUILayout.Space(8);

        GUILayout.Label("Rotation layers");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ New layer")) AddLayer($"Layer {layers.Count + 1}");
        using (new GUIEnabledScope(ActiveLayer != null))
            if (GUILayout.Button("Delete"))
            {
                layers.RemoveAt(activeLayerIndex);
                activeLayerIndex = layers.Count == 0 ? -1 : Mathf.Clamp(activeLayerIndex, 0, layers.Count - 1);
                RefreshCellColors(); UpdateAxisLine(); status = "Deleted the active layer.";
            }
        GUILayout.EndHorizontal();
        if (layers.Count > 0)
        {
            string[] layerNames = layers.ConvertAll(layer => layer.name).ToArray();
            int selectedLayer = GUILayout.SelectionGrid(activeLayerIndex, layerNames, Mathf.Min(3, layerNames.Length));
            if (selectedLayer != activeLayerIndex)
            {
                activeLayerIndex = selectedLayer;
                RefreshCellColors(); UpdateAxisLine();
            }
        }
        else GUILayout.Label("Create a layer, then choose its axis and grid slice.", hintStyle);

        RotationLayer active = ActiveLayer;
        using (new GUIEnabledScope(active != null))
        {
        GUILayout.Label(active == null ? "Rotation axis: —" : $"{active.name}: {active.axis} axis slice");
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(active != null && active.axis == Axis.X, "X axis", GUI.skin.button)) active.axis = Axis.X;
        if (GUILayout.Toggle(active != null && active.axis == Axis.Y, "Y axis", GUI.skin.button)) active.axis = Axis.Y;
        if (GUILayout.Toggle(active != null && active.axis == Axis.Z, "Z axis", GUI.skin.button)) active.axis = Axis.Z;
        GUILayout.EndHorizontal();
        if (active != null && active.axis == Axis.X)
        {
            active.axisPoint.x = AxisCoordinateRow("Layer X", active.axisPoint.x);
            active.axisPoint.y = AxisCoordinateRow("Axis Y", active.axisPoint.y);
            active.axisPoint.z = AxisCoordinateRow("Axis Z", active.axisPoint.z);
        }
        else if (active != null && active.axis == Axis.Y)
        {
            active.axisPoint.x = AxisCoordinateRow("Axis X", active.axisPoint.x);
            active.axisPoint.y = AxisCoordinateRow("Layer Y", active.axisPoint.y);
            active.axisPoint.z = AxisCoordinateRow("Axis Z", active.axisPoint.z);
        }
        else if (active != null)
        {
            active.axisPoint.x = AxisCoordinateRow("Axis X", active.axisPoint.x);
            active.axisPoint.y = AxisCoordinateRow("Axis Y", active.axisPoint.y);
            active.axisPoint.z = AxisCoordinateRow("Layer Z", active.axisPoint.z);
        }
        UpdateAxisLine();
        RefreshCellColors();
        }

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        using (new GUIEnabledScope(!rotating && undoHistory.Count > 0))
            if (GUILayout.Button("Undo")) UndoEditorAction();
        using (new GUIEnabledScope(!rotating && redoHistory.Count > 0))
            if (GUILayout.Button("Redo")) RedoEditorAction();
        GUILayout.EndHorizontal();

        GUILayout.Label("Place at any grid coordinate");
        GUILayout.BeginHorizontal();
        GUILayout.Label("X", GUILayout.Width(14));
        placementX = GUILayout.TextField(placementX, GUILayout.Width(48));
        GUILayout.Label("Y", GUILayout.Width(14));
        placementY = GUILayout.TextField(placementY, GUILayout.Width(48));
        GUILayout.Label("Z", GUILayout.Width(14));
        placementZ = GUILayout.TextField(placementZ, GUILayout.Width(48));
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Place cube at coordinate"))
        {
            if (!int.TryParse(placementX, out int x) || !int.TryParse(placementY, out int y) || !int.TryParse(placementZ, out int z))
                status = "Enter whole numbers for X, Y, and Z.";
            else
            {
                placementCoordinate = new Vector3Int(x, y, z);
                if (cells.ContainsKey(placementCoordinate)) status = $"Cell {placementCoordinate} is already occupied.";
                else
                {
                    AddCell(placementCoordinate);
                    status = $"Placed cube at {placementCoordinate}.";
                }
            }
        }

        GUILayout.BeginHorizontal();
        using (new GUIEnabledScope(!rotating && active != null && CanRotate(1, out _)))
            if (GUILayout.Button("Rotate +90°", GUILayout.Height(34))) RotateLayer(1);
        using (new GUIEnabledScope(!rotating && active != null && CanRotate(-1, out _)))
            if (GUILayout.Button("Rotate -90°", GUILayout.Height(34))) RotateLayer(-1);
        GUILayout.EndHorizontal();
        GUILayout.Label($"Cubes currently in layer: {(active == null ? 0 : GetLayerCubes(active).Count)}", hintStyle);

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        using (new GUIEnabledScope(!rotating))
        {
            if (GUILayout.Button("Clear all"))
            {
                ClearLevel();
                AddCell(Vector3Int.zero);
                status = "Cleared the level and left one starter cube at (0, 0, 0).";
            }
            if (GUILayout.Button("Reset example"))
            {
                ClearLevel();
                CreateExample();
                status = "Restored the irregular example.";
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Saved slots");
        RefreshSaveSlots();
        if (saveSlots.Count == 0) GUILayout.Label("No saved slots yet.", hintStyle);
        else
        {
            string[] slotNames = saveSlots.ConvertAll(GetSlotName).ToArray();
            selectedSaveSlotIndex = Mathf.Clamp(selectedSaveSlotIndex, 0, saveSlots.Count - 1);
            int previousSelection = selectedSaveSlotIndex;
            selectedSaveSlotIndex = GUILayout.SelectionGrid(selectedSaveSlotIndex, slotNames, 2);
            if (selectedSaveSlotIndex != previousSelection)
            {
                slotNameInput = GetSlotName(saveSlots[selectedSaveSlotIndex]);
                int slot = saveSlots[selectedSaveSlotIndex];
                RememberSelectedSlot(slot);
            }
        }
        GUILayout.BeginHorizontal();
        GUILayout.Label("Slot name", GUILayout.Width(68));
        slotNameInput = GUILayout.TextField(slotNameInput);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save as new slot")) SaveAsNewSlot();
        using (new GUIEnabledScope(selectedSaveSlotIndex >= 0 && selectedSaveSlotIndex < saveSlots.Count))
            if (GUILayout.Button("Overwrite selected")) OverwriteSelectedSlot();
        GUILayout.EndHorizontal();
        using (new GUIEnabledScope(selectedSaveSlotIndex >= 0 && selectedSaveSlotIndex < saveSlots.Count))
            if (GUILayout.Button("Rename selected slot")) RenameSelectedSlot();
        using (new GUIEnabledScope(selectedSaveSlotIndex >= 0 && selectedSaveSlotIndex < saveSlots.Count))
            if (GUILayout.Button("Delete selected slot")) pendingDeleteSlot = saveSlots[selectedSaveSlotIndex];
        if (pendingDeleteSlot >= 0)
        {
            GUILayout.Label($"Delete Slot {pendingDeleteSlot} permanently?");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm delete")) DeleteSlot(pendingDeleteSlot);
            if (GUILayout.Button("Cancel")) pendingDeleteSlot = -1;
            GUILayout.EndHorizontal();
        }
        using (new GUIEnabledScope(selectedSaveSlotIndex >= 0 && selectedSaveSlotIndex < saveSlots.Count))
            if (GUILayout.Button("Load selected slot")) LoadSelectedSlot();
        if (GUILayout.Button("Play saved level"))
        {
            if (selectedSaveSlotIndex >= 0 && selectedSaveSlotIndex < saveSlots.Count) OverwriteSelectedSlot();
            else SaveAsNewSlot();
            SceneManager.LoadScene("IrregularLevelPlay");
        }

        GUILayout.Space(8);
        GUILayout.Label(status, hintStyle);
        GUILayout.Label("Right-drag: orbit · Middle-drag: pan · Wheel: zoom", hintStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    int AxisCoordinateRow(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(68));
        if (GUILayout.Button("−", GUILayout.Width(28))) value--;
        GUILayout.Label(value.ToString(), GUILayout.Width(40));
        if (GUILayout.Button("+", GUILayout.Width(28))) value++;
        GUILayout.EndHorizontal();
        return value;
    }

    Vector3 MapCenter()
    {
        if (cells.Count == 0) return Vector3.zero;
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3Int cell in cells.Keys)
        {
            Vector3 position = CellToLocal(cell);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        return (min + max) * 0.5f;
    }

}
