#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayModeStartScene
{
    static PlayModeStartScene()
    {
        SetFirstSceneAsPlayModeStartScene();
    }

    private static void SetFirstSceneAsPlayModeStartScene()
    {
        var scenes = EditorBuildSettings.scenes;

        if (scenes == null || scenes.Length == 0)
        {
            Debug.LogWarning("Build Settings không có Scene nào.");
            return;
        }

        // Lấy scene enabled đầu tiên
        foreach (var scene in scenes)
        {
            if (!scene.enabled)
                continue;

            var sceneAsset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);

            EditorSceneManager.playModeStartScene = sceneAsset;

            Debug.Log($"Play Mode Start Scene: {scene.path}");
            return;
        }
    }
}

#endif