using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IrregularLevelRuntimeSceneBuilder
{
    const string ScenePath = "Assets/Scenes/IrregularLevelPlay.unity";

    [MenuItem("Tools/Marcus's Songs/Create Irregular Level Play Scene")]
    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var runtime = new GameObject("Irregular Level Runtime");
        runtime.AddComponent<IrregularLevelRuntime>();

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;
        cameraObject.AddComponent<AudioListener>();

        var lightObject = new GameObject("Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created the irregular level play scene at {ScenePath}");
    }
}
