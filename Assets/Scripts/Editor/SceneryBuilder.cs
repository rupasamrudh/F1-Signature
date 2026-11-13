// SceneryBuilder.cs — Instantiates data-driven scenery props from f1sig_props.json.
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using F1Sim.Simulation.Track;
using F1Sim.Track;

namespace F1Sim.Editor
{
    [System.Serializable]
    public class PropEntry
    {
        public float s;
        public string side;
        public float offset_m;
        public string type;
        public float rotation_deg;
    }

    [System.Serializable]
    public class PropData
    {
        public List<PropEntry> props;
    }

    public static class SceneryBuilder
    {
        [MenuItem("Tools/F1 Signature/Build Scenery")]
        public static void BuildSceneryMenu()
        {
            string propsPath = "Assets/Data/Tracks/f1sig_props.json";
            if (!File.Exists(propsPath))
            {
                Debug.LogError($"[SceneryBuilder] {propsPath} not found!");
                return;
            }

            var config = TrackDataLoader.LoadTrack();
            if (config == null) return;
            var spline = new TrackSpline(config);

            string json = File.ReadAllText(propsPath);
            var propData = JsonUtility.FromJson<PropData>(json);
            if (propData == null || propData.props == null) return;

            var oldRoot = GameObject.Find("Scenery_Built");
            if (oldRoot != null) Undo.DestroyObjectImmediate(oldRoot);

            var root = new GameObject("Scenery_Built");
            Undo.RegisterCreatedObjectUndo(root, "Build Scenery");

            var mats = TrackMaterialBuilder.GetOrCreateTrackMaterials();

            int count = 0;
            foreach (var p in propData.props)
            {
                var q = spline.QueryAtS(p.s);
                float rad = q.headingRad;
                Vector3 fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 left = new Vector3(-fwd.z, 0f, fwd.x);

                float sideSign = (p.side == "left") ? 1.0f : -1.0f;
                Vector3 pos = new Vector3(q.x, q.y, q.z) + left * (p.offset_m * sideSign);
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, p.rotation_deg, 0f);

                GameObject go = null;
                switch (p.type)
                {
                    case "grandstand":
                        go = CreateGrandstand(mats.barrier);
                        break;
                    case "pit_building":
                        go = CreatePitBuilding(mats.barrier);
                        break;
                    case "light_tower":
                        go = CreateLightTower(mats.barrier);
                        break;
                    case "sign_board":
                        go = CreateSignBoard(mats.barrier);
                        break;
                    case "tree":
                        go = CreateTree(mats.grass);
                        break;
                    case "bush":
                        go = CreateBush(mats.grass);
                        break;
                }

                if (go != null)
                {
                    go.name = $"{p.type}_{count:D3}";
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = pos;
                    go.transform.rotation = rot;
                    count++;
                }
            }

            Debug.Log($"[SceneryBuilder] Built {count} scenery props into Scenery_Built.");
        }

        static GameObject CreateGrandstand(Material mat)
        {
            var go = new GameObject();
            // Stepped seating: 3 tiers
            for (int i = 0; i < 3; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"Tier_{i}";
                step.transform.SetParent(go.transform, false);
                step.transform.localPosition = new Vector3(0f, (i + 0.5f) * 1.5f, i * 2.0f);
                step.transform.localScale = new Vector3(25f, 1.5f, 2.5f);
                step.GetComponent<Renderer>().sharedMaterial = mat;
            }
            return go;
        }

        static GameObject CreatePitBuilding(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(40f, 6f, 12f);
            go.transform.localPosition = new Vector3(0f, 3f, 0f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject CreateLightTower(Material mat)
        {
            var go = new GameObject();
            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.transform.SetParent(go.transform, false);
            mast.transform.localScale = new Vector3(0.5f, 9f, 0.5f);
            mast.transform.localPosition = new Vector3(0f, 9f, 0f);
            mast.GetComponent<Renderer>().sharedMaterial = mat;

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.transform.SetParent(go.transform, false);
            head.transform.localScale = new Vector3(3f, 1.5f, 1.5f);
            head.transform.localPosition = new Vector3(0f, 18f, 0f);
            head.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject CreateSignBoard(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(3.5f, 2.0f, 0.2f);
            go.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject CreateTree(Material mat)
        {
            var go = new GameObject();
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(go.transform, false);
            trunk.transform.localScale = new Vector3(0.6f, 2.5f, 0.6f);
            trunk.transform.localPosition = new Vector3(0f, 2.5f, 0f);

            var leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leaves.transform.SetParent(go.transform, false);
            leaves.transform.localScale = new Vector3(4.5f, 4.5f, 4.5f);
            leaves.transform.localPosition = new Vector3(0f, 6.5f, 0f);
            leaves.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject CreateBush(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.localScale = new Vector3(2.5f, 1.5f, 2.5f);
            go.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}

