# Virtual Museum — Spécification des interactions (état au 16/11/2026)

Statut : **décidé** = validé en séance · **proposé** = recommandation en attente de réponse.

---

## 1. Matériel et entrées

- **Meta Quest 2 + manettes** (pas de hand tracking).
- XR Interaction Toolkit 3.5.1 — sélection via **Near-Far Interactor**.

---

## 2. Mode Organisateur

### 2.1 Espace de travail

Pièce « atelier » sous le musée, contenant :

| Élément | Rôle |
|---|---|
| **Plan mural + maquette** | Tableau **vertical** face à l'utilisateur : plan du musée, édition des salles et œuvres |
| **Écran d'ordinateur** | Interface : catalogue, propriétés, parcours, sauvegarde |
| **Gacha** | Obtenir des œuvres (miniatures en capsule) |
| **Grand bac** | Stockage des objets ; ce qui tombe de loin réapparaît dedans |
| **Gachette** | Créer une salle |
| **Tableau tuto** | Sous le plan, **statique** (liste de toutes les commandes) |
| **Ascenseur + levier** | Passage au mode Visiteur |

### 2.2 Maquette

- **Plan vertical**, accroché comme un tableau (centre ≈ 1,35 m, 1,4 × 1,0 m). Le sol du musée est dans le plan du tableau ; le nord du musée est en haut.
- Plan **2D** (murs bas) par défaut.
- **Seule la salle survolée / sélectionnée passe en 3D** : elle « sort » du tableau vers l'utilisateur (murs pleine hauteur, œuvres visibles) — inspiré Metroid Prime. Réduit les occultations.
- **Taille du plan limitée au tableau** (pas de pan/zoom, échelle auto au chargement). Les zones éloignées sont atteintes **au rayon**.

### 2.3 Sélection et saisie

- Rayon Near-Far pour pointer. Grip = saisir l'objet pointé **au rayon** : il ne vient pas dans la main.
- L'objet suit **le point visé par le rayon sur le plan** (écart de prise conservé, pas de saut) : le curseur reste sur l'élément. Le **fantôme** à cet endroit = position d'atterrissage.
- Relâcher = pose à l'emplacement du fantôme. API appelée **au relâchement seulement** (`SetRoomTransform` / `SetArtworkTransform`).

### 2.4 Salles

