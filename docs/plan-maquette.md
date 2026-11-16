# Plan détaillé — Maquette, salles, génération (partie Eloi)

Périmètre : représentation du musée (2 échelles), maquette sur table, édition des salles et connexions, ligne de parcours.
Hors périmètre : locomotion visiteur, écran UI, gacha/bac, guidage (mais ils consomment `MuseumView`).

Échéances : **J1 16/11** étapes 0-3 · **J2 20/11** étapes 4-7 (scénario testable) · **23/11** étape 8 + polish.

---

## 0. Organisation du code (30 min)

```
Assets/Museum/                 ← notre code, séparé de Assets/VirtualMuseum (backend, intouché)
  Scripts/
    View/       MuseumView.cs, RoomView.cs, ConnectionView.cs, ArtworkView.cs, TourPathView.cs
    Model/      MuseumGeometry.cs        (calculs purs : murs, chevauchement, aimantation)
    Workbench/  RoomSelection.cs, RoomGizmo.cs, MoveHandle.cs, ResizeHandle.cs,
                HeightHandle.cs, ConnectHandle.cs, RoomCreator.cs, GhostRoom.cs
  Prefabs/      RoomView.prefab, Gizmo.prefab, Ghost.prefab, Cable.prefab
  Materials/    Floor, Wall, WallLow, Ghost_OK (vert transp.), Ghost_KO (rouge transp.), Cable, Highlight
  Scenes/       Sandbox_Maquette.unity  (ta scène de travail, merge plus tard dans VirtualMuseum.unity)
```

Règles :
- Namespace `VirtualMuseum.App`.
- Une scène sandbox à toi pour éviter les conflits de merge Unity sur la scène principale.
- `MuseumGeometry` = fonctions statiques sans MonoBehaviour → testables en EditMode.

---

## 1. MuseumView — lecture seule (≈ 3 h) ★ priorité absolue

### 1.1 MuseumView.cs
```csharp
public class MuseumView : MonoBehaviour {
    [SerializeField] MuseumBackend backend;
    [SerializeField] Transform museumRoot;        // échelle 1 (immersif) ou 0.02 (maquette)
    [SerializeField] bool miniature;              // pilote murs bas / colliders / épaisseurs
    [SerializeField] RoomView roomPrefab;
    [SerializeField] ConnectionView cablePrefab;

    readonly Dictionary<string, RoomView> rooms = new();
    readonly Dictionary<string, ConnectionView> cables = new(); // clé "idA|idB" triée

    public event Action Rebuilt;                   // pour gizmo/sélection
    public RoomView GetRoomView(string id);
    public bool TryGetRoomAt(Vector3 worldPos, out RoomView rv);

    void OnEnable()  { backend.Api.Changed += Refresh; Refresh(); }
    void OnDisable() { backend.Api.Changed -= Refresh; }
    void Refresh() { var data = backend.Api.Data; SyncRooms(data); SyncCables(data); Rebuilt?.Invoke(); }
}
```

### 1.2 Algorithme de réconciliation (SyncRooms)
1. `seen = new HashSet<string>()`.
2. Pour chaque `room` de `data.rooms` : si absente du dict → `Instantiate(roomPrefab, museumRoot)`; puis `rv.Apply(room, data)`; `seen.Add(id)`.
3. Pour chaque clé du dict ∉ `seen` → `Destroy` + remove.
4. Même schéma pour les câbles (clé = ids triés joints par `|`).

⚠ Ne pas tout détruire/recréer à chaque `Changed` : casse la sélection et les références tenues par l'interacteur.

### 1.3 RoomView.cs
- `string RoomId`, `Room Data` (dernière copie lue).
- `Apply(Room r, Museum m)` :
  - `transform.localPosition = r.position; localRotation = r.rotation;` (local à `museumRoot`).
  - Sol : cube `(w, 0.05, l)` centré en `(0, -0.025, 0)`.
  - 4 murs : épaisseur `0.1` ; hauteur = `expanded ? r.height : lowWallHeight` (0.3 m réel).
    - Nord `(0, h/2, l/2)` taille `(w, h, e)` ; Sud idem en `-l/2` ; Est `(w/2, h/2, 0)` taille `(e, h, l)` ; Ouest idem.
  - Étiquette nom (TextMeshPro) au-dessus du sol, orientée vers le haut (lisible en vue de dessus).
  - Œuvres : sous-dictionnaire `id → ArtworkView`, même réconciliation sur `m.artworks.Where(a => a.roomId == r.id)`.
