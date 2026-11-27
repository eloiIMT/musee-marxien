"""Convert the Sketchfab "glass window" (Noboa, CC-BY-4.0) into a skylight pane for Unity.
Run: blender -b --python build_glass_window.py -- <glass_window.glb> <out.fbx> [<preview.png>]
Output: two meshes, "Frame" and "Glass", lying flat (glass in the XY plane of Unity = horizontal),
centred on the origin, real size in metres. Materials are assigned in Unity (MuseumView)."""
import bpy, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
preview = argv[2] if len(argv) > 2 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in meshes:  # bake parent chain (Sketchfab root rotation + cm scale)
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def is_glass(o): return any(m and m.name == 'Material.064' for m in o.data.materials)
groups = {'Glass': [o for o in meshes if is_glass(o)], 'Frame': [o for o in meshes if not is_glass(o)]}
result = []
for name, objs in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = o.data.name = name
    o.data.materials.clear()
    o.data.materials.append(bpy.data.materials.new(name))
    result.append(o)

# Lay flat: the thinnest axis becomes Z (Blender up), then centre on the origin.
allv = [o.matrix_world @ v.co for o in result for v in o.data.vertices]
lo = Vector([min(v[i] for v in allv) for i in range(3)]); hi = Vector([max(v[i] for v in allv) for i in range(3)])
size = hi - lo
thin = min(range(3), key=lambda i: size[i])
print('raw size', tuple(round(s, 3) for s in size), 'thin axis', thin)
centre = (lo + hi) / 2
for o in result:
    for v in o.data.vertices:
        p = v.co - centre
        if thin == 0: p = Vector((p.y, p.z, p.x))
        elif thin == 1: p = Vector((p.x, p.z, p.y))
        v.co = p
    o.data.update()
    # glass is a single plane: make normals face down (-Z, towards the visitor below)
allv = [v.co for o in result for v in o.data.vertices]
print('flat size', tuple(round(max(v[i] for v in allv) - min(v[i] for v in allv), 3) for i in range(3)))

for o in list(bpy.context.scene.objects):
    if o not in result: bpy.data.objects.remove(o)
bpy.ops.object.select_all(action='DESELECT')
for o in result: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE',
                         add_leaf_bones=False, bake_anim=False)

if preview:
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.color_type = 'OBJECT'
    for o in result: o.color = (0.4, 0.7, 1, 1) if o.name == 'Glass' else (0.9, 0.9, 0.9, 1)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam)
    sc.camera = cam; cam.data.type = 'ORTHO'; cam.data.ortho_scale = 5
    cam.location = (3, -3, 4); cam.rotation_euler = (Vector((0, 0, 0)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x = sc.render.resolution_y = 600
    sc.render.filepath = preview; bpy.ops.render.render(write_still=True)
