import bpy, math, os
from mathutils import Vector
def bbox(o):
    bb=[o.matrix_world@Vector(c) for c in o.bound_box]
    return Vector([min(v[i] for v in bb) for i in range(3)]), Vector([max(v[i] for v in bb) for i in range(3)])
def mats(o): return [m.name for m in o.data.materials if m]
def setup_render(res=(1280,800)):
    sc=bpy.context.scene
    sc.render.engine='BLENDER_WORKBENCH'
    sc.display.shading.light='STUDIO'; sc.display.shading.color_type='MATERIAL'
    sc.display.shading.show_shadows=False; sc.display.shading.show_cavity=True
    sc.render.resolution_x,sc.render.resolution_y=res
    if not sc.world: sc.world=bpy.data.worlds.new('w')
    cam=bpy.data.objects.get('cam')
    if not cam:
        cam=bpy.data.objects.new('cam',bpy.data.cameras.new('cam')); sc.collection.objects.link(cam)
    sc.camera=cam; return cam
def shot(out,name,pos,target,lens=18,ortho=None):
    sc=bpy.context.scene; cam=sc.camera
    cam.data.type='ORTHO' if ortho else 'PERSP'
    if ortho: cam.data.ortho_scale=ortho
    cam.data.lens=lens
    cam.location=pos; d=Vector(target)-Vector(pos)
    cam.rotation_euler=d.to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=os.path.join(out,name+'.png'); bpy.ops.render.render(write_still=True)
