#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Audio;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace F1Sim.EditorTools
{
    public static class AudioSetupEditor
    {
        private const string ScenePath = "Assets/Scenes/Track_F1Signature.unity";
        private const string MixerPath = "Assets/Audio/F1VehicleAudioMixer.mixer";

        [MenuItem("Tools/F1 Signature/Setup Vehicle Audio System")]
        public static void SetupAudioSystemMenu()
        {
            SetupAudioSystem(true);
        }

        public static void SetupAudioSystem(bool logSuccess = true)
        {
            // 1. Ensure Mixer exists
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                var mixerControllerType = typeof(UnityEditor.AudioImporter).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
                if (mixerControllerType != null)
                {
                    var createMethod = mixerControllerType.GetMethod("CreateMixerControllerAtPath", 
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var createGroupMethod = mixerControllerType.GetMethod("CreateNewGroup", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var addChildMethod = mixerControllerType.GetMethod("AddChildToParent", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var masterGroupProp = mixerControllerType.GetProperty("masterGroup", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                    if (createMethod != null)
                    {
                        var controller = createMethod.Invoke(null, new object[] { MixerPath });
                        if (controller != null && masterGroupProp != null)
                        {
                            var mg = masterGroupProp.GetValue(controller);
                            if (createGroupMethod != null && addChildMethod != null)
                            {
                                var eg = createGroupMethod.Invoke(controller, new object[] { "Engine", true });
                                addChildMethod.Invoke(controller, new object[] { eg, mg });
                                var sg = createGroupMethod.Invoke(controller, new object[] { "SFX", true });
                                addChildMethod.Invoke(controller, new object[] { sg, mg });
                            }
                        }
                    }
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            }

            if (mixer == null)
            {
                Debug.LogError($"[AudioSetupEditor] Failed to create or load AudioMixer at {MixerPath}");
                return;
            }

            AudioMixerGroup[] masterGroups = mixer.FindMatchingGroups("Master");
            AudioMixerGroup[] engineGroups = mixer.FindMatchingGroups("Engine");
            AudioMixerGroup[] sfxGroups = mixer.FindMatchingGroups("SFX");

            AudioMixerGroup masterGroup = masterGroups.Length > 0 ? masterGroups[0] : null;
            AudioMixerGroup engineGroup = engineGroups.Length > 0 ? engineGroups[0] : masterGroup;
            AudioMixerGroup sfxGroup = sfxGroups.Length > 0 ? sfxGroups[0] : masterGroup;

            // 2. Load Audio Clips (The 3 Canonical Ferrari SF-26 Sounds)
            AudioClip sf26StandingStartClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SF26_Standing_Start.wav");
            AudioClip sf26UpClip            = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SF26_Gear_Upshift.wav");
            AudioClip sf26DownClip          = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SF26_Gear_Downshift.wav");

            // 3. Open Scene
            var currentScene = EditorSceneManager.GetActiveScene();
            if (currentScene.path != ScenePath)
            {
                currentScene = EditorSceneManager.OpenScene(ScenePath);
            }

            // 4. Find Car & Configure CarAudioManager
            var carController = Object.FindAnyObjectByType<CarController>();
            if (carController == null)
            {
                Debug.LogError("[AudioSetupEditor] CarController not found in scene!");
                return;
            }

            GameObject carObj = carController.gameObject;
            var audioMgr = carObj.GetComponent<CarAudioManager>();
            if (audioMgr == null)
            {
                audioMgr = carObj.AddComponent<CarAudioManager>();
            }

            // Clean up obsolete legacy child audio GameObjects if present
            RemoveChildIfPresent(carObj, "Engine_SF26_Acceleration");
            RemoveChildIfPresent(carObj, "Engine_SF26_Deceleration");
            RemoveChildIfPresent(carObj, "Engine_SF26_Idle");
            RemoveChildIfPresent(carObj, "Engine_SF26_StandingRev");
            RemoveChildIfPresent(carObj, "Crash");
            RemoveChildIfPresent(carObj, "TyreScreech");

            // 5. Ensure Child AudioSources (Engine_SF26 for continuous loop, GearShift for shift transients)
            AudioSource srcEngine = GetOrCreateChildSource(carObj, "Engine_SF26", engineGroup, true);
            AudioSource srcShift  = GetOrCreateChildSource(carObj, "GearShift",   sfxGroup,    false);

            // 6. Bind SerializedProperties
            SerializedObject so = new SerializedObject(audioMgr);
            so.Update();

            SetProp(so, "carController", carController);
            SetProp(so, "audioMixer", mixer);
            SetProp(so, "masterGroup", masterGroup);
            SetProp(so, "engineGroup", engineGroup);
            SetProp(so, "sfxGroup", sfxGroup);

            SetProp(so, "sf26StandingStartClip", sf26StandingStartClip);
            SetProp(so, "sf26GearUpshiftClip", sf26UpClip);
            SetProp(so, "sf26GearDownshiftClip", sf26DownClip);

            SetProp(so, "engineSource", srcEngine);
            SetProp(so, "gearShiftSource", srcShift);

            so.ApplyModifiedProperties();

            // 7. Verify AudioListeners in the scene
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>();
            int activeListeners = 0;
            foreach (var l in listeners)
            {
                if (l.enabled && l.gameObject.activeInHierarchy)
                {
                    activeListeners++;
                    if (l.gameObject.name != "Main Camera" && activeListeners > 1)
                    {
                        Debug.LogWarning($"[AudioSetupEditor] Disabling duplicate AudioListener on {l.gameObject.name}");
                        l.enabled = false;
                        EditorUtility.SetDirty(l);
                    }
                }
            }

            // 8. Mark dirty & save scene
            EditorUtility.SetDirty(carObj);
            EditorUtility.SetDirty(audioMgr);
            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);

            if (logSuccess)
            {
                Debug.Log($"[AudioSetupEditor] Successfully configured Ferrari SF-26 Audio System in {ScenePath}!");
            }
        }

        private static void RemoveChildIfPresent(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static AudioSource GetOrCreateChildSource(GameObject parent, string name, AudioMixerGroup group, bool loop)
        {
            Transform child = parent.transform.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent.transform, false);
                child = go.transform;
            }

            AudioSource src = child.GetComponent<AudioSource>();
            if (src == null)
            {
                src = child.gameObject.AddComponent<AudioSource>();
            }

            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0.85f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1.5f;
            src.maxDistance = 75.0f;
            src.dopplerLevel = 0.2f;
            src.outputAudioMixerGroup = group;

            EditorUtility.SetDirty(src);
            return src;
        }

        private static void SetProp(SerializedObject so, string propName, Object value)
        {
            SerializedProperty prop = so.FindProperty(propName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
            }
            else
            {
                Debug.LogWarning($"[AudioSetupEditor] Property '{propName}' not found on CarAudioManager");
            }
        }
    }
}
#endif
