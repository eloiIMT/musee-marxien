"""Museum door for Unity, from MuseumAsset.fbx (Vira Voloshyn, https://viravoloshyn.itch.io).
Run: blender -b --python build_door.py -- <MuseumAsset.fbx> <out.fbx> [<preview.png>]
Takes the pilaster arch ("Arch7"): a two-faced passage 2.3 m deep. Keeps its front half (cut 0.9 m behind the front
face, in the passage) so it stands against a wall: the cut plane is the wall plane. The free-standing columns (pedestal,
shaft, capital: every loose part reaching further than 0.5 m from the wall) are removed: the arch frame, its jambs and
its top band stay (0.42 m deep). Output "Door": centred on x, floor at 0, wall plane at y = 0, arch in front of it
(y < 0), real size (4.06 x 4 m, opening ~1.45 x 2.95 m). The material is given in Unity (same as the walls)."""
import bpy, bmesh, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
preview = argv[2] if len(argv) > 2 else None
CUT = 0.9  # metres behind the front face

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
door = bpy.data.objects['Arch7']
for o in list(bpy.context.scene.objects):
    if o != door: bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active = door
door.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

me = door.data
vs = [v.co for v in me.vertices]
lo = Vector([min(v[i] for v in vs) for i in range(3)]); hi = Vector([max(v[i] for v in vs) for i in range(3)])
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=Vector((0, lo.y + CUT, 0)),
                       plane_no=Vector((0, 1, 0)), clear_outer=True)
off = Vector(((lo.x + hi.x) / 2, lo.y + CUT, lo.z))
for v in bm.verts: v.co -= off
# remove the columns: loose parts standing out more than 0.5 m from the wall
bm.verts.ensure_lookup_table()
seen, columns = set(), []
for v in bm.verts:
    if v.index in seen: continue
    stack, comp = [v], []
    seen.add(v.index)
    while stack:
        x = stack.pop(); comp.append(x)
        for e in x.link_edges:
            y = e.other_vert(x)
            if y.index not in seen: seen.add(y.index); stack.append(y)
    if min(p.co.y for p in comp) < -0.5: columns.extend(comp)
print('removed column vertices', len(columns))
bmesh.ops.delete(bm, geom=columns, context='VERTS')
bm.to_mesh(me); bm.free()
door.name = me.name = 'Door'
me.materials.clear(); me.materials.append(bpy.data.materials.new('Door'))
vs = [v.co for v in me.vertices]
print('door size', tuple(round(max(v[i] for v in vs) - min(v[i] for v in vs), 3) for i in range(3)),
      'y range', round(min(v.y for v in vs), 3), round(max(v.y for v in vs), 3))
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z',
                         axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                         bake_anim=False)
if preview:
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.show_cavity = True
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 6
    cam.location = (3.5, -6, 3.5); t = Vector((0, -0.4, 2))
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x = sc.render.resolution_y = 600
    sc.render.filepath = preview; bpy.ops.render.render(write_still=True)
