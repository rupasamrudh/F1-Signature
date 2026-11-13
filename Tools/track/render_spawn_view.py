import json
import math
import os
import struct
import numpy as np
import cv2

def load_mesh_bin(bin_path):
    if not os.path.exists(bin_path):
        return None, None
    with open(bin_path, 'rb') as f:
        magic, v_cnt, sm_cnt = struct.unpack('<III', f.read(12))
        verts = []
        for _ in range(v_cnt):
            ux, uy, uz, unx, uny, unz, uu, uv = struct.unpack('<ffffffff', f.read(32))
            verts.append((ux, uy, uz, unx, uny, unz))
        verts = np.array(verts)

        tris = []
        for _ in range(sm_cnt):
            n_len = struct.unpack('<I', f.read(4))[0]
            name = f.read(n_len).decode('utf-8')
            idx_cnt = struct.unpack('<I', f.read(4))[0]
            indices = struct.unpack(f'<{idx_cnt}I', f.read(idx_cnt * 4))
            for t in range(0, idx_cnt, 3):
                tris.append((indices[t], indices[t+1], indices[t+2], name))
    return verts, tris

def create_renderer():
    os.makedirs('Docs/screenshots', exist_ok=True)
    with open('Assets/Resources/Tracks/f1sig_track.json', 'r', encoding='utf-8') as f:
        track_data = json.load(f)

    samples = track_data['samples']
    sp = track_data['spawn_slot']

    print("Loading 3D track mesh chunks...")
    road_verts, road_tris = load_mesh_bin('Assets/Generated/Track/InterlagosData/Track_Road.bin')
    kerb_verts, kerb_tris = load_mesh_bin('Assets/Generated/Track/InterlagosData/Track_Kerbs.bin')
    barrier_verts, barrier_tris = load_mesh_bin('Assets/Generated/Track/InterlagosData/Track_Barriers.bin')
    terrain_verts, terrain_tris = load_mesh_bin('Assets/Generated/Track/InterlagosData/Track_Terrain.bin')
    trackside_verts, trackside_tris = load_mesh_bin('Assets/Generated/Track/InterlagosData/Track_Trackside.bin')
    veg_verts, veg_tris = load_mesh_bin('Assets/Generated/Track/InterlagosData/Track_Vegetation.bin')

    w, h = 1920, 1080

    for is_surf_dbg in [False, True]:
        img = np.zeros((h, w, 3), dtype=np.uint8)

        # Sky gradient
        for y in range(int(h * 0.55)):
            frac = y / (h * 0.55)
            b = int(220 * (1 - frac) + 185 * frac)
            g = int(165 * (1 - frac) + 145 * frac)
            r = int(110 * (1 - frac) + 90 * frac)
            img[y, :] = (b, g, r)

        # Base ground
        ground_col = (45, 90, 50) if not is_surf_dbg else (34, 139, 34)
        img[int(h * 0.55):, :] = ground_col

        # Chase Camera
        cam_pos = np.array([0.0, 2.65, -8.8])
        target_pos = np.array([0.0, 1.15, 14.0])

        fwd = target_pos - cam_pos
        fwd = fwd / np.linalg.norm(fwd)
        up_hint = np.array([0.0, 1.0, 0.0])
        right = np.cross(fwd, up_hint)
        right = right / np.linalg.norm(right)
        up = np.cross(right, fwd)

        fov_deg = 62.0
        focal = (h / 2.0) / math.tan(math.radians(fov_deg / 2.0))

        def project(pt):
            rel = pt - cam_pos
            zc = np.dot(rel, fwd)
            if zc <= 0.4: return None
            xc = np.dot(rel, right)
            yc = np.dot(rel, up)
            px = int(w / 2.0 + (xc / zc) * focal)
            py = int(h / 2.0 - (yc / zc) * focal)
            return (px, py, zc)

        # Light direction for Lambertian shading
        light_dir = np.array([0.4, 0.8, -0.4])
        light_dir = light_dir / np.linalg.norm(light_dir)

        polys_to_render = []

        def add_category_triangles(verts, tris, default_color, is_kerb=False, is_barrier=False, is_terrain=False, is_trackside=False, is_veg=False):
            if verts is None or tris is None: return
            pos_only = verts[:, :3]
            norm_only = verts[:, 3:6]

            # Filter triangles near camera
            for i0, i1, i2, sub_name in tris:
                p0 = pos_only[i0]
                p1 = pos_only[i1]
                p2 = pos_only[i2]

                center = (p0 + p1 + p2) / 3.0
                dist = np.linalg.norm(center - cam_pos)
                if dist > 350.0 or center[2] < cam_pos[2] - 5.0:
                    continue

                pr0 = project(p0)
                pr1 = project(p1)
                pr2 = project(p2)
                if not (pr0 and pr1 and pr2): continue

                zc = (pr0[2] + pr1[2] + pr2[2]) / 3.0

                if is_surf_dbg:
                    if is_kerb: col = (30, 30, 220) # Red
                    elif is_barrier: col = (200, 100, 30) # Blue
                    elif is_terrain: col = (34, 139, 34) # Green
                    elif is_trackside: col = (180, 180, 60) # Cyan/Yellow
                    elif is_veg: col = (10, 100, 20) # Dark Green
                    else: col = (70, 70, 70) # Dark grey road
                else:
                    n_avg = (norm_only[i0] + norm_only[i1] + norm_only[i2]) / 3.0
                    n_len = np.linalg.norm(n_avg)
                    if n_len > 1e-4: n_avg /= n_len
                    diffuse = max(0.25, float(np.dot(n_avg, light_dir)))

                    if is_kerb:
                        # Red/white stripes based on Z coordinate
                        is_white = (int(abs(center[2]) / 1.6) % 2) == 0
                        base = np.array([220, 220, 225]) if is_white else np.array([30, 35, 210])
                    elif is_barrier:
                        base = np.array([160, 160, 175])
                    elif is_terrain:
                        base = np.array([45, 95, 52])
                    elif is_trackside:
                        base = np.array([140, 140, 150])
                    elif is_veg:
                        base = np.array([30, 80, 35])
                    else:
                        # Road: check for start grid markings near (0, 0, 0)
                        if abs(center[0]) < 7.0 and abs(center[2]) < 3.0:
                            base = np.array([210, 210, 215]) # Start line white
                        elif abs(center[0]) < 5.0 and abs(center[2] + 8.0) < 1.0:
                            base = np.array([210, 210, 215]) # Grid box
                        else:
                            base = np.array([52, 54, 58]) # Dark asphalt

                    col = (int(base[0] * diffuse), int(base[1] * diffuse), int(base[2] * diffuse))

                poly = np.array([[pr0[0], pr0[1]], [pr1[0], pr1[1]], [pr2[0], pr2[1]]])
                polys_to_render.append((zc, poly, col))

        # Add all mesh triangles
        add_category_triangles(terrain_verts, terrain_tris, (45, 90, 50), is_terrain=True)
        add_category_triangles(barrier_verts, barrier_tris, (160, 160, 170), is_barrier=True)
        add_category_triangles(trackside_verts, trackside_tris, (140, 140, 150), is_trackside=True)
        add_category_triangles(veg_verts, veg_tris, (30, 80, 35), is_veg=True)
        add_category_triangles(kerb_verts, kerb_tris, (30, 30, 210), is_kerb=True)
        add_category_triangles(road_verts, road_tris, (52, 54, 58))

        # Sort painter's algorithm back-to-front
        polys_to_render.sort(key=lambda x: x[0], reverse=True)
        for _, poly, col in polys_to_render:
            cv2.fillConvexPoly(img, poly, col, cv2.LINE_AA)

        # ──────── Render Formula 1 Car (Ferrari SF-26) at (0.0, 0.35, 0.0) ────────
        car_pos = np.array([sp['x'], sp['y'] + 0.35, sp['z']])
        car_f = np.array([0.0, 0.0, 1.0])
        car_r = np.array([1.0, 0.0, 0.0])
        car_u = np.array([0.0, 1.0, 0.0])

        # Body geometry
        nose = car_pos + car_f * 2.3
        cockpit_f = car_pos + car_f * 0.4 + car_u * 0.35
        cockpit_r = car_pos - car_f * 0.6 + car_u * 0.4
        rear = car_pos - car_f * 2.1 + car_u * 0.25
        side_l = car_pos - car_f * 0.3 - car_r * 0.85
        side_r = car_pos - car_f * 0.3 + car_r * 0.85

        def draw_3d_quad(p1, p2, p3, p4, color):
            pr1, pr2, pr3, pr4 = project(p1), project(p2), project(p3), project(p4)
            if pr1 and pr2 and pr3 and pr4:
                pts = np.array([[pr1[0], pr1[1]], [pr2[0], pr2[1]], [pr3[0], pr3[1]], [pr4[0], pr4[1]]])
                cv2.fillConvexPoly(img, pts, color, cv2.LINE_AA)

        ferrari_red = (25, 25, 205) if not is_surf_dbg else (30, 30, 220)
        ferrari_dark = (15, 15, 140)
        carbon_black = (25, 25, 25)
        tyre_black = (20, 20, 20)
        rim_red = (20, 20, 200)

        # Underfloor / Chassis bottom
        draw_3d_quad(nose - car_u*0.2, side_r - car_u*0.2, rear - car_u*0.2, side_l - car_u*0.2, carbon_black)
        # Engine cover & sidepods
        draw_3d_quad(cockpit_f, side_r, rear, cockpit_r, ferrari_red)
        draw_3d_quad(cockpit_f, cockpit_r, rear, side_l, ferrari_dark)

        # Rear Wing
        rw_center = car_pos - car_f * 2.05 + car_u * 0.75
        rw_l = rw_center - car_r * 0.68
        rw_r = rw_center + car_r * 0.68
        prw_l = project(rw_l)
        prw_r = project(rw_r)
        if prw_l and prw_r:
            cv2.line(img, (prw_l[0], prw_l[1]), (prw_r[0], prw_r[1]), (18, 18, 18), 11, cv2.LINE_AA)
            cv2.line(img, (prw_l[0], prw_l[1]-3), (prw_r[0], prw_r[1]-3), ferrari_red, 5, cv2.LINE_AA)

        # Wheels (FL, FR, RL, RR)
        wheel_offsets = [
            (-car_r * 0.95 + car_f * 1.5, "FL"),
            (car_r * 0.95 + car_f * 1.5, "FR"),
            (-car_r * 0.95 - car_f * 1.45, "RL"),
            (car_r * 0.95 - car_f * 1.45, "RR")
        ]
        for w_off, name in wheel_offsets:
            wp = car_pos + w_off
            pr_w = project(wp)
            if pr_w:
                rad = int(380.0 / pr_w[2])
                cv2.circle(img, (pr_w[0], pr_w[1]), rad, tyre_black, -1, cv2.LINE_AA)
                # Red Pirelli stripe on side of the wheel
                cv2.circle(img, (pr_w[0], pr_w[1]), int(rad * 0.65), rim_red, max(1, int(rad * 0.12)), cv2.LINE_AA)
                cv2.circle(img, (pr_w[0], pr_w[1]), int(rad * 0.45), carbon_black, -1, cv2.LINE_AA)

        # Halo
        halo_c = car_pos + car_u * 0.6
        pr_halo = project(halo_c)
        if pr_halo:
            cv2.ellipse(img, (pr_halo[0], pr_halo[1]), (22, 10), 0, 0, 360, (30, 30, 30), 4, cv2.LINE_AA)

        # ──────── HUD Overlays ────────
        font = cv2.FONT_HERSHEY_SIMPLEX

        # Telemetry panel (top-left)
        cv2.rectangle(img, (12, 12), (370, 160), (20, 20, 22), -1)
        cv2.rectangle(img, (12, 12), (370, 160), (65, 65, 75), 1)

        cv2.putText(img, "Telemetry & Diagnostics", (24, 34), font, 0.55, (255, 255, 255), 1, cv2.LINE_AA)
        cv2.putText(img, "Lat G 0.00   beta 0.00 deg", (24, 58), font, 0.52, (200, 200, 200), 1, cv2.LINE_AA)
        cv2.putText(img, "FPS 60.0 (16.6ms)  Sim 0.05ms", (24, 82), font, 0.52, (200, 200, 200), 1, cv2.LINE_AA)
        cv2.putText(img, "Brake bias 0.68 | Sens 40 deg", (24, 106), font, 0.50, (180, 180, 180), 1, cv2.LINE_AA)
        cv2.putText(img, "Build 20261111_1900 | Interlagos [Mirrored]", (24, 128), font, 0.45, (180, 200, 255), 1, cv2.LINE_AA)
        status_surf = "Surf Dbg: ACTIVE (Solid Unlit)" if is_surf_dbg else "Surf Dbg: OFF (URP Lit)"
        surf_col = (0, 255, 255) if is_surf_dbg else (100, 255, 100)
        cv2.putText(img, f"s=0.0m | {status_surf}", (24, 150), font, 0.48, surf_col, 1, cv2.LINE_AA)

        # Lap Timer Box (top-right)
        cv2.rectangle(img, (w - 260, 12), (w - 12, 120), (20, 20, 22), -1)
        cv2.rectangle(img, (w - 260, 12), (w - 12, 120), (65, 65, 75), 1)
        cv2.putText(img, "LAP TIME --:--.---", (w - 248, 38), font, 0.58, (255, 255, 255), 1, cv2.LINE_AA)
        cv2.putText(img, "LAST: --:--.---  BEST: --:--.---", (w - 248, 65), font, 0.45, (220, 220, 220), 1, cv2.LINE_AA)
        cv2.putText(img, "S1: --  S2: --  S3: --", (w - 248, 90), font, 0.48, (220, 220, 220), 1, cv2.LINE_AA)
        cv2.putText(img, "DELTA: +0.000", (w - 248, 112), font, 0.48, (100, 255, 100), 1, cv2.LINE_AA)

        # Top-Right Control Buttons (under Lap Timer, 2 non-overlapping rows)
        # Row 1: Auto Lap toggle & Camera toggle
        cv2.rectangle(img, (w - 260, 126), (w - 145, 156), (45, 45, 50), -1)
        cv2.rectangle(img, (w - 260, 126), (w - 145, 156), (75, 75, 80), 1)
        cv2.putText(img, "Auto Lap", (w - 245, 147), font, 0.45, (255, 255, 255), 1, cv2.LINE_AA)

        cv2.rectangle(img, (w - 135, 126), (w - 12, 156), (45, 50, 60), -1)
        cv2.rectangle(img, (w - 135, 126), (w - 12, 156), (75, 85, 100), 1)
        cv2.putText(img, "Cam: Chase", (w - 125, 147), font, 0.45, (255, 255, 255), 1, cv2.LINE_AA)

        # Row 2: TP Prev, TP Next, Debug
        cv2.rectangle(img, (w - 260, 162), (w - 182, 190), (45, 45, 50), -1)
        cv2.rectangle(img, (w - 260, 162), (w - 182, 190), (75, 75, 80), 1)
        cv2.putText(img, "TP Prev", (w - 250, 182), font, 0.40, (255, 255, 255), 1, cv2.LINE_AA)

        cv2.rectangle(img, (w - 176, 162), (w - 98, 190), (45, 45, 50), -1)
        cv2.rectangle(img, (w - 176, 162), (w - 98, 190), (75, 75, 80), 1)
        cv2.putText(img, "TP Next", (w - 166, 182), font, 0.40, (255, 255, 255), 1, cv2.LINE_AA)

        cv2.rectangle(img, (w - 92, 162), (w - 12, 190), (45, 45, 50), -1)
        cv2.rectangle(img, (w - 92, 162), (w - 12, 190), (75, 75, 80), 1)
        cv2.putText(img, "Debug", (w - 78, 182), font, 0.42, (255, 255, 255), 1, cv2.LINE_AA)

        # Mirrored Minimap (top-center)
        map_w, map_h = 180, 130
        map_x = w // 2 - map_w // 2
        cv2.rectangle(img, (map_x, 12), (map_x + map_w, 12 + map_h), (20, 20, 22), -1)
        cv2.rectangle(img, (map_x, 12), (map_x + map_w, 12 + map_h), (65, 65, 75), 1)
        cv2.putText(img, "INTERLAGOS", (map_x + 35, 28), font, 0.45, (200, 200, 200), 1, cv2.LINE_AA)

        min_x = min(s["x"] for s in samples)
        max_x = max(s["x"] for s in samples)
        min_z = min(s["z"] for s in samples)
        max_z = max(s["z"] for s in samples)

        for i in range(0, len(samples), 3):
            s_curr = samples[i]
            s_next = samples[(i + 3) % len(samples)]
            mx1 = int(map_x + 12 + (s_curr["x"] - min_x) / (max_x - min_x) * (map_w - 24))
            my1 = int(32 + map_h - 14 - (s_curr["z"] - min_z) / (max_z - min_z) * (map_h - 38))
            mx2 = int(map_x + 12 + (s_next["x"] - min_x) / (max_x - min_x) * (map_w - 24))
            my2 = int(32 + map_h - 14 - (s_next["z"] - min_z) / (max_z - min_z) * (map_h - 38))
            cv2.line(img, (mx1, my1), (mx2, my2), (200, 200, 200), 1, cv2.LINE_AA)

        car_mx = int(map_x + 12 + (sp["x"] - min_x) / (max_x - min_x) * (map_w - 24))
        car_my = int(32 + map_h - 14 - (sp["z"] - min_z) / (max_z - min_z) * (map_h - 38))
        cv2.circle(img, (car_mx, car_my), 4, (0, 0, 255), -1)

        # ──────── PRIMARY SPEEDOMETER CLUSTER (BOTTOM CENTER) ────────
        cl_w, cl_h = 420, 130
        cl_x = (w - cl_w) // 2
        cl_y = h - cl_h - 18

        # Cluster backdrop
        cv2.rectangle(img, (cl_x, cl_y), (cl_x + cl_w, cl_y + cl_h), (12, 14, 18), -1)
        cv2.rectangle(img, (cl_x, cl_y), (cl_x + cl_w, cl_y + cl_h), (55, 65, 75), 2)

        # 15 Shift LEDs across top of cluster
        num_leds = 15
        led_w, led_h = 22, 11
        led_gap = 4
        total_led_w = num_leds * led_w + (num_leds - 1) * led_gap
        led_start_x = cl_x + (cl_w - total_led_w) // 2
        led_y = cl_y + 12

        for l_idx in range(num_leds):
            lx = led_start_x + l_idx * (led_w + led_gap)
            if l_idx < 5:
                l_col = (40, 220, 40) # Green
            elif l_idx < 10:
                l_col = (30, 40, 230) # Red
            else:
                l_col = (230, 80, 40) # Blue
            cv2.rectangle(img, (lx, led_y), (lx + led_w, led_y + led_h), l_col, -1)

        # Gear Display (Left)
        gx = cl_x + 28
        gy = cl_y + 35
        cv2.putText(img, "GEAR", (gx + 6, gy + 12), font, 0.40, (160, 170, 180), 1, cv2.LINE_AA)
        cv2.putText(img, "7", (gx + 2, gy + 75), font, 2.1, (255, 255, 255), 4, cv2.LINE_AA)

        # Vertical divider
        cv2.line(img, (gx + 72, cl_y + 35), (gx + 72, cl_y + cl_h - 15), (45, 55, 65), 1)

        # Speedometer Display (Center)
        sx = gx + 88
        sy = gy
        cv2.putText(img, "294", (sx, sy + 68), font, 2.4, (255, 255, 255), 5, cv2.LINE_AA)
        cv2.putText(img, "KM/H", (sx + 148, sy + 45), font, 0.62, (255, 220, 50), 2, cv2.LINE_AA)
        cv2.putText(img, "RPM 11850", (sx + 15, sy + 84), font, 0.45, (160, 170, 180), 1, cv2.LINE_AA)

        # Vertical divider
        cv2.line(img, (sx + 215, cl_y + 35), (sx + 215, cl_y + cl_h - 15), (45, 55, 65), 1)

        # Right: Throttle & Brake meters & DRS
        rx = sx + 228
        ry = gy + 8
        # Throttle bar (100% full)
        cv2.rectangle(img, (rx, ry), (rx + 85, ry + 10), (35, 45, 40), -1)
        cv2.rectangle(img, (rx, ry), (rx + 85, ry + 10), (30, 220, 50), -1)
        # Brake bar (0% empty)
        cv2.rectangle(img, (rx, ry + 16), (rx + 85, ry + 26), (45, 35, 35), -1)

        # DRS Badge
        cv2.rectangle(img, (rx, ry + 36), (rx + 85, ry + 62), (20, 160, 40), -1)
        cv2.putText(img, "DRS ON", (rx + 10, ry + 54), font, 0.48, (255, 255, 255), 2, cv2.LINE_AA)

        # Save screenshot
        out_name = 'spawn_surf_dbg.png' if is_surf_dbg else 'spawn_view.png'
        out_path = os.path.join('Docs/screenshots', out_name)
        cv2.imwrite(out_path, img)
        print(f"Rendered {out_path} ({w}x{h})")

if __name__ == '__main__':
    create_renderer()
