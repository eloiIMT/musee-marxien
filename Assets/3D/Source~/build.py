"""Transforms workshop.glb into the organizer workshop (Atelier) - v2: architect plan on a wooden easel.

Run: blender -b --python build.py -- <workshop.glb> <out_dir> [<fbx_path> <blend_path> <blueprint_png>]

Coordinates: the scene is built so that, once exported to FBX and imported in Unity,
Unity (ux, uy, uz) == Blender (-ux, -uz, uy). Origin = the Workbench object of Sandbox_Maquette:
plan board centre projected on the floor; the user stands at Spot_Joueur (0, 0, -1) facing +Z.

Spot_* empties: for every station, the Unity object placed there has its front face (-Z) towards the user,
so the object's rotation = spot rotation * Euler(0, 180, 0); spot +Z = direction the object faces.
Spot_Joueur: rig position, +Z = look direction.
"""
import bpy, bmesh, math, sys, os
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(__file__))
from common import bbox, mats, setup_render, shot

args = sys.argv[sys.argv.index('--') + 1:]
GLB, OUT = args[0], args[1]
FBX = args[2] if len(args) > 2 else None
BLEND = args[3] if len(args) > 3 else None
BLUEPRINT = args[4] if len(args) > 4 else None

S = 1.4                    # original model is ~0.7x real size
X0, ZF = 0.27, -0.75       # original x mapped to Unity x = 0 (user centred between PC wall and elevator wall), floor height
# room enlargement (m): west / east walls pushed sideways, north wall + workbench pushed back. The machinery run follows
# the west wall; easel, lectern, buttons, bookshelf and Spot_Joueur stay put.
GROW_W, GROW_E, GROW_N = 0.6, 0.6, 0.6
Z_SHELF = 0.75             # Unity z of the bookshelf front (behind the easel)
DZ_WEST = -0.87            # machinery wall run moved back so the PC sits at the user's right
Z_BENCH = -1.95 - GROW_N   # Unity z of the workbench front (behind the user)

# plan / easel (Unity)
BOARD_W, BOARD_H, BOARD_T = 2.0, 1.4, 0.03
BOARD_C = Vector((0, 1.3, 0))
TILT = math.radians(15)    # top leans away from the user
PLAYER_Z = -1.0

XW, XE, ZN = 1.89 + GROW_W, -1.9 - GROW_E, -2.85 - GROW_N   # inner faces: west (PC), east (gacha), north (behind the workbench)
H = 2.6                              # ceiling height

# elevator to the museum (Unity): platform centre on the floor, in front of the machinery wall. The ceiling gets a
# square opening of half-size ELEV_R above it; the round shaft, its collar and the platform are built in Unity
# (ElevatorShaft, same radius). The lever is wall-mounted on the front of the tall machine cabinet (Unity x = 2.18,
# the west run moved by GROW_W), facing the platform, as high as the machine box sticking out above it allows (its
# underside is at 1.61): grip at ~1.5 m, head height.
ELEV_X, ELEV_Z, ELEV_R = 1.3, -1.9, 0.55
LEVER_X, LEVER_Y = 2.18, 1.3

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
scene = bpy.context.scene
for o in scene.objects:
    o.select_set(o.type == 'MESH')
bpy.context.view_layer.objects.active = next(o for o in scene.objects if o.type == 'MESH')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def ctr(o):
    a, b = bbox(o); return (a + b) / 2, a, b

def remap_x(o, lo, hi):
    a, b = bbox(o)
    for v in o.data.vertices:
        t = (v.co.x - a.x) / (b.x - a.x)
        v.co.x = lo + t * (hi - lo)

# ---------------------------------------------------------------- 1. clean-up + clustering (original coordinates)
removed, clusters = [], {}
def rm(o, why):
    removed.append((o.name, why)); bpy.data.objects.remove(o)

for o in list(scene.objects):
    if o.type != 'MESH':
        continue
    m, p = mats(o), len(o.data.polygons)
    c, a, b = ctr(o)
    if o.name == 'group1899386574':                                       rm(o, 'tarp'); continue
    if o.name in ('group987980187', 'group954819432', 'group1557021028'): rm(o, 'diorama plinth'); continue
    if o.name == 'group1713186528':                                       rm(o, 'machine crossing the east wall'); continue
    if m == ['mat13'] and p in (18, 20, 26):                              rm(o, 'debris'); continue
    if m == ['mat13'] and p == 12 and b.z < -0.6:                         rm(o, 'floor shards'); continue
    if m == ['mat18'] and p == 20:                                        rm(o, 'floor chips'); continue
    # floor tiles: rebuilt as a clean tiled floor for the new room size
    if b.z < -0.7 and a.z > -0.9 and p in (64, 78, 80, 88, 474):          rm(o, 'floor tile (rebuilt)'); continue
    if -0.55 < c.x < 0.15 and -0.9 < c.y < -0.2 and b.z < 0.25:           rm(o, 'chair'); continue
    # only one computer in the workshop: the PC desk on the machinery wall (monitor, keyboard, small tower)
    if o.name == 'group1079008815':                                       rm(o, 'laptop on the workbench'); continue
    if o.name == 'group1451214769':                                       rm(o, 'second computer tower next to the PC'); continue
    # sheets of paper on the walls / cork board / hanging paper strip
    if m == ['mat21'] and p <= 36:                                        rm(o, 'paper sheet'); continue
    # lone pipe elbows standing on the floor (no pipe to connect to)
    if o.name in ('group1095894712', 'group1994619716'):                  rm(o, 'pipe elbow'); continue
    # the model's own PC desk (monitor + keyboard, legs, cables, small tower): the workshop screen is built in Unity at Spot_PC
    if o.name in ('group1048239680', 'group1056998046', 'group966136559', 'group1035163510', 'group1046072727', 'group336546623', 'group73971407'):
        rm(o, 'model PC desk (monitor, keyboard, legs, cables, tower)'); continue
    # headphones and their cord on the workbench
    if o.name in ('group850969638', 'group466027787'):                    rm(o, 'headphones + cord on the workbench'); continue
    # contents of the open drawer (screwdriver sticking out, two small parts): the drawer is closed below
    if o.name in ('group816388643', 'group1132960755', 'group1293440205'): rm(o, 'open drawer contents'); continue
    # the model's own west wall panels (full-height slab + low wainscot, at other depths than the rebuilt walls): the west
    # wall is rebuilt like the east one (plain wall, base trim, stripe, pilasters)
    if o.name in ('group1100343911', 'group1807314114'):                  rm(o, 'model west wall panels (rebuilt)'); continue
    # workbench top, centre: room for the gacha machine
    if -0.45 < c.x < 0.72 and 0.38 < c.y < 0.95 and a.z > -0.14 and b.z < 0.32 and o.name != 'group850969638':
        rm(o, 'workbench top (gacha spot)'); continue
    if c.y < -0.85 and c.x > -0.95:   k = 'book'
    elif c.y > 0.3 and c.x > -0.95:   k = 'bench'
    elif c.x < -0.66:                 k = 'west'
    else:                             k = 'other'
    clusters[o.name] = k

