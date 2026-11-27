using UnityEngine;

namespace F1Sim.Vehicle
{
    public class WheelVisualBinder : MonoBehaviour
    {
        [Header("Wheel Visual Transforms")]
        public Transform wheelFL;
        public Transform wheelFR;
        public Transform wheelRL;
        public Transform wheelRR;

        [Header("Body / Chassis Visual Transform")]
        public Transform body;

        [Header("Car Controller Reference")]
        public CarController controller;

        [Header("DRS Active Aero Visuals (2026 Macarena Wing)")]
        public Transform rearWingFlap;
        public Transform frontWingFlap;
        public bool useMacarenaWing = true; // 2026 Ferrari SF-26 signature rotating active rear wing
        [Range(0f, 180f)] public float drsRearFlapAngle = 180.0f; // Macarena wing 180° inversion rotation
        [Range(0f, 45f)] public float drsFrontFlapAngle = 14.0f; // Active front wing incidence trim
        [Range(5f, 40f)] public float drsTransitionSpeed = 16.0f; // s^-1 snappy hydraulic actuation

        // Real-time DRS flap animation progress [0 = closed, 1 = fully open]
        float drsFlapProgress = 0f;

        // Mesh vertex flap animation cache for single-mesh chassis models
        MeshFilter bodyMeshFilter;
        Vector3[] baseBodyVertices;
        Vector3[] animatedBodyVertices;
        int[] rearFlapIndices;
        int[] frontFlapIndices;
        float[] rearFlapWeights;
        float[] frontFlapWeights;
        bool isMeshFlapInitialized = false;

        // Bounded spin angles in [0, 360)
        float flSpinAngle = 0f;
        float frSpinAngle = 0f;
        float rlSpinAngle = 0f;
        float rrSpinAngle = 0f;

        // Smooth steering angles for visual presentation (eliminates 50Hz physics stair-stepping & assist micro-jitter)
        float smoothSteerFL = 0f;
        float smoothSteerFR = 0f;

        // Smoothed speed for stable wheel rotation rate (eliminates render-physics dt aliasing)
        float smoothForwardSpeed = 0f;

        // Cached rest local positions to eliminate any hub displacement or drift
        Vector3 restPosFL;
        Vector3 restPosFR;
        Vector3 restPosRL;
        Vector3 restPosRR;
        Vector3 restBodyPos;
        Quaternion restBodyRot = Quaternion.identity;
        bool hasRestPositions = false;

        void Start()
        {
            if (controller == null)
                controller = GetComponentInParent<CarController>();

            CacheRestPositions();
        }

        void CacheRestPositions()
        {
            if (wheelFL != null) restPosFL = wheelFL.localPosition;
            if (wheelFR != null) restPosFR = wheelFR.localPosition;
            if (wheelRL != null) restPosRL = wheelRL.localPosition;
            if (wheelRR != null) restPosRR = wheelRR.localPosition;

            if (body == null)
            {
                // Auto-locate chassis Body transform in vehicle hierarchy
                body = transform.Find("Body") ??
                       transform.Find("Ferrari_SF26/Body") ??
                       transform.Find("Mercedes_W17/Body");

                if (body == null)
                {
                    var allTransforms = GetComponentsInChildren<Transform>();
                    for (int i = 0; i < allTransforms.Length; i++)
                    {
                        if (allTransforms[i].name == "Body")
                        {
                            body = allTransforms[i];
                            break;
                        }
                    }
                }
            }

            if (body != null)
            {
                restBodyPos = body.localPosition;
                restBodyRot = body.localRotation;

                if (!isMeshFlapInitialized)
                {
                    InitWingMeshFlaps();
                }
            }

            hasRestPositions = true;
        }

