using UnityEditor;
using UnityEngine;

public static class AddSceneToBuildTemp
{
    [MenuItem("Tools/Add TutorialScence to Build")]
    public static void DoAdd()
    {
        var scenes = EditorBuildSettings.scenes;
        bool found = false;
        foreach (var s in scenes)
        {
            if (s.path == "Assets/Scenes/TutorialScence.unity")
            {
                found = true;
                break;
            }
        }
        
        if (!found)
        {
            var newScenes = new EditorBuildSettingsScene[scenes.Length + 1];
            System.Array.Copy(scenes, newScenes, scenes.Length);
            newScenes[scenes.Length] = new EditorBuildSettingsScene("Assets/Scenes/TutorialScence.unity", true);
            EditorBuildSettings.scenes = newScenes;
            Debug.Log("[MCP] Added TutorialScence.unity to Build Settings manually via MenuItem.");
        }
    }
}