# back panel of the bookshelf reaches both side walls (original x for Unity x = XW + 0.084 and XE - 0.046)
remap_x(scene.objects['group1763104299'], X0 - (XW + 0.084) / S, X0 - (XE - 0.046) / S)

# ---------------------------------------------------------------- 2. to final coordinates (per cluster)
def M_for(k):
    if k == 'bench':
        z_of = lambda y: Z_BENCH - S * (y - 0.42)
    else:
        dz = DZ_WEST if k == 'west' else 0.0
        z_of = lambda y: Z_SHELF - S * (y + 1.02) + dz
    # Blender final: (bx, by, bz) = (-ux, -uz, uy) with ux = -S (x - X0), uy = S (z - ZF)
    by0 = -z_of(0.0)      # by = -z_of(y) = S*y + const
    dx = GROW_W if k == 'west' else 0.0                      # the machinery run follows the west wall
    return Matrix.Translation((-S * X0 - dx, by0, -S * ZF)) @ Matrix.Scale(S, 4)

for o in scene.objects:
    if o.type == 'MESH':
        o.data.transform(M_for(clusters.get(o.name, 'other')))
        o.data.update()
bpy.context.view_layer.update()

def U(x, y, z): return Vector((-x, -z, y))

# the open drawer is pushed in, flush with its neighbour (front 0.112 m further out)
scene.objects['group1329093171'].data.transform(Matrix.Translation(U(0, 0, -0.112)))

# bits that were buried in the old east wall follow it (they would float in the room otherwise)
for o in scene.objects:
    if o.type == 'MESH' and o.name != 'group1763104299' and -bbox(o)[1].x < XE + GROW_E - 0.05:
        o.data.transform(Matrix.Translation(U(-GROW_E, 0, 0))); o.data.update()
bpy.context.view_layer.update()

# anything of the moved runs now entirely outside the room is dropped
for o in list(scene.objects):
    if o.type != 'MESH': continue
    a, b = bbox(o)
    uz_max, uz_min = -a.y, -b.y
    uz_c = (uz_max + uz_min) / 2
    if uz_c < ZN: rm(o, 'outside the north wall after the move')

MAT = {m.name: m for m in bpy.data.materials}
wood = bpy.data.materials.new('Atelier_BoisChevalet'); wood.diffuse_color = (0.55, 0.33, 0.16, 1)
MAT[wood.name] = wood
# glTF-imported materials use a Principled BSDF: give the new one the same so the FBX carries its colour
wood.use_nodes = True
wood.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.55, 0.33, 0.16, 1)
wood.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.8
# ceiling light panels get their own material (made emissive in Unity by ApplyWorkshopLayout)
lum = bpy.data.materials.new('Atelier_Lumiere'); lum.use_nodes = True
lum.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (1, 0.97, 0.9, 1)
lum.diffuse_color = (1, 0.97, 0.9, 1)
MAT[lum.name] = lum

def mesh_obj(name, verts_u, faces, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata([U(*v) for v in verts_u], [], faces)
    me.update()
    o = bpy.data.objects.new(name, me); scene.collection.objects.link(o)
    me.materials.append(MAT[mat])
    return o

BOX_FACES = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]

def obox(name, center, ax, ay, az, sx, sy, sz, mat):
    """Oriented box (Unity coords): centre, unit axes, full sizes."""
    c = Vector(center); ax, ay, az = Vector(ax).normalized(), Vector(ay).normalized(), Vector(az).normalized()
    v = [c + ax * (sx / 2 * i) + ay * (sy / 2 * j) + az * (sz / 2 * k) for i in (-1, 1) for j in (-1, 1) for k in (-1, 1)]
    o = mesh_obj(name, [tuple(p) for p in v], BOX_FACES, mat)
    # make normals point outwards whatever the handedness of the axes
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(o.data); bm.free()
    return o

HOLE = (ELEV_X - ELEV_R, ELEV_X + ELEV_R, ELEV_Z - ELEV_R, ELEV_Z + ELEV_R)   # x0, x1, z0, z1

