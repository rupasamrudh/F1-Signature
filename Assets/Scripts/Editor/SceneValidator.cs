// SceneValidator.cs — Editor validator checking missing scripts, broken object references, and scene opening errors across all build scenes.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1Sim.Editor
{
    public class SceneValidationResult
    {
        public string scenePath;
        public List<string> missingScripts = new List<string>();
        public List<string> brokenReferences = new List<string>();
        public List<string> loggedErrors = new List<string>();

        public bool IsValid => missingScripts.Count == 0 && brokenReferences.Count == 0 && loggedErrors.Count == 0;
        public int TotalIssues => missingScripts.Count + brokenReferences.Count + loggedErrors.Count;
    }

    public static class SceneValidator
    {
        [MenuItem("Tools/F1 Signature/Validate Scenes")]
        public static bool ValidateAllBuildScenesMenu()
        {
            var results = ValidateAllBuildScenes();
            bool allValid = true;
            var sb = new StringBuilder();

            foreach (var r in results)
            {
                if (!r.IsValid)
                {
                    allValid = false;
                    sb.AppendLine($"[FAILED] {r.scenePath}:");
                    foreach (var s in r.missingScripts) sb.AppendLine($"  - Missing Script: {s}");
                    foreach (var b in r.brokenReferences) sb.AppendLine($"  - Broken Reference: {b}");
                    foreach (var e in r.loggedErrors) sb.AppendLine($"  - Logged Error: {e}");
                }
                else
                {
                    sb.AppendLine($"[PASSED] {r.scenePath} (0 missing scripts, 0 broken references, 0 errors)");
                }
            }

            string report = sb.ToString();
            Debug.Log($"[SceneValidator] Validation Report:\n{report}");

            EditorUtility.DisplayDialog(
                "Scene Validation",
                allValid ? "All build scenes PASSED validation!\n\n" + report : "Scene validation FAILED!\n\n" + report,
                "OK"
            );

            return allValid;
        }

        public static List<SceneValidationResult> ValidateAllBuildScenes()
        {
            var list = new List<SceneValidationResult>();
            foreach (var sceneEntry in EditorBuildSettings.scenes)
            {
                if (!sceneEntry.enabled) continue;
                list.Add(ValidateScene(sceneEntry.path));
            }
            return list;
        }

        public static SceneValidationResult ValidateScene(string scenePath)
        {
            var result = new SceneValidationResult { scenePath = scenePath };

            if (!System.IO.File.Exists(scenePath))
            {
                result.loggedErrors.Add($"Scene file does not exist: {scenePath}");
                return result;
            }

            // Hook error logs during scene loading
            void LogHandler(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    result.loggedErrors.Add(condition);
                }
            }

            Application.logMessageReceived += LogHandler;
            Scene scene;
            try
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            finally
            {
                Application.logMessageReceived -= LogHandler;
            }

            if (!scene.IsValid())
            {
                result.loggedErrors.Add($"Failed to load scene into valid state: {scenePath}");
                return result;
            }

            var rootObjects = scene.GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                ValidateGameObjectHierarchy(root, "", result);
            }

            return result;
        }

        static void ValidateGameObjectHierarchy(GameObject go, string parentPath, SceneValidationResult result)
        {
            string currentPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

            // (a) Check missing scripts
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    result.missingScripts.Add($"{currentPath} (component index {i})");
                    continue;
                }

                // (b) Check object references using SerializedProperty
                try
                {
                    var so = new SerializedObject(components[i]);
                    var prop = so.GetIterator();
                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            // Flag references with non-zero instance/entity id that resolves to null
                            if (prop.objectReferenceValue == null && prop.objectReferenceEntityIdValue != default)
                            {
                                result.brokenReferences.Add($"{currentPath} -> {components[i].GetType().Name}.{prop.propertyPath} (EntityId: {prop.objectReferenceEntityIdValue})");
                            }
                        }
                    }
                }
                catch
                {
                    // Some components might not serialize or be valid in editor
                }
            }

            // Recurse into children
            for (int i = 0; i < go.transform.childCount; i++)
            {
                ValidateGameObjectHierarchy(go.transform.GetChild(i).gameObject, currentPath, result);
            }
        }
    }
}
