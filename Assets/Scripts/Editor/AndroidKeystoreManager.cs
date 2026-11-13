// AndroidKeystoreManager.cs — Manages fixed custom keystore stored outside the repository.
using System;
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace F1Sim.Editor
{
    [Serializable]
    public class KeystoreConfig
    {
        public string keystorePath;
        public string keystorePass;
        public string keyaliasName;
        public string keyaliasPass;
    }

    public static class AndroidKeystoreManager
    {
        public const string SettingsPath = "UserSettings/AndroidKeystoreSettings.json";

        public static bool EnsureKeystoreConfigured()
        {
            KeystoreConfig config = LoadOrCreateConfig();
            if (config == null) return false;

            if (!File.Exists(config.keystorePath))
            {
                bool generated = TryGenerateKeystore(config);
                if (!generated)
                {
                    UnityEngine.Debug.LogError($"[AndroidKeystoreManager] Keystore file not found at: {config.keystorePath} and could not be generated.");
                    return false;
                }
            }

            // Apply to PlayerSettings
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = config.keystorePath;
            PlayerSettings.Android.keystorePass = config.keystorePass;
            PlayerSettings.Android.keyaliasName = config.keyaliasName;
            PlayerSettings.Android.keyaliasPass = config.keyaliasPass;

            UnityEngine.Debug.Log($"[AndroidKeystoreManager] Custom keystore configured: {config.keystorePath} (alias: {config.keyaliasName})");
            return true;
        }

        public static KeystoreConfig LoadOrCreateConfig()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    var cfg = JsonUtility.FromJson<KeystoreConfig>(json);
                    if (cfg != null && !string.IsNullOrEmpty(cfg.keystorePath))
                        return cfg;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[AndroidKeystoreManager] Error reading settings: {ex.Message}");
                }
            }

            // Create default settings pointing outside repo (in ~/.android)
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string androidDir = Path.Combine(userDir, ".android");
            if (!Directory.Exists(androidDir)) Directory.CreateDirectory(androidDir);

            string keystorePath = Path.Combine(androidDir, "f1signature.keystore");
            var defaultConfig = new KeystoreConfig
            {
                keystorePath = keystorePath,
                keystorePass = "f1sig2026pass",
                keyaliasName = "f1signature",
                keyaliasPass = "f1sig2026pass"
            };

            SaveConfig(defaultConfig);
            return defaultConfig;
        }

        public static void SaveConfig(KeystoreConfig config)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AndroidKeystoreManager] Failed to save {SettingsPath}: {ex.Message}");
            }
        }

        static bool TryGenerateKeystore(KeystoreConfig config)
        {
            string keytoolPath = FindKeytool();
            if (string.IsNullOrEmpty(keytoolPath) || !File.Exists(keytoolPath))
            {
                UnityEngine.Debug.LogError("[AndroidKeystoreManager] keytool.exe not found to generate keystore.");
                return false;
            }

            string dir = Path.GetDirectoryName(config.keystorePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string args = $"-genkeypair -v -keystore \"{config.keystorePath}\" -alias {config.keyaliasName} -keyalg RSA -keysize 2048 -validity 10000 -storepass \"{config.keystorePass}\" -keypass \"{config.keyaliasPass}\" -dname \"CN=F1Signature, OU=Game, O=F1Sig, L=Bangalore, S=KA, C=IN\"";

            try
            {
                var psi = new ProcessStartInfo(keytoolPath, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(10000);
                return p.ExitCode == 0 && File.Exists(config.keystorePath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[AndroidKeystoreManager] Failed to execute keytool: {ex.Message}");
                return false;
            }
        }

        public static string FindKeytool()
        {
            string jdkPath = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool.exe");
            if (File.Exists(jdkPath)) return jdkPath;

            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string kt = Path.Combine(javaHome, "bin", "keytool.exe");
                if (File.Exists(kt)) return kt;
            }
            return "keytool";
        }
    }
}

