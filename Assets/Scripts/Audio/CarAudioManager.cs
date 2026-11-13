using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// CarAudioManager manages all vehicular sound for the Ferrari SF-26.
/// Driven exclusively by the 3 canonical Ferrari SF-26 sounds:
/// 1. Standing Start / Engine Audio (SF26_Standing_Start): Authentic Ferrari SF-26 standing start
///    and continuous engine tone that glides dynamically with RPM and throttle.
/// 2. Gear Upshift (SF26_Gear_Upshift): Authentic Ferrari SF-26 pneumatic ignition cut upshift pop.
/// 3. Gear Downshift (SF26_Gear_Downshift): Authentic Ferrari SF-26 throttle blip downshift snap.
/// All previous/legacy sounds have been completely removed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CarController))]
public class CarAudioManager : MonoBehaviour
{
    [Header("Car Controller Reference")]
    [SerializeField] private CarController carController;

    [Header("Audio Mixer & Groups")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioMixerGroup masterGroup;
    [SerializeField] private AudioMixerGroup engineGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;

    [Header("Ferrari SF-26 Audio Clips (The 3 Canonical Sounds)")]
    [Tooltip("Authentic Ferrari SF-26 standing start & continuous engine sound")]
    [SerializeField] private AudioClip sf26StandingStartClip;
    [Tooltip("Authentic Ferrari SF-26 pneumatic ignition cut upshift pop")]
    [SerializeField] private AudioClip sf26GearUpshiftClip;
    [Tooltip("Authentic Ferrari SF-26 electronic throttle blip downshift snap")]
    [SerializeField] private AudioClip sf26GearDownshiftClip;

    [Header("Audio Sources")]
    [Tooltip("Primary continuous engine audio source")]
    [SerializeField] private AudioSource engineSource;
    [Tooltip("Shift audio source playing SF-26 upshifts and downshifts")]
    [SerializeField] private AudioSource gearShiftSource;

    [Header("Spatial Audio Settings")]
    [Range(0f, 1f)] [SerializeField] private float cockpitSpatialBlend = 0.25f;
    [Range(0f, 1f)] [SerializeField] private float chaseSpatialBlend = 0.85f;
    [SerializeField] private float minDistance = 1.5f;
    [SerializeField] private float maxDistance = 75.0f;

    [Header("Engine Tuning")]
    [SerializeField] private float idleRpm = 1000f;
    [SerializeField] private float redlineRpm = 15000f;
    [Tooltip("RPM pitch smoothing time during shifts (higher = smoother glide)")]
    [SerializeField] private float rpmSmoothTime = 0.18f;

    // Runtime state tracking
    private int _lastGear = 1;
    private float _shiftCooldownTimer = 0f;
    private float _currentSpatialBlend = 0.85f;
    private float _smoothedThrottle = 0f;
    private float _smoothedBrake = 0f;
    private float _smoothedRpm = 1000f;
    private float _rpmVelocity = 0f;
    private ChaseCamera _cachedChaseCam;

    void Awake()
    {
        if (carController == null)
            carController = GetComponent<CarController>();

        _lastGear = carController != null ? carController.Gear : 1;
        _smoothedRpm = carController != null ? carController.EngineRpm : idleRpm;
        _currentSpatialBlend = chaseSpatialBlend;

        LoadAssetsIfMissing();
        EnsureAudioSources();
        EnsureSingleAudioListener();
    }

    private void LoadAssetsIfMissing()
    {
        if (sf26StandingStartClip == null) sf26StandingStartClip = Resources.Load<AudioClip>("Audio/SF26_Standing_Start");
        if (sf26GearUpshiftClip == null)   sf26GearUpshiftClip   = Resources.Load<AudioClip>("Audio/SF26_Gear_Upshift");
        if (sf26GearDownshiftClip == null) sf26GearDownshiftClip = Resources.Load<AudioClip>("Audio/SF26_Gear_Downshift");

        if (audioMixer == null)
        {
            audioMixer = Resources.Load<AudioMixer>("Audio/F1VehicleAudioMixer");
        }

        if (audioMixer != null)
        {
            if (masterGroup == null)
            {
                var mg = audioMixer.FindMatchingGroups("Master");
                if (mg != null && mg.Length > 0) masterGroup = mg[0];
            }
            if (engineGroup == null)
            {
                var eg = audioMixer.FindMatchingGroups("Engine");
                if (eg != null && eg.Length > 0) engineGroup = eg[0];
                else engineGroup = masterGroup;
            }
            if (sfxGroup == null)
            {
                var sg = audioMixer.FindMatchingGroups("SFX");
                if (sg != null && sg.Length > 0) sfxGroup = sg[0];
                else sfxGroup = masterGroup;
            }
        }
    }

    private void EnsureSingleAudioListener()
    {
        AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>();
        int activeCount = 0;
        foreach (var l in listeners)
        {
            if (l.enabled && l.gameObject.activeInHierarchy)
            {
                activeCount++;
                if (activeCount > 1 && l.gameObject.name != "Main Camera")
                {
                    Debug.LogWarning($"[CarAudioManager] Disabling duplicate AudioListener on {l.gameObject.name}");
                    l.enabled = false;
                }
            }
        }
    }

    void Start()
    {
        _cachedChaseCam = UnityEngine.Object.FindAnyObjectByType<ChaseCamera>();

        // Start the single continuous Ferrari SF-26 engine source
        StartLoopingSource(engineSource, sf26StandingStartClip, initialVolume: 0.35f);
    }

    void Update()
    {
        if (carController == null) return;

        UpdateTimers();
        UpdateCameraSpatialBlend();
        UpdatePlayerInputEngineAudio();
        UpdateGearShiftAudio();
    }

    private void UpdateTimers()
    {
        float dt = Time.deltaTime;
        if (_shiftCooldownTimer > 0f) _shiftCooldownTimer -= dt;
    }

    private void UpdateCameraSpatialBlend()
    {
        float targetBlend = chaseSpatialBlend;
        if (_cachedChaseCam != null)
        {
            if (_cachedChaseCam.currentMode == ChaseCamera.CameraMode.Bonnet ||
                _cachedChaseCam.currentMode == ChaseCamera.CameraMode.TCam)
            {
                targetBlend = cockpitSpatialBlend;
            }
        }

        _currentSpatialBlend = Mathf.MoveTowards(_currentSpatialBlend, targetBlend, 4.0f * Time.deltaTime);

        ApplySpatialBlend(engineSource);
        ApplySpatialBlend(gearShiftSource);
    }

    private void ApplySpatialBlend(AudioSource src)
    {
        if (src != null)
            src.spatialBlend = _currentSpatialBlend;
    }

    /// <summary>
    /// Smooth, continuous engine sound processing using the authentic Ferrari SF-26 standing start audio.
    /// Pitch smoothly glides with Engine RPM (1,000 to 15,000 RPM).
    /// Volume smoothly scales with player throttle and RPM state.
    /// Zero audio cuts or disparate file switching.
    /// </summary>
    private void UpdatePlayerInputEngineAudio()
    {
        float dt = Time.deltaTime;
        float actualRpm = Mathf.Clamp(carController.EngineRpm, idleRpm, redlineRpm);
        float playerThrottle = Mathf.Clamp01(carController.Throttle);
        float playerBrake = Mathf.Clamp01(carController.Brake);

        // Smooth RPM glide using SmoothDamp: prevents abrupt stair-step pitch jumps during shifts
        _smoothedRpm = Mathf.SmoothDamp(_smoothedRpm, actualRpm, ref _rpmVelocity, rpmSmoothTime);
        _smoothedThrottle = Mathf.MoveTowards(_smoothedThrottle, playerThrottle, 6.0f * dt);
        _smoothedBrake = Mathf.MoveTowards(_smoothedBrake, playerBrake, 6.0f * dt);

        // Normalized RPM across operating range
        float rpmFrac = Mathf.InverseLerp(idleRpm, redlineRpm, _smoothedRpm);

        // Dynamic continuous engine pitch from 1,000 to 15,000 RPM
        // 1,000 RPM idle = ~0.65f pitch
        // 10,800 RPM launch = ~1.25f pitch
        // 15,000 RPM redline = ~1.55f pitch
        float enginePitch = Mathf.Lerp(0.65f, 1.55f, rpmFrac);
        if (engineSource != null)
        {
            engineSource.pitch = enginePitch;
        }

        // Dynamic volume calculation:
        // Idle off-throttle: 0.35f
        // Throttle applied: swells to 0.95f
        // Off-throttle overrun: 0.45f
        float overrunFactor = Mathf.Clamp01((_smoothedRpm - 2000f) / 7000f);
        float idleOrOverrunVol = 0.35f + overrunFactor * 0.15f;
        float targetVol = Mathf.Lerp(idleOrOverrunVol, 0.95f, _smoothedThrottle);

        if (engineSource != null)
        {
            engineSource.volume = Mathf.MoveTowards(engineSource.volume, targetVol, 5.0f * dt);
        }
    }

    /// <summary>
    /// Smooth gear shifting: authentic Ferrari SF-26 upshift pop / downshift blip
    /// that accents over the continuous engine tone without interrupting or cutting the audio.
    /// </summary>
    private void UpdateGearShiftAudio()
    {
        int currentGear = carController.Gear;

        if (currentGear != _lastGear && _shiftCooldownTimer <= 0f)
        {
            if (currentGear > _lastGear)
            {
                // Authentic Ferrari SF-26 ignition-cut upshift pop
                if (gearShiftSource != null && sf26GearUpshiftClip != null)
                {
                    gearShiftSource.pitch = UnityEngine.Random.Range(0.99f, 1.01f);
                    gearShiftSource.PlayOneShot(sf26GearUpshiftClip, 0.40f);
                }
            }
            else if (currentGear < _lastGear)
            {
                // Authentic Ferrari SF-26 throttle-blip downshift snap
                if (gearShiftSource != null && sf26GearDownshiftClip != null)
                {
                    gearShiftSource.pitch = UnityEngine.Random.Range(0.99f, 1.01f);
                    gearShiftSource.PlayOneShot(sf26GearDownshiftClip, 0.38f);
                }
            }

            _shiftCooldownTimer = 0.20f;
            _lastGear = currentGear;
        }
    }

    private void StartLoopingSource(AudioSource src, AudioClip clip, float initialVolume = 0.35f)
    {
        if (src == null || clip == null) return;

        src.clip = clip;
        src.loop = true;
        src.volume = initialVolume;
        if (!src.isPlaying)
        {
            src.Play();
        }
    }

    private void EnsureAudioSources()
    {
        engineSource    = SetupSource(engineSource,    "Engine_SF26", engineGroup, true);
        gearShiftSource = SetupSource(gearShiftSource, "GearShift",   sfxGroup,    false);

        // Remove any obsolete legacy audio sources from previous versions
        CleanObsoleteSource("Engine_SF26_Acceleration");
        CleanObsoleteSource("Engine_SF26_Deceleration");
        CleanObsoleteSource("Engine_SF26_Idle");
        CleanObsoleteSource("Engine_SF26_StandingRev");
        CleanObsoleteSource("Crash");
        CleanObsoleteSource("TyreScreech");
    }

    private void CleanObsoleteSource(string childName)
    {
        Transform t = transform.Find(childName);
        if (t != null)
        {
            Destroy(t.gameObject);
        }
    }

    private AudioSource SetupSource(AudioSource existing, string childName, AudioMixerGroup group, bool loop)
    {
        if (existing != null)
        {
            ConfigureSourceSettings(existing, group, loop);
            return existing;
        }

        Transform child = transform.Find(childName);
        if (child == null)
        {
            GameObject go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            child = go.transform;
        }

        AudioSource src = child.GetComponent<AudioSource>();
        if (src == null)
        {
            src = child.gameObject.AddComponent<AudioSource>();
        }

        ConfigureSourceSettings(src, group, loop);
        return src;
    }

    private void ConfigureSourceSettings(AudioSource src, AudioMixerGroup group, bool loop)
    {
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = _currentSpatialBlend;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.dopplerLevel = 0.2f;

        if (group != null)
        {
            src.outputAudioMixerGroup = group;
        }
    }

    // ── Public Mixer Volume Controls ──────────────────────────────────────
    public void SetMasterVolume(float linearVolume)
    {
        SetMixerVolume("MasterVolume", linearVolume);
    }

    public void SetEngineVolume(float linearVolume)
    {
        SetMixerVolume("EngineVolume", linearVolume);
    }

    public void SetSfxVolume(float linearVolume)
    {
        SetMixerVolume("SFXVolume", linearVolume);
    }

    private void SetMixerVolume(string exposedParam, float linearVolume)
    {
        if (audioMixer == null) return;
        float dB = (linearVolume > 0.0001f) ? Mathf.Log10(linearVolume) * 20.0f : -80.0f;
        dB = Mathf.Clamp(dB, -80.0f, 0.0f);
        audioMixer.SetFloat(exposedParam, dB);
    }
}
