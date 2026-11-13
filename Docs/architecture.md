F1 2026 Mobile Simulation: Architecture Document
Version 0.1 · Target: Android and iOS · Engine: Unity 6 (URP) · Built with Antigravity
1. Vision and scope
A mobile single-player Formula 1 simulation with physically grounded car behaviour and Real Racing 3-level visuals, based on the 2026 regulations (active aero, 50/50 power split, no MGU-H). The goal is realism where it affects feel (tires, weight transfer, aero, energy deployment) and simplification everywhere else.
Out of scope for v1: online multiplayer, full 22-car grid, damage modelling, dynamic weather, VR, wheel/force-feedback support.
Legal note: use original team names, liveries, driver names and logos. Real team marks and the "F1" brand are trademarked. Use real-world physics and regulation values, but fictional branding.
2. Targets and constraints
Item
Target
Frame rate
60 FPS on mid-range devices, 30 FPS fallback mode
Physics rate
120 Hz fixed step for the player car (60 Hz for AI cars)
Minimum device
Android 8 GB RAM class (Snapdragon 7-series or better), iPhone 11 or newer
Build size
Under 1.5 GB install, assets streamed where possible
Grid size (v1)
8 to 12 cars on track
Thermal
Quality scaler drops resolution before frame rate when the device heats up
Input
Touch (tilt or on-screen wheel, auto or manual throttle/brake), gamepad optional
3. Technology stack
Layer
Choice
Notes
Engine
Unity 6, Universal Render Pipeline
Best mobile support and tooling
Language
C#
Burst + Jobs for hot paths (AI, spline queries)
Vehicle physics
Custom model on top of a single Unity Rigidbody
Do not use WheelCollider
Data
ScriptableObjects (in editor) exported to JSON
All car, tire and track parameters are data, not code
Audio
FMOD (or Unity audio with RPM-blended loops for v1)

Assets
Blender, Substance or free PBR textures
Original designs only
Track source data
OpenStreetMap, SRTM elevation, reference photos

Validation tools
Python, Jupyter, FastF1
Offline only, never shipped
Version control
Git with Git LFS for art assets

Dev workflow
Antigravity agents, one module at a time
See section 12
4. High-level architecture
The game is split into five layers. Each layer talks to the one below it through interfaces, so any module can be tested alone.
1. Presentation: UI, HUD, menus, camera, audio, VFX
2. Game flow: race state machine, session rules, pit logic, timing and scoring
3. Simulation: vehicle dynamics, tires, aero, powertrain, energy system, brakes
4. World: track spline, surface data, weather (static in v1), environment
5. Data and platform: config loading, save data, input abstraction, telemetry logging
The key rule: the Simulation layer never references Unity UI, audio or rendering. It reads inputs and car state, and outputs forces and state. This keeps it testable and lets the same model run in the editor, in unit tests and in offline validation.
Main loop
Each render frame:
  read input
  accumulate time
  while accumulated >= fixedDt:
      AI.Update(fixedDt)
      Car.Step(fixedDt)        // full sim step
      Rigidbody integrates forces
      accumulated -= fixedDt
  interpolate visuals
  update HUD, audio, camera