def box(name, x0, x1, y0, y1, z0, z1, mat):
    # ceiling-level boxes (ceiling slab, beams, cornices, light panels) leave the elevator opening free
    hx0, hx1, hz0, hz1 = HOLE
    if y0 >= H - 0.15 and x0 < hx1 and hx0 < x1 and z0 < hz1 and hz0 < z1:
        if name.startswith('Ceiling_Light'):
            return None                                   # a light panel cut in two would look odd: dropped
        last = None
        for k, (a, b, c, d) in enumerate(((x0, hx0, z0, z1), (hx1, x1, z0, z1),
                                          (max(x0, hx0), min(x1, hx1), z0, hz0), (max(x0, hx0), min(x1, hx1), hz1, z1))):
            if b - a > 1e-3 and d - c > 1e-3:
                last = box(f'{name}_{k}', a, b, y0, y1, c, d, mat)
        return last
    return obox(name, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (1, 0, 0), (0, 1, 0), (0, 0, 1),
                x1 - x0, y1 - y0, z1 - z0, mat)

def beam(name, p0, p1, w, d, hint, mat):
    """Beam from p0 to p1 (Unity), cross-section w x d; 'hint' gives the direction of the d side."""
    p0, p1 = Vector(p0), Vector(p1)
    axis = (p1 - p0)
    side = axis.cross(Vector(hint)).normalized()
    nrm = side.cross(axis.normalized()).normalized()
    return obox(name, (p0 + p1) / 2, side, axis.normalized(), nrm, w, axis.length, d, mat)

# ---------------------------------------------------------------- 3. room shell
new = []
# tiled floor: dark slab + tan tiles (a few slate ones, like the original)
new.append(box('Floor_Base', XE - 0.3, XW + 0.4, -0.12, -0.012, ZN - 0.3, 1.6, 'mat17'))
T, G = 0.88, 0.02
slate = {(1, 1), (3, 0), (0, 3), (2, 4), (4, 2)}
for i, x in enumerate(np.arange(XE - 0.1, XW + 0.3, T)):
    for j, z in enumerate(np.arange(ZN - 0.1, 1.55, T)):
        new.append(box(f'Tile_{i}_{j}', x + G / 2, min(x + T, XW + 0.4) - G / 2, -0.03, 0.0, z + G / 2, min(z + T, 1.6) - G / 2,
                       'mat16' if (i, j) in slate else 'mat18'))
new.append(box('Ceiling', XE - 0.3, XW + 0.4, H, H + 0.1, ZN - 0.3, 1.6, 'mat17'))
new.append(box('Wall_East', XE - 0.2, XE, 0, H, ZN - 0.2, 1.6, 'mat16'))
new.append(box('Wall_North', XE - 0.2, XW + 0.4, 0, H, ZN - 0.2, ZN, 'mat16'))
# west wall: same plain wall as the east one (thicker outwards so the backs of the machinery run stay inside it)
new.append(box('Wall_West', XW, XW + 0.4, 0, H, ZN - 0.2, 1.6, 'mat16'))
new.append(box('Wall_South_Upper', XE, XW + 0.4, 1.74, H, 1.2, 1.45, 'mat16'))
new.append(box('Wall_South', XE - 0.2, XW + 0.4, 0, H, 1.44, 1.6, 'mat16'))           # seen beside the bookshelf
# side walls get the same base trim, stripe and pilasters (mirrored), the north wall its own run
for n_, sx, xw in (('East', 1, XE), ('West', -1, XW)):
    def side(name, d, y0, y1, z0, z1, mat):          # box from the wall face d metres into the room
        new.append(box(name, *sorted((xw, xw + sx * d)), y0, y1, z0, z1, mat))
    side(f'Trim_{n_}_Base', 0.03, 0, 0.12, ZN, 0.75, 'mat17')
    side(f'Trim_{n_}_Stripe', 0.015, 1.05, 1.1, ZN, 0.7, 'mat13')
    for i, z in enumerate(np.linspace(0.7, ZN + 0.1, 4)):
        side(f'Pilaster_{n_}_{i}', 0.06, 0, H, z - 0.06, z + 0.06, 'mat17')
    side(f'Cornice_{n_}', 0.08, H - 0.12, H, ZN, 1.45, 'mat17')
new.append(box('Trim_North_Base', XE, XW, 0, 0.12, ZN, ZN + 0.03, 'mat17'))
new.append(box('Trim_North_Stripe', XE, XW, 1.05, 1.1, ZN, ZN + 0.015, 'mat13'))
for i, x in enumerate(np.linspace(XE + 0.1, XW - 0.1, 5)):
    new.append(box(f'Pilaster_North_{i}', x - 0.06, x + 0.06, 0, H, ZN, ZN + 0.06, 'mat17'))
new.append(box('Cornice_North', XE, XW, H - 0.12, H, ZN, ZN + 0.08, 'mat17'))
new.append(box('Cornice_South', XE, XW, H - 0.12, H, 1.12, 1.2, 'mat17'))
for i, z in enumerate(np.linspace(0.45, ZN + 0.65, 4)):
    new.append(box(f'Ceiling_Beam_{i}', XE, XW, H - 0.12, H, z - 0.08, z + 0.08, 'mat16'))
LX = (XW - XE) / 4                                    # light panels at a quarter of the width from the centre
for i, (x, z) in enumerate(((-LX, -0.2), (LX, -0.2), (-LX, -1.55 - GROW_N), (LX, -1.55 - GROW_N))):
    new.append(box(f'Ceiling_Light_{i}', x - 0.4, x + 0.4, H - 0.03, H, z - 0.2, z + 0.2, 'Atelier_Lumiere'))

