// PreBuildChecker.cs — Validates scenes, scripts, runtime data access, and track scene before build.
using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using F1Sim.Track;

namespace F1Sim.Editor
{
    public static class PreBuildChecker
    {
        public static List<string> RunAllChecks(string[] scenePaths)
        {
            var errors = new List<string>();

            // (a) Any scene referenced in the scene list is missing or dirty
            CheckScenes(scenePaths, errors);

            // (b) Any script outside an Editor folder uses UnityEditor without a #if UNITY_EDITOR guard
            CheckUnityEditorUsage(errors);

            // (c) Any runtime script reads Application.dataPath, "Assets/" paths or System.IO.File on project files
            CheckRuntimeFileAccess(errors);

            // (d) The Track scene has no Track object or the track data asset is not referenced
            CheckTrackScene(scenePaths, errors);

            // (e) Scene validator: missing scripts, broken object references, scene opening errors
            CheckSceneValidation(scenePaths, errors);

            return errors;
        }

        public static void CheckSceneValidation(string[] scenePaths, List<string> errors)
        {
            if (scenePaths == null) return;
            foreach (var path in scenePaths)
            {
                var vr = SceneValidator.ValidateScene(path);
                if (!vr.IsValid)
                {
                    foreach (var ms in vr.missingScripts)
                        errors.Add($"[SceneValidator] Missing script on: {ms} in scene '{vr.scenePath}'");
                    foreach (var br in vr.brokenReferences)
                        errors.Add($"[SceneValidator] Broken object reference: {br} in scene '{vr.scenePath}'");
                    foreach (var le in vr.loggedErrors)
                        errors.Add($"[SceneValidator] Error during load: {le} in scene '{vr.scenePath}'");
                }
            }
        }

        public static void CheckScenes(string[] scenePaths, List<string> errors)
        {
            if (scenePaths == null || scenePaths.Length == 0)
            {
                errors.Add("No scenes provided in build scene list.");
                return;
            }

            foreach (var path in scenePaths)
            {
                if (!File.Exists(path))
                {
                    errors.Add($"Scene file missing: {path}");
                    continue;
                }

                // Check if currently loaded and has unsaved changes
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.path == path && scene.isDirty)
                    {
                        errors.Add($"Scene has unsaved modifications (dirty): {path}. Save it before building.");
                    }
                }
            }
        }

        public static void CheckUnityEditorUsage(List<string> errors)
        {
            var csFiles = GetRuntimeScriptFiles();
            foreach (var file in csFiles)
            {
                string relPath = file.Replace('\\', '/');
                string[] lines = File.ReadAllLines(file);
                int editorGuardDepth = 0;

                for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
                {
                    string rawLine = lines[lineIdx];
                    string line = rawLine.Trim();

                    // Track #if / #endif guards
                    if (line.StartsWith("#if"))
                    {
                        if (line.Contains("UNITY_EDITOR")) editorGuardDepth++;
                    }
                    else if (line.StartsWith("#endif"))
                    {
                        if (editorGuardDepth > 0) editorGuardDepth--;
                    }

                    // Ignore comments
                    if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*"))
                        continue;

                    if (editorGuardDepth == 0)
                    {
                        if (line.Contains("using UnityEditor;") || Regex.IsMatch(line, @"\bUnityEditor\b"))
                        {
                            errors.Add($"[Check (b)] Script outside Editor folder '{relPath}' uses UnityEditor on line {lineIdx + 1} without #if UNITY_EDITOR guard: \"{line}\"");
                        }
                    }
                }
            }
        }

        public static void CheckRuntimeFileAccess(List<string> errors)
        {
            var csFiles = GetRuntimeScriptFiles();
            foreach (var file in csFiles)
            {
                string relPath = file.Replace('\\', '/');
                string[] lines = File.ReadAllLines(file);
                int editorGuardDepth = 0;

                for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
                {
                    string rawLine = lines[lineIdx];
                    string line = rawLine.Trim();

                    if (line.StartsWith("#if"))
                    {
                        if (line.Contains("UNITY_EDITOR")) editorGuardDepth++;
                    }
                    else if (line.StartsWith("#endif"))
                    {
                        if (editorGuardDepth > 0) editorGuardDepth--;
                    }

                    if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*"))
                        continue;

                    if (editorGuardDepth == 0)
                    {
                        // Check Application.dataPath
                        if (line.Contains("Application.dataPath"))
                        {
                            errors.Add($"[Check (c)] Runtime script '{relPath}' reads Application.dataPath on line {lineIdx + 1}: \"{line}\"");
                        }

                        // Check "Assets/" or "Assets\\" path strings
                        if (line.Contains("\"Assets/") || line.Contains("\"Assets\\\\"))
                        {
                            errors.Add($"[Check (c)] Runtime script '{relPath}' uses hardcoded project path on line {lineIdx + 1}: \"{line}\"");
                        }

                        // Check System.IO.File calls on project files (exclude persistentDataPath for telemetry)
                        if (Regex.IsMatch(line, @"\b(System\.IO\.)?File\.(ReadAllText|ReadAllLines|OpenRead|Exists|Open)\b") &&
                            !line.Contains("persistentDataPath"))
                        {
                            errors.Add($"[Check (c)] Runtime script '{relPath}' calls File IO on project files on line {lineIdx + 1}: \"{line}\"");
                        }
                    }
                }
            }
        }

        public static void CheckTrackScene(string[] scenePaths, List<string> errors)
        {
            string trackScenePath = "Assets/Scenes/Track_F1Signature.unity";
            if (!File.Exists(trackScenePath))
            {
                errors.Add($"[Check (d)] Track scene missing: {trackScenePath}");
                return;
            }

            // Inspect scene YAML for Track object
            string sceneText = File.ReadAllText(trackScenePath);
            bool hasTrackObject = sceneText.Contains("m_Name: Track_Built") ||
                                 sceneText.Contains("m_Name: Track") ||
                                 sceneText.Contains("Track.prefab");

            if (!hasTrackObject)
            {
                errors.Add($"[Check (d)] Track scene '{trackScenePath}' contains no Track object (expected 'Track_Built' or 'Track'). Run Tools/F1 Signature/Setup Track Scene first.");
            }

            // Check track data asset in Resources
            var trackAsset = Resources.Load<TextAsset>("Tracks/f1sig_track");
            if (trackAsset == null)
            {
                errors.Add("[Check (d)] Track data asset missing in Resources/Tracks/f1sig_track.json. Devices cannot load track data.");
            }
            else
            {
                var cfg = TrackDataLoader.LoadTrack();
                if (cfg == null || cfg.samples == null || cfg.samples.Count == 0)
                {
                    errors.Add("[Check (d)] Track data in Resources/Tracks/f1sig_track failed to deserialize or has 0 samples.");
                }
            }
        }

        static List<string> GetRuntimeScriptFiles()
        {
            var result = new List<string>();
            string scriptsDir = "Assets/Scripts";
            if (!Directory.Exists(scriptsDir)) return result;

            var allCs = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);
            foreach (var f in allCs)
            {
                string norm = f.Replace('\\', '/');
                if (!norm.Contains("/Editor/"))
                {
                    result.Add(f);
                }
            }
            return result;
        }
    }
}

