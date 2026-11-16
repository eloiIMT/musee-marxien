Projet — Virtual Museum
Créez et aménagez un musée sur une maquette interactive, puis explorez-le en immersion. Un micro-projet centré sur la sélection, la manipulation, la navigation et le guidage en réalité virtuelle.

1. Votre mission
Concevez et développez Virtual Museum, une application de réalité virtuelle permettant de créer, d’organiser et de visiter un musée composé de plusieurs salles interconnectées.

Votre application proposera deux modes complémentaires :

Organisateur : aménager le musée depuis une maquette 3D interactive posée sur une table de travail virtuelle (workbench).
Visiteur : explorer le même musée à la première personne, à échelle humaine, librement ou en suivant un parcours de visite.
Les deux modes doivent exploiter les mêmes données. Une salle déplacée, une œuvre ajoutée ou un parcours créé dans le mode Organisateur doivent se retrouver dans le mode Visiteur.

Le cœur du projet est la conception des interactions 3D. Vous devrez choisir comment sélectionner, manipuler, naviguer et présenter les informations, puis implémenter et justifier vos solutions. Une application fonctionnelle est nécessaire ; la richesse des modèles 3D ne remplace pas la qualité des interactions.

2. Objectifs pédagogiques
Ce projet vous amène à articuler plusieurs dimensions de l’interaction en réalité virtuelle :

Sélection et manipulation : désigner des objets proches ou éloignés, contrôler leur position et leur orientation, ajuster leurs dimensions et fournir des retours compréhensibles.
Navigation et locomotion : accéder aux différentes parties d’une maquette et se déplacer dans un musée immersif en tenant compte de l’efficacité, de l’orientation et du confort.
Changement de référentiel et d’échelle : passer d’une vue exocentrique, où vous observez le musée de l’extérieur, à une vue égocentrique, où vous êtes à l’intérieur.
Interfaces immersives : rendre accessibles un catalogue, des propriétés, des commandes et des informations de visite dans un environnement tridimensionnel.
Conception argumentée : relier chaque technique aux tâches et aux contraintes auxquelles elle répond, identifier des alternatives et reconnaître ses limites.
3. Ce qui est fourni et ce que vous développez
Le projet de départ
Le projet Unity fourni utilise Unity 6000.3.26f1. Sa configuration comprend notamment XR Interaction Toolkit 3.5.1, OpenXR 1.17.1 et l’Input System. Conservez les versions du projet pour démarrer.

Récupérez le projet de départ sur GitLab IMT Atlantique — Projet RX, branche main. Le dépôt est accessible aux utilisateurs internes connectés : utilisez votre compte GitLab IMT Atlantique pour le consulter et le récupérer.

Pour cloner le dépôt en SSH :

git clone git@gitlab.imt-atlantique.fr:e20peill/projet-rx.git
cd projet-rx
Cette commande nécessite une clé SSH associée à votre compte GitLab. Vous pouvez également utiliser les options de récupération proposées par le bouton Code sur la page du dépôt.

Dans Unity Hub, ajoutez le dossier cloné projet-rx comme projet existant, puis ouvrez-le avec la version indiquée ci-dessus. Ouvrez la scène Assets/Scenes/VirtualMuseum.unity : le composant MuseumBackend y est déjà installé. Consultez le README à la racine pour prendre en main le socle fourni.

Le dossier Assets/VirtualMuseum contient un backend dans l’espace de noms C# VirtualMuseum. Ici, « backend » désigne le modèle de données et sa logique de gestion, exécutés dans Unity.

Élément fourni	Rôle
MuseumBackend	Composant d’accès au modèle, à l’initialisation et à la sauvegarde ; son attribut Api donne accès à MuseumApi.
MuseumApi	Opérations de gestion des salles, des œuvres, des parcours et de la progression de visite.
ArtworkCatalog et ArtworkDefinition	Catalogue des œuvres disponibles, avec leurs informations et leurs prefabs.
Museum, Room, ArtworkInstance, VisitorTour, VisitorSession	Données du musée, de ses salles, des œuvres placées, des parcours et de la session de visite.
MuseumSpatial	Utilitaires de transformations et de conversion de coordonnées, utilisables aussi pour la maquette.
MuseumPersistence	Enregistrement et chargement d’une configuration et de sa session dans un fichier JSON.
Une configuration d’exemple initialise trois salles connectées, six instances d’œuvres et deux parcours, « Highlights » et « Full Collection ». Le premier parcours est sélectionné au départ. Le catalogue fourni contient six œuvres réalisées avec des primitives Unity.

