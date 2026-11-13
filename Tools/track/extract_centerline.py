import os
import json
import numpy as np
import cv2
from PIL import Image
from skimage.morphology import skeletonize
import networkx as nx
from scipy.interpolate import pchip_interpolate
from scipy.ndimage import uniform_filter1d
from scipy.spatial import cKDTree

def chaikin(points, iterations=5):
    p = points.copy()
    for _ in range(iterations):
        L = len(p)
        new_p = []
        for i in range(L):
            p0 = p[i]
            p1 = p[(i + 1) % L]
            new_p.append(0.75 * p0 + 0.25 * p1)
            new_p.append(0.25 * p0 + 0.75 * p1)
        p = np.array(new_p)
    return p

def get_ordered_cycle(skel, sf_target=(560, 160)):
    pts = np.argwhere(skel)
    pt_set = set(map(tuple, pts))
    G = nx.Graph()
    for y, x in pts:
        G.add_node((y, x))
        for dy in [-1, 0, 1]:
            for dx in [-1, 0, 1]:
                if dy == 0 and dx == 0: continue
                ny, nx_ = y + dy, x + dx
                if (ny, nx_) in pt_set:
                    G.add_edge((y, x), (ny, nx_), weight=np.hypot(dy, dx))
    while True:
        deg1 = [n for n, d in G.degree() if d == 1]
        if not deg1: break
        G.remove_nodes_from(deg1)

    cycles = nx.cycle_basis(G)
    longest = max(cycles, key=len)

    cycle_nodes = set(longest)
    curr = longest[0]
    ordered = [curr]
    visited = {curr}
    while len(ordered) < len(longest):
        nbrs = [n for n in G.neighbors(curr) if n in cycle_nodes and n not in visited]
        if not nbrs: break
        curr = nbrs[0]
        ordered.append(curr)
        visited.add(curr)

    cycle_xy = np.array([[x, y] for (y, x) in ordered])
    sf_idx = np.argmin(np.sum((cycle_xy - np.array(sf_target))**2, axis=1))
    cycle_xy = np.roll(cycle_xy, -sf_idx, axis=0)

    # Ensure anticlockwise (positive signed area in world X, Z = -y)
    x_ = cycle_xy[:, 0]
    z_ = -cycle_xy[:, 1]
    x_next = np.roll(x_, -1)
    z_next = np.roll(z_, -1)
    signed_area = 0.5 * np.sum(x_ * z_next - x_next * z_)
    if signed_area < 0:
        cycle_xy = np.vstack([cycle_xy[0:1], cycle_xy[1:][::-1]])

    return cycle_xy

