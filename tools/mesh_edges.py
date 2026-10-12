"""Python port of McLayer.Build, rasterised back into the picture to check it against Minecraft's own.

    python tools/frame_grab.py frame.npz [Local\\SubnautiCraftFrame_25612]
    python tools/mesh_edges.py frame.npz [grid step]

Builds the mesh three ways: "cells" (whole cells only, normals averaged like Unity's RecalculateNormals), "edges"
(cells cut where the depth jumps) and "faces" (what McLayer does now: also cut where one face of a block meets the
next, with each face's own direction). Prints which drawn pixels each covers and which empty ones it smears, and
saves mesh_<way>.png: the picture lit by a sun from the upper left through each mesh's normals (blocks should read as
cubes, with flat faces and sharp edges).
"""
import sys

import numpy as np
from PIL import Image

f = np.load(sys.argv[1])
color, depth = f["color"], f["depth"]
near, far, fov = float(f["near"]), float(f["far"]), float(f["fov"])
H, W = depth.shape
STEP = int(sys.argv[2]) if len(sys.argv) > 2 else 4
TOL = 0.12
CREASE = 0.9
maxDepth = far * 0.98
tanHalf = np.tan(np.radians(fov) * 0.5)
aspect = W / H

raw = depth.astype(np.float64)
with np.errstate(divide="ignore", invalid="ignore"):
    LIN = np.where(raw > 0, near * far / (near + raw * (far - near)), -1.0)
LIN[LIN > maxDepth] = -1.0


def lin(x, y):
    return LIN[y, x]


def view_point(x, y, z):
    return np.array([((x + 0.5) / W * 2 - 1) * tanHalf * aspect * z, ((y + 0.5) / H * 2 - 1) * tanHalf * z, z])


def near_(a, b):
    return a > 0 and b > 0 and abs(a - b) <= TOL * min(a, b) + 0.05


def pixel_facing(x, y, z):
    p = view_point(x, y, z)
    left = lin(x - 1, y) if x > 0 else -1.0
    right = lin(x + 1, y) if x + 1 < W else -1.0
    down = lin(x, y - 1) if y > 0 else -1.0
    up = lin(x, y + 1) if y + 1 < H else -1.0
    l, r, d, u = near_(z, left), near_(z, right), near_(z, down), near_(z, up)
    if r and (not l or abs(right - z) <= abs(z - left)):
        across = view_point(x + 1, y, right) - p
    elif l:
        across = p - view_point(x - 1, y, left)
    else:
        return None
    if u and (not d or abs(up - z) <= abs(z - down)):
        along = view_point(x, y + 1, up) - p
    elif d:
        along = p - view_point(x, y - 1, down)
    else:
        return None
    # Unity's Vector3.Cross (left-handed formula is the same algebra)
    n = np.cross(along, across)
    length = np.linalg.norm(n)
    if length < 1e-9:
        return None
    n /= length
    if np.dot(n, p) > 0:
        n = -n
    return n


def facing(x, y, z):
    n = pixel_facing(x, y, z)
    if n is None:
        v = view_point(x, y, 1.0)
        n = -v / np.linalg.norm(v)
    return n


def joined3(a, b, c):
    if a <= 0 or b <= 0 or c <= 0:
        return False
    return max(a, b, c) - min(a, b, c) <= TOL * min(a, b, c) + 0.05