Le README à la racine du projet explique l’API, les coordonnées, la persistance et les tests disponibles. Il constitue votre référence technique avec le code de Assets/VirtualMuseum/Runtime.

Votre responsabilité
Vous développez les représentations du musée et toutes les interfaces nécessaires à son utilisation :

la table de travail et la maquette interactive ;
les salles, leurs connexions et les œuvres dans les deux modes ;
les techniques de sélection, de manipulation et de commande ;
les interfaces du catalogue, des propriétés et des parcours ;
la locomotion, l’orientation et le guidage du visiteur ;
l’affichage des informations et de la progression ;
le passage cohérent entre les deux modes.
Le backend ne crée aucun musée visible à l’initialisation. Vous n’avez pas à le modifier pour réaliser les fonctionnalités demandées. Construisez vos composants autour de son API, en séparant le modèle, ses représentations et les interactions.

4. Mode Organisateur : concevoir le musée
4.1. Observer et utiliser la maquette
Représentez le musée sous forme de maquette 3D sur une table virtuelle. L’utilisateur doit pouvoir en comprendre l’organisation et accéder aux éléments à modifier, y compris lorsque le musée dépasse la surface immédiatement accessible de la table.

Choisissez comment observer et parcourir cette représentation. Prenez en compte les occultations, la précision de sélection et les différences d’échelle. Les techniques employées pour accéder à la maquette et la manipuler sont à concevoir.

4.2. Organiser les salles
L’utilisateur doit pouvoir :

visualiser les salles et leurs connexions ;
ajouter et supprimer des salles ;
déplacer et orienter une salle ;
modifier sa largeur, sa longueur et sa hauteur ;
créer et supprimer des connexions entre salles.
Le musée doit pouvoir former un réseau avec des branches et des chemins alternatifs. Ne limitez pas sa structure à une succession linéaire de salles.

Les connexions du modèle sont des liens bidirectionnels entre salles. Elles ne définissent ni porte, ni couloir, ni ouverture automatique dans un mur. À vous de concevoir leur représentation et la manière dont le visiteur les emprunte. La solution doit rendre les relations entre salles compréhensibles et permettre de les parcourir.

4.3. Installer et gérer les œuvres
Proposez une interface permettant de consulter le catalogue et de sélectionner une œuvre. L’utilisateur doit pouvoir :

ajouter une instance de l’œuvre choisie dans une salle ;
la déplacer et l’orienter dans cette salle ;
la retirer du musée ;
consulter et modifier son titre et sa description.
Plusieurs instances d’une même œuvre doivent pouvoir coexister. Distinguez la définition du catalogue de chaque instance placée : deux instances partagent une référence de catalogue, mais possèdent leur propre transformation et leurs propres textes modifiables.

Le changement d’échelle d’une œuvre est une extension facultative. La technique de sélection et de placement reste libre ; choisissez-la en fonction de la taille des objets et de la précision requise.

4.4. Composer les parcours de visite
L’organisateur doit pouvoir définir plusieurs parcours. Chaque parcours possède un nom, une description et une liste ordonnée d’instances d’œuvres à visiter.

Votre interface doit permettre de :

créer, sélectionner et supprimer un parcours ;
modifier son nom et sa description ;
ajouter des œuvres situées dans différentes salles ;
définir et modifier leur ordre de visite ;
retirer une œuvre du parcours sans la supprimer du musée ;
visualiser le parcours et son ordre sur la maquette.
Réfléchissez à la façon de sélectionner des œuvres dispersées dans le musée et de comprendre leur succession. Un parcours fixe les étapes de la visite ; il ne définit pas la technique de déplacement entre ces étapes.

5. Mode Visiteur : explorer le musée en immersion
5.1. Retrouver le musée à échelle humaine
Le visiteur doit retrouver la configuration produite dans le mode Organisateur : dimensions, positions et orientations des salles, connexions, œuvres et informations associées.

Concevez le passage entre les modes pour que l’utilisateur comprenne son changement de point de vue et d’échelle. La maquette et la vue immersive doivent rester deux représentations cohérentes du même musée.

5.2. Choisir une visite
Au début de la visite, l’utilisateur doit pouvoir consulter les parcours disponibles, lire leur nom et leur description, puis sélectionner et démarrer celui de son choix.

