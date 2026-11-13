using UnityEngine;
using UnityEngine.InputSystem;

public class ChaseCamera : MonoBehaviour
{
    public enum CameraMode
    {
        Chase,
        Bonnet,
        TCam
    }

    [Header("Target & References")]
    public Transform target;
    public CarController car;

    [Header("Camera Mode")]
    public CameraMode currentMode = CameraMode.Chase;
    public bool showOnScreenToggle = true;

    [Header("Chase Camera Settings")]
    public Vector3 offset = new Vector3(0f, 2.5f, -9f);
    public float posLag = 8f;
    public float rotLag = 6f;
    public float fovBase = 60f;
    public float fovMax = 78f;

    [Header("Bonnet Camera Settings (Rigid Nose Mount)")]
    public Vector3 bonnetOffset = new Vector3(0f, 0.62f, 1.15f);
    public float bonnetLookHeight = 0.45f;
    public float bonnetLookDistance = 30f;
    public float bonnetFov = 72f;

    [Header("T-Cam Settings (Rigid Airbox Mount)")]
    public Vector3 tcamOffset = new Vector3(0f, 0.88f, -0.22f);
    public float tcamLookHeight = 0.45f;
    public float tcamLookDistance = 30f;
    public float tcamFov = 68f;

    Camera camComponent;

    void Awake()
    {
        camComponent = GetComponent<Camera>();
        if (camComponent != null)
        {
            if (camComponent.nearClipPlane < 0.3f)
                camComponent.nearClipPlane = 0.3f;
            if (camComponent.farClipPlane < 1500f)
                camComponent.farClipPlane = 1500f;
        }

        // Restore saved preference
        int saved = PlayerPrefs.GetInt("F1Sig_CameraMode", (int)CameraMode.Chase);
        currentMode = (CameraMode)Mathf.Clamp(saved, 0, 2);
    }

    void Start()
    {
        SnapToTarget();
    }

    void Update()
    {
        // Keyboard toggle support (Key C)
        var kb = Keyboard.current;
        if (kb != null && kb.cKey.wasPressedThisFrame)
        {
            ToggleCameraMode();
        }
    }

    public void ToggleCameraMode()
    {
        CameraMode nextMode = currentMode switch
        {
            CameraMode.Chase => CameraMode.Bonnet,
            CameraMode.Bonnet => CameraMode.TCam,
            CameraMode.TCam => CameraMode.Chase,
            _ => CameraMode.Chase
        };
        SetCameraMode(nextMode);
    }

    public void SetCameraMode(CameraMode newMode)
    {
        currentMode = newMode;
        PlayerPrefs.SetInt("F1Sig_CameraMode", (int)currentMode);
        PlayerPrefs.Save();
        SnapToTarget();
    }

    public void SnapToTarget()
    {
        if (!target) return;
        if (camComponent == null) camComponent = GetComponent<Camera>();

        switch (currentMode)
        {
            case CameraMode.Bonnet:
                transform.position = target.TransformPoint(bonnetOffset);
                Vector3 bLook = target.TransformPoint(new Vector3(0f, bonnetLookHeight, bonnetLookDistance));
                transform.rotation = Quaternion.LookRotation(bLook - transform.position, target.up);
                if (camComponent != null) camComponent.fieldOfView = bonnetFov;
                break;

            case CameraMode.TCam:
                transform.position = target.TransformPoint(tcamOffset);
                Vector3 tLook = target.TransformPoint(new Vector3(0f, tcamLookHeight, tcamLookDistance));
                transform.rotation = Quaternion.LookRotation(tLook - transform.position, target.up);
                if (camComponent != null) camComponent.fieldOfView = tcamFov;
                break;

            default: // Chase
                transform.position = target.TransformPoint(offset);
                Vector3 cLook = target.position + target.forward * 4f + Vector3.up * 0.8f;
                transform.rotation = Quaternion.LookRotation(cLook - transform.position, Vector3.up);
                if (camComponent != null) camComponent.fieldOfView = fovBase;
                break;
        }
    }

    void LateUpdate()
    {
        if (!target) return;

        float dt = Time.deltaTime;

        if (currentMode == CameraMode.Bonnet)
        {
            // Rigid mount on nose cone: 0 lag, 0 drift during acceleration & braking
            transform.position = target.TransformPoint(bonnetOffset);
            Vector3 lookTarget = target.TransformPoint(new Vector3(0f, bonnetLookHeight, bonnetLookDistance));
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, target.up);

            if (camComponent != null) camComponent.fieldOfView = bonnetFov;
        }
        else if (currentMode == CameraMode.TCam)
        {
            // Rigid mount on airbox T-bar: 0 lag, full view of halo, helmet, steering wheel & road
            transform.position = target.TransformPoint(tcamOffset);
            Vector3 lookTarget = target.TransformPoint(new Vector3(0f, tcamLookHeight, tcamLookDistance));
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, target.up);

            if (camComponent != null) camComponent.fieldOfView = tcamFov;
        }
        else // Chase
        {
            // Chase camera with smooth follow lag & dynamic speed FOV
            Vector3 desiredPos = target.TransformPoint(offset);
            transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-posLag * dt));

            Vector3 lookTarget = target.position + target.forward * 4f + Vector3.up * 0.8f;
            Quaternion desiredRot = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-rotLag * dt));

            if (camComponent != null && car != null)
            {
                float speedRatio = Mathf.Clamp01(car.SpeedKmh / 350f);
                camComponent.fieldOfView = Mathf.Lerp(fovBase, fovMax, speedRatio);
            }
        }
    }

    void OnGUI()
    {
        if (!showOnScreenToggle) return;

        // If CarController is in the scene, CarController draws the unified top-right button panel
        if (car != null || FindAnyObjectByType<CarController>() != null) return;

        // Position on top-right below the lap timing box
        float sw = Screen.width;
        float btnW = 110f;
        float btnH = 32f;
        float btnX = sw - btnW - 10f;
        float btnY = 126f;

        string btnText = currentMode switch
        {
            CameraMode.Chase => "📷 Chase",
            CameraMode.Bonnet => "📷 Bonnet",
            CameraMode.TCam => "📷 T-Cam",
            _ => "📷 Cam"
        };

        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), btnText))
        {
            ToggleCameraMode();
        }
    }
}