        void InitWingMeshFlaps()
        {
            if (body == null) return;
            bodyMeshFilter = body.GetComponent<MeshFilter>();
            if (bodyMeshFilter == null || bodyMeshFilter.sharedMesh == null) return;

            // Instantiate a unique runtime copy so we don't modify the asset on disk
            Mesh runtimeMesh = Instantiate(bodyMeshFilter.sharedMesh);
            runtimeMesh.name = $"{bodyMeshFilter.sharedMesh.name}_AeroRuntime";
            runtimeMesh.MarkDynamic();
            bodyMeshFilter.mesh = runtimeMesh;

            baseBodyVertices = runtimeMesh.vertices;
            animatedBodyVertices = new Vector3[baseBodyVertices.Length];
            System.Array.Copy(baseBodyVertices, animatedBodyVertices, baseBodyVertices.Length);

            var rIndices = new System.Collections.Generic.List<int>();
            var rWeights = new System.Collections.Generic.List<float>();
            var fIndices = new System.Collections.Generic.List<int>();
            var fWeights = new System.Collections.Generic.List<float>();

            // Identify vertices located in the rear wing upper flap and front wing flap
            for (int i = 0; i < baseBodyVertices.Length; i++)
            {
                Vector3 v = baseBodyVertices[i];

                // Rear Wing Macarena Flap:
                // Upper aerodynamic element that inverts: Y in [0.54, 0.68], Z in [-2.64, -2.44], |X| <= 0.52 (inside endplates)
                // Isolates the upper flap completely from the lower beam wing (Y < 0.53) and endplates (|X| > 0.52).
                if (v.z >= -2.64f && v.z <= -2.44f && v.y >= 0.54f && v.y <= 0.68f && Mathf.Abs(v.x) <= 0.52f)
                {
                    rIndices.Add(i);
                    rWeights.Add(1.0f); // 1.0f weight for full rigid 180° inversion
                }

                // Front Wing Flap (Upper movable cascade flap):
                // Strictly isolates the flap between nose cone (|X| >= 0.22) and endplate inner wall (|X| <= 0.56).
                // Vertices with |X| > 0.56 or Y > 0.04 belong to the endplate and are completely excluded!
                // Vertices with Y < -0.09 belong to the main plane and are completely excluded!
                if (v.z >= 2.15f && v.z <= 2.32f && v.y >= -0.085f && v.y <= 0.035f && Mathf.Abs(v.x) >= 0.22f && Mathf.Abs(v.x) <= 0.56f)
                {
                    fIndices.Add(i);
                    fWeights.Add(1.0f); // 100% rigid rotation of the front flap element only
                }
            }

            rearFlapIndices = rIndices.ToArray();
            rearFlapWeights = rWeights.ToArray();
            frontFlapIndices = fIndices.ToArray();
            frontFlapWeights = fWeights.ToArray();

            isMeshFlapInitialized = true;
        }

        void LateUpdate()
        {
            if (controller == null) return;
            if (!hasRestPositions) CacheRestPositions();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float rawSpeedMs = controller.ForwardSpeedMs;
            float tireRadius = (controller.config != null && controller.config.tireRadius > 0.05f)
                ? controller.config.tireRadius
                : 0.358f;

            // Critically smooth forward speed to prevent 50Hz physics discrete velocity steps from jerking spin rate
            smoothForwardSpeed = Mathf.Lerp(smoothForwardSpeed, rawSpeedMs, 1f - Mathf.Exp(-25f * dt));

            // Angular spin rate: v / r (rad/s) -> deg/s
            float rawSpinStepDeg = (smoothForwardSpeed / tireRadius) * Mathf.Rad2Deg * dt;

            // Anti-aliasing cap for high speeds:
            // At high speeds (>45 km/h), raw spin step can exceed the spoke symmetry Nyquist limit (~24°/frame at 60fps),
            // which causes stroboscopic wagon-wheel backwards jumping and visual vibration.
            // Capping the frame spin step guarantees silky, continuous forward blur rotation at any speed.
            const float kMaxFrameSpinDeg = 24.0f;
            float spinStepDeg = Mathf.Clamp(rawSpinStepDeg, -kMaxFrameSpinDeg, kMaxFrameSpinDeg);

            // Spin accumulation kept strictly in [0, 360) to eliminate 32-bit float truncation jitter
            flSpinAngle = (flSpinAngle + spinStepDeg) % 360f;
            if (flSpinAngle < 0f) flSpinAngle += 360f;
            frSpinAngle = flSpinAngle;

            rlSpinAngle = (rlSpinAngle + spinStepDeg) % 360f;
            if (rlSpinAngle < 0f) rlSpinAngle += 360f;
            rrSpinAngle = rlSpinAngle;

            // Target Ackermann steering angles (negative for Unity coordinate system: +Y up, turning right is positive yaw)
            float targetSteerFL = -controller.SteerAngleDegFL;
            float targetSteerFR = -controller.SteerAngleDegFR;

            // Apply fine deadband for straight-line running to eliminate stability assist micro-chatter
            if (Mathf.Abs(targetSteerFL) < 0.08f) targetSteerFL = 0f;
            if (Mathf.Abs(targetSteerFR) < 0.08f) targetSteerFR = 0f;

            // High-stiffness steering response (60 rad/s ~ 16ms settling):
            // Eliminates visual lag and mushy jelly steering feel
            float steerFilterT = 1f - Mathf.Exp(-60f * dt);
            smoothSteerFL = Mathf.Lerp(smoothSteerFL, targetSteerFL, steerFilterT);
            smoothSteerFR = Mathf.Lerp(smoothSteerFR, targetSteerFR, steerFilterT);

            // ── Wheel Transforms: Firmly locked to suspension wishbone hubs ───────────
            // Absolute zero gap: wheels and wishbones move as ONE rigid CAD-aligned assembly.
            // Wheel never moves away from wheel base whether up or down, turning or straight.
            if (wheelFL != null)
            {
                wheelFL.localPosition = restPosFL;
                wheelFL.localRotation = Quaternion.AngleAxis(smoothSteerFL, Vector3.up) *
                                        Quaternion.AngleAxis(flSpinAngle, Vector3.right);
            }

            if (wheelFR != null)
            {
                wheelFR.localPosition = restPosFR;
                wheelFR.localRotation = Quaternion.AngleAxis(smoothSteerFR, Vector3.up) *
                                        Quaternion.AngleAxis(frSpinAngle, Vector3.right);
            }

            // Rear wheels: rolling spin only (fixed heading) anchored at hub center
            if (wheelRL != null)
            {
                wheelRL.localPosition = restPosRL;
                wheelRL.localRotation = Quaternion.AngleAxis(rlSpinAngle, Vector3.right);
            }

            if (wheelRR != null)
            {
                wheelRR.localPosition = restPosRR;
                wheelRR.localRotation = Quaternion.AngleAxis(rrSpinAngle, Vector3.right);
            }

            // Chassis Body firmly locked to rest position — zero gap and zero jelly wobbling
            if (body != null)
            {
                body.localPosition = restBodyPos;
                body.localRotation = restBodyRot;
            }

            // ── DRS / Active Aero Wing Flap Visual Animation ──────────────────────
            UpdateWingFlapVisuals(dt);
        }