- **Création** : Curseur sur le plan → salle fantôme attachée au rayon → gachette = posée (`CreateRoom`). Fantôme rouge si chevauchement.
- **Sélection** : rayon + gâchette sur la salle. Affiche le **gizmo** :
  - **Poignée de coin** → largeur / longueur (`ResizeRoom`)
  - **Curseur de hauteur** → hauteur
  - **Logo central** → déplacer / orienter (aussi : grip directement sur le sol d'une salle)
  - **Éléments sur les côtés** (milieu de chaque mur) → connexions
- **Déplacement** : aimantation grille 0,5 m (échelle réelle) ; rotation = **torsion du poignet** (comme un bouton) autour de l'axe du tableau, crans de 15°.

### 2.5 Connexions

- **Création** : tirer un câble depuis un côté d'une salle vers le côté d'une autre (`ConnectRooms`).
- Représentation maquette : câble entre les salles.

### 2.6 Œuvres

- **Obtention** : gacha **aléatoire** → capsule → miniature.
- **Placement** : même mécanisme saisie + fantôme. Œuvres manipulables **uniquement dans la salle sélectionnée** (3D).
- Relâcher au-dessus d'une autre salle = **transfert** (`TransferArtwork`).
- **Aimantation au mur** si < 0,5 m, orientée face à la salle.
- Plusieurs instances d'une même œuvre possibles.

### 2.7 Écran d'ordinateur

- **Rayon = souris**, gâchette = clic. **Clavier virtuel XR** pour l'édition de texte (titre, description, noms de parcours).

---

## 3. Passage entre modes

- **Organisateur → Visiteur** : levier d'ascenseur ; on s'élève et on arrive **par le dessous** dans le musée.
- **Visiteur → Organisateur** : autre levier, descente vers l'atelier.
- **Version provisoire** : simple téléporteur.
- **Point d'arrivée** : **première salle du parcours sélectionné**.

---

## 4. Mode Visiteur

### 4.1 Locomotion

- **Déplacement continu** (joystick gauche), direction = **manette gauche**.
- **Rotation par crans de 45°** (joystick droit).
- **Vignette** (réduction du champ de vision) pendant le mouvement.

### 4.2 Passage entre salles

- **V1** : **plan mural fixe** dans chaque salle. Pointer une salle + gâchette = téléportation.
  - Seulement vers les **salles connectées** (autres grisées).
  - Arrivée au centre, orienté vers la salle de provenance, fondu court.
- **V2** : portes-portails.

---

## 5. Décisions du 16/11

- **Q24** — Gacha **entièrement aléatoire**, pas de sélection d'œuvre précise (écart assumé vs consigne 4.3, à justifier).
- **Q26** — Bouton « Vider le bac » sur l'écran. **Tout objet tombé au sol réapparaît dans le bac.**
- **Q29** — Connexions : **autant que l'utilisateur veut**. Contrainte : **distance minimale** entre œuvres et entre salles.
- **Q33** — **Pas de balise lumineuse** pour l'instant.

- **Bac / chute** — Bac = objets hors musée. Œuvre placée arrivant au bac (lâchée dedans ou tombée) = `RemoveArtwork` (perd textes + étapes, message d'avertissement). Salle tombée = retour à sa place, jamais dans le bac.
- **Connexions** — Point d'attache libre le long du mur pendant la session ; au chargement, câble redessiné entre les murs les plus proches. Une connexion par paire de salles.
- **Distances minimales** — Salles : 1 m entre murs. Œuvres : 0,8 m entre centres. Fantôme rouge, relâcher = retour position précédente.
- **Gizmo** — Salle saisissable par le logo central **ou directement par son sol** (grip, sans sélection préalable ; la salle est sélectionnée à la saisie) ; désélection = gâchette sur zone vide.
- **Ascenseur** — Boutons = parcours (survol : nom + description) + « Visite libre » ; levier = montée. Levier de descente sous le plan mural de chaque salle.
- **Consulter une œuvre** — Rayon droit ; survol = contour + titre ; gâchette = panneau à côté de l'œuvre (titre, description, badge « Étape n/N » / « Hors parcours »). Fermeture : gâchette ailleurs ou > 4 m.
- **Validation** — < 2 m de l'œuvre de l'étape + panneau ouvert → bouton « Valider l'étape ». Fin = panneau « Parcours terminé » + son. « Étape précédente » sur la montre.
- **Guidage** — Chemin surligné sur le plan mural + montre au poignet (« Étape 2/5 · Salle B » + flèche ; vers le plan mural si cible ailleurs) + contour coloré permanent sur l'œuvre de l'étape.
- **Édition des parcours** — Onglet Parcours sur l'écran (créer / sélectionner / supprimer, nom + description au clavier virtuel). « Mode ajout » : toutes les œuvres pointables, gâchette = ajout en fin. Ordre ↑↓, ✕ = retirer. Maquette : numéros + ligne colorée.

- **Suppression de connexion** — Rayon + gâchette sur le câble = surligné → bouton « Déconnecter » sur l'écran.
- **Redimensionnement** — Coin opposé fixe (recalcul position après `ResizeRoom`), aimantation 0,5 m, minimum 2×2×2 m, cotes affichées pendant le glissement ; œuvres hors murs surlignées en rouge, non déplacées.
- **Sauvegarde / chargement** — Onglet Fichier : Sauvegarder / Charger. Message vert + heure si succès, rouge + erreur si échec. Confirmation avant chargement (modifications non sauvegardées perdues).

- **Saisie au rayon (16/11)** — Remplace la saisie « dans la main » : l'objet suit le point du rayon sur le plan. Hauteur d'une salle : seule poignée pilotée par le mouvement de la main (tirer / repousser).
- **Plan vertical (16/11)** — La maquette est sur un tableau mural vertical, pas sur une table. Hauteur d'une salle = poignée tirée vers soi / repoussée vers le tableau.

## 6. Questions ouvertes

Aucune. V2 prévue : portes-portails, ascenseur animé (remplace téléporteur).

---

## 7. Points de vigilance vis-à-vis des consignes

- **4.3** — gacha purement aléatoire = pas de sélection d'une œuvre précise (voir Q24).
- **4.1** — plan limité au tableau : justifier l'accès aux bords par le rayon (précision réduite sur les petites cibles lointaines).
- **6.3** — le modèle ne stocke ni côté de connexion, ni salle d'entrée, ni état « rangé » : tout ce qui en dépend doit être recalculé ou rester hors modèle.
