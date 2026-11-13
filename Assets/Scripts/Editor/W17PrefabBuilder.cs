using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    public static class W17PrefabBuilder
    {
        [MenuItem("Tools/F1 Signature/Build Mercedes W17 Prefab")]
        public static void BuildPrefab()
        {
            Debug.Log("[W17PrefabBuilder] Delegating to W17ModelProcessor.BuildPrefab...");
            W17ModelProcessor.BuildPrefab();
        }
    }
}
