using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IrregularLevelEditorSceneBuilder
{
    const string ScenePath = "Assets/Scenes/IrregularLevelEditor.unity";

    [MenuItem("Tools/Marcus's Songs/Create Irregular Level Editor Scene")]
    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var level = new GameObject("Irregular Level Editor");
        level.AddComponent<IrregularLevelEditor>();

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        cameraObject.AddComponent<AudioListener>();

        var lightObject = new GameObject("Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created the isolated irregular level editor scene at {ScenePath}");
    }
}
