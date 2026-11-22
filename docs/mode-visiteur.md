# Vue visiteur : implémentation (20/11/2026)

Code : `Assets/Museum/Scripts/Visitor/` (+ `Model/MuseumPaths.cs`, shaders `Museum/Overlay` et `Museum/WorldText`).
Installation dans une scène qui a déjà la maquette : **Tools > Virtual Museum > Add Visitor Mode** (déjà fait dans `Sandbox_Maquette.unity`).
Les parcours ne sont pas gérés ici : la vue visiteur ne lit ni ne modifie la session du backend.
**Aucun texte flottant** : tout texte lu par le visiteur fait partie du monde (cartels au sol, plan mural), et il est masqué par les murs et les objets comme n'importe quel objet.

## Déroulé

1. **Atelier → bouton « Vue visiteur »** (à droite du plan, sous « Nouvelle salle ») : téléportation provisoire.
2. **Transition** : fondu au noir + son, **sans aucun texte** ni pendant ni après, puis arrivée au centre de la première salle, face à son plan mural.
3. **Dans le musée** :
   - joystick gauche = déplacement continu dans la direction de la **manette gauche**, 1,4 m/s ; joystick droit = rotation par crans de 45° ; **vignette** pendant le déplacement ;
   - **plan mural** de la salle courante : salle bleue = ici, flèche rouge = position + orientation, salles vertes = reliées (gâchette = téléportation avec fondu, arrivée face à la salle de provenance), grises = non reliées (refus expliqué, avec la salle à traverser d'abord) ;
   - **œuvres** : chaque œuvre a son **cartel**, un pupitre posé au sol à sa droite (vu de face), plaque inclinée à ~1 m : titre + description de l'instance. Pointer l'œuvre au rayon = contour blanc + cartel éclairé (lien œuvre ↔ cartel).
4. **Bouton « Retour à l'atelier »** sous le plan mural : retour à l'endroit quitté dans l'atelier.

## Architecture

| Classe | Rôle |
|---|---|
| `VisitorMode` | Bascule de mode (affiche l'atelier **ou** le musée échelle 1, jamais les deux : pas de chevauchement quelles que soient les salles créées), transitions avec fondu, déplacement du rig, réglages de locomotion |
| `VisitorGuide` | Salle courante, câblage des œuvres de la vue immersive |
| `RoomPlan` | Plan mural unique, replacé sur un mur de la salle courante (mur avec le moins d'œuvres). `MuseumView` miniature sur les mêmes données que la maquette |
| `VisitorTeleportButton` | Bouton provisoire atelier → vue visiteur |
| `ArtworkTarget` | Œuvre pointable (collider englobant, contour) + son cartel au sol |
| `CameraOverlay` | Fondu et vignette de confort (aucun texte) |
| `UiKit` | Plaques, textes à profondeur (`Museum/WorldText`), boutons, sons |
| `MuseumPaths` | BFS sur le graphe des salles (testé en EditMode) |

## Justifications (rapport)

| Choix | Alternatives | Raison / limite |
|---|---|---|
| Déplacement continu + vignette + rotation par crans | Téléportation libre | Garde la continuité spatiale et l'échelle perçue dans la salle ; vignette et crans de 45° limitent le flux optique périphérique. Limite : sujets sensibles, à vérifier au casque |
| Direction = manette gauche | Direction = tête | Regarder une œuvre en marchant à côté ; cohérent avec la spec 4.1 |
| Passage entre salles par le plan mural | Portes / portails (V2) | Les connexions du modèle n'ont pas de position de porte. Le plan rend visibles le réseau et les chemins alternatifs. Limite : moins « naturel » qu'une porte |
| Plan orienté « vers l'avant » (le haut du plan = la direction où l'on regarde) | Nord en haut comme la maquette | Pas de rotation mentale (principe des plans « vous êtes ici »). Limite : orientation différente de la maquette de l'atelier |
| Un seul plan replacé à chaque changement de salle | Un plan par salle | Même expérience, coût constant quel que soit le nombre de salles |
| Fondu sans texte à chaque saut | Coupure sèche, carte explicative | Pas de glissement visible (confort) ; aucun texte hors du monde. Limite : le changement d'échelle n'est pas expliqué, il doit se comprendre par le plan mural |
| Cartel sur pied au sol (diégétique) | Fiche flottante, panneau collé à la tête | Métaphore du musée réel, toujours lisible sans commande ; l'œuvre reste visible. Limite : texte petit (1,7 cm), il faut s'approcher ; le cartel est poussé à 30 cm des murs si l'œuvre en est proche |

## À vérifier au casque

- Taille des textes (cartel : titre 3,4 cm, description 1,7 cm ; plan), lisibilité du plan à 1,5 m.
- Cartels qui gênent le passage ou se chevauchent quand deux œuvres sont proches (aucune collision).
- Le rayon de téléportation XRI (joystick droit vers l'avant) apparaît mais n'a aucune zone cible : à désactiver si gênant.
- Confort de la vignette (angles dans `CameraOverlay`) et vitesse 1,4 m/s.
