using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Lighting of the life-size museum, whose rooms are closed by ceilings and lit through frosted skylights
    /// (<see cref="RoomCeiling"/>). While this object is active (visitor mode): the scene's sun comes from high above,
    /// as through the skylight, and the ambient light is even and warm (bounced off the parquet and white walls), so all
    /// four walls read alike. The workshop's lighting is put back when it is deactivated. No extra realtime light: cheap
    /// on Quest (the URP Performance config renders no additional light).
    /// </summary>
    public class SkylightLighting : MonoBehaviour
    {
        [Tooltip("Scene sun (directional light); RenderSettings.sun if empty.")]
        [SerializeField] Light sun;
        [Tooltip("Sun direction while visiting: nearly vertical, through the skylight.")]
        [SerializeField] Vector3 sunAngles = new(78f, 35f, 0f);
        [SerializeField] Color sunColor = new(1f, 0.97f, 0.92f);
        [SerializeField] float sunIntensity = 0.75f;
        [Header("Ambient (trilight)")]
        [Tooltip("From above: the skylight.")]
        [SerializeField] Color skyAmbient = new(0.86f, 0.88f, 0.9f);
        [Tooltip("Horizontal: light bounced between the white walls.")]
        [SerializeField] Color wallAmbient = new(0.8f, 0.78f, 0.74f);
        [Tooltip("From below: light bounced off the parquet (lights the ceiling).")]
        [SerializeField] Color floorAmbient = new(0.5f, 0.44f, 0.38f);

        bool applied;
        Quaternion savedRotation;
        Color savedColor, savedSky, savedEquator, savedGround;
        float savedIntensity, savedAmbientIntensity;
        AmbientMode savedMode;
        SphericalHarmonicsL2 savedProbe;

        void OnEnable()
        {
            if (sun == null) sun = RenderSettings.sun;
            savedMode = RenderSettings.ambientMode;
            savedSky = RenderSettings.ambientSkyColor;
            savedEquator = RenderSettings.ambientEquatorColor;
            savedGround = RenderSettings.ambientGroundColor;
            savedAmbientIntensity = RenderSettings.ambientIntensity;
            savedProbe = RenderSettings.ambientProbe;
            if (sun != null)
            {
                savedRotation = sun.transform.rotation;
                savedColor = sun.color;
                savedIntensity = sun.intensity;
                sun.transform.rotation = Quaternion.Euler(sunAngles);
                sun.color = sunColor;
                sun.intensity = sunIntensity;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyAmbient;
            RenderSettings.ambientEquatorColor = wallAmbient;
            RenderSettings.ambientGroundColor = floorAmbient;
            RenderSettings.ambientIntensity = 1f;
            applied = true;
        }

        void OnDisable()
        {
            if (!applied) return;
            applied = false;
            if (sun != null)
            {
                sun.transform.rotation = savedRotation;
                sun.color = savedColor;
                sun.intensity = savedIntensity;
            }
            RenderSettings.ambientMode = savedMode;
            RenderSettings.ambientSkyColor = savedSky;
            RenderSettings.ambientEquatorColor = savedEquator;
            RenderSettings.ambientGroundColor = savedGround;
            RenderSettings.ambientIntensity = savedAmbientIntensity;
            RenderSettings.ambientProbe = savedProbe;
        }
    }
}