        void UpdateWingFlapVisuals(float dt)
        {
            bool isDrsOn = controller != null && controller.IsDrsActive;
            float targetProgress = isDrsOn ? 1.0f : 0.0f;

            float prevProgress = drsFlapProgress;
            drsFlapProgress = Mathf.MoveTowards(drsFlapProgress, targetProgress, drsTransitionSpeed * dt);

            // 1. If dedicated flap Transforms are assigned (or found in hierarchy), rotate them directly
            if (rearWingFlap != null)
            {
                // Macarena wing: rotating flap flips 180 degrees into straightline low-drag mode
                float rearRotAngle = useMacarenaWing ? 180f : drsRearFlapAngle;
                rearWingFlap.localRotation = Quaternion.Euler(-rearRotAngle * drsFlapProgress, 0f, 0f);
            }

            if (frontWingFlap != null)
            {
                // Flap opens upward and backward to shed front downforce
                frontWingFlap.localRotation = Quaternion.Euler(drsFrontFlapAngle * drsFlapProgress, 0f, 0f);
            }

            // 2. Real-time dynamic vertex flap deformation for monolithic body meshes
            if (isMeshFlapInitialized && bodyMeshFilter != null && Mathf.Abs(drsFlapProgress - prevProgress) > 0.0005f)
            {
                // Rear wing flap:
                // For the signature 2026 Ferrari SF-26 "Macarena" wing, the aerodynamic flap rotates 180° around its central axis,
                // turning the aerofoil completely upside down into Straightline Mode (SLM).
                Vector3 rearHingePivot = new Vector3(0f, 0.59f, -2.53f);
                float rearRotDeg = useMacarenaWing ? 180.0f : drsRearFlapAngle;
                // Rotates 180 degrees around local X axis
                Quaternion rearRot = Quaternion.Euler(-rearRotDeg * drsFlapProgress, 0f, 0f);

                // Front wing flap hinge pivot: located at the leading edge of the upper flap (Z ~ 2.30, Y ~ -0.063)
                // Flap opens cleanly upward (+Euler pitch lifts trailing edge up), leaving endplate and main plane rigid!
                Vector3 frontHingePivot = new Vector3(0f, -0.063f, 2.30f);
                Quaternion frontRot = Quaternion.Euler(drsFrontFlapAngle * drsFlapProgress, 0f, 0f);

                // Update rear wing flap vertices
                if (rearFlapIndices != null)
                {
                    for (int k = 0; k < rearFlapIndices.Length; k++)
                    {
                        int vIdx = rearFlapIndices[k];
                        Vector3 baseV = baseBodyVertices[vIdx];
                        float weight = rearFlapWeights[k];

                        // Rigid rotation of the flap element around its chordwise pivot axis
                        Vector3 rotated = rearHingePivot + rearRot * (baseV - rearHingePivot);
                        animatedBodyVertices[vIdx] = Vector3.Lerp(baseV, rotated, weight);
                    }
                }

                // Update front wing flap vertices
                if (frontFlapIndices != null)
                {
                    for (int k = 0; k < frontFlapIndices.Length; k++)
                    {
                        int vIdx = frontFlapIndices[k];
                        Vector3 baseV = baseBodyVertices[vIdx];
                        float weight = frontFlapWeights[k];

                        Vector3 rotated = frontHingePivot + frontRot * (baseV - frontHingePivot);
                        animatedBodyVertices[vIdx] = Vector3.Lerp(baseV, rotated, weight);
                    }
                }

                bodyMeshFilter.mesh.vertices = animatedBodyVertices;
                bodyMeshFilter.mesh.RecalculateNormals();
            }
        }
    }
}
