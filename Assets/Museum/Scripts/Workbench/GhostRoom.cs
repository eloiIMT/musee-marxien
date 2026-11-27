using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Preview of where a room will land: a green (valid) or red (refused) outline on the plan,
    /// a dimension / reason label, and a vertical line linking the hand to the preview.
    /// Lives in museum coordinates under the miniature.
    /// </summary>
    public class GhostRoom : MonoBehaviour
    {
        static readonly Color Valid = new Color(0.2f, 1f, 0.4f);
        static readonly Color Invalid = new Color(1f, 0.2f, 0.15f);
        const float BarThickness = 0.25f;

        Maquette maquette;
        Transform[] bars;
        TextMesh label;
        LineRenderer handLine;

        public void Init(Maquette owner)
        {
            maquette = owner;
            bars = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Bar";
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = owner.HandleMaterial;
                go.transform.SetParent(transform, false);
                bars[i] = go.transform;
            }
            label = Labels.Create(transform, "", 0.025f, Color.white);
            var lineGo = new GameObject("Hand Line");
            lineGo.transform.SetParent(transform, false);
            handLine = lineGo.AddComponent<LineRenderer>();
            handLine.useWorldSpace = true;
            handLine.positionCount = 2;
            handLine.widthMultiplier = 0.003f;
            handLine.sharedMaterial = owner.HandleMaterial;
            Hide();
        }

        /// <param name="center">Footprint centre, museum coordinates.</param>
        /// <param name="height">Height of the outline above the floor (museum metres).</param>
        /// <param name="text">Label (dimensions, reason); null = none.</param>
        /// <param name="hand">World hand position to link with a line; null = no line.</param>
        /// <param name="colorOverride">Neutral colour (e.g. the object held in the hand) instead of green / red.</param>
        /// <param name="outline">False: label (and hand line) only, no outline.</param>
        public void Show(Vector3 center, float yaw, float width, float length, bool valid, string text = null,
                         Vector3? hand = null, float height = 0.5f, Color? colorOverride = null, bool outline = true)
        {
            gameObject.SetActive(true);
            foreach (var b in bars) b.gameObject.SetActive(outline);
            transform.SetLocalPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
            float hw = width / 2f, hl = length / 2f, t = BarThickness;
            Set(bars[0], new Vector3(0, height, hl), new Vector3(width + t, t, t));
            Set(bars[1], new Vector3(0, height, -hl), new Vector3(width + t, t, t));
            Set(bars[2], new Vector3(hw, height, 0), new Vector3(t, t, length + t));
            Set(bars[3], new Vector3(-hw, height, 0), new Vector3(t, t, length + t));
            var color = colorOverride ?? (valid ? Valid : Invalid);
            foreach (var b in bars) Tint.Set(b.GetComponent<Renderer>(), color);

            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
            label.text = text ?? "";
            label.color = color;
            label.transform.localPosition = new Vector3(0, height + 1.5f, 0);
            Labels.SetWorldHeight(label, 0.025f);

            handLine.enabled = hand.HasValue;
            if (hand.HasValue)
            {
                handLine.SetPosition(0, hand.Value);
                handLine.SetPosition(1, transform.TransformPoint(new Vector3(0, height, 0)));
                Tint.Set(handLine, color);
            }
        }

        public void Hide() => gameObject.SetActive(false);

        static void Set(Transform t, Vector3 position, Vector3 scale)
        {
            t.localPosition = position;
            t.localScale = scale;
        }
    }
}
