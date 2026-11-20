using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Provisional way into the visitor view: a button in the workshop (ray + trigger) that teleports the user into
    /// the life-size museum. Built facing -Z of this transform.
    /// </summary>
    public class VisitorTeleportButton : MonoBehaviour
    {
        [SerializeField] VisitorMode mode;

        TextMesh message;
        float messageUntil;

        void Awake()
        {
            if (mode == null) mode = FindAnyObjectByType<VisitorMode>();
            var button = UiButton.Create(transform, "Vue visiteur", Vector3.zero, new Vector2(0.22f, 0.08f), new Color(0.2f, 0.45f, 0.75f), 0.03f);
            button.Clicked += Go;
            message = UiKit.Text(transform, "", new Vector3(0, -0.07f, -0.01f), 0.022f, UiKit.Error, TextAnchor.UpperCenter, TextAlignment.Center);
        }

        void Go()
        {
            if (!mode.EnterVisitor(out var error))
            {
                Sounds.Play(Sounds.Error, 0.6f);
                message.text = UiKit.Wrap(error, 40);
                messageUntil = Time.time + 4f;
            }
        }

        void Update()
        {
            if (message.text.Length > 0 && Time.time > messageUntil) message.text = "";
        }
    }
}