Il doit ensuite pouvoir identifier les œuvres appartenant à ce parcours et comprendre sa progression. L’exploration libre doit rester possible : suivre un parcours ne suppose pas nécessairement un déplacement imposé.

5.3. Se déplacer et s’orienter
Implémentez une ou plusieurs techniques de locomotion permettant de se déplacer dans les salles et de passer entre les salles connectées.

Votre conception doit prendre en compte :

les salles de tailles différentes et les changements de direction ;
la compréhension de la position et de l’orientation du visiteur ;
l’accès aux œuvres et aux passages ;
le confort, la fatigue et la limitation du mal des simulateurs (cybersickness).
Vous choisissez les techniques adaptées. La téléportation, le déplacement continu ou d’autres solutions sont des possibilités, sans constituer des exigences. Vérifiez au casque les effets de vos choix sur l’échelle perçue, la lisibilité et le confort.

5.4. Guider et suivre la progression
Lorsqu’un parcours est sélectionné, le visiteur doit pouvoir :

identifier l’œuvre constituant l’étape à atteindre ;
comprendre comment s’y rendre, y compris lorsqu’elle se trouve dans une autre salle ;
consulter sa progression ;
valider l’étape et passer à la suivante ;
identifier clairement l’achèvement du parcours.
Le retour à une étape précédente est facultatif ; le backend permet de l’implémenter.

Distinguez locomotion et guidage : la locomotion détermine comment se déplacer ; le guidage permet de savoir où aller. Des repères spatiaux, indications contextuelles ou formes de navigation assistée peuvent être envisagés. Leur choix et leur articulation avec l’exploration libre vous appartiennent.

Le backend suit une séquence d’étapes : il ne détecte pas automatiquement qu’un visiteur a atteint ou observé une œuvre. Définissez une règle compréhensible pour valider une étape et déclencher la progression.

5.5. Consulter les œuvres
Le visiteur doit pouvoir désigner ou sélectionner une œuvre, consulter son titre et sa description et identifier si elle appartient au parcours en cours.

Soignez la distance de lecture, le placement des informations et les retours de sélection. L’accès aux informations doit rester compatible avec la navigation et ne pas rendre les autres commandes ambiguës.

6. Intégrer le socle technique
6.1. Prendre en main les données
Clonez le dépôt indiqué dans la section « Le projet de départ », puis ouvrez-le dans sa version Unity.
Ouvrez Assets/Scenes/VirtualMuseum.unity : le backend est déjà présent. Pour travailler dans une autre scène, utilisez Tools > Virtual Museum > Add Backend to Current Scene, ou ajoutez le prefab Assets/VirtualMuseum/Sample/MuseumBackend.prefab, puis enregistrez la scène.
Lancez Play : le composant initialise les données d’exemple, sans créer de représentation visuelle.
Référencez ce composant depuis vos scripts et accédez à backend.Api.
Le menu contextuel du composant propose Museum > Verify, Save, Load et Reset Example pour explorer le fonctionnement du socle. Ces outils d’éditeur servent à la prise en main ; les actions requises par les utilisateurs doivent être accessibles dans votre application VR.

6.2. Repères d’API
Les noms ci-dessous correspondent à l’API fournie. Consultez le README pour les signatures et les exemples d’utilisation.

Besoin	Membres disponibles
Créer et transformer les salles	CreateRoom, DeleteRoom, SetRoomTransform, ResizeRoom
Gérer le réseau	ConnectRooms, DisconnectRooms, GetConnectedRooms
Placer et modifier les œuvres	AddArtwork, RemoveArtwork, SetArtworkTransform, EditArtwork, TransferArtwork
Composer les parcours	CreateTour, EditTour, DeleteTour, AddTourArtwork, RemoveTourArtwork, ReorderTourArtwork
Consulter les données	Data, GetRoom, GetArtwork, GetTour, GetArtworksInRoom, GetTourArtworks
Piloter la visite	StartTour, Advance, GoBack, ResetTour
Lire son état	Session, CurrentArtwork, NextArtwork, PreviousArtwork, Progress, IsComplete
Réagir aux modifications	Événements Changed et SessionChanged
Enregistrer et recharger	MuseumBackend.Save(out string error) et MuseumBackend.Load(out string error)
Dans la session, CurrentArtwork est l’œuvre de l’étape à atteindre ou à valider. NextArtwork désigne celle qui suit. Advance() valide la progression vers l’étape suivante ; après la dernière, IsComplete vaut vrai et CurrentArtwork est nul. Votre interface doit traduire ces états sans ambiguïté.

