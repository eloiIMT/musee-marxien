"""Converts Kenney's "Computer Screen" (CC0, computer_screen.glb) into Assets/3D/EcranParcours.fbx (tour screen).

Run: blender -b --python build_screen.py -- <computer_screen.glb> <fbx_path>

The glb holds the same monitor twice (two overlapping meshes): only one is kept. The mesh is re-centred so the
origin is the middle of the foot's underside; the display faces Unity -Z (front convention of the Atelier spots).
Real size (~0.39 m wide): the scale is applied in Unity (TourScreen). Materials renamed Ecran_Cadre (dark frame)
and Ecran_Dalle (display surface, replaced at runtime by the screen UI's backdrop).

Display rectangle in Unity coordinates (scale 1), printed at the end and mirrored in TourScreen.cs:
bottom edge centre (0, 0.0556, 0.0358), top edge centre (0, 0.287, 0.0683), width 0.379 (top leans back ~8°).
"""
import bpy, sys
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index('--') + 1:]
GLB, FBX = args[0], args[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
keep = meshes[0]
for o in meshes[1:]:
    bpy.data.objects.remove(o)
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o)

keep.data.transform(keep.matrix_world)
keep.matrix_world = Matrix.Identity(4)
vs = [v.co for v in keep.data.vertices]
cx = (min(v.x for v in vs) + max(v.x for v in vs)) / 2
cy = (min(v.y for v in vs) + max(v.y for v in vs)) / 2
z0 = min(v.z for v in vs)
keep.data.transform(Matrix.Translation((-cx, -cy, -z0)))
keep.data.update()
keep.name = keep.data.name = 'EcranParcours'
for m in keep.data.materials:
    m.name = {'metalDark': 'Ecran_Cadre', 'metal': 'Ecran_Dalle'}.get(m.name, m.name)

# Display face (material Ecran_Dalle), in Unity coordinates: Unity (x, y, z) = Blender (-x, z, -y) after the export axes.
dalle = [i for i, m in enumerate(keep.data.materials) if m.name == 'Ecran_Dalle'][0]
pts = [keep.data.vertices[i].co for p in keep.data.polygons if p.material_index == dalle for i in p.vertices]
u = [(-p.x, p.z, -p.y) for p in pts]
print('DISPLAY x', round(min(a[0] for a in u), 4), round(max(a[0] for a in u), 4),
      'bottom', min(u, key=lambda a: a[1]), 'top', max(u, key=lambda a: a[1]))

bpy.ops.object.select_all(action='DESELECT')
keep.select_set(True)
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, axis_forward='-Z', axis_up='Y',
                         apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         object_types={'MESH'}, mesh_smooth_type='FACE', add_leaf_bones=False)
print('EXPORTED', FBX)
