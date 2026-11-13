// AdbDeviceChecker.cs — Verifies connected Android device via ADB before build.
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace F1Sim.Editor
{
    public class AdbDeviceInfo
    {
        public string Serial;
        public string Status;
        public string Model;
        public string RawLine;

        public override string ToString()
        {
            return !string.IsNullOrEmpty(Model) ? $"{Model} ({Serial})" : Serial;
        }
    }

    public static class AdbDeviceChecker
    {
        public static string FindAdb()
        {
            // Unity internal SDK path
            string sdkAdb = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
            if (File.Exists(sdkAdb)) return sdkAdb;

            // Android SDK Root from environment
            string sdkRoot = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            if (!string.IsNullOrEmpty(sdkRoot))
            {
                string adb = Path.Combine(sdkRoot, "platform-tools/adb.exe");
                if (File.Exists(adb)) return adb;
            }

            return "adb";
        }

        public static List<AdbDeviceInfo> GetConnectedDevices()
        {
            var devices = new List<AdbDeviceInfo>();
            string adbPath = FindAdb();

            try
            {
                var psi = new ProcessStartInfo(adbPath, "devices -l")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null) return devices;

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("List of devices") || trimmed.StartsWith("* daemon"))
                        continue;

                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var info = new AdbDeviceInfo
                        {
                            Serial = parts[0],
                            Status = parts[1],
                            RawLine = trimmed
                        };

                        // Extract model:XXX
                        foreach (var part in parts)
                        {
                            if (part.StartsWith("model:"))
                            {
                                info.Model = part.Substring("model:".Length).Replace('_', ' ');
                            }
                        }

                        devices.Add(info);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AdbDeviceChecker] adb query failed: {ex.Message}");
            }

            return devices;
        }

        /// Returns true to proceed with build; false to cancel.
        public static bool CheckDevicesForBuild(out string targetDeviceName)
        {
            var devices = GetConnectedDevices();
            if (devices.Count == 0)
            {
                targetDeviceName = "No device";
                if (Application.isBatchMode)
                {
                    UnityEngine.Debug.LogWarning("[AdbDeviceChecker] No Android devices connected via ADB (batch mode). Proceeding with APK build.");
                    return true;
                }
                bool proceed = EditorUtility.DisplayDialog(
                    "ADB Device Check",
                    "No Android devices connected via ADB.\n\nMake sure USB Debugging is enabled on your phone and it is connected via USB.\n\nProceed with building the APK anyway?",
                    "Build APK Anyway", "Cancel");
                return proceed;
            }
            else if (devices.Count == 1)
            {
                var dev = devices[0];
                targetDeviceName = dev.ToString();
                UnityEngine.Debug.Log($"[AdbDeviceChecker] Connected Android Device: {targetDeviceName} (status: {dev.Status})");
                return true;
            }
            else
            {
                targetDeviceName = $"{devices.Count} devices";
                if (Application.isBatchMode)
                {
                    UnityEngine.Debug.LogWarning($"[AdbDeviceChecker] Multiple Android devices connected ({devices.Count}) (batch mode). Proceeding with build.");
                    return true;
                }
                string list = string.Join("\n", devices);
                bool proceed = EditorUtility.DisplayDialog(
                    "ADB Device Check - Multiple Devices",
                    $"Multiple Android devices connected:\n{list}\n\nUnity will target the first active device. Proceed with build?",
                    "Proceed", "Cancel");
                return proceed;
            }
        }
    }
}