6.3. Maintenir des représentations cohérentes
Les lectures du modèle renvoient des copies instantanées. Modifier une valeur obtenue par Data ou GetArtwork, par exemple, ne modifie pas le musée. Effectuez les changements par les méthodes de MuseumApi, puis actualisez vos représentations à partir des événements et de nouvelles lectures.

Utilisez les identifiants stables pour associer vos objets visuels aux entités du modèle. Les deux modes doivent rester cohérents après un ajout, une transformation, une suppression ou un chargement. La suppression d’une salle supprime ses œuvres et ses connexions ; la suppression d’une œuvre retire aussi ses étapes des parcours. Une modification du contenu du parcours sélectionné réinitialise sa progression.

Les dimensions sont exprimées en mètres. L’origine d’une salle se trouve au centre de son sol ; les œuvres utilisent des coordonnées locales à leur salle. Déplacer une salle entraîne donc ses œuvres. Redimensionner une salle ne les replace pas automatiquement. Le backend n’impose ni confinement des œuvres ni absence de chevauchement entre salles : prenez en compte ces situations dans vos interfaces et vos choix de représentation.

La méthode MuseumBackend.InstantiateArtwork(instanceId, roomParent) permet de créer explicitement le prefab d’une œuvre sous votre représentation de salle. Elle ne prend en charge ni sa mise à jour ultérieure ni sa suppression visuelle.

6.4. Sauvegarder et retrouver un musée
Permettez d’enregistrer puis de recharger le musée avec les fonctions fournies, afin de retrouver son aménagement et ses parcours. Le fichier JSON inclut également la session de visite. Aucune sauvegarde ni aucun chargement n’est automatique. Rendez le résultat de ces actions compréhensible, y compris en cas d’échec.

Les tests du socle sont accessibles dans Window > General > Test Runner > EditMode, sous VirtualMuseum.Tests. Ils vérifient le modèle ; ils ne remplacent pas vos vérifications des interactions et des représentations.

7. Concevoir et justifier vos interactions
Pour chaque technique importante, soyez capables d’expliquer :

la tâche visée : que cherche à accomplir l’utilisateur ?
le choix effectué : comment la technique répond-elle à cette tâche et à ce référentiel ?
les alternatives : quelles autres approches étaient possibles, et quels compromis ont orienté votre choix ?
les retours et la précision : comment l’utilisateur identifie-t-il la cible, l’action en cours et son résultat ?
les limites : quelles difficultés apparaissent avec des objets petits, éloignés, masqués ou nombreux ?
l’ergonomie en VR : comment sont pris en compte la lisibilité, la fatigue, l’amplitude des gestes et le confort de déplacement ?
Cette réflexion concerne autant la maquette que la visite. Une technique efficace à échelle humaine ne convient pas nécessairement à une miniature. De même, transposer un menu 2D dans l’espace ne suffit pas à en garantir l’utilisabilité : justifiez son placement, son accès et son mode d’actionnement.

Vous pouvez utiliser les composants XR disponibles, les adapter ou développer vos propres techniques. Le projet n’impose aucune métaphore unique de sélection, de manipulation, de menu ou de navigation.

8. Scénario de démonstration attendu
Votre application doit permettre d’enchaîner les deux phases suivantes avec un même musée.

Phase 1 — Organiser
Accéder à la maquette et examiner les salles et leurs connexions.
Ajouter ou réorganiser plusieurs salles, ajuster des connexions et modifier les dimensions d’une salle.
Ajouter plusieurs œuvres depuis le catalogue et régler leur position et leur orientation.
Modifier le titre et la description d’une œuvre placée.
Créer un parcours reliant des œuvres de plusieurs salles, modifier leur ordre et vérifier sa représentation sur la maquette.
Enregistrer le musée.
Phase 2 — Visiter
Passer à la représentation immersive et retrouver l’aménagement réalisé.
Consulter les parcours et sélectionner celui qui vient d’être créé.
Se déplacer entre les salles et utiliser le guidage pour trouver les œuvres du parcours.
Consulter les informations des œuvres et progresser jusqu’à la fin de la visite.
Vérifier également qu’un chargement restitue la configuration enregistrée et actualise les représentations.
Vérifiez aussi les actions de retrait d’une œuvre, de suppression d’une salle ou d’un parcours et la cohérence des interfaces après ces opérations. Le scénario illustre un enchaînement d’usage ; il ne remplace pas l’ensemble des fonctionnalités obligatoires décrites ci-dessus.