# cabinet doors: the original fronts had paper posters taped on them; without the posters the bare dark patches and
# tape pieces looked odd, so each cabinet gets plain wooden doors set just in front of its front face
for cab, (xmin, xmax) in (('group259666405', (None, XW - 0.02)), ('group1819965688', (XE + 0.02, None))):
    o = scene.objects[cab]
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    us = [(-v.x, v.z, -v.y) for v in vs]                      # Unity coordinates
    x0 = max(min(u[0] for u in us), xmin if xmin is not None else -9) + 0.03
    x1 = min(max(u[0] for u in us), xmax if xmax is not None else 9) - 0.03
    zf = min(u[2] for u in us if 0.15 < u[1] < 1.65)         # front-most point (tape included)
    y0, y1 = 0.17, 1.62
    zc0, zc1 = zf - 0.02, zf + 0.03
    mid = (x0 + x1) / 2
    for k_, (a_, b_) in enumerate(((x0, mid - 0.006), (mid + 0.006, x1))):
        new.append(box(f'Door_{cab}_{k_}', a_, b_, y0, y1, zc0, zc1, 'mat18'))
        new.append(box(f'Door_{cab}_{k_}_Inset', a_ + 0.06, b_ - 0.06, y0 + 0.08, y1 - 0.08, zc0 - 0.008, zc0, 'mat19'))
        new.append(box(f'Door_{cab}_{k_}_Panel', a_ + 0.085, b_ - 0.085, y0 + 0.105, y1 - 0.105, zc0 - 0.01, zc0 - 0.002, 'mat18'))
        hx = mid - 0.05 if k_ == 0 else mid + 0.05
        new.append(box(f'Door_{cab}_{k_}_Handle', hx - 0.012, hx + 0.012, 0.85, 1.05, zc0 - 0.035, zc0 - 0.008, 'mat17'))
    new.append(box(f'Door_{cab}_Gap', mid - 0.006, mid + 0.006, y0, y1, zc0 + 0.005, zc1, 'mat17'))

# ---------------------------------------------------------------- 4. easel (Chevalet) + button shelf + tutorial lectern
c, s = math.cos(TILT), math.sin(TILT)
UPB = Vector((0, c, s))          # board up (top leans away from the user)
FWB = Vector((0, -s, c))         # board normal pointing away from the user (back side)
XB = Vector((1, 0, 0))
def bp(x, t, f):                 # point in the board frame: x across, t along board up, f behind the board
    return BOARD_C + XB * x + UPB * t + FWB * f

easel = []
LEG_F = BOARD_T / 2 + 0.03       # front legs just behind the board
t_floor = -(BOARD_C.y - FWB.y * -LEG_F) / UPB.y      # t where a leg in the leg plane meets the floor
t_floor = (-(BOARD_C.y + FWB.y * LEG_F)) / UPB.y
t_top = BOARD_H / 2 + 0.22
def leg_x(t, side):              # legs splay: wide at the floor, close at the top
    f = (t - t_floor) / (t_top - t_floor)
    return side * (0.95 + (0.12 - 0.95) * f)
for side, nm in ((-1, 'L'), (1, 'R')):
    easel.append(beam(f'Easel_Leg_{nm}', bp(leg_x(t_floor, side), t_floor, LEG_F), bp(leg_x(t_top, side), t_top, LEG_F),
                      0.055, 0.035, FWB, 'Atelier_BoisChevalet'))
t_brace = t_floor + 0.32
easel.append(beam('Easel_Brace', bp(leg_x(t_brace, -1), t_brace, LEG_F), bp(leg_x(t_brace, 1), t_brace, LEG_F),
                  0.04, 0.03, FWB, 'Atelier_BoisChevalet'))
easel.append(beam('Easel_Mast', bp(0, t_brace, LEG_F + 0.035), bp(0, t_top + 0.1, LEG_F + 0.035), 0.05, 0.035, FWB, 'Atelier_BoisChevalet'))
hinge = bp(0, t_top - 0.05, LEG_F + 0.07)
foot = Vector((0, 0, 0.62))
easel.append(beam('Easel_BackLeg', hinge, foot, 0.05, 0.035, (1, 0, 0), 'Atelier_BoisChevalet'))
easel.append(obox('Easel_Hinge', hinge, XB, UPB, FWB, 0.16, 0.08, 0.07, 'mat17'))
# back stay between mast and back leg
t_stay = -0.35
p_m = bp(0, t_stay, LEG_F + 0.035)
k = (p_m.y - hinge.y) / (foot.y - hinge.y)
easel.append(beam('Easel_Stay', p_m, hinge.lerp(foot, k), 0.03, 0.02, (1, 0, 0), 'Atelier_BoisChevalet'))
# ledge under the board, with a small lip in front
tb = -BOARD_H / 2
easel.append(obox('Easel_Ledge', bp(0, tb - 0.018, -0.005), XB, UPB, FWB, BOARD_W + 0.2, 0.036, 0.13, 'Atelier_BoisChevalet'))
easel.append(obox('Easel_Ledge_Lip', bp(0, tb + 0.005, -BOARD_T / 2 - 0.018), XB, UPB, FWB, BOARD_W + 0.2, 0.05, 0.02, 'Atelier_BoisChevalet'))
for side in (-1, 1):
    easel.append(obox(f'Easel_Peg_{side}', bp(leg_x(tb - 0.03, side), tb - 0.05, LEG_F), XB, UPB, FWB, 0.07, 0.05, 0.06, 'mat17'))
