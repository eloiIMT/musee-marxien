# Virtual Museum backend

A data-only starting point for Unity 6000.3.26f1, using the existing XR packages and configuration. Open Assets/Scenes/VirtualMuseum.unity for the cleaned student starting scene.

## Start

1. Open this project in its existing Unity version.
2. Open **Assets/Scenes/VirtualMuseum.unity**: the backend is already present. For another scene, choose **Tools > Virtual Museum > Add Backend to Current Scene**, or drag `Assets/VirtualMuseum/Sample/MuseumBackend.prefab` into it and save the scene.
3. Enter Play mode. The component initializes three connected rooms, six primitive artwork definitions/instances, two tours, and the first tour's session. It creates no visible museum objects.
4. On the component's Inspector context menu, use **Museum > Verify**, **Save**, **Load**, or **Reset Example**. Reset replaces data in memory; Save persists it. These actions also work in Edit mode, but in-memory edits do not survive domain reloads: Save first, then Load.

The supplied catalog/prefabs are already generated. **Create Missing Example Assets** regenerates a missing catalog/backend prefab without overwriting existing assets. To start with an empty museum and a custom catalog, disable **Initialize With Example** on the backend. Example initialization/reset requires at least five definitions.

## Access and edit

Reference the backend from a student script (assign the field in the Inspector):

```csharp
using UnityEngine;
using VirtualMuseum;

public class MuseumProject : MonoBehaviour
{
    [SerializeField] MuseumBackend backend;

    void Start()
    {
        MuseumApi api = backend.Api; // lazy initialization; also runs in backend.Awake
        Room room = api.CreateRoom("New gallery", Vector3.zero, 8, 10, 3);
        api.SetRoomTransform(room.id, new Vector3(10, 0, 0), Quaternion.Euler(0, 90, 0));
        api.ResizeRoom(room.id, 9, 10, 4);
        api.ConnectRooms(room.id, api.Data.rooms[0].id);

        ArtworkDefinition definition = api.Catalog.Definitions[0];
        ArtworkInstance art = api.AddArtwork(definition.id, room.id, new Vector3(0, 1, 0));
        api.SetArtworkTransform(art.id, new Vector3(1, 1, 0), Quaternion.identity, Vector3.one);
        api.EditArtwork(art.id, "My title", "My description");

        VisitorTour tour = api.CreateTour("Student tour", "A short collection");
        api.AddTourArtwork(tour.id, art.id);
        api.StartTour(tour.id);
    }
}
```

Use `RenameMuseum`, `RenameRoom`, `DisconnectRooms`, `DeleteRoom`, `RemoveArtwork`, `TransferArtwork`, `EditTour`, `DeleteTour`, `RemoveTourArtwork(tourId, index)` and `ReorderTourArtwork(tourId, fromIndex, toIndex)` as needed. Reordering uses the final zero-based destination index. `AddTourArtwork` optionally accepts an insertion index. Repeated stops are allowed. Deleting rooms removes their artworks and connections; deleting artworks removes every matching tour stop. Content changes to the selected tour reset its session to index zero; deleting that tour clears the session.

`Data`, `Session`, `GetRoom`, `GetArtwork`, `GetTour`, `GetArtworksInRoom`, `GetConnectedRooms` and `GetTourArtworks` return snapshots. Edit through the API: modifying a snapshot has no effect. ID queries return null when missing; invalid editing arguments throw `ArgumentException` before modifying data. Dimensions and scale must be positive and finite; rotations must be unit quaternions. `Validate()` returns validation errors. `Replace(data, session)` validates and copies a complete configuration for bulk editing/import.

## Visitor session

```csharp
api.StartTour(tourId);             // selects and resets
ArtworkInstance current = api.CurrentArtwork;
ArtworkInstance next = api.NextArtwork;
ArtworkInstance previous = api.PreviousArtwork;
api.Advance();                     // false when no further progress is possible
api.GoBack();
float progress = api.Progress;     // acknowledged stops / total stops, 0..1
bool finished = api.IsComplete;
api.ResetTour();                   // keeps selection
VisitorSession checkpoint = api.Session;
api.RestoreSession(checkpoint);    // validates against the current museum
```

Index zero is the first stop. Advancing past the last stop completes the tour: index equals `TourLength`, current/next are null, and previous is the final stop. Going back reopens it. A selected empty tour is complete; no selected tour is incomplete with zero progress. Sessions describe sequence progress only.

## Changes

```csharp
void OnEnable() => backend.Api.Changed += OnMuseumChanged;
void OnDisable() => backend.Api.Changed -= OnMuseumChanged;
void OnMuseumChanged(MuseumChange change)
{
    Debug.Log($"{change.Entity} {change.Operation} {change.Id}");
    // Query fresh snapshots and update your own representations.
}
```

