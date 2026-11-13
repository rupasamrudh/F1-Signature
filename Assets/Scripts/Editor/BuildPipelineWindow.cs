// BuildPipelineWindow.cs — One-click Build and Run Android with pre-build checks, keystore, and report dialog.
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace F1Sim.Editor
{
    public static class BuildPipelineWindow
    {
        public const string DefaultApkPath = "Builds/F1Signature.apk";

        [MenuItem("Tools/F1 Signature/Build and Run Android")]
        public static void BuildAndRunAndroidMenu()
        {
            BuildAndroid(autoRun: true);
        }

        [MenuItem("Tools/F1 Signature/Build Android (APK Only)")]
        public static void BuildAndroidOnlyMenu()
        {
            BuildAndroid(autoRun: false);
        }

        [MenuItem("Tools/F1 Signature/Run Pre-Build Checks")]
        public static void RunPreBuildChecksMenu()
        {
            string[] scenes = GetTargetSceneList();
            var errors = PreBuildChecker.RunAllChecks(scenes);
            if (errors.Count == 0)
            {
                EditorUtility.DisplayDialog("Pre-Build Checks", "All pre-build checks PASSED successfully! The project is safe to build.", "OK");
                UnityEngine.Debug.Log("[PreBuildChecker] All checks passed.");
            }
            else
            {
                string msg = $"Pre-Build Checks FAILED with {errors.Count} error(s):\n\n" + string.Join("\n\n", errors);
                EditorUtility.DisplayDialog("Pre-Build Checks Failed", msg, "OK");
                UnityEngine.Debug.LogError($"[PreBuildChecker] {msg}");
            }
        }

        public static void BuildAndroid(bool autoRun)
        {
            // 1. Save all open scenes and assets
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            // 2. Build target scenes list
            string[] scenes = GetTargetSceneList();

            // 3. Pre-build checks
            var preBuildErrors = PreBuildChecker.RunAllChecks(scenes);
            if (preBuildErrors.Count > 0)
            {
                string errorMsg = $"Build Aborted! Pre-Build Checks FAILED with {preBuildErrors.Count} error(s):\n\n" +
                                  string.Join("\n\n", preBuildErrors);
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("Build Pre-Check Failed", errorMsg, "OK");
                }
                UnityEngine.Debug.LogError($"[BuildPipeline] {errorMsg}");
                return;
            }

            // 4. ADB Device Check
            string deviceDesc = "Skipped";
            if (autoRun)
            {
                bool adbOk = AdbDeviceChecker.CheckDevicesForBuild(out deviceDesc);
                if (!adbOk)
                {
                    UnityEngine.Debug.Log("[BuildPipeline] Build cancelled by user during ADB device check.");
                    return;
                }
            }

            // 5. Ensure custom keystore configured
            bool keystoreOk = AndroidKeystoreManager.EnsureKeystoreConfigured();
            if (!keystoreOk)
            {
                EditorUtility.DisplayDialog("Keystore Error", "Failed to configure custom keystore outside repository. Aborting build.", "OK");
                return;
            }

            // 6. Auto-increment Android bundle version code
            PlayerSettings.Android.bundleVersionCode++;
            UnityEngine.Debug.Log($"[BuildPipeline] Incremented Android bundleVersionCode to {PlayerSettings.Android.bundleVersionCode}");

            // 7. Update Build Stamp
            BuildStampPreprocessor.GenerateBuildStamp();
            AssetDatabase.SaveAssets();

            // 8. Delete old APK
            string apkPath = DefaultApkPath;
            string outDir = Path.GetDirectoryName(apkPath);
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            if (File.Exists(apkPath))
            {
                try { File.Delete(apkPath); }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[BuildPipeline] Could not delete old APK: {ex.Message}");
                }
            }

            DateTime buildStartTime = DateTime.UtcNow;

            // 9. Build Player
            var buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = autoRun ? BuildOptions.AutoRunPlayer : BuildOptions.None
            };

            UnityEngine.Debug.Log($"[BuildPipeline] Starting Android build... Target: {apkPath}, Scenes: {string.Join(", ", scenes)}");
            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

            // 10. Check results & verify APK modified time
            bool apkExists = File.Exists(apkPath);
            bool isModifiedNewer = false;
            long apkSizeBytes = 0;

            if (apkExists)
            {
                DateTime lastWrite = File.GetLastWriteTimeUtc(apkPath);
                isModifiedNewer = lastWrite >= buildStartTime.AddSeconds(-2);
                apkSizeBytes = new FileInfo(apkPath).Length;
            }

            string sizeStr = apkExists ? $"{(apkSizeBytes / (1024f * 1024f)):F2} MB ({apkSizeBytes:N0} bytes)" : "0 MB";

            // Extract first error if failed
            string firstError = "";
            if (report.summary.totalErrors > 0)
            {
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        {
                            firstError = msg.content;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(firstError)) break;
                }
            }

            string timeStr = $"{report.summary.totalTime.TotalSeconds:F1}s";
            string modifiedCheckStr = isModifiedNewer
                ? "PASSED (Newer than build start)"
                : (apkExists ? "FAILED (APK timestamp not updated!)" : "FAILED (APK not created)");

            string summaryMsg = $"Result: {report.summary.result}\n" +
                                $"Total Errors: {report.summary.totalErrors}\n" +
                                $"Build Time: {timeStr}\n" +
                                $"APK Size: {sizeStr}\n" +
                                $"APK Modified Time: {modifiedCheckStr}\n" +
                                $"Target Device: {deviceDesc}\n" +
                                $"Bundle Version Code: {PlayerSettings.Android.bundleVersionCode}\n\n" +
                                $"Included Scenes ({scenes.Length}):\n  • {string.Join("\n  • ", scenes)}" +
                                (!string.IsNullOrEmpty(firstError) ? $"\n\nFirst Error:\n{firstError}" : "");

            UnityEngine.Debug.Log($"[BuildPipeline] Build Finished:\n{summaryMsg}");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("F1 Signature — Android Build Report", summaryMsg, "OK");
            }
        }

        public static string[] GetTargetSceneList()
        {
            var scenes = new List<string>();
            string trackScene = "Assets/Scenes/Track_F1Signature.unity";
            string playgroundScene = "Assets/Scenes/Playground.unity";
            string sampleScene = "Assets/Scenes/SampleScene.unity";

            if (File.Exists(trackScene)) scenes.Add(trackScene);
            if (File.Exists(playgroundScene)) scenes.Add(playgroundScene);
            else if (File.Exists(sampleScene)) scenes.Add(sampleScene);

            if (scenes.Count == 0)
            {
                scenes.Add("Assets/Scenes/Track_F1Signature.unity");
            }
            return scenes.ToArray();
        }
    }
}