def build(way):
    gw = (W - 1 + STEP - 1) // STEP + 1
    gh = (H - 1 + STEP - 1) // STEP + 1
    verts = []  # (x, y, z, u, v, normal)
    lin_g = [0.0] * (gw * gh)
    fac = [None] * (gw * gh)
    for gy in range(gh):
        y = min(gy * STEP, H - 1)
        for gx in range(gw):
            x = min(gx * STEP, W - 1)
            z = lin(x, y)
            i = gy * gw + gx
            lin_g[i] = z
            fac[i] = facing(x, y, z) if z > 0 else np.array([0.0, 0.0, -1.0])
            verts.append((x + 0.5, y + 0.5, z, x + 0.5, y + 0.5, fac[i]))
    faces = way == "faces"

    def same(i, j):
        return near_(lin_g[i], lin_g[j]) and (not faces or np.dot(fac[i], fac[j]) > CREASE)

    tris = []
    hE = (gw - 1) * gh
    cross = {}
    crease = {}

    def same_facing(x, y, z, n):
        if not faces:
            return True
        here = pixel_facing(x, y, z)
        return here is None or np.dot(here, n) > CREASE

    def walk(edge):
        vertical = edge >= hE
        g0 = edge - hE if vertical else edge // (gw - 1) * gw + edge % (gw - 1)
        g1 = g0 + gw if vertical else g0 + 1
        x0, y0 = min(g0 % gw * STEP, W - 1), min(g0 // gw * STEP, H - 1)
        x1, y1 = min(g1 % gw * STEP, W - 1), min(g1 // gw * STEP, H - 1)
        length = y1 - y0 if vertical else x1 - x0

        def at(k):
            return (x0, y0 + k) if vertical else (x0 + k, y0)
        k0, z0 = 0, lin_g[g0]
        if z0 > 0:
            for k in range(1, length):
                px, py = at(k)
                z = lin(px, py)
                if not near_(z0, z) or not same_facing(px, py, z, fac[g0]):
                    break
                z0, k0 = z, k
        k1, z1 = length, lin_g[g1]
        if z1 > 0:
            for k in range(length - 1, 0, -1):
                px, py = at(k)
                z = lin(px, py)
                if not near_(z1, z) or not same_facing(px, py, z, fac[g1]):
                    break
                z1, k1 = z, k
        e0, e1 = k0 + 1.0, float(k1)
        fold = faces and lin_g[g0] > 0 and lin_g[g1] > 0 and near_(z0, z1)
        crease[edge] = fold
        if fold or e1 < e0:
            e0 = e1 = (e0 + e1) * 0.5
        if fold:
            z0 = z1 = (z0 + z1) * 0.5

        def ev(end, tex, z, n):
            if vertical:
                verts.append((x0 + 0.5, y0 + end, z, x0 + 0.5, y0 + tex, n))
            else:
                verts.append((x0 + end, y0 + 0.5, z, x0 + tex, y0 + 0.5, n))
            return len(verts) - 1
        cross[edge] = (ev(e0, min(k0 + 0.5, e0), z0, fac[g0]) if lin_g[g0] > 0 else -1,
                       ev(e1, max(k1 + 0.5, e1), z1, fac[g1]) if lin_g[g1] > 0 else -1)

    def crossing(edge, frm):
        if edge not in cross:
            walk(edge)
        g0 = edge - hE if edge >= hE else edge // (gw - 1) * gw + edge % (gw - 1)
        return cross[edge][0 if frm == g0 else 1]

    def whole(a, b, c, d):
        if joined3(lin_g[a], lin_g[b], lin_g[c]):
            tris.append((a, b, c))
            if joined3(lin_g[a], lin_g[c], lin_g[d]):
                tris.append((a, c, d))
        elif joined3(lin_g[a], lin_g[c], lin_g[d]):
            tris.append((a, c, d))
        elif joined3(lin_g[a], lin_g[b], lin_g[d]):
            tris.append((a, b, d))
        elif joined3(lin_g[b], lin_g[c], lin_g[d]):
            tris.append((b, c, d))

    def fan(poly):
        for t in range(1, len(poly) - 1):
            tris.append((poly[0], poly[t], poly[t + 1]))

    for gy in range(gh - 1):
        for gx in range(gw - 1):
            a = gy * gw + gx
            b = a + gw
            c = b + 1
            d = a + 1
            if way == "cells":
                whole(a, b, c, d)
                continue
            corner = [a, b, c, d]
            side = [hE + a, (gy + 1) * (gw - 1) + gx, hE + d, gy * (gw - 1) + gx]
            joined = [same(corner[k], corner[(k + 1) & 3]) for k in range(4)]
            cuts = joined.count(False)
            if cuts < 2:
                whole(a, b, c, d)
                continue
            for start in range(4):
                before = (start + 3) & 3
                if joined[before] or lin_g[corner[start]] <= 0:
                    continue
                poly = [crossing(side[before], corner[start])]
                k = start
                while True:
                    poly.append(corner[k])
                    if not joined[k]:
                        break
                    k = (k + 1) & 3
                poly.append(crossing(side[k], corner[k]))
                assert -1 not in poly, poly
                fan(poly)
            if cuts >= 3:
                poly = []
                for k in range(4):
                    if not joined[k]:
                        if not crease.get(side[k], False) or lin_g[corner[k]] <= 0:
                            poly = None
                            break
                        poly.append(crossing(side[k], corner[k]))
                if poly:
                    fan(poly)
    if way == "cells":
        # Unity's RecalculateNormals: each vertex gets the average of the faces around it
        acc = [np.zeros(3) for _ in verts]
        for t in tris:
            p = [view_point(verts[i][0] - 0.5, verts[i][1] - 0.5, verts[i][2]) for i in t]
            n = np.cross(p[1] - p[0], p[2] - p[0])
            if np.dot(n, p[0]) > 0:
                n = -n  # front faces face the camera
            for i in t:
                acc[i] += n
        verts = [(v[0], v[1], v[2], v[3], v[4], acc[i] / (np.linalg.norm(acc[i]) or 1.0)) for i, v in enumerate(verts)]
    return verts, tris


def raster(verts, tris, light):
    """Draw the triangles in picture space (y up), lit: colour x (ambient + sun x facing)."""
    out = np.zeros((H, W, 3), np.float32)
    cov = np.zeros((H, W), bool)
    col = color[:, :, :3].astype(np.float32)
    back = 0
    for t in tris:
        v0, v1, v2 = (verts[i] for i in t)
        x0, y0, x1, y1, x2, y2 = v0[0], v0[1], v1[0], v1[1], v2[0], v2[1]
        area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        if area >= 0:  # clockwise in a y-up picture = negative signed area; anything else would be culled
            back += 1
            continue
        xs, ys = np.meshgrid(np.arange(max(int(np.floor(min(x0, x1, x2))), 0), min(int(np.ceil(max(x0, x1, x2))), W)) + 0.5,
                             np.arange(max(int(np.floor(min(y0, y1, y2))), 0), min(int(np.ceil(max(y0, y1, y2))), H)) + 0.5)
        w0 = ((x1 - xs) * (y2 - ys) - (x2 - xs) * (y1 - ys)) / area
        w1 = ((x2 - xs) * (y0 - ys) - (x0 - xs) * (y2 - ys)) / area
        w2 = 1 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any():
            continue
        u = w0 * v0[3] + w1 * v1[3] + w2 * v2[3] - 0.5
        v = w0 * v0[4] + w1 * v1[4] + w2 * v2[4] - 0.5
        ui, vi = np.clip(np.floor(u).astype(int), 0, W - 2), np.clip(np.floor(v).astype(int), 0, H - 2)
        fu, fv = np.clip(u - ui, 0, 1)[..., None], np.clip(v - vi, 0, 1)[..., None]
        c = (col[vi, ui] * (1 - fu) * (1 - fv) + col[vi, ui + 1] * fu * (1 - fv) + col[vi + 1, ui] * (1 - fu) * fv + col[vi + 1, ui + 1] * fu * fv)
        n = w0[..., None] * v0[5] + w1[..., None] * v1[5] + w2[..., None] * v2[5]
        n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-9
        shade = 0.35 + 0.65 * np.clip(n @ light, 0, 1)
        px = (xs[inside] - 0.5).astype(int)
        py = (ys[inside] - 0.5).astype(int)
        out[py, px] = (c * shade[..., None])[inside]
        cov[py, px] = True
    return np.clip(out, 0, 255).astype(np.uint8), cov, back


valid = LIN > 0
sun = np.array([-0.5, 0.75, -0.45])  # from the upper left, a bit towards the camera (view space: x right, y up, z ahead)
sun /= np.linalg.norm(sun)
for way in ("cells", "edges", "faces"):
    verts, tris = build(way)
    img, cov, back = raster(verts, tris, sun)
    missed = valid & ~cov
    smeared = ~valid & cov
    print("%s: %d vertices %d triangles, back-facing %d | drawn pixels missed %d (%.1f%%), empty pixels covered %d" % (
        way, len(verts), len(tris), back, missed.sum(), 100.0 * missed.sum() / max(1, valid.sum()), smeared.sum()))
    view = np.zeros((H, W, 3), np.uint8)
    view[:] = (20, 60, 90)
    view[cov] = img[cov]
    Image.fromarray(view[::-1]).save("mesh_%s.png" % way)
