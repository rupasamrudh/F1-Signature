import json
import struct
import os
import shutil
import numpy as np
from scipy.spatial import cKDTree
from scipy.interpolate import splprep, splev

def run():
    src_glb = 'Assets/Models/Track/Interlagos/source/interlagos.glb'
    out_dir = 'Assets/Generated/Track/InterlagosData'
    mesh_dir = 'Assets/Generated/Track/InterlagosMeshes'
    prefab_path = 'Assets/Generated/Track/Track.prefab'

    print(f"Reading {src_glb}...")
    with open(src_glb, 'rb') as f:
        f.read(12)
        chunk_len, chunk_type = struct.unpack('<II', f.read(8))
        gltf = json.loads(f.read(chunk_len).decode('utf-8'))
        bin_len, bin_type = struct.unpack('<II', f.read(8))
        bin_data = f.read(bin_len)

    # Clean previous generated data
    if os.path.exists(out_dir):
        shutil.rmtree(out_dir)
    os.makedirs(out_dir, exist_ok=True)

    if os.path.exists(mesh_dir):
        shutil.rmtree(mesh_dir)
    os.makedirs(mesh_dir, exist_ok=True)

    if os.path.exists(prefab_path):
        os.remove(prefab_path)
    if os.path.exists(prefab_path + '.meta'):
        os.remove(prefab_path + '.meta')

    meshes = gltf['meshes']
    materials = gltf['materials']
    accessors = gltf['accessors']
    bufferViews = gltf['bufferViews']
    prims = meshes[0]['primitives']

    # Exact centering offset based on start/finish grid (roadsf.004 at Z=0)
    # Mirrored lateral track coordinates:
    # ux = -(rx - X0)
    # uy = (-rz) - Y0
    # uz = ry - Z0
    X0 = -483.20
    Y0 = 10.75
    Z0 = 0.00

    city_keywords = [
        'house', 'flat', 'outerbuild', 'bgbuild', 'city', 'predio', 'redbuild', 
        'prhouse', 't56_build', 'watertower', 'paddock_light', 'floodlight_lamp',
        'material_#5258', 'material_#5163', 'outer', 'pr_brickwall', 'vidro_predio'
    ]

    road_keywords = [
        'roada', 'roadp', 'roadsf', 'roadd', 'roadseam', 'rdil', 'asphalt', 'xsec', 'marbles', 
        'rdln', 'creteg', 'groove', 'concdrain', 'conc_gfloor', 'cement_no_ao', 'pitwlfloor'
    ]

    kerb_keywords = ['rmbl', 'kerb', 'curb', 'rmbg']

    barrier_keywords = [
        'wall', 'armco', 'fence', 'tyre', 'guard', 'silverbars', 'concw', 'pwall'
    ]

    terrain_keywords = [
        'grass', 'inerfield', 'lake', 'terrain', 'land', 'verge', 'tapete'
    ]

    categories = {
        'Track_Road': [],
        'Track_Kerbs': [],
        'Track_Barriers': [],
        'Track_Terrain': [],
        'Track_Trackside': [],
        'Track_Vegetation': []
    }

    excluded_city_count = 0
    excluded_city_verts = 0

    for i, p in enumerate(prims):
        mat_idx = p.get('material', -1)
        mat_name = materials[mat_idx].get('name', f'mat_{i}') if mat_idx >= 0 else f'mat_{i}'
        mlow = mat_name.lower()
        cnt = accessors[p['attributes']['POSITION']]['count']

        # Exclude city primitives completely
        if any(k in mlow for k in city_keywords):
            excluded_city_count += 1
            excluded_city_verts += cnt
            continue

        if any(k in mlow for k in terrain_keywords):
            cat = 'Track_Terrain'
        elif any(k in mlow for k in kerb_keywords):
            cat = 'Track_Kerbs'
        elif any(k in mlow for k in road_keywords):
            cat = 'Track_Road'
        elif any(k in mlow for k in barrier_keywords):
            cat = 'Track_Barriers'
        elif any(k in mlow for k in ['tree', 'bush', 'arvore']):
            cat = 'Track_Vegetation'
        else:
            cat = 'Track_Trackside'

        categories[cat].append((i, p, mat_name))

    print(f"Excluded {excluded_city_count} city primitives ({excluded_city_verts} vertices).")

    # Process each category
    for cat_name, prim_list in categories.items():
        if len(prim_list) == 0:
            continue
        print(f"Processing {cat_name} ({len(prim_list)} primitives)...")
        cat_verts = []
        submeshes = []
        vert_offset = 0

        for p_idx, p, mat_name in prim_list:
            pos_acc = accessors[p['attributes']['POSITION']]
            norm_acc = accessors[p['attributes'].get('NORMAL')] if 'NORMAL' in p['attributes'] else None
            uv_acc = accessors[p['attributes'].get('TEXCOORD_0')] if 'TEXCOORD_0' in p['attributes'] else None
            idx_acc = accessors[p['indices']]

            # Vertices
            bv_pos = bufferViews[pos_acc['bufferView']]
            pos_offset = bv_pos.get('byteOffset', 0) + pos_acc.get('byteOffset', 0)
            cnt = pos_acc['count']

            bv_norm = bufferViews[norm_acc['bufferView']] if norm_acc else None
            norm_offset = (bv_norm.get('byteOffset', 0) + norm_acc.get('byteOffset', 0)) if norm_acc else None

            bv_uv = bufferViews[uv_acc['bufferView']] if uv_acc else None
            uv_offset = (bv_uv.get('byteOffset', 0) + uv_acc.get('byteOffset', 0)) if uv_acc else None

            for v in range(cnt):
                rx, ry, rz = struct.unpack_from('<fff', bin_data, pos_offset + v * 12)
                # Mirrored track:
                ux = -(rx - X0)
                uy = (-rz) - Y0
                uz = ry - Z0

                if norm_offset is not None:
                    rnx, rny, rnz = struct.unpack_from('<fff', bin_data, norm_offset + v * 12)
                    unx = -rnx
                    uny = -rnz
                    unz = rny
                else:
                    unx, uny, unz = 0.0, 1.0, 0.0

                if uv_offset is not None:
                    ru, rv = struct.unpack_from('<ff', bin_data, uv_offset + v * 8)
                    uu, uv = ru, 1.0 - rv
                else:
                    uu, uv = 0.0, 0.0

                cat_verts.append((ux, uy, uz, unx, uny, unz, uu, uv))

            # Indices: reversed winding to keep front-facing polygons with negated X
            bv_idx = bufferViews[idx_acc['bufferView']]
            idx_offset = bv_idx.get('byteOffset', 0) + idx_acc.get('byteOffset', 0)
            idx_cnt = idx_acc['count']
            idx_comp = idx_acc['componentType']

            raw_indices = []
            if idx_comp == 5123:
                for j in range(idx_cnt):
                    raw_indices.append(struct.unpack_from('<H', bin_data, idx_offset + j * 2)[0])
            elif idx_comp == 5125:
                for j in range(idx_cnt):
                    raw_indices.append(struct.unpack_from('<I', bin_data, idx_offset + j * 4)[0])

            indices = []
            for t in range(0, idx_cnt, 3):
                indices.append(vert_offset + raw_indices[t])
                indices.append(vert_offset + raw_indices[t + 2])
                indices.append(vert_offset + raw_indices[t + 1])

            submeshes.append((mat_name, indices))
            vert_offset += cnt

        # Write binary file
        bin_path = os.path.join(out_dir, f"{cat_name}.bin")
        print(f"Writing {bin_path}: {len(cat_verts)} verts, {len(submeshes)} submeshes...")
        with open(bin_path, 'wb') as out_f:
            out_f.write(struct.pack('<III', 0x46315452, len(cat_verts), len(submeshes)))
            for v in cat_verts:
                out_f.write(struct.pack('<ffffffff', *v))
            for mat_name, indices in submeshes:
                mat_bytes = mat_name.encode('utf-8')
                out_f.write(struct.pack('<I', len(mat_bytes)))
                out_f.write(mat_bytes)
                out_f.write(struct.pack('<I', len(indices)))
                for idx in indices:
                    out_f.write(struct.pack('<I', idx))

    print("\nExtracting mirrored smooth track centerline centered at (0, 0, 0)...")
    # Read Track_Road to trace centerline
    with open(os.path.join(out_dir, "Track_Road.bin"), 'rb') as f:
        magic, v_cnt, sm_cnt = struct.unpack('<III', f.read(12))
        road_verts = []
        for _ in range(v_cnt):
            ux, uy, uz, unx, uny, unz, uu, uv = struct.unpack('<ffffffff', f.read(32))
            road_verts.append((ux, uy, uz))
        road_verts = np.array(road_verts)

        road_tris = []
        for s in range(sm_cnt):
            name_len = struct.unpack('<I', f.read(4))[0]
            name = f.read(name_len).decode('utf-8')
            idx_cnt = struct.unpack('<I', f.read(4))[0]
            indices = struct.unpack(f'<{idx_cnt}I', f.read(idx_cnt * 4))
            if any(r in name for r in ['roada', 'roadsf']) and 'pit' not in name:
                for t in range(0, idx_cnt, 3):
                    road_tris.append((indices[t], indices[t+1], indices[t+2]))

    road_tris = np.array(road_tris)
    road_pts = road_verts[np.unique(road_tris)]
    tree = cKDTree(road_pts[:, [0, 2]])

    # Start line is at (0, 0, 0)
    curr_pos = np.array([0.0, 0.0, 0.0])
    curr_fwd = np.array([0.0, 0.0, 1.0])

    raw_centerline = [curr_pos.copy()]
    ds = 2.0

    for step in range(3000):
        pred = curr_pos + curr_fwd * ds
        idxs = tree.query_ball_point([pred[0], pred[2]], r=10.0)
        if len(idxs) < 5: break
        pts = road_pts[idxs]
        diffs = pts - pred
        dot_fwd = diffs[:, 0] * curr_fwd[0] + diffs[:, 2] * curr_fwd[2]
        slab = np.abs(dot_fwd) < 1.0
        slab_pts = pts[slab] if np.sum(slab) >= 3 else pts
        new_pos = slab_pts.mean(axis=0)

        d_vec = new_pos - curr_pos
        d_len = np.linalg.norm(d_vec[[0, 2]])
        if d_len > 0.1:
            new_fwd = np.array([d_vec[0] / d_len, 0.0, d_vec[2] / d_len])
            curr_fwd = 0.8 * curr_fwd + 0.2 * new_fwd
            curr_fwd = curr_fwd / np.linalg.norm(curr_fwd[[0, 2]])

        curr_pos = new_pos
        raw_centerline.append(curr_pos.copy())

        if step > 1000:
            dist_to_start = np.linalg.norm(curr_pos[[0, 2]] - np.array([0.0, 0.0]))
            if dist_to_start < 5.0 and curr_fwd[2] > 0.8:
                break

    raw_cl = np.array(raw_centerline)
    raw_cl[-1] = raw_cl[0]

    tck, u = splprep([raw_cl[:, 0], raw_cl[:, 1], raw_cl[:, 2]], s=50.0, per=True)
    u_fine = np.linspace(0, 1, 5000)
    x_f, y_f, z_f = splev(u_fine, tck)
    dists = np.sqrt(np.diff(x_f)**2 + np.diff(y_f)**2 + np.diff(z_f)**2)
    total_len = dists.sum()

    cum_dists = np.insert(np.cumsum(dists), 0, 0.0)
    target_s = np.arange(0, total_len, 2.0)
    u_uniform = np.interp(target_s, cum_dists, u_fine)
    x_u, y_u, z_u = splev(u_uniform, tck)
    dx_u, dy_u, dz_u = splev(u_uniform, tck, der=1)
    ddx_u, ddy_u, ddz_u = splev(u_uniform, tck, der=2)

    print("Computing exact physical barrier envelope from Track_Barriers and Track_Trackside...")
    # Load barrier vertices
    all_barrier_pts = []
    for b_name in ["Track_Barriers.bin", "Track_Trackside.bin"]:
        p_path = os.path.join(out_dir, b_name)
        if os.path.exists(p_path):
            with open(p_path, 'rb') as bf:
                b_magic, b_vcnt, b_smcnt = struct.unpack('<III', bf.read(12))
                for _ in range(b_vcnt):
                    b_ux, b_uy, b_uz, b_unx, b_uny, b_unz, b_uu, b_uv = struct.unpack('<ffffffff', bf.read(32))
                    all_barrier_pts.append((b_ux, b_uy, b_uz))
    all_barrier_pts = np.array(all_barrier_pts)
    b_tree = cKDTree(all_barrier_pts[:, [0, 2]])

    raw_left = []
    raw_right = []
    for i in range(len(target_s)):
        cx, cz, cy = float(x_u[i]), float(z_u[i]), float(y_u[i])
        dx, dz = dx_u[i], dz_u[i]
        speed = np.sqrt(dx**2 + dz**2)
        fx, fz = dx / (speed + 1e-9), dz / (speed + 1e-9)
        # Left normal: (-fz, fx)
        lx, lz = -fz, fx

        idxs = b_tree.query_ball_point([cx, cz], r=45.0)
        if len(idxs) == 0:
            raw_left.append(30.0)
            raw_right.append(30.0)
            continue

        pts = all_barrier_pts[idxs]
        y_diff = np.abs(pts[:, 1] - cy)
        valid_y = y_diff < 3.5
        pts = pts[valid_y]

        diffs = pts[:, [0, 2]] - np.array([cx, cz])
        dots_f = diffs[:, 0] * fx + diffs[:, 1] * fz
        slab = np.abs(dots_f) < 2.5
        slab_pts = diffs[slab]

        lat_offsets = slab_pts[:, 0] * lx + slab_pts[:, 1] * lz
        pos_l = lat_offsets[lat_offsets > 2.0]
        pos_r = -lat_offsets[lat_offsets < -2.0]

        min_l = float(pos_l.min()) if len(pos_l) > 0 else 30.0
        min_r = float(pos_r.min()) if len(pos_r) > 0 else 30.0
        raw_left.append(min_l)
        raw_right.append(min_r)

    raw_left = np.array(raw_left)
    raw_right = np.array(raw_right)
    # Clamp to track half-width + 0.5m buffer (track width is 14m, half-width is 7.0m)
    raw_left = np.maximum(raw_left, 7.5)
    raw_right = np.maximum(raw_right, 7.5)

    samples = []
    for i in range(len(target_s)):
        s = float(target_s[i])
        x = float(x_u[i])
        y = float(y_u[i])
        z = float(z_u[i])

        dx, dz = dx_u[i], dz_u[i]
        ddx, ddz = ddx_u[i], ddz_u[i]
        speed = np.sqrt(dx**2 + dz**2)
        curv = (dx * ddz - dz * ddx) / (speed**3 + 1e-9)
        radius = 1.0 / (abs(curv) + 1e-9)
        pitch = float(np.arctan2(dy_u[i], speed) * 180.0 / np.pi)

        px_x = float(50 + (x - x_u.min()) / (x_u.max() - x_u.min()) * 700)
        px_y = float(50 + (z - z_u.min()) / (z_u.max() - z_u.min()) * 700)

        samples.append({
            'index': i,
            's': round(s, 2),
            'x': round(x, 2),
            'y': round(y, 2),
            'z': round(z, 2),
            'width': 14.0,
            'banking_deg': 0.0,
            'pitch_deg': round(pitch, 2),
            'curvature': round(float(curv), 6),
            'radius_m': round(float(radius), 1),
            'px_x': round(px_x, 1),
            'px_y': round(px_y, 1),
            'barrier_left': round(float(raw_left[i]), 2),
            'barrier_right': round(float(raw_right[i]), 2)
        })

    track_json = {
        'version': 1,
        'name': 'Autodromo Jose Carlos Pace (Interlagos) [Mirrored]',
        'total_length_m': round(float(total_len), 1),
        'sample_spacing_m': 2.0,
        'scale_m_per_px': 1.0,
        'airDensity': 1.10,
        'default_width_m': 14.0,
        'spawn_slot': {
            's': 0.0,
            'x': 0.0,
            'y': 0.0,
            'z': 0.0,
            'yaw_deg': 0.0
        },
        'sectors': [
            {'sector': 1, 'start_s': 0.0, 'end_s': round(float(total_len * 0.33), 1), 'length_m': round(float(total_len * 0.33), 1)},
            {'sector': 2, 'start_s': round(float(total_len * 0.33), 1), 'end_s': round(float(total_len * 0.69), 1), 'length_m': round(float(total_len * 0.36), 1)},
            {'sector': 3, 'start_s': round(float(total_len * 0.69), 1), 'end_s': round(float(total_len), 1), 'length_m': round(float(total_len * 0.31), 1)}
        ],
        'straight_mode_zones': [
            {'name': 'Reta dos Boxes', 'start_s': round(float(total_len - 300), 1), 'end_s': round(float(total_len), 1), 'length_m': 300.0},
            {'name': 'Reta Oposta', 'start_s': 800.0, 'end_s': 1450.0, 'length_m': 650.0}
        ],
        'corners': [],
        'samples': samples
    }

    with open('Assets/Data/Tracks/f1sig_track.json', 'w') as f:
        json.dump(track_json, f, indent=2)

    with open('Assets/Resources/Tracks/f1sig_track.json', 'w') as f:
        json.dump(track_json, f, indent=2)

    print(f"Mirrored track centered at (0, 0, 0) with length {total_len:.1f}m. f1sig_track.json written!")

if __name__ == '__main__':
    run()
