# Todo — Maquette et salles

Checklist actionnable. Détails techniques : [plan-maquette.md](plan-maquette.md).
Légende : `[x]` fait · `[ ]` à faire · ⏱ estimation · 🧪 test de validation.

---

## Étape 1 — Afficher le musée (MuseumView) · ⏱ 3 h · 16/11

- [x] `RoomView` : sol + 4 murs générés, `Apply(room)`, `Expanded`
- [x] `RoomView.SyncArtworks` : œuvres réconciliées par id
- [x] `MuseumView` : abonnement `Changed`, réconciliation salles
- [x] Câbles : 1 `LineRenderer` par paire, clé triée
- [x] Menu `Tools > Virtual Museum > Create Maquette Sandbox`
- [ ] Compiler, 0 erreur Console
- [ ] Créer `Assets/Museum/Scenes/Sandbox_Maquette.unity` + lancer le menu
- [ ] 🧪 Play : 3 salles, 6 œuvres, câbles, en double (immersif + maquette)
- [ ] 🧪 Reset Example / Save / Load : pas de doublon
- [ ] Vérifier épaisseur câbles sur maquette
- [ ] Commit + push branche `maquette`, prévenir le groupe

---

## Étape 2 — Table et échelle automatique · ⏱ 1 h · 16/11

- [ ] Script `MaquetteFitter` sur la racine maquette
  - [ ] Calculer bornes du musée (union `MuseumSpatial.MuseumBounds`)
  - [ ] `scale = min(tableW / museeW, tableL / museeL) × 0.9`
  - [ ] Centrer la maquette sur le plateau (`-centre × scale`)
  - [ ] Fallback échelle fixe si musée vide
- [ ] Recalcul uniquement au démarrage, Load, Reset (bouton/méthode publique `Fit()`)
- [ ] Grille visuelle sur le plateau (pas de 0,5 m réel)
- [ ] Ajouter un XR Origin dans la sandbox, face à la table
- [ ] 🧪 Au casque : musée entier visible, lisible depuis 0,6 m, pas besoin de se pencher

---

## Étape 3 — Plan 2D + sélection · ⏱ 1 h 30 · 16/11

- [ ] `MuseumView` maquette : `startExpanded = false` (murs bas)
- [ ] Désactiver colliders des murs en maquette (rayon passe au-dessus)
- [ ] `RoomSelection` : `HoveredId`, `SelectedId`, événement `SelectionChanged`
- [ ] `XRSimpleInteractable` ajouté au sol de chaque salle (par `MuseumView` ou un composant dédié)
  - [ ] Survol → `HoveredId`
  - [ ] Gâchette → `SelectedId`
- [ ] Règle : `Expanded = sélectionnée || (survolée && rien de sélectionné)`
- [ ] Surbrillance sol : survol (léger), sélection (fort)
- [ ] Interactable sur le plateau vide → désélection
- [ ] Rebrancher la sélection après `Refreshed` (par id)
- [ ] 🧪 Une seule salle en 3D à la fois ; sélection survit à un `ResizeRoom`

---

## Étape 4 — Gizmo + déplacer/orienter · ⏱ 4 h · 20/11

- [ ] `MuseumGeometry` (statique, pur)
  - [ ] `Snap(Vector3, step)`, `SnapAngle(deg, step)`
  - [ ] `FootprintCorners(pos, yaw, w, l)`
  - [ ] `Overlaps(A, B, margin)` (SAT 2D rectangles orientés)
  - [ ] `OverlapsAny(museum, ignoreId, pos, yaw, w, l, margin)`
- [ ] Tests EditMode `MuseumGeometry` (alignés, 45°, 0,9 m KO, 1,1 m OK)
- [ ] `GhostRoom` : rectangle transparent vert/rouge
- [ ] `RoomGizmo` : suit la salle sélectionnée, caché sinon, poignées taille constante (compense `lossyScale`)
- [ ] `MoveHandle` (logo central)
  - [ ] `XRGrabInteractable` + force grab sur Near-Far Interactor
  - [ ] Chaque frame : projection plateau → coord musée → snap 0,5 m → yaw cran 15°
  - [ ] Fantôme vert/rouge selon `OverlapsAny` (marge 1 m)
  - [ ] Relâcher : vert → `SetRoomTransform` ; rouge → rien + son
  - [ ] Remettre la poignée en place, re-parenter, cacher fantôme
