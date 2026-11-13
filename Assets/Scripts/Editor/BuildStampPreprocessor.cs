// BuildStampPreprocessor.cs — Generates Resources/BuildInfo.txt at build time.
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace F1Sim.Editor
{
    public class BuildStampPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            GenerateBuildStamp(report);
        }

        public static string GenerateBuildStamp(BuildReport report = null)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string gitHash = GetGitShortHash();
            string unityVer = Application.unityVersion;
            int versionCode = PlayerSettings.Android.bundleVersionCode;

            List<string> sceneNames = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.enabled)
                {
                    sceneNames.Add(Path.GetFileNameWithoutExtension(s.path));
                }
            }
            string sceneListStr = string.Join(";", sceneNames);

            string content = $"timestamp={timestamp}\ngitHash={gitHash}\nunityVersion={unityVer}\nversionCode={versionCode}\nscenes={sceneListStr}\n";

            string resDir = "Assets/Resources";
            if (!Directory.Exists(resDir)) Directory.CreateDirectory(resDir);

            string buildInfoPath = Path.Combine(resDir, "BuildInfo.txt");
            File.WriteAllText(buildInfoPath, content);
            AssetDatabase.ImportAsset("Assets/Resources/BuildInfo.txt", ImportAssetOptions.ForceUpdate);

            UnityEngine.Debug.Log($"[BuildStamp] Generated BuildInfo: {timestamp} {gitHash} | Unity {unityVer} | v{versionCode} | Scenes: {sceneListStr}");
            return content;
        }

        public static string GetGitShortHash()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(3000);
                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return output;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[BuildStamp] git rev-parse failed: {ex.Message}");
            }
            return "nogit";
        }
    }
}