Subscribe/unsubscribe to `SessionChanged` in the same way for visitor progress. Room updates include movement, rotation, resizing, renaming and connection changes. Deletion sends removal events and updates affected neighbours/tours. Load/reset sends a museum `Reset`; rebuild your own representations then. Events run synchronously after valid state is available, on the calling thread (use Unity's main thread). Keep subscribers short and avoid editing the model from within a callback.

## Coordinates and prefabs

Metres, Y up, X = room width, Z = room length. Room origin is the **floor centre**; room position/rotation is museum-local. Artwork TRS is relative to that room. Moving/rotating rooms carries their artworks; resizing does not reposition or constrain them. Connections are bidirectional graph edges with no spatial/door semantics. No overlap or containment constraints are imposed.

`MuseumSpatial.RoomMatrix`, `ArtworkMatrix`, `ToMuseum`, `ToRoom`, `LocalBounds`, and `MuseumBounds` supply poses/bounds. MuseumBounds is an axis-aligned enclosure of the rotated room, not an exact oriented box. `ToWorld`/`WorldToRoom` take a student-owned museum root Transform (including its miniature scale). For representations, place a room root at its room position/rotation under your museum root; artworks then use their local TRS. `TransferArtwork` preserves museum pose by default; pass false to keep the old local TRS.

Select `Sample/ArtworkCatalog.asset` in the Inspector to edit definitions. Add definitions via the list, assign a unique stable string ID, title/description and prefab asset; artist/year are optional. Never change an ID used by saved museums. Multiple instances refer to one definition by ID, with their own copied, editable text and independent transforms. Catalog definitions are shared authoring assets, not runtime museum state. JSON stores definition IDs, never Unity object references; retain the matching catalog assets alongside the application.

`backend.InstantiateArtwork(instanceId, roomParent)` explicitly creates one prefab and applies its local TRS. The parent must be your representation of its room. The helper does not track updates or destroy representations. Use identity prefab roots (the examples put geometry proportions on children). All six sample assets use Unity primitives and need no external content.

## Save/load

```csharp
if (!backend.Save(out string error)) Debug.LogWarning(error);
if (!backend.Load(out error)) Debug.LogWarning(error);
// Or MuseumPersistence.TrySave(api, out error, optionalFullPath);
// MuseumPersistence.TryLoad(api, out error, optionalFullPath);
```

Default file: `Path.Combine(Application.persistentDataPath, "virtual-museum.json")`; inspect `MuseumPersistence.DefaultPath` for the exact location. One versioned JSON file saves configuration, rooms, instances, tours and session together. IDs/references remain stable across saves. Saves use a temporary file and replacement. Missing, malformed, unsupported or invalid files return false and an error, preserving current data. Nothing auto-loads or auto-saves. `api.ResetToExample()` creates a fresh example with new entity IDs and the first tour selected. `ToJson`/`TryLoadJson` also support in-memory round trips.

## Verification and student responsibilities

EditMode tests: **Window > General > Test Runner > EditMode > Run All** (or select `VirtualMuseum.Tests`). They cover graph/deletion cleanup, transforms, tours, session boundaries, snapshots/events, invalid input, JSON/disk round trips and the supplied assets. Runtime code is in `Assets/VirtualMuseum/Runtime`; Editor setup tools and tests have separate assemblies.

Students implement all room/wall/door representations, workbench scaling and editing UI, VR interactions, inspection text, visitor rendering/locomotion, tour selection/guidance and transitions between modes. The backend supplies data, validation and opt-in prefab utilities only. The existing XR template remains responsible for its own behavior.

## Base du projet pour les étudiants

Ouvrir **Assets/Scenes/VirtualMuseum.unity**, l'unique scène activée dans les Build Settings. Elle conserve le rig XR du template, la lumière, le sol, l'EventSystem, le XR Interaction Manager et le MuseumBackend déjà configuré. En Play, le backend initialise les données ; il ne dessine pas le musée. Pour les vérifier : sélectionner MuseumBackend dans la Hierarchy, puis dans l'Inspector ouvrir les trois points du composant **Museum Backend (Script) > Museum > Verify**.

- `Assets/VirtualMuseum` : backend, catalogue, six prefabs primitifs, outils Editor et tests.
- `Assets/Scenes` : scène de départ et ressources du sol.
- `Assets/XR/Essentials` : uniquement les assets du template nécessaires au rig (manettes, entrées, interacteurs et scripts associés). Leurs GUIDs sont conservés.
- `Assets/XR`, `Assets/XRI`, `Assets/Settings`, `Assets/CompositionLayers` : réglages XR et rendu existants.

Les autres scènes de démonstration, tutoriels, vidéos et assets inutilisés ont été retirés. Les packages Unity existants sont conservés pour préserver les dépendances et la configuration XR.

### Versionnement Git

Le `.gitignore` à la racine exclut les caches Unity (`Library`, `Temp`), les logs, les réglages utilisateur, les builds et les fichiers générés par les IDE. Versionner **Assets avec tous ses fichiers `.meta`**, **Packages** (y compris `packages-lock.json`), **ProjectSettings**, le README et le `.gitignore`. Unity reconstruit les caches au premier lancement d'un clone. Aucun dépôt, commit ou remote n'est créé automatiquement.