9. Extensions facultatives et ressources graphiques
Après avoir réalisé le socle fonctionnel, vous pouvez notamment proposer :

le redimensionnement des œuvres ;
le retour à une étape précédente du parcours ;
l’observation détaillée ou la manipulation d’une œuvre pendant la visite ;
des aides au placement ou à l’alignement ;
plusieurs techniques d’interaction ou de locomotion adaptées à des situations différentes ;
un catalogue enrichi ou une scénographie plus élaborée.
Ces pistes sont facultatives et ne sont pas exhaustives. Elles doivent servir une intention d’interaction et rester compatibles avec les fonctions principales.

Les primitives fournies suffisent pour développer et démontrer vos techniques. Vous pouvez créer ou importer des ressources et les partager avec les autres groupes, dans le respect de leurs licences. La modélisation et la qualité esthétique ne sont pas les objectifs principaux de ce micro-projet.

10. Critères d’évaluation
L’évaluation privilégie l’adéquation des techniques aux tâches, la cohérence de l’expérience et votre capacité à argumenter les choix effectués.

Critère	Pondération indicative	Points d’attention
Interaction et manipulation	30 %	Sélection, transformations, commandes et précision, notamment sur la maquette.
Navigation et guidage	30 %	Déplacements, orientation, confort et compréhension du parcours.
Conception et justification	20 %	Analyse des tâches, pertinence des choix, alternatives et limites.
Visualisation et ergonomie	10 %	Lisibilité, retours d’action, organisation et cohérence des interfaces.
Intégration et robustesse	10 %	Fonctions attendues, utilisation de l’API et cohérence entre les deux modes.
Cette répartition est indicative. L’organisation des groupes, le calendrier, le suivi du projet et les livrables finaux sont détaillés ci-dessous. Les modalités pratiques de remise seront précisées séparément.

Le résultat attendu est une application fonctionnelle dont les interactions sont pertinentes, cohérentes et justifiées. Des solutions différentes peuvent répondre aux mêmes besoins : c’est à vous de concevoir et de défendre la vôtre.

11. Organisation, jalons et suivi du projet
Groupes et contributions individuelles
Vous pouvez travailler en groupes de 2 ou 3 étudiants. Dans le rapport final, les contributions de chaque membre du groupe doivent être clairement identifiables : précisez qui a contribué à la conception, au développement, aux tests et à l’analyse, en indiquant également les tâches réalisées en commun.

Calendrier et jalons
Les séances de travail du projet ont lieu en novembre 2026. Les soutenances auront lieu le vendredi 8 janvier 2027 au matin. Les séances en autonomie ont lieu sur site.


Séance	Jalon et avancement attendu	Organisation
Vendredi 13 novembre — matin	Lancement et conception des deux modes : constituer les groupes, prendre en main le backend et identifier les tâches de l’organisateur et du visiteur. Explorer plusieurs pistes de sélection, de manipulation, de locomotion et de guidage, puis définir une première conception de la maquette et de la visite immersive et répartir le travail.	Encadré
Lundi 16 novembre — matin	Premières interactions sur la maquette : développer la représentation miniature et les interactions d’édition des salles et des œuvres en s’appuyant sur l’API. Vérifier les retours de sélection et la précision des transformations.	Autonomie
Lundi 16 novembre — après-midi	Articulation entre organisation et visite : poursuivre l’édition des parcours et la visite à échelle humaine, avec sélection d’un parcours, locomotion, guidage et consultation des œuvres. Intégrer progressivement les composants et vérifier que les deux modes reflètent les mêmes données.	Autonomie
Vendredi 20 novembre — matin	Scénario testable et protocole : finaliser une version permettant d’aménager le musée, de composer un parcours et de le suivre en immersion. Préparer des tâches de test couvrant les deux modes et définir les observations à recueillir sur la compréhension, la précision, l’orientation et le confort.	Autonomie
Lundi 23 novembre — matin	Ajustements et préparation des tests : poursuivre le développement et ajuster les interactions, les retours ou le guidage. Vérifier au casque la cohérence entre les modes et le bon déroulement du scénario complet, puis finaliser les tâches et le protocole pour les tests de l’après-midi.	Autonomie
Lundi 23 novembre — après-midi	Tests utilisateurs des interactions : faire valider le protocole, puis observer des utilisateurs aménager la maquette et suivre un parcours dans le musée. Relever les difficultés de sélection, de manipulation, de lecture et de navigation, ainsi que les éventuels problèmes de confort, et commencer leur analyse avec l’enseignant.	Encadré
Vendredi 27 novembre — matin	Poursuite du travail et consolidation : poursuivre les améliorations issues des tests, compléter les fonctionnalités et consolider l’intégration des deux modes. Vérifier le scénario complet et avancer dans la préparation des livrables finaux.	Autonomie
Vendredi 8 janvier 2027 — matin	Soutenance, démonstration et évaluation : démontrer l’aménagement sur maquette puis la visite du parcours créé. Justifier les techniques retenues dans les deux modes, présenter les principaux constats des tests et les améliorations apportées, et discuter les limites restantes.	Encadré
La répartition du développement entre les deux séances du 16 novembre constitue un repère : organisez le travail au sein du groupe pour faire progresser les deux modes et leur intégration. L’objectif du 20 novembre est de disposer d’une expérience suffisamment complète et stable pour que les tests portent sur les interactions et la navigation.

