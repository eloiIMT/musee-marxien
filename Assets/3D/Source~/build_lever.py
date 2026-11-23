"""Converts HV_lever_animated.glb (high-voltage lever) into Assets/3D/Levier.fbx (elevator lever).

Run: blender -b --python build_lever.py -- <HV_lever_animated.glb> <fbx_path>

Unity does not import .glb: the model is re-exported as FBX with two meshes under an empty root "Levier":
  - Levier_Socle: everything that does not move (the display plinth under the base is dropped), origin at the floor;
  - Levier_Manche: the animated hub and the handle, origin ON THE PIVOT, upright (the glb's animation is dropped:
    ElevatorLever drives the angle). The middle of the grip crossbar is 0.96 above the pivot (printed below, mirrored
    in ElevatorLever.GripHeight; an empty child would come out misplaced: the FBX exporter mishandles child empties).
Real size (~1.6 x 2.0 m base, 1.8 m tall): the scale is applied in Unity (ElevatorLever).

Unity (x, y, z) = Blender (-x, z, -y) after the export axes. The axle is the X axis. In Unity the hub rotates
around its local X: +angle tilts the handle towards +Z (the "OFF" side of the base), -angle towards -Z ("ON" side,
the front of the lever, which faces the user). Printed at the end: pivot height and grip position (Unity, model units).
"""
import bpy, sys
from mathutils import Vector, Matrix, Quaternion

args = sys.argv[sys.argv.index('--') + 1:]
GLB, FBX = args[0], args[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
scene = bpy.context.scene

hub = next(o for o in scene.objects if 'animated hub' in o.name)
# drop the animation and put the lever upright (rest pose = rotation identity)
for o in scene.objects:
    if o.animation_data:
        o.animation_data_clear()
hub.rotation_mode = 'QUATERNION'
hub.rotation_quaternion = Quaternion()
bpy.context.view_layer.update()

pivot = hub.matrix_world.translation.copy()
moving = [hub] + [o for o in hub.children_recursive if o.type == 'MESH']
grip = next(o for o in moving if 'crossbar' in o.name.lower())
grip_centre = sum((grip.matrix_world @ Vector(c) for c in grip.bound_box), Vector()) / 8

for o in list(scene.objects):
    if o.type == 'MESH' and 'Display plinth' in o.name:
        bpy.data.objects.remove(o)
    elif o.type not in ('MESH',):
        bpy.data.objects.remove(o)

# bake every mesh in world space, unparent
meshes = [o for o in scene.objects if o.type == 'MESH']
for o in meshes:
    o.data = o.data.copy()          # glb instances share data: make each unique before baking
    o.data.transform(o.matrix_world)
for o in meshes:
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
bpy.context.view_layer.update()

# ON / OFF lettering: turned half a turn on the plate so it reads right way up once the lever is wall-mounted
# (ElevatorLever: model up = out of the wall, OFF side up)
for o in meshes:
    if 'marking' in o.name.lower():
        c = sum((v.co for v in o.data.vertices), Vector()) / len(o.data.vertices)
        o.data.transform(Matrix.Translation(c) @ Matrix.Rotation(3.141592653589793, 4, 'Z') @ Matrix.Translation(-c))

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    j = bpy.context.view_layer.objects.active
    j.name = j.data.name = name
    return j

moving_names = {o.name for o in moving}
manche = join([o for o in scene.objects if o.type == 'MESH' and o.name in moving_names], 'Levier_Manche')
socle = join([o for o in scene.objects if o.type == 'MESH' and o is not manche], 'Levier_Socle')

# Manche: origin on the pivot
manche.data.transform(Matrix.Translation(-pivot))
manche.location = pivot

root = bpy.data.objects.new('Levier', None); scene.collection.objects.link(root)
socle.parent = root
manche.parent = root

for m in bpy.data.materials:
    m.name = m.name.replace('HV • ', 'Levier_').replace(' ', '_')

print('PIVOT unity', (-pivot.x, pivot.z, -pivot.y))
g = grip_centre - pivot
print('GRIP unity (from pivot)', (round(-g.x, 4), round(g.z, 4), round(-g.y, 4)))
print('POLYS socle', len(socle.data.polygons), 'manche', len(manche.data.polygons))

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, axis_forward='-Z', axis_up='Y',
                         apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         object_types={'MESH', 'EMPTY'}, mesh_smooth_type='FACE', add_leaf_bones=False,
                         bake_anim=False)
print('EXPORTED', FBX)
