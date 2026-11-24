using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VirtualMuseum.App
{
    /// <summary>
    /// Tour editor on the workshop's computer screen (Assets/3D/EcranParcours.fbx, Kenney "Computer Screen"), standing on
    /// the workbench behind the user. Everything with the ray + TRIGGER:
    ///  - left column: the tours (select), "+ Nouveau parcours";
    ///  - right column, selected tour: "Proposer aux visiteurs" (backend session = the tour offered to visitors),
    ///    "Supprimer" (two clicks), "Renommer" (on-screen AZERTY keyboard in place of the list), and two tabs:
    ///      Étapes: ordered artworks, ↑ ↓ to reorder, × to remove; under each step its room and the rooms to cross
    ///              from the previous step (shortest path on the room connections), red when unreachable;
    ///      Ajouter: every artwork of the museum, "+" appends it to the tour.
    ///  - footer: whole route from the museum entrance (first room, where the visitor view starts).
    /// The UI is rebuilt (once per frame at most) whenever the museum or the session changes.
    /// Built on the "Display" child, facing its -Z, in metres; the model child is scaled by <see cref="modelScale"/>.
    /// </summary>
    public class TourScreen : MonoBehaviour
    {
        // Display face of EcranParcours.fbx at scale 1 (see Assets/3D/Source~/build_screen.py).
        const float DalleWidth = 0.379f, DalleHeight = 0.2337f, DalleCentreY = 0.1713f, DalleTilt = 8f;
        const int TourRows = 5, ListRows = 5;
        const float M = 0.025f;
        const int MaxNameLength = 32;
        static readonly Color ScreenColor = new Color(0.04f, 0.07f, 0.11f);
        static readonly Color RowA = new Color(0.07f, 0.1f, 0.15f), RowB = new Color(0.05f, 0.08f, 0.12f);
        static readonly Color TourColor = new Color(0.2f, 0.3f, 0.42f);
        static readonly Color NewColor = new Color(0.2f, 0.62f, 0.35f);
        static readonly Color OfferColor = new Color(0.2f, 0.45f, 0.75f);
        static readonly Color DeleteColor = new Color(0.75f, 0.25f, 0.22f);
        static readonly Color SmallColor = new Color(0.25f, 0.28f, 0.33f);

        [SerializeField] MuseumBackend backend;
        [Tooltip("Scale of the Kenney model (real size ~0.39 m wide).")]
        [SerializeField] float modelScale = 2.5f;

        enum Tab { Steps, Add }

        MuseumApi Api => backend.Api;
        float W => DalleWidth * modelScale;
        float H => DalleHeight * modelScale;

        Transform display, dynamicRoot;
        TextMesh status, tourName, route, emptyText, tourPageText, listPageText;
        UiButton newTour, offer, delete, rename, stepsTab, addTab, tourPrev, tourNext, listPrev, listNext;
        string selectedTourId;
        Tab tab = Tab.Steps;
        int tourPage, listPage;
        bool dirty = true;

        // Rename keyboard (static, shown in place of the tabs and list while renaming).
        Transform keyboard;
        TextMesh draftText;
        readonly List<(UiButton button, string ch)> charKeys = new List<(UiButton, string)>();
        UiButton shiftKey;
        bool renaming, shift;
        string draft = "", renameTourId;
        float confirmDeleteUntil, statusUntil;

        void Awake()
        {
            if (backend == null) backend = FindAnyObjectByType<MuseumBackend>();
            Build();
        }

        void OnEnable()
        {
            if (backend == null) return;
            Api.Changed += OnModelChanged;
            Api.SessionChanged += MarkDirty;
            dirty = true;
        }

        void OnDisable()
        {
            if (backend == null) return;
            Api.Changed -= OnModelChanged;
            Api.SessionChanged -= MarkDirty;
        }

        void OnModelChanged(MuseumChange _) => dirty = true;
        void MarkDirty() => dirty = true;

        // ---------------------------------------------------------------- static layout

        void Build()
        {
            display = new GameObject("Display").transform;
            display.SetParent(transform, false);
            display.SetLocalPositionAndRotation(new Vector3(0, DalleCentreY * modelScale, -0.004f), Quaternion.Euler(DalleTilt, 0, 0));
            // Backdrop over the model's display surface; its collider stops the ray on the screen.
            UiKit.Plate(display, "Backdrop", Vector3.zero, new Vector3(W, H, 0.004f), ScreenColor, collider: true);

            float left = -W / 2f + M, right = W / 2f - M, top = H / 2f - M;
            UiKit.Text(display, "PARCOURS DE VISITE", new Vector3(left, top + 0.005f, -0.004f), 0.036f, Color.white);
            status = UiKit.Text(display, "", new Vector3(right, top, -0.004f), 0.02f, UiKit.MutedText, TextAnchor.UpperRight, TextAlignment.Right);
            UiKit.Plate(display, "Separator", new Vector3(0, top - 0.058f, -0.003f), new Vector3(W - 2 * M, 0.003f, 0.001f), SmallColor);

            // Left column: tours.
            float colL = left + 0.14f;
            UiKit.Text(display, "Parcours", new Vector3(left, top - 0.075f, -0.004f), 0.022f, UiKit.MutedText);
            tourPrev = SmallButton("←", new Vector3(left + 0.03f, top - 0.42f, -0.01f));
            tourNext = SmallButton("→", new Vector3(left + 0.25f, top - 0.42f, -0.01f));
            tourPageText = UiKit.Text(display, "", new Vector3(colL, top - 0.42f, -0.004f), 0.02f, UiKit.MutedText, TextAnchor.MiddleCenter, TextAlignment.Center);
            tourPrev.Clicked += () => { tourPage--; dirty = true; };
            tourNext.Clicked += () => { tourPage++; dirty = true; };
            newTour = UiButton.Create(display, "+ Nouveau parcours", new Vector3(colL, -H / 2f + M + 0.025f, -0.01f), new Vector2(0.28f, 0.05f), NewColor, 0.024f);
            newTour.Clicked += CreateTour;

            // Right column: selected tour.
            float rx = left + 0.31f;
            tourName = UiKit.Text(display, "", new Vector3(rx, top - 0.075f, -0.004f), 0.03f, Color.white);
            offer = UiButton.Create(display, "Proposer aux visiteurs", new Vector3(right - 0.235f, top - 0.088f, -0.01f), new Vector2(0.215f, 0.042f), OfferColor, 0.019f);
            offer.Clicked += OfferTour;
            delete = UiButton.Create(display, "Supprimer", new Vector3(right - 0.06f, top - 0.088f, -0.01f), new Vector2(0.12f, 0.042f), DeleteColor, 0.019f);
            delete.Clicked += DeleteTour;
            stepsTab = UiButton.Create(display, "Étapes", new Vector3(rx + 0.085f, top - 0.135f, -0.01f), new Vector2(0.17f, 0.042f), TourColor, 0.021f);
            stepsTab.Clicked += () => { tab = Tab.Steps; listPage = 0; dirty = true; };
            addTab = UiButton.Create(display, "+ Ajouter", new Vector3(rx + 0.265f, top - 0.135f, -0.01f), new Vector2(0.17f, 0.042f), TourColor, 0.021f);
            addTab.Clicked += () => { tab = Tab.Add; listPage = 0; dirty = true; };
            rename = UiButton.Create(display, "Renommer", new Vector3(right - 0.09f, top - 0.135f, -0.01f), new Vector2(0.17f, 0.042f), TourColor, 0.021f);
            rename.Clicked += StartRename;
            BuildKeyboard(rx, right, top - 0.135f);

            listPrev = SmallButton("←", new Vector3(right - 0.17f, -H / 2f + M + 0.025f, -0.01f));
            listNext = SmallButton("→", new Vector3(right - 0.02f, -H / 2f + M + 0.025f, -0.01f));
            listPageText = UiKit.Text(display, "", new Vector3(right - 0.095f, -H / 2f + M + 0.025f, -0.004f), 0.02f, UiKit.MutedText, TextAnchor.MiddleCenter, TextAlignment.Center);
            listPrev.Clicked += () => { listPage--; dirty = true; };
            listNext.Clicked += () => { listPage++; dirty = true; };
            route = UiKit.Text(display, "", new Vector3(rx, -H / 2f + M + 0.068f, -0.004f), 0.018f, UiKit.TextColor);
            emptyText = UiKit.Text(display, "", new Vector3(rx, top - 0.18f, -0.004f), 0.024f, UiKit.MutedText);
        }

        UiButton SmallButton(string label, Vector3 pos) => UiButton.Create(display, label, pos, new Vector2(0.042f, 0.042f), SmallColor, 0.026f);

        /// <summary>Name field + AZERTY keys (10 per row) over the right column, from the tabs row down.</summary>
        void BuildKeyboard(float x0, float x1, float fieldY)
        {
            keyboard = new GameObject("Keyboard").transform;
            keyboard.SetParent(display, false);
            float w = x1 - x0, pitch = w / 10f, gap = 0.006f, rowStep = 0.054f;
            UiKit.Plate(keyboard, "Field", new Vector3(x0 + w / 2f, fieldY, -0.003f), new Vector3(w, 0.046f, 0.001f), RowA);
            draftText = UiKit.Text(keyboard, "", new Vector3(x0 + 0.012f, fieldY, -0.004f), 0.026f, Color.white, TextAnchor.MiddleLeft);

            float X(float units) => x0 + pitch * units; // centre of a key spanning [units - span/2, units + span/2]
            UiButton Key(string label, float centreUnits, float y, float spanUnits, Color color) =>
                UiButton.Create(keyboard, label, new Vector3(X(centreUnits), y, -0.01f), new Vector2(pitch * spanUnits - gap, rowStep - gap), color, 0.024f);
            void CharKey(string ch, float centreUnits, float y)
            {
                var b = Key(ch, centreUnits, y, 1, SmallColor);
                b.Clicked += () => Type(ch);
                charKeys.Add((b, ch));
            }

            string[] rows = { "1234567890", "azertyuiop", "qsdfghjklm", "wxcvbnéè'-" };
            float y0 = fieldY - 0.062f;
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r].Length; i++)
                    CharKey(rows[r][i].ToString(), i + 0.5f, y0 - r * rowStep);

            float y = y0 - rows.Length * rowStep;
            shiftKey = Key("Maj", 1f, y, 2, SmallColor);
            shiftKey.Clicked += () => { shift = !shift; RefreshKeyboard(); };
            CharKey("à", 2.5f, y);
            CharKey("ç", 3.5f, y);
            Key("Espace", 6f, y, 4, SmallColor).Clicked += () => Type(" ");
            Key("Effacer", 9f, y, 2, SmallColor).Clicked += Backspace;

            y -= rowStep + 0.01f;
            Key("Annuler", 5.5f, y, 3, SmallColor).Clicked += () => { renaming = false; dirty = true; };
            Key("Valider", 8.5f, y, 3, NewColor).Clicked += CommitRename;
            keyboard.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- dynamic content

        void LateUpdate()
        {
            if (confirmDeleteUntil > 0 && Time.time > confirmDeleteUntil) { confirmDeleteUntil = 0; dirty = true; }
            if (statusUntil > 0 && Time.time > statusUntil) { statusUntil = 0; status.text = ""; }
            if (!dirty || backend == null) return;
            dirty = false;
            Rebuild();
        }

        void Rebuild()
        {
            if (dynamicRoot != null) Destroy(dynamicRoot.gameObject);
            dynamicRoot = new GameObject("Dynamic").transform;
            dynamicRoot.SetParent(display, false);

            var data = Api.Data;
            var session = Api.Session;
            if (data.tours.Find(t => t.id == selectedTourId) == null)
                selectedTourId = (data.tours.Find(t => t.id == session.tourId) ?? data.tours.FirstOrDefault())?.id;
            var tour = data.tours.Find(t => t.id == selectedTourId);

            float left = -W / 2f + M, right = W / 2f - M, top = H / 2f - M;

            // Tours.
            int tourPages = Mathf.Max(1, Mathf.CeilToInt(data.tours.Count / (float)TourRows));
            tourPage = Mathf.Clamp(tourPage, 0, tourPages - 1);
            for (int i = 0; i < TourRows; i++)
            {
                int index = tourPage * TourRows + i;
                if (index >= data.tours.Count) break;
                var t = data.tours[index];
                var label = (t.id == session.tourId ? "• " : "") + UiKit.Wrap(Name(t), 20, 1) + $"  ({t.artworkIds.Count})";
                var b = UiButton.Create(dynamicRoot, label, new Vector3(left + 0.14f, top - 0.125f - i * 0.058f, -0.01f), new Vector2(0.28f, 0.05f), TourColor, 0.021f);
                b.Selected = t.id == selectedTourId;
                var id = t.id;
                b.Clicked += () => { selectedTourId = id; listPage = 0; confirmDeleteUntil = 0; renaming = false; dirty = true; };
            }
            bool pagedTours = tourPages > 1;
            tourPrev.gameObject.SetActive(pagedTours);
            tourNext.gameObject.SetActive(pagedTours);
            tourPrev.Interactable = tourPage > 0;
            tourNext.Interactable = tourPage < tourPages - 1;
            tourPageText.text = pagedTours ? $"{tourPage + 1} / {tourPages}" : "";
            if (data.tours.Count == 0)
                UiKit.Text(dynamicRoot, "Aucun parcours.", new Vector3(left, top - 0.115f, -0.004f), 0.022f, UiKit.MutedText);

            // Selected tour header.
            bool hasTour = tour != null;
            if (tour?.id != renameTourId) renaming = false; // renamed tour deleted meanwhile
            keyboard.gameObject.SetActive(renaming);
            foreach (var b in new[] { offer, delete, rename, stepsTab, addTab }) b.gameObject.SetActive(hasTour && !renaming);
            route.text = "";
            if (!hasTour)
            {
                tourName.text = "";
                emptyText.text = UiKit.Wrap("Créez un parcours avec « + Nouveau parcours », puis ajoutez-y des œuvres dans l'ordre de la visite.", 46);
                listPrev.gameObject.SetActive(false);
                listNext.gameObject.SetActive(false);
                listPageText.text = "";
                return;
            }
            tourName.text = UiKit.Wrap(Name(tour), 16, 1);
            if (renaming)
            {
                emptyText.text = "";
                listPrev.gameObject.SetActive(false);
                listNext.gameObject.SetActive(false);
                listPageText.text = "";
                return;
            }
            bool offered = tour.id == session.tourId;
            offer.SetLabel(offered ? "• Proposé aux visiteurs" : "Proposer aux visiteurs");
            offer.SetColor(offered ? NewColor : OfferColor);
            delete.SetLabel(confirmDeleteUntil > 0 ? "Confirmer ?" : "Supprimer");
            stepsTab.SetLabel($"Étapes ({tour.artworkIds.Count})");
            stepsTab.Selected = tab == Tab.Steps;
            addTab.Selected = tab == Tab.Add;
            stepsTab.SetColor(tab == Tab.Steps ? OfferColor : TourColor);
            addTab.SetColor(tab == Tab.Add ? OfferColor : TourColor);

            // List.
            var byId = data.artworks.ToDictionary(a => a.id);
            int count = tab == Tab.Steps ? tour.artworkIds.Count : data.artworks.Count;
            int listPages = Mathf.Max(1, Mathf.CeilToInt(count / (float)ListRows));
            listPage = Mathf.Clamp(listPage, 0, listPages - 1);
            bool pagedList = listPages > 1;
            listPrev.gameObject.SetActive(pagedList);
            listNext.gameObject.SetActive(pagedList);
            listPrev.Interactable = listPage > 0;
            listNext.Interactable = listPage < listPages - 1;
            listPageText.text = pagedList ? $"{listPage + 1} / {listPages}" : "";

            emptyText.text = count > 0 ? "" : tab == Tab.Steps
                ? UiKit.Wrap("Parcours vide : ouvrez l'onglet « + Ajouter » et choisissez les œuvres dans l'ordre de la visite.", 46)
                : "Le musée ne contient aucune œuvre.";

            var steps = tab == Tab.Steps ? StepInfos(data, tour, byId) : null;
            var artworks = tab == Tab.Add ? OrderedArtworks(data) : null;
            float rx = left + 0.31f, rowW = right - rx;
            for (int i = 0; i < ListRows; i++)
            {
                int index = listPage * ListRows + i;
                if (index >= count) break;
                float y = top - 0.195f - i * 0.058f;
                UiKit.Plate(dynamicRoot, "Row", new Vector3(rx + rowW / 2f, y - 0.002f, -0.003f), new Vector3(rowW, 0.054f, 0.001f), i % 2 == 0 ? RowA : RowB);
                if (tab == Tab.Steps) StepRow(tour, index, steps[index], rx, right, y);
                else AddRow(tour, artworks[index], data, rx, right, y);
            }

            if (tab == Tab.Steps) route.text = RouteText(data, steps);
        }

        // ---------------------------------------------------------------- rows

        struct StepInfo
        {
            public ArtworkInstance artwork;
            public Room room;
            public List<string> path; // from the previous step's room (or the entrance), both included; null = unreachable
        }

        static List<StepInfo> StepInfos(Museum data, VisitorTour tour, Dictionary<string, ArtworkInstance> byId)
        {
            var list = new List<StepInfo>();
            string previousRoom = data.rooms.Count > 0 ? data.rooms[0].id : null; // visitor view starts in the first room
            foreach (var id in tour.artworkIds)
            {
                byId.TryGetValue(id, out var a);
                var room = a != null ? data.rooms.Find(r => r.id == a.roomId) : null;
                var path = room != null && previousRoom != null ? MuseumPaths.ShortestPath(data, previousRoom, room.id) : null;
                list.Add(new StepInfo { artwork = a, room = room, path = path });
                if (room != null) previousRoom = room.id;
            }
            return list;
        }

        void StepRow(VisitorTour tour, int index, StepInfo s, float x0, float x1, float y)
        {
            var title = s.artwork != null ? s.artwork.title : "?";
            UiKit.Text(dynamicRoot, $"{index + 1}. " + UiKit.Wrap(title, 30, 1), new Vector3(x0 + 0.012f, y + 0.021f, -0.004f), 0.023f, Color.white);
            string sub = s.room != null ? RoomName(s.room) : "";
            if (s.path == null) sub += "  · inaccessible depuis l'étape précédente";
            else if (s.path.Count > 2) sub += "  · via " + string.Join(", ", s.path.Skip(1).Take(s.path.Count - 2).Select(id => RoomName(id)));
            UiKit.Text(dynamicRoot, UiKit.Wrap(sub, 50, 1), new Vector3(x0 + 0.012f, y - 0.006f, -0.004f), 0.016f, s.path == null ? UiKit.Error : UiKit.MutedText);

            var id = tour.id;
            var up = RowButton("↑", x1 - 0.135f, y);
            up.Interactable = index > 0;
            up.Clicked += () => Edit(() => Api.ReorderTourArtwork(id, index, index - 1));
            var down = RowButton("↓", x1 - 0.085f, y);
            down.Interactable = index < tour.artworkIds.Count - 1;
            down.Clicked += () => Edit(() => Api.ReorderTourArtwork(id, index, index + 1));
            var remove = RowButton("×", x1 - 0.028f, y, DeleteColor);
            remove.Clicked += () => Edit(() => Api.RemoveTourArtwork(id, index), $"« {title} » retirée du parcours.");
        }

        void AddRow(VisitorTour tour, ArtworkInstance a, Museum data, float x0, float x1, float y)
        {
            UiKit.Text(dynamicRoot, UiKit.Wrap(a.title, 34, 1), new Vector3(x0 + 0.012f, y + 0.021f, -0.004f), 0.023f, Color.white);
            var sub = RoomName(data.rooms.Find(r => r.id == a.roomId));
            var positions = tour.artworkIds.Select((id, i) => (id, i)).Where(p => p.id == a.id).Select(p => (p.i + 1).ToString()).ToList();
            if (positions.Count > 0) sub += $"  · déjà étape {string.Join(", ", positions)}";
            UiKit.Text(dynamicRoot, UiKit.Wrap(sub, 50, 1), new Vector3(x0 + 0.012f, y - 0.006f, -0.004f), 0.016f, UiKit.MutedText);
            var id = tour.id;
            var add = RowButton("+", x1 - 0.028f, y, NewColor);
            add.Clicked += () => Edit(() => Api.AddTourArtwork(id, a.id), $"« {a.title} » ajoutée (étape {tour.artworkIds.Count + 1}).");
        }

        UiButton RowButton(string label, float x, float y, Color? color = null) =>
            UiButton.Create(dynamicRoot, label, new Vector3(x, y, -0.01f), new Vector2(0.042f, 0.042f), color ?? SmallColor, 0.028f);

        static List<ArtworkInstance> OrderedArtworks(Museum data)
        {
            var roomOrder = data.rooms.Select((r, i) => (r.id, i)).ToDictionary(p => p.id, p => p.i);
            return data.artworks.OrderBy(a => roomOrder.TryGetValue(a.roomId, out var i) ? i : int.MaxValue)
                                .ThenBy(a => a.title).ToList();
        }

        string RouteText(Museum data, List<StepInfo> steps)
        {
            if (steps.Count == 0) return "";
            if (steps.Any(s => s.path == null))
                return $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Error)}>" +
                       UiKit.Wrap("Trajet impossible : une étape n'est reliée à la précédente par aucun passage. Reliez les salles sur le plan.", 42, 3) + "</color>";
            var rooms = new List<string>();
            foreach (var s in steps)
                foreach (var id in s.path)
                    if (rooms.Count == 0 || rooms[^1] != id) rooms.Add(id);
            int distinct = rooms.Distinct().Count();
            var text = "Trajet depuis l'entrée : " + string.Join(" → ", rooms.Select(id => RoomName(id))) +
                       $"  ({rooms.Count - 1} passage{(rooms.Count - 1 > 1 ? "s" : "")}, {distinct} salle{(distinct > 1 ? "s" : "")})";
            return UiKit.Wrap(text, 42, 3);
        }

        // ---------------------------------------------------------------- actions

        void CreateTour()
        {
            var names = new HashSet<string>(Api.Data.tours.Select(t => t.name));
            int n = 1;
            while (names.Contains("Parcours " + n)) n++;
            var t = Api.CreateTour("Parcours " + n);
            selectedTourId = t.id;
            renaming = false;
            tab = Tab.Add;
            listPage = 0;
            tourPage = int.MaxValue; // clamped to the last page, where the new tour is
            Status($"« {t.name} » créé : ajoutez des œuvres.", UiKit.Ok);
            dirty = true;
        }

        void OfferTour()
        {
            if (selectedTourId == null) return;
            if (Api.Session.tourId == selectedTourId) { Status("Ce parcours est déjà proposé aux visiteurs.", UiKit.MutedText); return; }
            Api.StartTour(selectedTourId);
            Status("Parcours proposé aux visiteurs.", UiKit.Ok);
        }

        void DeleteTour()
        {
            if (selectedTourId == null) return;
            if (confirmDeleteUntil <= 0)
            {
                confirmDeleteUntil = Time.time + 3f;
                dirty = true;
                return;
            }
            confirmDeleteUntil = 0;
            var name = Name(Api.GetTour(selectedTourId));
            Api.DeleteTour(selectedTourId);
            selectedTourId = null;
            Status($"« {name} » supprimé.", UiKit.MutedText);
        }

        void StartRename()
        {
            if (selectedTourId == null) return;
            draft = Api.GetTour(selectedTourId).name ?? "";
            if (draft.Length > MaxNameLength) draft = draft.Substring(0, MaxNameLength);
            shift = draft.Length == 0;
            renaming = true;
            renameTourId = selectedTourId;
            confirmDeleteUntil = 0;
            RefreshKeyboard();
            dirty = true;
        }

        void Type(string ch)
        {
            if (draft.Length >= MaxNameLength)
            {
                Sounds.Play(Sounds.Error, 0.5f);
                Status($"{MaxNameLength} caractères maximum.", UiKit.Error);
                return;
            }
            draft += shift ? ch.ToUpperInvariant() : ch;
            shift = false;
            RefreshKeyboard();
        }

        void Backspace()
        {
            if (draft.Length > 0) draft = draft.Substring(0, draft.Length - 1);
            if (draft.Length == 0) shift = true; // capitalise the first letter
            RefreshKeyboard();
        }

        void RefreshKeyboard()
        {
            draftText.text = draft + "_";
            foreach (var (button, ch) in charKeys) button.SetLabel(shift ? ch.ToUpperInvariant() : ch);
            shiftKey.Selected = shift;
            shiftKey.SetColor(shift ? OfferColor : SmallColor);
        }

        void CommitRename()
        {
            var name = draft.Trim();
            if (name.Length == 0)
            {
                Sounds.Play(Sounds.Error, 0.5f);
                Status("Le nom ne peut pas être vide.", UiKit.Error);
                return;
            }
            var id = renameTourId;
            var description = Api.GetTour(id).description;
            renaming = false;
            dirty = true;
            Edit(() => Api.EditTour(id, name, description), $"Parcours renommé « {name} ».");
        }

        void Edit(System.Action change, string message = null)
        {
            try
            {
                change();
                if (message != null) Status(message, UiKit.Ok);
            }
            catch (System.ArgumentException e)
            {
                Sounds.Play(Sounds.Error, 0.5f);
                Status(e.Message, UiKit.Error);
            }
        }

        void Status(string message, Color color)
        {
            status.text = UiKit.Wrap(message, 44, 1);
            status.color = color;
            statusUntil = Time.time + 3.5f;
        }

        static string Name(VisitorTour t) => t == null ? "" : string.IsNullOrWhiteSpace(t.name) ? "Sans nom" : t.name;
        static string RoomName(Room r) => r == null ? "?" : string.IsNullOrWhiteSpace(r.name) ? "Salle sans nom" : r.name;
        string RoomName(string id) => RoomName(Api.GetRoom(id));
    }
}