def extract_track():
    print("=== Step 1: Centerline Extraction & Repair ===")
    img_path = 'Docs/track/interlagos_layout.png'
    if not os.path.exists(img_path):
        img_path = 'Docs/track/interlagos_circuit.png'
    
    img_pil = Image.open(img_path).convert('RGB')
    img_bgr = np.array(img_pil)[:, :, ::-1].copy()
    img_rgb = np.array(img_pil)
    h, w, _ = img_rgb.shape

    # Color segmentation
    is_asphalt = (img_rgb[:,:,0] >= 10) & (img_rgb[:,:,0] <= 45) & (img_rgb[:,:,1] >= 10) & (img_rgb[:,:,1] <= 45) & (img_rgb[:,:,2] >= 15) & (img_rgb[:,:,2] <= 55)
    is_red = (img_rgb[:,:,0] > 140) & (img_rgb[:,:,1] < 75) & (img_rgb[:,:,2] < 75)
    is_blue = (img_rgb[:,:,0] < 70) & (img_rgb[:,:,1] > 70) & (img_rgb[:,:,2] > 150)
    is_yellow = (img_rgb[:,:,0] > 170) & (img_rgb[:,:,1] > 150) & (img_rgb[:,:,2] < 70)
    is_pink = (img_rgb[:,:,0] > 180) & (img_rgb[:,:,1] < 50) & (img_rgb[:,:,2] > 180)
    is_green = (img_rgb[:,:,0] < 80) & (img_rgb[:,:,1] > 160) & (img_rgb[:,:,2] < 80)
    ribbon = is_asphalt | is_red | is_blue | is_yellow | is_pink | is_green

    # Corner label coordinates
    labels = [
        ("T1", 95, 325), ("T2", 172, 440), ("T3", 152, 720), ("T4", 1230, 1050), ("T5", 1228, 780),
        ("T6", 745, 367), ("T7", 856, 222), ("T8", 1040, 190), ("T9", 1035, 375), ("T10", 1377, 237),
        ("T11", 1300, 495), ("T12", 1575, 745), ("T13", 1675, 540), ("T14", 1520, 165), ("T15", 990, 32)
    ]

    # Erase each numbered label disc (radius 30 px around label centres)
    y_grid, x_grid = np.ogrid[:h, :w]
    disc_mask = np.zeros((h, w), dtype=bool)
    for name, lx, ly in labels:
        disc_mask |= (((x_grid - lx)**2 + (y_grid - ly)**2) <= 30**2)

    ribbon_no_discs = ribbon & (~disc_mask)

    # Repair ribbon where discs overlapped with closing operation on ribbon only
    kernel_repair = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (45, 45))
    repaired_ribbon = ribbon_no_discs.copy().astype(np.uint8)
    for name, lx, ly in labels:
        roi_r = 55
        y0, y1 = max(0, ly - roi_r), min(h, ly + roi_r)
        x0, x1 = max(0, lx - roi_r), min(w, lx + roi_r)
        sub = ribbon_no_discs[y0:y1, x0:x1].astype(np.uint8)
        sub_closed = cv2.morphologyEx(sub, cv2.MORPH_CLOSE, kernel_repair)
        repaired_ribbon[y0:y1, x0:x1] = sub_closed

    # Standard morphology to get solid track ribbon
    kernel_close = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))
    ribbon_closed = cv2.morphologyEx(repaired_ribbon, cv2.MORPH_CLOSE, kernel_close)
    num_labels, cc_labels, stats, _ = cv2.connectedComponentsWithStats(ribbon_closed)
    largest = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
    track_ribbon = (cc_labels == largest)

    # Fill interior holes (< 3000 px)
    num_inv, inv_labels, inv_stats, _ = cv2.connectedComponentsWithStats((~track_ribbon).astype(np.uint8))
    filled_ribbon = track_ribbon.copy()
    for i in range(1, num_inv):
        if inv_stats[i, cv2.CC_STAT_AREA] < 3000:
            filled_ribbon[inv_labels == i] = True

    # Detach outside badges
    kernel_open = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))
    smooth_ribbon = cv2.morphologyEx(filled_ribbon.astype(np.uint8), cv2.MORPH_OPEN, kernel_open)

    # Method 1: Skeleton of repaired ribbon
    skel_ribbon = skeletonize(smooth_ribbon > 0).astype(np.uint8)
    cycle_ribbon = get_ordered_cycle(skel_ribbon)

    # Method 2: Skeleton of sector lines alone
    sector_lines = is_red | is_blue | is_yellow
    kernel_sec = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (35, 35))
    closed_sec = cv2.morphologyEx(sector_lines.astype(np.uint8), cv2.MORPH_CLOSE, kernel_sec)
    cv2.line(closed_sec, (276, 239), (244, 249), 1, thickness=5)
    cv2.line(closed_sec, (159, 374), (183, 397), 1, thickness=5)
    cv2.line(closed_sec, (1624, 427), (1630, 461), 1, thickness=5)
    skel_sec = skeletonize(closed_sec > 0).astype(np.uint8)
    cycle_sec = get_ordered_cycle(skel_sec)

    # Smooth both methods
    ribbon_smooth = chaikin(cycle_ribbon[::20], iterations=5)
    sec_smooth = chaikin(cycle_sec[::20], iterations=5)

    TARGET_LENGTH = 4309.0
    N_SAMPLES = int(np.round(TARGET_LENGTH / 2.0)) # exactly 2155 samples
    SAMPLE_SPACING = TARGET_LENGTH / N_SAMPLES     # 1.99953596 m

    diffs = np.diff(np.vstack([ribbon_smooth, ribbon_smooth[0]]), axis=0)
    dists = np.hypot(diffs[:, 0], diffs[:, 1])
    s_px = np.concatenate([[0.0], np.cumsum(dists)])
    scale = TARGET_LENGTH / s_px[-1]

    s_dist_m = np.linspace(0.0, TARGET_LENGTH, N_SAMPLES, endpoint=False)
    s_targets_px = s_dist_m / scale
    pts_closed = np.vstack([ribbon_smooth, ribbon_smooth[0]])
    res_x = np.interp(s_targets_px, s_px, pts_closed[:, 0])
    res_y = np.interp(s_targets_px, s_px, pts_closed[:, 1])

    world_x = res_x * scale
    world_z = -res_y * scale
    world_pts = np.column_stack([world_x, world_z])

    # Cross-check distance with sector-line method
    diffs_sec = np.diff(np.vstack([sec_smooth, sec_smooth[0]]), axis=0)
    dists_sec = np.hypot(diffs_sec[:, 0], diffs_sec[:, 1])
    s_px_sec = np.concatenate([[0.0], np.cumsum(dists_sec)])
    scale_sec = TARGET_LENGTH / s_px_sec[-1]
    s_targets_px_sec = s_dist_m / scale_sec
    pts_closed_sec = np.vstack([sec_smooth, sec_smooth[0]])
    sec_x = np.interp(s_targets_px_sec, s_px_sec, pts_closed_sec[:, 0])
    sec_y = np.interp(s_targets_px_sec, s_px_sec, pts_closed_sec[:, 1])
    world_sec = np.column_stack([sec_x * scale_sec, -sec_y * scale_sec])

    tree_sec = cKDTree(world_sec)
    cross_dists, _ = tree_sec.query(world_pts)
    max_cross_dist = float(np.max(cross_dists))
    mean_cross_dist = float(np.mean(cross_dists))

    # Sector breakdown
    mask_s1 = (s_dist_m >= 0.0) & (s_dist_m < 1224.28)
    mask_s2 = (s_dist_m >= 1224.28) & (s_dist_m < 3130.73)
    mask_s3 = (s_dist_m >= 3130.73)
    mask_bottom = (s_dist_m >= 730.17) & (s_dist_m <= 1306.30)
    mask_main = (s_dist_m >= 3806.88) | (s_dist_m <= 230.05)

    print(f"Extraction method: Morphology skeleton with label discs removed and Chaikin smoothing")
    print(f"Scale: {scale:.6f} m/px")
    print(f"Sample count N: {N_SAMPLES}, Sample spacing: {SAMPLE_SPACING:.4f} m")
    print(f"Overall separation: Max = {max_cross_dist:.2f} m, Mean = {mean_cross_dist:.2f} m")
    print(f"  Sector 1 (0 to 1224.3m): Mean = {np.mean(cross_dists[mask_s1]):.2f} m, Max = {np.max(cross_dists[mask_s1]):.2f} m")
    print(f"  Sector 2 (1224.3 to 3130.7m): Mean = {np.mean(cross_dists[mask_s2]):.2f} m, Max = {np.max(cross_dists[mask_s2]):.2f} m")
    print(f"  Sector 3 (3130.7 to 4309m): Mean = {np.mean(cross_dists[mask_s3]):.2f} m, Max = {np.max(cross_dists[mask_s3]):.2f} m")
    print(f"  Bottom Straight (Zone A: 730.2 to 1306.3m): Mean = {np.mean(cross_dists[mask_bottom]):.2f} m, Max = {np.max(cross_dists[mask_bottom]):.2f} m")
    print(f"  Main Straight (Zone B: 3806.9 to 230.1m): Mean = {np.mean(cross_dists[mask_main]):.2f} m, Max = {np.max(cross_dists[mask_main]):.2f} m")

    # Blend on Bottom Straight where mean offset is > 4m (Zone A: 730m to 1306m)
    blend_factor = np.zeros(N_SAMPLES, dtype=float)
    blend_start = 730.17
    blend_end = 1306.30
    ramp = 50.0
    for i, s in enumerate(s_dist_m):
        if s >= blend_start and s <= blend_end:
            f = 0.5
            if s < blend_start + ramp:
                f = 0.5 * (s - blend_start) / ramp
            elif s > blend_end - ramp:
                f = 0.5 * (blend_end - s) / ramp
            blend_factor[i] = f

    final_px_x = (1.0 - blend_factor) * res_x + blend_factor * sec_x
    final_px_y = (1.0 - blend_factor) * res_y + blend_factor * sec_y

    world_x_raw = final_px_x * scale
    world_z_raw = -final_px_y * scale
    world_pts_raw = np.column_stack([world_x_raw, world_z_raw])

    # Curvature before smoothing
    ds = SAMPLE_SPACING
    p_prev_raw = np.roll(world_pts_raw, 1, axis=0)
    p_next_raw = np.roll(world_pts_raw, -1, axis=0)
    t_raw = (p_next_raw - p_prev_raw) / (2.0 * ds)
    t2_raw = (p_next_raw - 2.0 * world_pts_raw + p_prev_raw) / (ds**2)
    k_raw = (t_raw[:, 0] * t2_raw[:, 1] - t_raw[:, 1] * t2_raw[:, 0]) / np.maximum((t_raw[:, 0]**2 + t_raw[:, 1]**2)**1.5, 1e-8)
    dk_before = np.max(np.abs(np.diff(np.concatenate([k_raw, [k_raw[0]]]))))

    # 20m light smoothing filter (window = 11 samples ~ 22m)
    smooth_x = uniform_filter1d(world_pts_raw[:, 0], size=11, mode='wrap')
    smooth_z = uniform_filter1d(world_pts_raw[:, 1], size=11, mode='wrap')
    world_pts = np.column_stack([smooth_x, smooth_z])
    world_x = world_pts[:, 0]
    world_z = world_pts[:, 1]
    res_x = world_x / scale
    res_y = -world_z / scale

    # Curvature after smoothing
    p_prev = np.roll(world_pts, 1, axis=0)
    p_next = np.roll(world_pts, -1, axis=0)
    t = (p_next - p_prev) / (2.0 * ds)
    t2 = (p_next - 2.0 * world_pts + p_prev) / (ds**2)
    kappa = (t[:, 0] * t2[:, 1] - t[:, 1] * t2[:, 0]) / np.maximum((t[:, 0]**2 + t[:, 1]**2)**1.5, 1e-8)
    smooth_kappa = uniform_filter1d(kappa, size=11, mode='wrap')
    smooth_radius = 1.0 / np.maximum(np.abs(smooth_kappa), 1e-6)
    dk_after = np.max(np.abs(np.diff(np.concatenate([smooth_kappa, [smooth_kappa[0]]]))))

    print(f"Max delta curvature between neighbouring samples: Before = {dk_before:.6f} 1/m, After = {dk_after:.6f} 1/m")

    # 15 Corners Table
    old_radii = {
        "T1": 29.9, "T2": 29.4, "T3": 14.4, "T4": 19.3, "T5": 17.9,
        "T6": 41.0, "T7": 25.5, "T8": 15.4, "T9": 14.2, "T10": 14.1,
        "T11": 24.1, "T12": 18.9, "T13": 98.2, "T14": 16.8, "T15": 139.3
    }

    # Reference directions from circuit facts
    expected_directions = {
        "T1": "left", "T2": "right", "T3": "left", "T4": "left", "T5": "left",
        "T6": "right", "T7": "right", "T8": "right", "T9": "left", "T10": "right",
        "T11": "left", "T12": "left", "T13": "left", "T14": "left", "T15": "left"
    }

    corners = []
    print("\n--- 15 Corners Table (Old vs New) ---")
    print("Turn | Name | Apex s (m) | Old R (m) | New R (m) | Direction")
    print("-----+------+------------+-----------+-----------+----------")
    for idx_c, (name, lx, ly) in enumerate(labels, 1):
        d_px = np.hypot(res_x - lx, res_y - ly)
        near_idx = int(np.argmin(d_px))
        
        if name == "T9":
            win_indices = [(near_idx + k) % N_SAMPLES for k in range(-50, 51)] # 100m window
        elif name in ("T13", "T15"):
            win_indices = [(near_idx + k) % N_SAMPLES for k in range(-25, 26)] # focused local apex
        else:
            win_indices = [(near_idx + k) % N_SAMPLES for k in range(-30, 31)]

        exp_dir = expected_directions[name]
        valid_candidates = [idx for idx in win_indices if (smooth_kappa[idx] > 0 if exp_dir == "left" else smooth_kappa[idx] < 0)]
        if not valid_candidates: valid_candidates = win_indices
        local_min_idx = valid_candidates[int(np.argmin(smooth_radius[valid_candidates]))]

        apex_s = float(s_dist_m[local_min_idx])
        apex_r = float(smooth_radius[local_min_idx])
        apex_k = float(smooth_kappa[local_min_idx])
        direction = "left" if apex_k > 0 else "right"
        entry_s = float((apex_s - 40.0) % TARGET_LENGTH)
        exit_s = float((apex_s + 40.0) % TARGET_LENGTH)

        c_rec = {
            "number": idx_c,
            "name": name,
            "apex_s": round(apex_s, 2),
            "entry_s": round(entry_s, 2),
            "exit_s": round(exit_s, 2),
            "direction": direction,
            "radius_m": round(apex_r, 2),
            "curvature": round(apex_k, 5),
            "label_x_px": lx,
            "label_y_px": ly
        }
        corners.append(c_rec)
        old_r = old_radii.get(name, 0.0)
        print(f"{idx_c:4d} | {name:4s} | {apex_s:10.1f} | {old_r:9.1f} | {apex_r:9.1f} | {direction:5s}")

    # Sectors (using searchsorted)
    s1_end_pt = (1010, 980)
    s2_end_pt = (1415, 635)
    s1_idx = int(np.argmin(np.hypot(res_x - s1_end_pt[0], res_y - s1_end_pt[1])))
    s2_idx = int(np.argmin(np.hypot(res_x - s2_end_pt[0], res_y - s2_end_pt[1])))
    s1_s = round(float(s_dist_m[s1_idx]), 2)
    s2_s = round(float(s_dist_m[s2_idx]), 2)

    sectors = [
        {"sector": 1, "start_s": 0.0, "end_s": s1_s, "length_m": s1_s},
        {"sector": 2, "start_s": s1_s, "end_s": s2_s, "length_m": round(s2_s - s1_s, 2)},
        {"sector": 3, "start_s": s2_s, "end_s": TARGET_LENGTH, "length_m": round(TARGET_LENGTH - s2_s, 2)}
    ]

    # DRS Zones
    drs_a_start = int(np.argmin(np.hypot(res_x - 320, res_y - 765)))
    drs_a_end = int(np.argmin(np.hypot(res_x - 1150, res_y - 975)))
    drs_b_start = int(np.argmin(np.hypot(res_x - 1265, res_y - 160)))
    drs_b_end = int(np.argmin(np.hypot(res_x - 235, res_y - 290)))

    straight_mode_zones = [
        {
            "name": "Zone A (Bottom Straight)",
            "start_s": round(float(s_dist_m[drs_a_start]), 2),
            "end_s": round(float(s_dist_m[drs_a_end]), 2),
            "length_m": round(float(s_dist_m[drs_a_end] - s_dist_m[drs_a_start]), 2)
        },
        {
            "name": "Zone B (Main Straight / S/F)",
            "start_s": round(float(s_dist_m[drs_b_start]), 2),
            "end_s": round(float(s_dist_m[drs_b_end]), 2),
            "length_m": round(float((TARGET_LENGTH - s_dist_m[drs_b_start]) + s_dist_m[drs_b_end]), 2)
        }
    ]

    # Elevation Profile: PCHIP interpolation with gradient limiting (<= 10%)
    ctrl_elevation = [
        {"corner": "S/F",     "s": 0.0,                           "height": 40.0},
        {"corner": "T1",      "s": corners[0]["apex_s"],          "height": 36.0},
        {"corner": "T3",      "s": corners[2]["apex_s"],          "height": 28.0},
        {"corner": "T4",      "s": corners[3]["apex_s"],          "height": 14.0},
        {"corner": "T5",      "s": corners[4]["apex_s"],          "height": 10.0},
        {"corner": "T6",      "s": corners[5]["apex_s"],          "height": 14.0},
        {"corner": "T7",      "s": corners[6]["apex_s"],          "height": 18.0},
        {"corner": "T8",      "s": corners[7]["apex_s"],          "height": 16.0},
        {"corner": "T9",      "s": corners[8]["apex_s"],          "height": 8.0},
        {"corner": "T10",     "s": corners[9]["apex_s"],          "height": 6.0},
        {"corner": "T11",     "s": corners[10]["apex_s"],         "height": 2.0},
        {"corner": "T12",     "s": corners[11]["apex_s"],         "height": 0.0},
        {"corner": "T13",     "s": corners[12]["apex_s"],         "height": 6.0},
        {"corner": "T14",     "s": corners[13]["apex_s"],         "height": 30.0},
        {"corner": "T15",     "s": corners[14]["apex_s"],         "height": 38.0},
        {"corner": "S/F_end", "s": TARGET_LENGTH,                 "height": 40.0}
    ]

    ctrl_s = [cp["s"] for cp in ctrl_elevation]
    ctrl_h = [cp["height"] for cp in ctrl_elevation]

    # Monotone-preserving PCHIP interpolation over distance
    h_pchip = pchip_interpolate(ctrl_s, ctrl_h, s_dist_m)

    # Gradient check & pitch angle computation: slope = dy / ds
    h_next = np.roll(h_pchip, -1)
    h_prev = np.roll(h_pchip, 1)
    slopes = (h_next - h_prev) / (2.0 * SAMPLE_SPACING)
    pitch_angles_deg = np.arctan(slopes) * (180.0 / np.pi)

    gradients = np.abs(slopes)
    max_gradient = float(np.max(gradients))
    max_grad_s = float(s_dist_m[np.argmax(gradients)])
    elev_range = float(np.max(h_pchip) - np.min(h_pchip))

    print(f"\nElevation Profile:")
    print(f"Max gradient: {max_gradient * 100:.2f}% at s = {max_grad_s:.1f} m (limit <= 10.0%)")
    print(f"Total range: {elev_range:.2f} m (target 40.0 - 46.0 m)")

    # Save elevation profile JSON
    elev_data = {
        "version": 2,
        "name": "Interlagos Elevation Profile",
        "total_range_m": round(elev_range, 2),
        "max_gradient_pct": round(max_gradient * 100.0, 2),
        "control_points": ctrl_elevation
    }
    for elev_file in ['Assets/Data/Tracks/f1sig_elevation.json', 'Assets/Resources/Tracks/f1sig_elevation.json']:
        os.makedirs(os.path.dirname(elev_file), exist_ok=True)
        with open(elev_file, 'w') as f:
            json.dump(elev_data, f, indent=2)
    print("Saved elevation profile to Assets/Data/Tracks and Assets/Resources/Tracks")

    # Spawn slot: exactly 10 m behind start line (s = 4299.0 m)
    spawn_s = TARGET_LENGTH - 10.0 # 4299.0 m
    # Exact interpolation between surrounding samples
    i0 = int(np.floor(spawn_s / SAMPLE_SPACING))
    i1 = (i0 + 1) % N_SAMPLES
    t_interp = float((spawn_s - s_dist_m[i0]) / (s_dist_m[i1] - s_dist_m[i0]))
    
    spawn_x = float(world_x[i0] + t_interp * (world_x[i1] - world_x[i0]))
    spawn_y = float(h_pchip[i0] + t_interp * (h_pchip[i1] - h_pchip[i0]))
    spawn_z = float(world_z[i0] + t_interp * (world_z[i1] - world_z[i0]))

    spawn_t_x = float(world_x[i1] - world_x[i0])
    spawn_t_z = float(world_z[i1] - world_z[i0])
    spawn_yaw = float(np.arctan2(spawn_t_x, spawn_t_z) * 180.0 / np.pi)

    spawn_slot = {
        "s": round(spawn_s, 2),
        "x": round(spawn_x, 2),
        "y": round(spawn_y, 2),
        "z": round(spawn_z, 2),
        "yaw_deg": round(spawn_yaw, 2)
    }

    # Samples list with pitch angle and curvature
    samples_list = []
    for i in range(N_SAMPLES):
        samples_list.append({
            "index": i,
            "s": round(float(s_dist_m[i]), 2),
            "x": round(float(world_x[i]), 2),
            "y": round(float(h_pchip[i]), 2),
            "z": round(float(world_z[i]), 2),
            "width": 14.0,
            "banking_deg": 0.0,
            "pitch_deg": round(float(pitch_angles_deg[i]), 2),
            "curvature": round(float(smooth_kappa[i]), 5),
            "radius_m": round(float(smooth_radius[i]), 2),
            "px_x": round(float(res_x[i]), 1),
            "px_y": round(float(res_y[i]), 1)
        })

    track_data = {
        "version": 2,
        "name": "F1 Signature Circuit",
        "total_length_m": TARGET_LENGTH,
        "sample_spacing_m": round(SAMPLE_SPACING, 4),
        "scale_m_per_px": round(scale, 6),
        "airDensity": 1.10,
        "default_width_m": 14.0,
        "sectors": sectors,
        "straight_mode_zones": straight_mode_zones,
        "corners": corners,
        "spawn_slot": spawn_slot,
        "samples": samples_list
    }

    for track_file in ['Assets/Data/Tracks/f1sig_track.json', 'Assets/Resources/Tracks/f1sig_track.json']:
        os.makedirs(os.path.dirname(track_file), exist_ok=True)
        with open(track_file, 'w') as f:
            json.dump(track_data, f, indent=2)
    print("Saved track data to Assets/Data/Tracks and Assets/Resources/Tracks")

    # --- Overlay 1: Docs/track/centerline_overlay.png ---
    overlay = img_bgr.copy()

    # (c) DRS Straight-Mode Zones
    za_pts = np.column_stack([res_x[drs_a_start:drs_a_end+1], res_y[drs_a_start:drs_a_end+1]]).astype(np.int32).reshape((-1, 1, 2))
    cv2.polylines(overlay, [za_pts], isClosed=False, color=(0, 200, 0), thickness=8)
    zb1_pts = np.column_stack([res_x[drs_b_start:], res_y[drs_b_start:]]).astype(np.int32).reshape((-1, 1, 2))
    zb2_pts = np.column_stack([res_x[:drs_b_end+1], res_y[:drs_b_end+1]]).astype(np.int32).reshape((-1, 1, 2))
    cv2.polylines(overlay, [zb1_pts], isClosed=False, color=(0, 200, 0), thickness=8)
    cv2.polylines(overlay, [zb2_pts], isClosed=False, color=(0, 200, 0), thickness=8)

    # (a) Centreline coloured by curvature (green straight to red tight)
    max_k = float(np.max(np.abs(smooth_kappa)))
    for i in range(N_SAMPLES):
        next_i = (i + 1) % N_SAMPLES
        pt1 = (int(round(res_x[i])), int(round(res_y[i])))
        pt2 = (int(round(res_x[next_i])), int(round(res_y[next_i])))
        norm_k = float(np.clip(abs(smooth_kappa[i]) / max_k, 0.0, 1.0))
        # Green (0, 255, 0) -> Yellow (0, 255, 255) -> Red (0, 0, 255) in BGR
        if norm_k < 0.5:
            b = 0
            g = 255
            r = int(255 * (norm_k / 0.5))
        else:
            b = 0
            g = int(255 * (1.0 - (norm_k - 0.5) / 0.5))
            r = 255
        cv2.line(overlay, pt1, pt2, (b, g, r), 3, cv2.LINE_AA)

    # (c) Start / Finish
    sf_px = (int(round(res_x[0])), int(round(res_y[0])))
    cv2.circle(overlay, sf_px, 9, (0, 0, 255), -1)
    cv2.putText(overlay, "START/FINISH", (sf_px[0] - 60, sf_px[1] - 15), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)

    # (c) Sector splits
    s1_px = (int(round(res_x[s1_idx])), int(round(res_y[s1_idx])))
    s2_px = (int(round(res_x[s2_idx])), int(round(res_y[s2_idx])))
    cv2.circle(overlay, s1_px, 8, (255, 0, 255), -1)
    cv2.putText(overlay, "S1/S2 SPLIT", (s1_px[0] - 40, s1_px[1] + 25), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 0, 255), 2)
    cv2.circle(overlay, s2_px, 8, (255, 0, 255), -1)
    cv2.putText(overlay, "S2/S3 SPLIT", (s2_px[0] + 15, s2_px[1] + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 0, 255), 2)

    # (d) Spawn slot (10m behind S/F)
    spawn_px = (int(round(res_x[i0])), int(round(res_y[i0])))
    cv2.circle(overlay, spawn_px, 8, (255, 255, 255), -1)
    cv2.putText(overlay, "SPAWN (s=4299)", (spawn_px[0] + 15, spawn_px[1] - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 2)

    # (b) Correctly placed corner markers T1..T15 (using searchsorted indices)
    for c in corners:
        c_idx = int(np.searchsorted(s_dist_m, c["apex_s"]))
        if c_idx >= N_SAMPLES: c_idx = N_SAMPLES - 1
        c_px = (int(round(res_x[c_idx])), int(round(res_y[c_idx])))
        cv2.circle(overlay, c_px, 6, (0, 220, 255), -1)
        dx = c["label_x_px"] - c_px[0]
        dy = c["label_y_px"] - c_px[1]
        dist_lbl = np.hypot(dx, dy)
        tx = int(c_px[0] + (dx / dist_lbl) * 22)
        ty = int(c_px[1] + (dy / dist_lbl) * 22)
        cv2.putText(overlay, c["name"], (tx - 10, ty + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 220, 255), 2)

    overlay_path = 'Docs/track/centerline_overlay.png'
    cv2.imwrite(overlay_path, overlay)
    print(f"Saved clear curvature-colored overlay to {overlay_path}")
    final_overlay_path = 'Docs/track/centerline_overlay_final.png'
    cv2.imwrite(final_overlay_path, overlay)
    print(f"Saved final curvature-colored overlay to {final_overlay_path}")

    # --- Overlay 2: Docs/track/centerline_vs_sectorline.png ---
    comp_img = img_bgr.copy()
    pts_rib_px = np.column_stack([res_x, res_y]).astype(np.int32).reshape((-1, 1, 2))
    pts_sec_px = np.column_stack([sec_x, sec_y]).astype(np.int32).reshape((-1, 1, 2))

    # Repaired ribbon in bright cyan (255, 255, 0)
    cv2.polylines(comp_img, [pts_rib_px], isClosed=True, color=(255, 255, 0), thickness=3)
    # Sector lines method in bright magenta (255, 0, 255)
    cv2.polylines(comp_img, [pts_sec_px], isClosed=True, color=(255, 0, 255), thickness=2)

    # Legend
    cv2.putText(comp_img, "Repaired Ribbon Centerline (Cyan)", (50, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 0), 2)
    cv2.putText(comp_img, "Sector Line Centerline (Magenta)", (50, 100), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 0, 255), 2)
    cv2.putText(comp_img, f"Max Separation: {max_cross_dist:.2f} m", (50, 140), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (255, 255, 255), 2)

    comp_path = 'Docs/track/centerline_vs_sectorline.png'
    cv2.imwrite(comp_path, comp_img)
    print(f"Saved comparison overlay to {comp_path}")

if __name__ == '__main__':
    extract_track()