# top clamp holding the board's upper edge
easel.append(obox('Easel_Clamp', bp(0, BOARD_H / 2 + 0.03, 0.01), XB, UPB, FWB, 0.16, 0.09, 0.09, 'Atelier_BoisChevalet'))
easel.append(obox('Easel_Clamp_Lip', bp(0, BOARD_H / 2 - 0.005, -BOARD_T / 2 - 0.012), XB, UPB, FWB, 0.12, 0.03, 0.015, 'Atelier_BoisChevalet'))

# tutorial lectern on the right of the easel (where the old, empty button shelf stood), facing the user, top tilted
LEC = Vector((1.45, 0, -0.45))
YAW_LEC = math.radians(60)          # object yaw: its front (-Z) points at the user
TILT_LEC = math.radians(25)
ly = Vector((0, 1, 0))
lx = Vector((math.cos(YAW_LEC), 0, -math.sin(YAW_LEC)))
lz = Vector((math.sin(YAW_LEC), 0, math.cos(YAW_LEC)))       # back
top_up = ly * math.cos(TILT_LEC) + lz * math.sin(TILT_LEC)
top_back = lz * math.cos(TILT_LEC) - ly * math.sin(TILT_LEC)
TOP_C = LEC + Vector((0, 1.15, 0))
lectern = []
lectern.append(obox('Lectern_Top', TOP_C, lx, top_up, top_back, 1.32, 0.62, 0.035, 'Atelier_BoisChevalet'))
lectern.append(obox('Lectern_Top_Lip', TOP_C - top_up * 0.32 - top_back * 0.02, lx, top_up, top_back, 1.32, 0.04, 0.06, 'Atelier_BoisChevalet'))
lectern.append(beam('Lectern_Post', LEC + Vector((0, 0.03, 0)), TOP_C + top_back * 0.03 - top_up * 0.05, 0.08, 0.08, lz, 'Atelier_BoisChevalet'))
lectern.append(obox('Lectern_Foot', LEC + Vector((0, 0.02, 0)), lx, ly, lz, 0.6, 0.04, 0.42, 'mat17'))

# ---------------------------------------------------------------- 4b. storage table + blue bin (unused artworks)
# Between the gacha machine and the plan, against nothing: a small copy of the workbench (its own top, leg frames and
# drawer meshes, resized per axis) with a blue stacking bin (hopper front) on it, both turned towards the user.
def ubox(o):                     # Unity-space bbox of a mesh (object transforms are identity at this point)
    vs = [(-v.co.x, v.co.z, -v.co.y) for v in o.data.vertices]
    return [min(v[i] for v in vs) for i in range(3)], [max(v[i] for v in vs) for i in range(3)]

def yaw_axes(yaw):               # Unity object yaw -> world directions of its local x and z
    return Vector((math.cos(yaw), 0, -math.sin(yaw))), Vector((math.sin(yaw), 0, math.cos(yaw)))

def clone_into(src_name, name, dst_lo, dst_hi, origin, yaw):
    """Copy of an existing mesh, its Unity bbox remapped per axis onto [dst_lo, dst_hi] (object-local, None = keep the
    source range), turned by yaw and moved to origin. Per-axis scaling only: no mirroring, normals stay outwards."""
    src = scene.objects[src_name]
    lo, hi = ubox(src)
    dst_lo = [lo[i] if dst_lo[i] is None else dst_lo[i] for i in range(3)]
    dst_hi = [hi[i] if dst_hi[i] is None else dst_hi[i] for i in range(3)]
    ax, az = yaw_axes(yaw)
    me = src.data.copy(); me.name = name
    o = bpy.data.objects.new(name, me); scene.collection.objects.link(o)
    for v in me.vertices:
        u = (-v.co.x, v.co.z, -v.co.y)
        p = [dst_lo[i] + (u[i] - lo[i]) * (dst_hi[i] - dst_lo[i]) / (hi[i] - lo[i]) for i in range(3)]
        v.co = U(*(origin + ax * p[0] + Vector((0, p[1], 0)) + az * p[2]))
    me.update()
    return o

# The workbench's front (towards the room) is +Z in its source meshes, so the table keeps +Z as its front: yaw 90 turns
# it towards the user (+X). Width (local x) runs along the east wall, between the gacha (z < -1.56) and the easel.
TABLE_C = Vector((-1.7, 0, -0.85))
TABLE_YAW = math.radians(90)
TW, TD = 1.1, 0.6                # top width (local x), depth (local z): the bin is 0.95 m wide with its rim
SRC_TOP, SRC_LEG, SRC_DRAWER = 'group975002159', 'group1184716703', 'group1527169260'   # workbench top, leg frame, drawer
table = [clone_into(SRC_TOP, 'Table_Top', (-TW / 2, None, -TD / 2), (TW / 2, None, TD / 2), TABLE_C, TABLE_YAW)]
LEG = 0.06                       # square legs, one per corner (the workbench's leg frame squeezed to a post)
for sx, nx in ((-1, 'L'), (1, 'R')):
    for sz, nz in ((-1, 'B'), (1, 'F')):
        xa, xb = sorted((sx * (TW / 2 - 0.04), sx * (TW / 2 - 0.04 - LEG)))
        za, zb = sorted((sz * (TD / 2 - 0.04), sz * (TD / 2 - 0.04 - LEG)))
        table.append(clone_into(SRC_LEG, f'Table_Leg_{nx}{nz}', (xa, None, za), (xb, None, zb), TABLE_C, TABLE_YAW))