- [ ] 🧪 Saisir salle au fond de la table, poser, œuvres suivent ; poser sur autre salle → refus

---

## Étape 5 — Redimensionner · ⏱ 3 h · 20/11

- [ ] `ResizeHandle` (coin)
  - [ ] Début : mémoriser coin opposé (repère salle)
  - [ ] Pendant : main → `MuseumSpatial.ToRoom`, nouvelles w/l snap 0,5, min 2 m
  - [ ] Nouveau centre = milieu coin fixe / main
  - [ ] Fantôme + cotes « 6.5 × 4.0 m »
  - [ ] Œuvres hors murs en rouge (`LocalBounds`)
  - [ ] Relâcher : `ResizeRoom` puis `SetRoomTransform` (si pas de chevauchement)
- [ ] `HeightHandle` : glissement vertical, snap 0,5, borné 2–8 m, cote affichée
- [ ] `ArtworkView`/marqueur d'avertissement rouge
- [ ] 🧪 Agrandir vers l'est : mur ouest fixe ; réduire sous une œuvre → rouge, non déplacée

---

## Étape 6 — Créer, supprimer, connecter · ⏱ 4 h · 20/11

- [ ] `RoomCreator`
  - [ ] Déclencheur (bouton/objet « gachette » de l'atelier)
  - [ ] Fantôme 6×6 suit l'impact du rayon, snap
  - [ ] Gâchette : vert → `CreateRoom("Salle X", p)` ; rouge → son
  - [ ] Nom auto (lettre suivante)
  - [ ] Sortie du mode (B ou après pose)
- [ ] Suppression : bouton ✕ du gizmo, confirmation 2 appuis, nombre d'œuvres perdues affiché → `DeleteRoom`
- [ ] `ConnectHandle` (4 milieux de murs)
  - [ ] Saisie → ligne temporaire jusqu'à la main
  - [ ] Salle sous la main surlignée
  - [ ] Relâcher sur autre salle → `ConnectRooms` (déjà connectée → message)
  - [ ] Points d'attache mémorisés hors modèle (session), fallback murs les plus proches
- [ ] Câbles : collider + `XRSimpleInteractable`, sélection surlignée
- [ ] Événement `SelectedConnection` pour l'écran + bouton ✕ temporaire → `DisconnectRooms`
- [ ] 🧪 Réseau avec boucle A-B-C-A + branche B-D ; supprimer B → câbles disparus

---

## Étape 7 — Parcours sur maquette · ⏱ 1 h 30 · 20/11

- [ ] `TourPathView` : écoute `Changed` + `SessionChanged`
- [ ] Parcours affiché = sélectionné côté écran (sinon `Session.tourId`) → se mettre d'accord avec le binôme UI
- [ ] `GetTourArtworks` → `MuseumSpatial.ToWorld` → `LineRenderer` au-dessus des murs bas
- [ ] Pastilles numérotées (TextMeshPro) au-dessus de chaque étape
- [ ] Œuvres du parcours visibles même salle repliée (icône/disque)
- [ ] 🧪 Réordonner le parcours → ligne et numéros suivent

---

## Étape 8 — Intégration et robustesse · ⏱ 2 h · 23/11

- [ ] Prefab `Workbench` (table + maquette + gizmo + sélection)
- [ ] Intégrer dans `Assets/Scenes/VirtualMuseum.unity` (coordonner avec le groupe)
- [ ] Donner au binôme visiteur : `MuseumView` échelle 1, `GetRoomView`, `Refreshed`
- [ ] Load : `Fit()`, désélection, fantômes cachés
- [ ] Musée vide : aucune exception, création possible
- [ ] JSON avec salles qui se chevauchent : affichage OK + contour rouge
- [ ] Perf : `Refresh` < 2 ms (15 salles / 50 œuvres)
- [ ] Test casque : textes, poignées, précision au fond de la table
- [ ] Remplir tableau des justifications (plan-maquette.md) pour le rapport
- [ ] Mail d'avancement fin de séance

---

## Si retard — ordre de priorité

1 → 2 → 3 → 4 → 6 (créer + connecter) → 5 → 7 → 6 (supprimer/déconnecter) → 8