- `bool Expanded { set; }` → recalcule hauteurs murs + `artworksRoot.SetActive(value)`.

### 1.4 ArtworkView.cs
- Créé via `backend.InstantiateArtwork(id, roomView.ArtworksRoot)`.
- `Apply(ArtworkInstance a)` : `localPosition/localRotation/localScale`.
- Si `definitionId` change (n'arrive pas en pratique) → détruire/recréer.
- Collider ajouté si absent (pour pointage rayon).

### 1.5 ConnectionView.cs
- `LineRenderer`, 2 points. V1 : centres des sols à hauteur `0.2`.
- V2 (étape 6) : milieux des murs les plus proches (voir `MuseumGeometry.ClosestWallPoints`).
- Largeur en mètres **monde** : `0.02 / museumRoot.lossyScale.x` sinon invisible en miniature.

### ✅ Test étape 1
- `Reset Example`, Play → 3 salles, 6 œuvres, câbles.
- Dupliquer le GameObject : `MuseumView` A à scale 1, B à scale 0.02 sur la table → identiques.
- Dans un script de debug, appeler `Api.ResizeRoom(...)` au clavier → les deux vues suivent.
- `Load` depuis menu contextuel → vues rafraîchies sans doublon.

→ **Pousser sur git et prévenir le groupe dès ici.**

---

## 2. Table et maquette (≈ 1 h)

- Table : cube `1.6 × 0.9 × 1.0 m`, plateau à **0.85 m** (debout, Quest).
- `MaquetteRoot` enfant de la table, posé sur le plateau, `localScale` réglé auto :
  `scale = min(tableW / museumW, tableL / museumL) * 0.9` avec bornes musée = union des `MuseumBounds(room)`.
  - Recalcul **seulement** au chargement / reset (pas à chaque `Changed`, sinon la maquette "respire" pendant l'édition).
  - Fallback fixe `0.02` si musée vide.
- Centre de la maquette = centre des bornes (décaler `MaquetteRoot` de `-center * scale`).
- Grille au sol de la table (texture 0.5 m réel × scale) → repère pour l'aimantation.

### ✅ Test
Musée exemple tient entièrement sur la table, marge visible, lisible en vue de dessus depuis 0.6 m.

---

## 3. Plan 2D / salle active en 3D (≈ 1 h 30)

- `RoomSelection.cs` (singleton maquette) : `Hovered`, `Selected`, événements `SelectionChanged`.
- Règle : `rv.Expanded = (rv == Selected) || (rv == Hovered && Selected == null)`.
- Collider : BoxCollider sur le sol uniquement (évite que les murs 3D bloquent le rayon vers d'autres salles).
- Survol : contour/émissif sur le sol (`Highlight`). Sélection : couleur plus forte + gizmo.
- Interacteur : `XRSimpleInteractable` sur chaque sol ; `hoverEntered/Exited`, `selectEntered` → `RoomSelection`.
- Désélection : gâchette sur zone vide de la table (collider plateau avec son propre interactable).
- Sélection persistante après `Refresh` : stocker l'**id**, pas la référence.

### ✅ Test
Pointer plusieurs salles : une seule en 3D à la fois, autres restent plates ; sélection survit à un `ResizeRoom`.

---

## 4. Gizmo + déplacement / orientation (≈ 4 h)

### 4.1 RoomGizmo.cs
Instancié une fois, se re-parente sous la salle sélectionnée, caché sinon. Contient :
| Poignée | Position locale salle | Comportement |
|---|---|---|
| Logo central | `(0, 0.1, 0)` | Move/rotate |
| Coin | `(w/2, 0, l/2)` | Resize w/l |
| Hauteur | `(0, h, 0)` (ou au coin) | Resize h |
| 4 × connecteur | milieux des murs | Câble |

Taille des poignées **constante en monde** (~3 cm) : compenser `lossyScale`.

### 4.2 MoveHandle.cs (logo)
- `XRGrabInteractable`, `trackPosition/Rotation = true`, **force grab** activé sur le Near-Far, `throwOnDetach = false`, `movementType = Instantaneous`.
- Pendant la saisie (`Update`) :
  1. `hand = transform` du logo tenu.
  2. Projection verticale sur le plateau → `p = museumRoot.InverseTransformPoint(projected)` ; `p.y = 0`.
  3. `p = Snap(p, 0.5f)`.
  4. `yaw = SnapAngle(hand.eulerAngles.y - museumRoot.eulerAngles.y, 15f)`.
  5. `GhostRoom.Show(room.width, room.length, p, yaw)` ; couleur = `MuseumGeometry.Overlaps(...) ? KO : OK`.
- `selectExited` :
  - OK → `Api.SetRoomTransform(id, p, Quaternion.Euler(0, yaw, 0))`.
  - KO → rien (la vue n'a pas bougé, seule la poignée revient).
  - Toujours : remettre le logo à sa place locale, cacher le fantôme.
- ⚠ La `RoomView` ne bouge **pas** pendant le drag → pas de conflit avec `Refresh`.

### 4.3 MuseumGeometry (pur, testable)
```csharp
static Vector3 Snap(Vector3 p, float step);
static float SnapAngle(float deg, float step);
static Vector2[] FootprintCorners(Vector3 pos, float yaw, float w, float l);   // XZ
static bool Overlaps(corners A, corners B, float margin);  // SAT 2D sur rectangles orientés, margin = 1 m
static bool OverlapsAny(Museum m, string ignoreId, Vector3 pos, float yaw, float w, float l, float margin);
```
Écrire 4-5 tests EditMode (rect alignés, tournés 45°, contact à 0.9 m = KO, 1.1 m = OK).

### ✅ Test
Saisir le logo d'une salle lointaine (force grab), la poser : aimantée, crans 15°, œuvres suivent ; poser sur une autre salle → rouge → revient.

---

## 5. Redimensionnement (≈ 3 h)

### 5.1 ResizeHandle.cs (coin)
- Au début du drag : mémoriser le **coin opposé** en coordonnées musée `anchor = ToMuseum(room, (-w/2, 0, -l/2))` (signes selon le coin saisi).
- Pendant : point main → `local = MuseumSpatial.ToRoom(room, museumPoint)` (repère de la salle, rotation conservée).
  - `newW = max(2, Snap(|local.x - anchorLocal.x|, 0.5))`, idem `newL`.
  - Nouveau centre local `= (anchorLocal + cornerLocal)/2` → `ToMuseum` → position.
  - Fantôme de la nouvelle emprise + cotes `"6.5 × 4.0 m"` (TextMeshPro au-dessus).
  - Œuvres hors `[−w/2, w/2]×[−l/2, l/2]` → `ArtworkView.SetWarning(true)` (rouge).
- Relâcher (si pas de chevauchement) : `Api.ResizeRoom(id, newW, newL, h)` **puis** `Api.SetRoomTransform(id, newCenter, rot)`.
  - 2 événements `Changed` → OK, la réconciliation encaisse.

### 5.2 HeightHandle.cs
Drag vertical uniquement, `newH = clamp(Snap(y, 0.5), 2, 8)`, cote affichée, `ResizeRoom(id, w, l, newH)`.

### ✅ Test
Agrandir vers l'est : mur ouest immobile ; réduire sous une œuvre → œuvre rouge, non déplacée ; minimum 2 m respecté.

---

## 6. Création / suppression de salle, connexions (≈ 4 h)

### 6.1 RoomCreator.cs
- Déclenché par la « gachette » de l'atelier (objet physique ou bouton) → mode création.
- Fantôme 6×6 suit le point d'impact du rayon sur le plateau (snap 0.5).
- Gâchette : si OK → `Api.CreateRoom($"Salle {n}", p)` ; sinon son d'erreur.
- Sortie du mode : B / grip / après une pose.
- Nom auto : lettre suivante (`A, B, C…`) basée sur les noms existants.

### 6.2 Suppression
- Bouton ✕ sur le gizmo (ou action sur l'écran du binôme) → confirmation (2ᵉ appui dans 2 s, bouton devient rouge) → `Api.DeleteRoom(id)`.
- Afficher le nombre d'œuvres perdues dans la confirmation (`GetArtworksInRoom(id).Count`).

### 6.3 ConnectHandle.cs (câble)
- Grab sur un connecteur → `LineRenderer` temporaire du connecteur à la main.
- Survol d'une autre salle (raycast/`TryGetRoomAt` sous la main) → salle cible surlignée.
- Relâcher sur une salle ≠ source → `Api.ConnectRooms(a, b)` (déjà connectées = no-op + feedback).
- Point d'attache : stocker hors modèle dans `Dictionary<pairKey, (Vector3 localA, Vector3 localB)>` pour la session ; fallback `ClosestWallPoints`.

### 6.4 Déconnexion
- Câble = `MeshCollider`/capsule + `XRSimpleInteractable` → gâchette = surligné, exposer `SelectedConnection` (événement) pour le bouton « Déconnecter » de l'écran.
- En attendant l'écran : bouton ✕ flottant au milieu du câble.

### ✅ Test
Créer 2 salles, relier en réseau avec une boucle (A-B, B-C, C-A) + une branche (B-D) ; supprimer B → câbles de B disparus.

---

## 7. Ligne de parcours sur maquette (≈ 1 h 30)

- `TourPathView.cs` : écoute `Changed` + `SessionChanged` ; lit `Session.tourId` (ou parcours sélectionné côté écran).
- `GetTourArtworks(tourId)` → positions monde via `MuseumSpatial.ToWorld(room, a.localPosition, museumRoot)`.
- `LineRenderer` légèrement au-dessus des murs bas + pastilles numérotées (1, 2, 3…) au-dessus de chaque œuvre, visibles même salle non dépliée.
- Œuvres du parcours visibles en icône (petit disque coloré) dans les salles plates.

---

## 8. Intégration et robustesse (≈ 2 h, 20-23/11)

- [ ] Fusionner `Sandbox_Maquette` dans `VirtualMuseum.unity` (prefab `Workbench` unique).
- [ ] Fournir au binôme visiteur : `MuseumView` à l'échelle 1 + `GetRoomView(id)` + `TryGetRoomAt`.
- [ ] Load : recalcul échelle maquette, désélection, fantômes cachés.
- [ ] Musée vide : pas d'exception, création possible.
- [ ] Salles qui se chevauchent dans un JSON chargé : affichage OK, juste contour rouge.
- [ ] Profiler rapide : `Refresh` < 2 ms avec 15 salles / 50 œuvres.
- [ ] Test casque : lisibilité étiquettes, précision rayon au fond de la table, taille poignées.

---

## Ordre de priorité si retard

1 → 2 → 3 → 4 → 6.1/6.3 → 5 → 7 → 6.2/6.4 → 8.
Minimum démontrable : voir, déplacer, créer, connecter, redimensionner.

## Pièges connus

- Copies du modèle : jamais muter `Data`/`GetRoom`, toujours appeler l'API.
- Rotation : le backend accepte un `Quaternion` complet → forcer un lacet pur `Euler(0, yaw, 0)`.
- Échelle : épaisseurs de murs, largeur de lignes, poignées, textes → tout compenser par `lossyScale` en miniature.
- Force grab + objet enfant d'une maquette scalée : le `XRGrabInteractable` détache l'objet → toujours le re-parenter et remettre `localScale` au relâchement.
- `ResizeRoom` puis `SetRoomTransform` = 2 refresh : ne pas stocker de références `RoomView` entre les deux.
- Conflits Unity : 1 scène par personne, prefabs pour partager.

## Justifications à noter au fil de l'eau (pour le rapport)

| Choix | Alternative écartée | Raison |
|---|---|---|
| Plan 2D + salle active 3D | Maquette 3D complète | Occultations, sélection précise |
| Échelle auto sans pan/zoom | World-in-Miniature avec pan/zoom | Simplicité, vue d'ensemble constante ; limite : précision au fond |
| Force grab + fantôme | Manipulation directe à distance (rayon) | Amplitude de geste réduite, retour clair du point d'atterrissage |
| API appelée au relâchement | Mise à jour continue | Pas de spam d'événements, annulation facile |
| Aimantation 0.5 m / 15° | Placement libre | Précision malgré le tremblement de la main en miniature |