Un mail d’avancement à chaque fin de séance
Après chaque séance, chaque groupe doit envoyer un mail d’avancement à etienne.peillard@imt-atlantique.fr, avec tous les membres du groupe en copie. Ce mail permet le suivi de l’avancement du projet.

Dans chaque mail, indiquez brièvement :

le groupe et ses membres ;
ce qui a été réalisé pendant la séance et l’état actuel de l’application ;
les choix de conception effectués ou révisés ;
les difficultés rencontrées et les éventuels besoins d’aide ;
les prochaines étapes prévues ou, pour la séance finale, le bilan du projet.
Après les tests utilisateurs, précisez également les principaux constats et les corrections réalisées ou envisagées, ainsi que les points restant à traiter. Ce suivi doit rester synthétique et permettre de comprendre la progression du groupe d’une séance à l’autre.

12. Livrables finaux
Les attendus finaux comprennent le projet, les vidéos, le rapport, ainsi que la soutenance et son support de slides.

Un projet fonctionnel dans un dépôt Git
Fournissez le lien vers un dépôt Git contenant le projet Unity fonctionnel. Le dépôt doit contenir les fichiers et ressources nécessaires pour ouvrir et exécuter le projet, ainsi que les instructions de lancement et les éventuelles étapes de configuration. Vérifiez que la version remise peut être récupérée depuis le dépôt et exécutée.

Une ou plusieurs vidéos de démonstration
Fournissez une ou plusieurs vidéos montrant le fonctionnement de l’application. Elles doivent permettre de voir les fonctionnalités réalisées dans les deux modes : aménagement du musée sur la maquette, création d’un parcours, puis visite immersive avec locomotion, guidage et consultation des œuvres. Rendez les interactions et leurs résultats suffisamment lisibles pour comprendre l’utilisation de l’application.

Un rapport final
Le rapport doit contenir :

Une description de l’application : son organisation générale et l’articulation entre les modes Organisateur et Visiteur.
Un détail des fonctionnalités réalisées, accompagné des choix techniques et de leur justification. Accordez une attention particulière aux techniques de sélection et de manipulation, aux interfaces, aux techniques de locomotion et au guidage : expliquez les choix retenus, leur adéquation aux tâches et leurs limites.
Un retour sur les difficultés rencontrées : les problèmes de conception, de développement ou d’intégration, les solutions mises en œuvre et les difficultés éventuellement non résolues.
Une identification claire des contributions de chaque membre, y compris les tâches réalisées en commun, conformément aux consignes d’organisation du groupe.
Un paragraphe personnel de retour d’expérience par étudiant, identifié par son nom : chacun présente ce qu’il a appris, les difficultés qui l’ont marqué et le recul qu’il porte sur son travail et sur le projet.
Une soutenance et les slides de présentation
Chaque groupe doit réaliser une soutenance avec une démonstration de l’application : présenter les deux modes, expliquer et justifier les choix d’interaction et de locomotion, puis revenir sur les tests utilisateurs, les améliorations apportées et les limites restantes.

Le support de slides utilisé lors de la soutenance doit également être remis parmi les livrables finaux. Les soutenances auront lieu le vendredi 8 janvier 2027 au matin.