table.append(clone_into(SRC_DRAWER, 'Table_Drawer', (-0.31, None, TD / 2 - 0.38), (0.31, None, TD / 2 - 0.02), TABLE_C, TABLE_YAW))
TABLE_TOP_Y = ubox(scene.objects[SRC_TOP])[1][1]

# blue plastic stacking bin (hopper front, rolled rim, ribs, label holder): local front = -Z, like the Unity objects
blue = bpy.data.materials.new('Atelier_BacBleu'); blue.use_nodes = True
blue_d = bpy.data.materials.new('Atelier_BacBleuFonce'); blue_d.use_nodes = True
for m_, col in ((blue, (0.03, 0.115, 0.61, 1)), (blue_d, (0.01, 0.03, 0.19, 1))):
    m_.diffuse_color = col
    m_.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = col
    m_.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.45
    MAT[m_.name] = m_

BIN_YAW = math.radians(-90)      # front (-Z) towards the user (+X)
BIN_O = Vector((TABLE_C.x, TABLE_TOP_Y, TABLE_C.z))
bx, bz = yaw_axes(BIN_YAW)
up = Vector((0, 1, 0))
def L(x, y, z): return BIN_O + bx * x + up * y + bz * z
BW, BD, BH, BT = 0.92, 0.4, 0.24, 0.012      # width, depth, height, wall thickness (ArtworkBin.cs mirrors these)
HF, SL = 0.11, 0.17                          # front panel height, slope run of the side walls
bac = []
def bbox_l(name, x0, x1, y0, y1, z0, z1, mat):
    bac.append(obox(name, L((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), bx, up, bz, x1 - x0, y1 - y0, z1 - z0, mat))
def prism(name, pts_zy, xa, xb, mat):
    n = len(pts_zy)
    verts = [tuple(L(xa, y, z)) for z, y in pts_zy] + [tuple(L(xb, y, z)) for z, y in pts_zy]
    faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    o = mesh_obj(name, verts, faces, mat)
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(o.data); bm.free()
    bac.append(o)

side_profile = [(-BD / 2 + 0.03, 0), (-BD / 2, HF), (-BD / 2 + SL, BH), (BD / 2, BH), (BD / 2, 0)]
bbox_l('Bac_Fond', -BW / 2, BW / 2, 0, BT, -BD / 2 + 0.03, BD / 2, 'Atelier_BacBleu')
bbox_l('Bac_Dos', -BW / 2, BW / 2, 0, BH, BD / 2 - BT, BD / 2, 'Atelier_BacBleu')
for sx, nm in ((-1, 'G'), (1, 'D')):
    xa, xb = sorted((sx * BW / 2, sx * (BW / 2 - BT)))
    prism(f'Bac_Flanc_{nm}', side_profile, xa, xb, 'Atelier_BacBleu')
    p0, p1 = L(sx * (BW / 2 + 0.004), HF, -BD / 2), L(sx * (BW / 2 + 0.004), BH, -BD / 2 + SL)
    bac.append(beam(f'Bac_Rebord_Pente_{nm}', p0, p1, 0.01, 0.024, bx, 'Atelier_BacBleu'))
    xa, xb = sorted((sx * (BW / 2 - BT), sx * (BW / 2 + 0.016)))
    bbox_l(f'Bac_Rebord_{nm}', xa, xb, BH - 0.01, BH, -BD / 2 + SL, BD / 2 + 0.016, 'Atelier_BacBleu')
    for k_, zc in enumerate((-0.03, 0.11)):                      # outer ribs on the full-height part of the side
        xa, xb = sorted((sx * BW / 2, sx * (BW / 2 + 0.012)))
        bbox_l(f'Bac_Nervure_{nm}{k_}', xa, xb, 0.015, BH - 0.01, zc - 0.015, zc + 0.015, 'Atelier_BacBleu')
bbox_l('Bac_Rebord_Dos', -BW / 2 - 0.016, BW / 2 + 0.016, BH - 0.01, BH, BD / 2 - BT, BD / 2 + 0.02, 'Atelier_BacBleu')
for k_, xc in enumerate((-BW / 6, BW / 6)):                        # inner ribs on the back wall
    bbox_l(f'Bac_Nervure_Dos{k_}', xc - 0.015, xc + 0.015, BT, BH - 0.01, BD / 2 - BT - 0.01, BD / 2 - BT, 'Atelier_BacBleu')
# low front panel, slightly slanted (bottom set back), with a lip on top and the label holder on its face
f0, f1 = L(0, 0, -BD / 2 + 0.03 + BT / 2), L(0, HF, -BD / 2 + BT / 2)
bac.append(beam('Bac_Face', f0, f1, BW, BT, bz, 'Atelier_BacBleu'))
bbox_l('Bac_Levre', -BW / 2 - 0.008, BW / 2 + 0.008, HF - 0.01, HF, -BD / 2 - 0.03, -BD / 2 + BT, 'Atelier_BacBleu')
fa = (f1 - f0).normalized()
fn = fa.cross(bx).normalized()
if fn.dot(-bz) < 0: fn = -fn                                         # outwards = towards the front
mid = (f0 + f1) / 2
bac.append(obox('Bac_Etiquette_Cadre', mid + fn * (BT / 2 + 0.002), bx, fa, fn, 0.2, 0.07, 0.004, 'Atelier_BacBleuFonce'))
bac.append(obox('Bac_Etiquette', mid + fn * (BT / 2 + 0.0045), bx, fa, fn, 0.17, 0.045, 0.002, 'mat21'))

# ---------------------------------------------------------------- 5. spots
A = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))          # Unity = A @ Blender
def unity_euler(x, y, z):
    x, y, z = map(math.radians, (x, y, z))
    Rx = Matrix(((1, 0, 0), (0, math.cos(x), -math.sin(x)), (0, math.sin(x), math.cos(x))))
    Ry = Matrix(((math.cos(y), 0, math.sin(y)), (0, 1, 0), (-math.sin(y), 0, math.cos(y))))
    Rz = Matrix(((math.cos(z), -math.sin(z), 0), (math.sin(z), math.cos(z), 0), (0, 0, 1)))
    return Ry @ Rx @ Rz

