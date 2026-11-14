using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RubiksCubeController))]
public class RubiksCubeControllerEditor : Editor
{
    static readonly string[] moves = { "U", "U′", "D", "D′", "L", "L′", "R", "R′", "F", "F′", "B", "B′" };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var cube = (RubiksCubeController)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Debug", EditorStyles.boldLabel);
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("配置会在进入 Play Mode 时应用。运行时调试按钮会在播放时显示。", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Cubies", cube.Cubies.Count.ToString());
        EditorGUILayout.LabelField("State", cube.IsBusy ? "Animating" : "Ready");
        using (new EditorGUI.DisabledScope(cube.IsBusy))
        {
            if (GUILayout.Button("Shuffle")) cube.DebugShuffle();
            if (GUILayout.Button("Refresh Player Highlights")) cube.DebugRefreshPlayer();
            EditorGUILayout.LabelField("Face turns");
            for (int i = 0; i < moves.Length; i += 2)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(moves[i])) cube.DebugMove(moves[i]);
                if (GUILayout.Button(moves[i + 1])) cube.DebugMove(moves[i + 1]);
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