Use Unity's FixedUpdate set to 1/120 s for the player car. Run AI cars on a lower-frequency step with simplified dynamics (section 9).
5. Module breakdown
Module
Responsibility
Depends on
CarController
Orchestrates one sim step per car
all sim modules
TireModel
Slip, grip, temperature, wear, combined forces
CarState
SuspensionModel
Spring, damper, anti-roll, ride height, load per wheel
CarState, TrackSurface
AeroModel
Downforce, drag, ride-height sensitivity, wake effect, active aero modes
CarState
Powertrain
ICE torque curve, gearbox, differential, shift logic
EnergySystem
EnergySystem
MGU-K deploy and harvest, battery state of charge, lap limits
Powertrain, BrakeModel
BrakeModel
Brake torque, bias, temperature, fade, brake-by-wire blending
EnergySystem
InputMapper
Touch, tilt, gamepad to normalized steering, throttle, brake
platform
DriverAssist
Steering smoothing, traction control, ABS, brake assist, racing-line help
CarState, TrackSpline
TrackSpline
Centerline, width, banking, elevation, grip, nearest-point queries
track data
AIDriver
Racing-line following, speed profile, overtaking, mistakes
TrackSpline, CarState
RaceManager
Grid, lights out, laps, penalties, pit stops, results
all cars
Telemetry
Records channels for debug and validation
CarState
6. Core data structures
CarState (plain struct, no Unity types in the hot path):
• Position, velocity, orientation, angular velocity
• Per wheel: angular velocity, load F_z, slip angle, slip ratio, tire temperature, wear, contact patch force
• Engine RPM, gear, throttle, brake, steering
• Battery state of charge, deploy mode, aero mode
• Fuel mass
CarConfig (ScriptableObject exported to JSON): mass, inertia (I_z), wheelbase, track widths, CG height, weight distribution, spring and damper rates, aero coefficients per mode, gear ratios, torque curve, brake data, energy limits.
TireConfig: Pacejka coefficients per compound (soft, medium, hard, intermediate, wet), thermal window, wear rate, load sensitivity.
TrackConfig: spline control points, width profile, banking profile, elevation, kerb zones, grip map.
7. Vehicle dynamics: calculations
Build the model in stages. Each stage is validated before the next is added.
7.1 Tires (most important)
• Slip angle: α = atan(v_y / |v_x|) − δ for front wheels (δ is steering angle), same without δ at the rear. Use a low-speed blend to avoid division by zero.
• Slip ratio: κ = (ωR − v_x) / max(|v_x|, v_min)
• Pacejka Magic Formula: F = D · sin(C · atan(Bx − E(Bx − atan(Bx)))), with x = α for lateral force and x = κ for longitudinal force. D = μ · F_z.
• Load sensitivity: μ = μ0 · (1 − k · (F_z − F_z0) / F_z0), so more load gives more force, but with diminishing returns.
• Combined slip: scale F_x and F_y so (F_x / F_x,max)² + (F_y / F_y,max)² ≤ 1.
• Thermal model: grip multiplier is a bell curve around the optimal temperature (about 90 to 110 °C). Heat in from slip power (force × slip velocity), heat out to air (proportional to speed) and to the track.
• Wear: wear rate proportional to slip energy per second, grip falls as wear accumulates. Tune so a soft tire lasts roughly 15 to 25 laps, depending on the track.
• Relaxation length: first-order lag on slip response (about 0.2 to 0.5 m) so the car does not snap-react at high speed.
7.2 Weight transfer and load per wheel
• Longitudinal: ΔF_z = m · a_x · h / L
• Lateral: ΔF_z = m · a_y · h / t, split between front and rear axles by roll stiffness distribution
• Static load: (m · g · weight distribution) / 2 per wheel
• Aero load: add front and rear downforce to the corresponding axle
In the stage 1 simple model, compute these directly. In the full model, per-wheel loads come from the suspension spring and damper forces.
7.3 Aerodynamics
• Downforce: F_down = ½ · ρ · v² · C_L · A
• Drag: F_drag = ½ · ρ · v² · C_D · A
• ρ ≈ 1.225 kg/m³ at sea level and 15 °C, corrected for track altitude and temperature
• Two modes (2026 active aero): corner mode (high C_L, high C_D) and straight mode (lower C_L, much lower C_D). Blend between them over about 0.1 to 0.2 s.
• Ride height map: C_L is a function of front and rear ride height (lookup table). Add a porpoising penalty if ride height is too low.
• Wake effect: when within about 1 to 2 s behind another car, scale C_L down by up to 25 to 35 percent and C_D down slightly. Use a distance-based curve.
• Balance: split downforce front and rear with an aero balance value (for example 45 percent front).
7.4 Powertrain and energy (2026 rules)
• Wheel RPM: ω_wheel = v / R
• Engine RPM: ω_engine = ω_wheel · gear ratio · final drive
• Wheel torque: T = T_engine(RPM, throttle) · gear · final drive · efficiency (about 0.92 to 0.95)
• Torque curve: lookup table, roughly 400 kW of ICE power at peak, redline around 15,000 RPM (use public figures as a starting point).
• MGU-K: up to 350 kW deploy and harvest. Deployment tapers as speed rises, following the regulation's speed-based limit.
• Battery model: state of charge (SoC) in MJ, per-lap deploy cap and harvest cap, SoC clamped to a usable window.
• Deploy modes: at least Charge, Balanced and Overtake in v1. The driver picks the mode, and the AI uses a simple strategy.
• Gearbox: 8 gears, shift time about 20 to 30 ms, torque cut on shift.
• Differential: start with a locked or simple preload model, upgrade to a clutch-pack model later.
7.5 Suspension and chassis
• Per corner: F = k · x + c · v (spring plus damper), with separate bump and rebound damping.
• Anti-roll bar: extra force proportional to the left-right travel difference.
• Bump stops at travel limits.
• Yaw dynamics: I_z · dr/dt = Σ (tire force moments) + aero yaw moment
• Mobile simplification: v1 can use a rigid chassis with 4 raycast contact points instead of a full multi-body model. The spring-damper still runs per corner through the raycast.
7.6 Brakes
• Brake torque = pressure · brake gain · bias (front and rear)
• Temperature model: heat from braking power, cooling from airflow. Grip or torque fades above a threshold.
• Regen blending: the rear brake-by-wire reduces friction braking by the amount the MGU-K harvests.
• ABS: simple slip-ratio limiter, available as an assist option.
7.7 Numerical integration
• Semi-implicit (symplectic) Euler at the fixed step.
• Clamp tire slip and force to safe ranges.
• Use sub-stepping (2 to 4 sub-steps) only when speed or slip exceeds a threshold, to protect battery life.
8. Track and world
• Representation: closed spline of centerline points, with width, banking and elevation sampled by distance along the track.
• Queries: nearest spline point (cached, local search from the last known index), lateral offset, heading, curvature.
• Surface types: asphalt, kerb, grass, gravel, each with a grip multiplier and drag. Detected by lateral offset and the grip map.
• Racing line: precomputed offline with a minimum-curvature or minimum-time optimizer and stored as data.
• Speed profile: v_max(s) = √(μ · g / κ(s)) with aero added, then a backward pass for braking limits and a forward pass for acceleration limits. Stored per car class.
• Collision: simple capsule or box colliders for cars and barriers. Damage is cosmetic or off in v1.
9. AI architecture
• Racing-line follower: steers with a pure-pursuit controller toward a look-ahead point (look-ahead distance scales with speed). Target speed comes from the stored speed profile times a skill factor.
• Simplified dynamics: AI cars use a reduced model (bicycle model with a lookup grip limit) at 60 Hz instead of the full model, which keeps CPU cost manageable.
• Skill parameters: pace factor (0.95 to 1.0), braking aggression, consistency (noise on speed and line), mistake probability.
• Overtaking: offset the target line sideways when a car ahead is within a closing-speed window. Return to line after the pass. Respect the defender's position and a minimum gap.
• Strategy: pit window based on tire wear, simple deploy mode choice by lap and gap.
• Difficulty levels change only the AI parameters, never the player's physics.
10. Race systems
• State machine: Menu, Garage, Grid, Formation, Lights Out, Racing, Safety Car (later), Finished, Results.
• Timing: sector and lap times from spline distance crossings, with interpolation for accuracy.
• Rules (v1): track limits warning, simple time penalties, no flags besides the finish.
• Pit stops: pit lane spline with speed limiter, fixed or variable stop time, tire change.
• Fuel: mass decreases with consumption, which affects lap time (about 0.03 s per lap per 1 kg is a common rule of thumb, so verify and tune).
• Save data: local JSON or SQLite for career progress, settings and best laps.
11. Mobile graphics and performance
• URP with a mobile-tuned renderer asset and an SRP batcher.
• Lighting: baked lightmaps and reflection probes for the track, one real-time directional light, simple cascaded shadows limited to the nearby area.
• Car: about 80,000 to 120,000 triangles for the player car, LODs for AI cars (a third of that at mid distance, a fraction at far distance). Clearcoat car paint via Shader Graph.
• Post-processing: light bloom and color grading. Motion blur is optional on high tier only.
• Particles: capped counts for tire smoke, sparks and spray.
• Draw distance: strong use of occlusion culling, track chunk streaming and billboards for far objects.
• Quality tiers: Low, Medium, High, picked automatically from device benchmarks, with a dynamic resolution scaler tied to frame time and device temperature.
• Memory: texture atlases, ASTC compression, no per-frame allocations in the sim loop.
12. Validation and tuning
Realism comes from tuning against real data, not from adding more formulas.
1. Unit tests for each model (tire force at known slip, aero force at a known speed, gear ratio math).
2. Standard maneuvers: skidpad (steady-state lateral g), straight-line acceleration (0 to 100 and 0 to 300 km/h), braking from 300 km/h to 0, step steer. Compare against known figures (peak cornering around 4 to 6 g at speed, braking from 300 km/h in roughly 4 to 5 s and about 100 to 125 m).
3. Lap-time check: compare simulated speed, throttle, brake and gear traces against real telemetry pulled with FastF1 for one reference circuit.
4. Telemetry logger in the game writes CSV so the same Python notebooks can analyze both real and simulated laps.
5. Tuning loop: change data files only, rerun maneuvers, compare, repeat.
13. Project structure
/Assets
  /Scripts
    /Simulation      (pure C#, no UnityEngine UI/audio)
      Tires/  Aero/  Powertrain/  Energy/  Suspension/  Brakes/
    /Vehicle         (CarController, CarState, Rigidbody bridge)
    /AI
    /Race
    /Track
    /Input
    /UI
    /Audio
    /Telemetry
  /Data              (CarConfig, TireConfig, TrackConfig assets)
  /Art  /Audio  /Scenes  /Shaders
/Tests               (EditMode tests for the Simulation folder)
/Tools               (Python: FastF1 export, racing-line optimizer, plots)
/Docs
14. Milestones
Milestone
Deliverable
Exit check
M0
Unity 6 project, repo, folder structure, CI build to a phone
App runs on a real device
M1
One car on a flat plane: bicycle model, basic tires, touch input, chase camera
Stable at 300 km/h, no jitter
M2
4-wheel model, weight transfer, aero, brakes, gearbox
Skidpad and braking tests within range
M3
Track spline, surface grip, lap timing, first graybox circuit
Complete lap, sensible lap time
M4
Tire temperature and wear, fuel, energy system, active aero
Tire and energy strategy visibly matters
M5
AI opponents, race start, pit stops, results
Full race with 8 cars at 60 FPS on target phone
M6
Art pass: car, track, lighting, VFX, audio
Visual target met within the performance budget
M7
UI, menus, assists, settings, save data, polish
Playable end to end
M8
Playtest, tuning, optimization, soft launch
Stable on the device matrix
15. Working with Antigravity
• Keep this document in the repo (/Docs/architecture.md) so every agent session starts with the same context.
• Give each agent task one module, a clear interface and the tests that module must pass.
• Ask for tests before implementation for the Simulation modules, since they are pure C# and easy to test.
• Review physics code yourself for units (m, s, kg, rad, N). Unit mistakes are the most common bug.
• Commit after every working step so you can roll back an agent change.
16. Risks and mitigations
Risk
Mitigation
Physics too heavy for phones
Simplified AI model, 120 Hz only for player, profile early on a real device
Car feels undrivable on touch
Strong default assists, steering smoothing, tunable sensitivity, tilt calibration
Instability at high speed
Fixed step, clamps, sub-stepping, slip-limit blending at low speed
Scope creep
Hold the v1 scope in section 1, add features only after M5
Legal issues with branding
Original names, liveries and logos from day one
Parameter guessing
Validate against FastF1 and published figures, keep every number in data files
17. Open decisions
• Touch control scheme: tilt steering with auto or manual throttle, or on-screen wheel with manual pedals, or both and gear system(manual or auto)
• First circuit: an original fictional track, or a layout inspired by a real one
• Single race only at launch, or a short career mode
• Android first, iOS first, or both from the start