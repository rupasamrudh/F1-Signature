import os
import json
import numpy as np
import cv2
from PIL import Image

def extract_track():
    print("=== Step 1: Centerline Extraction ===")
    img_path = 'Docs/track/interlagos_layout.png'
    if not os.path.exists(img_path):
        img_path = 'Docs/track/interlagos_circuit.png'
    
    img_pil = Image.open(img_path).convert('RGB')
    img_bgr = np.array(img_pil)[:, :, ::-1].copy()
    img_rgb = np.array(img_pil)
    h, w, _ = img_rgb.shape

    # 1. Color masking
    is_asphalt = (img_rgb[:,:,0] >= 10) & (img_rgb[:,:,0] <= 45) & (img_rgb[:,:,1] >= 10) & (img_rgb[:,:,1] <= 45) & (img_rgb[:,:,2] >= 15) & (img_rgb[:,:,2] <= 55)
    is_red = (img_rgb[:,:,0] > 140) & (img_rgb[:,:,1] < 75) & (img_rgb[:,:,2] < 75)
    is_blue = (img_rgb[:,:,0] < 70) & (img_rgb[:,:,1] > 70) & (img_rgb[:,:,2] > 150)
    is_yellow = (img_rgb[:,:,0] > 170) & (img_rgb[:,:,1] > 150) & (img_rgb[:,:,2] < 70)
    is_pink = (img_rgb[:,:,0] > 180) & (img_rgb[:,:,1] < 50) & (img_rgb[:,:,2] > 180)
    is_green = (img_rgb[:,:,0] < 80) & (img_rgb[:,:,1] > 160) & (img_rgb[:,:,2] < 80)

    ribbon = is_asphalt | is_red | is_blue | is_yellow | is_pink | is_green

    # Close holes across painted lines and text
    kernel_close = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))
    ribbon_closed = cv2.morphologyEx(ribbon.astype(np.uint8), cv2.MORPH_CLOSE, kernel_close)

    # Retain largest connected component
    num_labels, cc_labels, stats, centroids = cv2.connectedComponentsWithStats(ribbon_closed)
    largest = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
    track_ribbon = (cc_labels == largest)

    # Fill small interior holes (< 3000 px)
    inv = ~track_ribbon
    num_inv, inv_labels, inv_stats, _ = cv2.connectedComponentsWithStats(inv.astype(np.uint8))
    filled_ribbon = track_ribbon.copy()
    for i in range(1, num_inv):
        if inv_stats[i, cv2.CC_STAT_AREA] < 3000:
            filled_ribbon[inv_labels == i] = True

    # Detach outside badges
    kernel_open = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))
    smooth_ribbon = cv2.morphologyEx(filled_ribbon.astype(np.uint8), cv2.MORPH_OPEN, kernel_open)

    # Skeletonize
    from skimage.morphology import skeletonize
    skel = skeletonize(smooth_ribbon > 0).astype(np.uint8)

    # Graph & spur pruning to get clean closed loop
    import networkx as nx
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
    cycle_subgraph = G.subgraph(longest).copy()
    ordered_nodes = list(nx.dfs_preorder_nodes(cycle_subgraph, source=longest[0]))
    cycle_xy = np.array([[x, y] for (y, x) in ordered_nodes])

    # Orient starting at S/F (560, 160)
    sf_target = np.array([560, 160])
    sf_idx = np.argmin(np.sum((cycle_xy - sf_target)**2, axis=1))
    cycle_xy = np.roll(cycle_xy, -sf_idx, axis=0)

    # Ensure anticlockwise (right-to-left along top straight: dx < 0)
    dx = cycle_xy[15, 0] - cycle_xy[0, 0]
    if dx > 0:
        cycle_xy = np.vstack([cycle_xy[0:1], cycle_xy[1:][::-1]])

    # Chaikin smooth
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

    ctrl_pts = cycle_xy[::20]
    smoothed = chaikin(ctrl_pts, iterations=5)

    diffs = np.diff(np.vstack([smoothed, smoothed[0]]), axis=0)
    dists = np.hypot(diffs[:, 0], diffs[:, 1])
    s_px = np.concatenate([[0.0], np.cumsum(dists)])
    total_s_px = s_px[-1]

    TARGET_LENGTH = 4309.0 # m
    scale = TARGET_LENGTH / total_s_px
    print(f"Extraction method: Automatic morphology skeleton with graph cycle extraction and Chaikin smoothing")
    print(f"Scale: {scale:.6f} m/px (Total pixel perimeter = {total_s_px:.2f} px)")

    # Resample at approximately 1.95 m intervals so closing distance to start is strictly < 2.0 m
    # 2210 samples gives step size ~1.949 m and closing distance ~1.95 m (< 2.0 m)
    num_samples = 2210
    s_dist_m = np.linspace(0.0, TARGET_LENGTH, num_samples, endpoint=False)
    s_targets_px = s_dist_m / scale
    pts_closed = np.vstack([smoothed, smoothed[0]])
    res_x = np.interp(s_targets_px, s_px, pts_closed[:, 0])
    res_y = np.interp(s_targets_px, s_px, pts_closed[:, 1])

    # World coordinates: X = px_x * scale, Z = -px_y * scale
    world_x = res_x * scale
    world_z = -res_y * scale
    world_pts = np.column_stack([world_x, world_z])

    # Curvature & radius
    p_prev = np.roll(world_pts, 1, axis=0)
    p_next = np.roll(world_pts, -1, axis=0)
    t = (p_next - p_prev) / 4.0
    t2 = (p_next - 2.0 * world_pts + p_prev) / 4.0
    num = t[:, 0] * t2[:, 1] - t[:, 1] * t2[:, 0]
    den = (t[:, 0]**2 + t[:, 1]**2)**1.5
    kappa = num / np.maximum(den, 1e-6)
    radius = 1.0 / np.maximum(np.abs(kappa), 1e-6)

    # Corner labels
    labels = [
        ("T1", 95, 325), ("T2", 172, 440), ("T3", 152, 720), ("T4", 1230, 1050), ("T5", 1228, 780),
        ("T6", 745, 367), ("T7", 856, 222), ("T8", 1040, 190), ("T9", 1035, 375), ("T10", 1377, 237),
        ("T11", 1300, 495), ("T12", 1575, 745), ("T13", 1675, 540), ("T14", 1520, 165), ("T15", 990, 32)
    ]

    corners = []
    print("\n--- 15 Corners Table ---")
    for idx_c, (name, lx, ly) in enumerate(labels, 1):
        d_px = np.hypot(res_x - lx, res_y - ly)
        near_idx = np.argmin(d_px)
        window = range(near_idx - 35, near_idx + 36)
        win_indices = [w % num_samples for w in window]
        local_min_idx = win_indices[np.argmin(radius[win_indices])]
        
        apex_s = float(s_dist_m[local_min_idx])
        apex_r = float(radius[local_min_idx])
        apex_k = float(kappa[local_min_idx])
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
        print(f"Turn {idx_c:2d} ({name:3s}): apex_s = {apex_s:6.1f} m, radius = {apex_r:5.1f} m, dir = {direction:5s}")

    # Sectors
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
    print(f"\nSectors: S1 len = {s1_s}m, S2 len = {s2_s - s1_s:.1f}m, S3 len = {TARGET_LENGTH - s2_s:.1f}m")

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
    print(f"DRS Zone A: s {straight_mode_zones[0]['start_s']}m -> {straight_mode_zones[0]['end_s']}m")
    print(f"DRS Zone B: s {straight_mode_zones[1]['start_s']}m -> {straight_mode_zones[1]['end_s']}m (crosses S/F)")

    # Elevation control points
    elevation_ctrl = [
        {"corner": "S/F", "s": 0.0, "height": 40.0},
        {"corner": "T1",  "s": corners[0]["apex_s"], "height": 36.0},
        {"corner": "T3",  "s": corners[2]["apex_s"], "height": 28.0},
        {"corner": "T4",  "s": corners[3]["apex_s"], "height": 14.0},
        {"corner": "T5",  "s": corners[4]["apex_s"], "height": 10.0},
        {"corner": "T6",  "s": corners[5]["apex_s"], "height": 14.0},
        {"corner": "T7",  "s": corners[6]["apex_s"], "height": 18.0},
        {"corner": "T8",  "s": corners[7]["apex_s"], "height": 16.0},
        {"corner": "T9",  "s": corners[8]["apex_s"], "height": 8.0},
        {"corner": "T10", "s": corners[9]["apex_s"], "height": 6.0},
        {"corner": "T11", "s": corners[10]["apex_s"], "height": 2.0},
        {"corner": "T12", "s": corners[11]["apex_s"], "height": 0.0},
        {"corner": "T13", "s": corners[12]["apex_s"], "height": 14.0},
        {"corner": "T14", "s": corners[13]["apex_s"], "height": 30.0},
        {"corner": "T15", "s": corners[14]["apex_s"], "height": 38.0},
        {"corner": "S/F_end", "s": TARGET_LENGTH, "height": 40.0}
    ]

    # Save elevation json
    elev_file = 'Assets/Data/Tracks/f1sig_elevation.json'
    with open(elev_file, 'w') as f:
        json.dump({
            "version": 1,
            "name": "Interlagos Elevation Profile",
            "total_range_m": 40.0,
            "control_points": elevation_ctrl
        }, f, indent=2)
    print(f"Saved elevation profile to {elev_file}")

    # Interpolate height y for each sample
    ctrl_s = [cp["s"] for cp in elevation_ctrl]
    ctrl_h = [cp["height"] for cp in elevation_ctrl]
    sample_y = np.interp(s_dist_m, ctrl_s, ctrl_h)

    # Spawn slot: 10 m behind start line along track direction
    # S/F is at s = 0. 10m behind is s = TARGET_LENGTH - 10.0m
    spawn_s = TARGET_LENGTH - 10.0
    spawn_idx = int(np.round(spawn_s / 2.0)) % num_samples
    # Tangent at spawn:
    spawn_t_x = world_x[(spawn_idx + 1) % num_samples] - world_x[(spawn_idx - 1) % num_samples]
    spawn_t_z = world_z[(spawn_idx + 1) % num_samples] - world_z[(spawn_idx - 1) % num_samples]
    spawn_yaw = float(np.arctan2(spawn_t_x, spawn_t_z) * 180.0 / np.pi)

    spawn_slot = {
        "s": round(spawn_s, 2),
        "x": round(float(world_x[spawn_idx]), 2),
        "y": round(float(sample_y[spawn_idx]), 2),
        "z": round(float(world_z[spawn_idx]), 2),
        "yaw_deg": round(spawn_yaw, 2)
    }

    # Samples list
    samples_list = []
    for i in range(num_samples):
        samples_list.append({
            "index": i,
            "s": round(float(s_dist_m[i]), 2),
            "x": round(float(world_x[i]), 2),
            "y": round(float(sample_y[i]), 2),
            "z": round(float(world_z[i]), 2),
            "width": 14.0,
            "banking_deg": 0.0,
            "curvature": round(float(kappa[i]), 5),
            "radius_m": round(float(radius[i]), 2),
            "px_x": round(float(res_x[i]), 1),
            "px_y": round(float(res_y[i]), 1)
        })

    track_data = {
        "version": 1,
        "name": "F1 Signature Circuit",
        "total_length_m": TARGET_LENGTH,
        "sample_spacing_m": 2.0,
        "scale_m_per_px": round(scale, 6),
        "airDensity": 1.10,
        "default_width_m": 14.0,
        "sectors": sectors,
        "straight_mode_zones": straight_mode_zones,
        "corners": corners,
        "spawn_slot": spawn_slot,
        "samples": samples_list
    }

    track_file = 'Assets/Data/Tracks/f1sig_track.json'
    with open(track_file, 'w') as f:
        json.dump(track_data, f, indent=2)
    print(f"Saved track data to {track_file}")

    # Generate Docs/track/centerline_overlay.png
    overlay = img_bgr.copy()
    
    # 1. Draw DRS Zones under track
    # Zone A
    za_pts = np.column_stack([res_x[drs_a_start:drs_a_end+1], res_y[drs_a_start:drs_a_end+1]]).astype(np.int32).reshape((-1, 1, 2))
    cv2.polylines(overlay, [za_pts], isClosed=False, color=(0, 255, 0), thickness=9)
    # Zone B
    zb1_pts = np.column_stack([res_x[drs_b_start:], res_y[drs_b_start:]]).astype(np.int32).reshape((-1, 1, 2))
    zb2_pts = np.column_stack([res_x[:drs_b_end+1], res_y[:drs_b_end+1]]).astype(np.int32).reshape((-1, 1, 2))
    cv2.polylines(overlay, [zb1_pts], isClosed=False, color=(0, 255, 0), thickness=9)
    cv2.polylines(overlay, [zb2_pts], isClosed=False, color=(0, 255, 0), thickness=9)

    # 2. Draw Centreline in bright cyan / orange
    track_pts = np.column_stack([res_x, res_y]).astype(np.int32).reshape((-1, 1, 2))
    cv2.polylines(overlay, [track_pts], isClosed=True, color=(255, 255, 0), thickness=3)

    # 3. Mark Start / Finish (red circle + label)
    sf_px = (int(res_x[0]), int(res_y[0]))
    cv2.circle(overlay, sf_px, 9, (0, 0, 255), -1)
    cv2.putText(overlay, "START/FINISH", (sf_px[0] - 50, sf_px[1] - 15), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)

    # 4. Mark Sector Points
    s1_px = (int(res_x[s1_idx]), int(res_y[s1_idx]))
    s2_px = (int(res_x[s2_idx]), int(res_y[s2_idx]))
    cv2.circle(overlay, s1_px, 8, (255, 0, 255), -1)
    cv2.putText(overlay, "S1/S2 SPLIT", (s1_px[0] - 40, s1_px[1] + 25), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 0, 255), 2)
    cv2.circle(overlay, s2_px, 8, (255, 0, 255), -1)
    cv2.putText(overlay, "S2/S3 SPLIT", (s2_px[0] + 15, s2_px[1] + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 0, 255), 2)

    # 5. Mark Corner Points T1..T15
    for c in corners:
        c_idx = int(np.round(c["apex_s"] / 2.0)) % num_samples
        c_px = (int(res_x[c_idx]), int(res_y[c_idx]))
        cv2.circle(overlay, c_px, 6, (0, 165, 255), -1)
        # Offset text towards label
        dx = c["label_x_px"] - c_px[0]
        dy = c["label_y_px"] - c_px[1]
        dist = np.hypot(dx, dy)
        tx = int(c_px[0] + (dx / dist) * 22)
        ty = int(c_px[1] + (dy / dist) * 22)
        cv2.putText(overlay, c["name"], (tx - 10, ty + 5), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 200, 255), 2)

    overlay_path = 'Docs/track/centerline_overlay.png'
    cv2.imwrite(overlay_path, overlay)
    print(f"Saved clear overlay to {overlay_path}")

if __name__ == '__main__':
    extract_track()