def spot(name, pos_u, obj_euler=None, spot_euler=None):
    """obj_euler: Unity rotation of the object placed there (front = -Z); the spot gets obj * Euler(0,180,0)."""
    Ru = unity_euler(*obj_euler) @ unity_euler(0, 180, 0) if obj_euler is not None else unity_euler(*spot_euler)
    Rb = A.transposed() @ Ru @ A
    e = bpy.data.objects.new(name, None); scene.collection.objects.link(e)
    e.matrix_world = Matrix.Translation(U(*pos_u)) @ Rb.to_4x4()
    e.empty_display_type = 'ARROWS'; e.empty_display_size = 0.3
    return e

lec_front = TOP_C - top_back * 0.025
spots = [
    spot('Spot_Plan', tuple(BOARD_C), obj_euler=(15, 0, 0)),
    spot('Spot_Tuto', tuple(lec_front), obj_euler=(25, math.degrees(YAW_LEC), 0)),
    spot('Spot_PC', (1.62 + GROW_W, 1.27, -0.56), obj_euler=(0, 90, 0)),   # where the removed desk's screen was, on the machinery wall
    spot('Spot_Gacha', (0.24, 0.9, -2.31 - GROW_N), obj_euler=(0, 180, 0)),
    spot('Spot_Bac', tuple(BIN_O), obj_euler=(0, math.degrees(BIN_YAW), 0)),   # bin bottom centre, on the storage table
    spot('Spot_Ascenseur', (ELEV_X, 0, ELEV_Z), spot_euler=(0, 0, 0)),          # platform centre (floor level)
    spot('Spot_Levier', (LEVER_X, LEVER_Y, ELEV_Z), obj_euler=(0, 90, 0)),      # on the cabinet, front towards the platform (-X)
    spot('Spot_Joueur', (0, 0, PLAYER_Z), spot_euler=(0, 0, 0)),
]

# ---------------------------------------------------------------- 6. blueprint paper texture
if BLUEPRINT:
    Wpx, Hpx = 2048, int(2048 * BOARD_H / BOARD_W)
    ppm = Wpx / BOARD_W
    img = np.zeros((Hpx, Wpx, 3), np.float32)
    base = np.array([0.06, 0.22, 0.45], np.float32)
    yy, xx = np.mgrid[0:Hpx, 0:Wpx]
    rng = np.random.default_rng(3)
    vign = 1.0 - 0.18 * (((xx / Wpx - 0.5) ** 2 + (yy / Hpx - 0.5) ** 2) * 2)
    img[:] = base
    img *= vign[..., None]
    img += (rng.normal(0, 0.012, (Hpx, Wpx)))[..., None]
    def lines(step_m, width_px, color, alpha):
        step = step_m * ppm
        for axis, n in ((xx, Wpx), (yy, Hpx)):
            d = np.abs(((axis + step / 2) % step) - step / 2)
            a = np.clip(width_px / 2 + 0.5 - d, 0, 1) * alpha
            img[:] = img * (1 - a[..., None]) + np.array(color, np.float32) * a[..., None]
    lines(0.05, 1.6, (0.55, 0.72, 0.92), 0.35)
    lines(0.25, 2.6, (0.80, 0.89, 0.98), 0.55)
    def rect(x0, y0, x1, y1, w, color=(0.9, 0.95, 1.0), alpha=0.85):   # metres from the bottom-left corner
        X0_, X1_, Y0_, Y1_ = (int(v * ppm) for v in (x0, x1, y0, y1))
        for (a0, a1, b0, b1) in ((X0_, X1_, Y0_, Y0_ + w), (X0_, X1_, Y1_ - w, Y1_), (X0_, X0_ + w, Y0_, Y1_), (X1_ - w, X1_, Y0_, Y1_)):
            img[b0:b1, a0:a1] = img[b0:b1, a0:a1] * (1 - alpha) + np.array(color) * alpha
    rect(0.03, 0.03, BOARD_W - 0.03, BOARD_H - 0.03, 5)            # sheet border
    rect(0.045, 0.045, BOARD_W - 0.045, BOARD_H - 0.045, 2)
    tb0x, tb1x, tb0y, tb1y = BOARD_W - 0.55, BOARD_W - 0.045, 0.045, 0.22   # title block (cartouche)
    rect(tb0x, tb0y, tb1x, tb1y, 3)
    img[int(0.13 * ppm):int(0.13 * ppm) + 2, int(tb0x * ppm):int(tb1x * ppm)] = (0.9, 0.95, 1.0)
    img[int(tb0y * ppm):int(tb1y * ppm), int((tb0x + 0.2) * ppm):int((tb0x + 0.2) * ppm) + 2] = (0.9, 0.95, 1.0)
    img = np.clip(img, 0, 1)
    img = img[::-1, ::-1].copy()   # Unity's cube shows its front (-Z) face texture rotated by 180 deg: pre-rotate
    bimg = bpy.data.images.new('blueprint', Wpx, Hpx, alpha=False)
    rgba = np.concatenate([img, np.ones((Hpx, Wpx, 1), np.float32)], axis=2)   # row 0 = bottom in Blender images
    bimg.pixels.foreach_set(rgba.ravel())
    bimg.filepath_raw = BLUEPRINT; bimg.file_format = 'PNG'; bimg.save()

# ---------------------------------------------------------------- 7. report + renders
with open(os.path.join(OUT, 'report.txt'), 'w') as f:
    for n, w in removed: f.write(f'removed {n} ({w})\n')
    for n, k in sorted(clusters.items(), key=lambda kv: kv[1]):
        if k == 'other' and n in scene.objects: f.write(f'other {n}\n')
    for s_ in spots: f.write(f'{s_.name} blender {tuple(round(v, 3) for v in s_.location)}\n')

# a stand-in board for the renders (the real board is the Unity 'Plan Board')
board_dbg = obox('DBG_Board', tuple(BOARD_C), XB, UPB, FWB, BOARD_W, BOARD_H, BOARD_T, 'mat5') if 'mat5' in MAT else None

def render_all():
    setup_render((1280, 800))
    dbg = bpy.data.materials.new('DBG'); dbg.diffuse_color = (1, 0, 0.6, 1)
    markers = []
    for s_ in spots:
        fwd_b = s_.matrix_world.to_3x3() @ Vector((0, -1, 0))      # Unity +Z = Blender -Y
        for k_, (dx, size) in enumerate(((0, 0.07), (0.1, 0.035), (0.17, 0.025))):
            me = bpy.data.meshes.new('m'); bm = bmesh.new(); bmesh.ops.create_cube(bm, size=size); bm.to_mesh(me); bm.free()
            mo = bpy.data.objects.new('DBG_' + s_.name, me); scene.collection.objects.link(mo); me.materials.append(dbg)
            mo.location = s_.location + fwd_b * dx; markers.append(mo)
    eye = 1.6
    P = U(0, eye, PLAYER_Z)
    for name, d in (('eye_plan', (0, 0, 1)), ('eye_right_pc', (1, 0, 0)), ('eye_back_gacha', (0, 0, -1)), ('eye_left_lift', (-1, 0, 0))):
        shot(OUT, name, P, U(d[0], eye - 0.3, PLAYER_Z + d[2]), lens=16)
    for k_ in range(6):                      # 360 audit from the user's position
        ang = math.radians(60 * k_)
        shot(OUT, f'aud_{k_}', P, U(math.sin(ang), eye - 0.35, PLAYER_Z + math.cos(ang)), lens=14)
    for k_, (cx, cz) in enumerate(((XW - 0.8, 0.5), (XE + 0.2, 0.5), (XW - 0.8, ZN + 0.25), (XE + 0.2, ZN + 0.25))):   # room corners
        shot(OUT, f'corner_{k_}', U(cx, 2.2, cz), U(0, 0.9, -1.0), lens=14)
    shot(OUT, 'side_easel', U(-1.7, 1.3, -0.2), U(0.3, 1.1, 0.2), lens=18)
    hide = [o for o in scene.objects if o.name.startswith(('Ceiling', 'Cornice'))]
    for o in hide: o.hide_render = True
    shot(OUT, 'top', U(0, 9, (ZN + 1.6) / 2), U(0, 0, (ZN + 1.6) / 2 - 0.001), ortho=max(XW - XE, (1.6 - ZN) * 1.6) * 1.1)   # 1280x800: height = 0.625 x width
    shot(OUT, 'iso', U(3.0 + GROW_W, 5.0, -4.8 - GROW_N), U(0, 0.6, -0.8), lens=22)
    for o in hide: o.hide_render = False
    for mo in markers: bpy.data.objects.remove(mo)

render_all()
if board_dbg: bpy.data.objects.remove(board_dbg)

# ---------------------------------------------------------------- 8. join + export
if FBX:
    def join(objs, name):
        objs = [o for o in objs if o.name in scene.objects]
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
        j = bpy.context.view_layer.objects.active; j.name = name; j.data.name = name
        return j
    keep_apart = set(easel) | set(lectern) | set(table) | set(bac)
    decor = [o for o in scene.objects if o.type == 'MESH' and o not in keep_apart]
    join(decor, 'Atelier_Decor')
    join(easel, 'Chevalet')
    join(lectern, 'Pupitre_Tuto')
    join(table, 'Table_Bac')
    join(bac, 'Bac_Oeuvres')
    names = {'mat16': 'Atelier_Ardoise', 'mat17': 'Atelier_Noir', 'mat18': 'Atelier_Bois', 'mat13': 'Atelier_Orange',
             'mat15': 'Atelier_Metal', 'mat21': 'Atelier_Blanc', 'mat12': 'Atelier_Jaune', 'mat24': 'Atelier_Verre',
             'mat22': 'Atelier_Gris', 'mat23': 'Atelier_NoirProfond', 'mat20': 'Atelier_Brun', 'mat19': 'Atelier_Cuivre'}
    for k_, v in names.items():
        if k_ in bpy.data.materials: bpy.data.materials[k_].name = v
    root = bpy.data.objects.new('Atelier', None); scene.collection.objects.link(root)
    for o in list(scene.objects):
        if o is not root and o.type in ('MESH', 'EMPTY') and o.parent is None:
            o.parent = root
    bpy.ops.object.select_all(action='SELECT')
    cam = bpy.data.objects.get('cam')
    if cam: cam.select_set(False)
    bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, axis_forward='-Z', axis_up='Y',
                             apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                             object_types={'MESH', 'EMPTY'}, mesh_smooth_type='FACE', add_leaf_bones=False)
    if BLEND:
        os.makedirs(os.path.dirname(BLEND), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=BLEND)
