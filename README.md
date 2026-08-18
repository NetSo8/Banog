# Banog — automate de dossiers pour Windows

Surveillance de dossiers et application automatique de règles (tri, renommage, déplacement,
nettoyage) dès qu'un fichier apparaît ou change. Exécutable natif autonome, aucun runtime à
installer, aucun compte, aucune télémétrie.

> **Version bêta :** Banog est encore en développement. L'application et son format de
> configuration sont susceptibles de changer fortement avant une version stable. Pensez à
> conserver une copie de vos règles si vous testez une nouvelle version.

> Nom de travail. Voir « Nom » plus bas.

## Stack

| | |
|---|---|
| UI | Avalonia 11.3.18, MVVM, XAML compilé, bindings compilés |
| Runtime | .NET 10, Native AOT |
| Cible | `win-x64` uniquement (v1) |
| Déploiement | self-contained, exécutable natif |

## Structure

```
src/
  App.Core      moteur de règles, modèles sérialisables JSON, logique pure — aucune dépendance UI
  App.Watcher   surveillance native (ReadDirectoryChangesW), debounce, stabilisation
  App.UI        vues, viewmodels, styles Avalonia — ne référence ni App.Watcher ni Win32
  App.Host      point d'entrée, composition, configuration AOT/publish
tests/
  App.Core.Tests  82 tests (conditions, actions, tokens, sérialisation, sécurité)
```

Le sens des dépendances est strict :

```
App.Host ──> App.UI ──> App.Core
   │                       ▲
   └────> App.Watcher ─────┘
```

`App.UI` ne connaît le moteur en marche qu'à travers `IAutomationController`, implémenté dans
l'hôte. Le moteur ne connaît le disque qu'à travers `IFileSystem`, `IProcessRunner`,
`ISystemClock`. Les tests s'exécutent entièrement en mémoire.

## Compiler et lancer

```bash
dotnet test
```

```bash
dotnet run --project src/App.Host
```

## Publier l'exécutable natif

L'édition de liens Native AOT a besoin de `link.exe` : installez la charge de travail
**Développement Desktop en C++** de Visual Studio (MSVC x64 + Windows SDK), puis :

```bash
publish.cmd
```

Le script initialise l'environnement MSVC puis appelle `dotnet publish`. Sortie dans `publish/`.

### Note sur le « single-file »

Native AOT produit `Banog.exe` (~23 Mo) qui embarque tout le code managé et le runtime : rien
à installer côté utilisateur, c'est l'objectif atteint. Avalonia charge en revanche son moteur
de rendu par bibliothèques natives, non fusionnables dans l'image AOT :

```
Banog.exe  libSkiaSharp.dll  libHarfBuzzSharp.dll  av_libglesv2.dll
```

Un « un seul fichier » littéral demanderait d'embarquer ces DLL en ressources et de les
extraire au démarrage — ce qui réintroduit une extraction disque au lancement, exactement le
coût que l'AOT cherchait à supprimer. Quatre fichiers dans un même dossier restent compatibles
avec une distribution en téléchargement direct (ZIP, ou installeur à un clic).

## Format de règles

Fichier : `%APPDATA%\Banog\rules.json`, écrit de façon atomique.

```json
{
  "schemaVersion": 1,
  "debounceMilliseconds": 750,
  "theme": "System",
  "folders": [
    { "id": "…", "path": "C:\\Users\\moi\\Downloads", "includeSubfolders": false, "enabled": true }
  ],
  "rules": [
    {
      "name": "Factures",
      "match": "All",
      "conditions": [
        { "type": "extension", "match": "IsOneOf", "extensions": ["pdf"] },
        { "type": "name", "mode": "Contains", "value": "facture" }
      ],
      "actions": [
        { "type": "rename", "template": "{created:yyyy-MM-dd}_{name}.{ext}" },
        { "type": "move", "destination": "D:\\Compta\\{created:yyyy}" }
      ]
    }
  ]
}
```

### Conditions v1

`extension` · `name` (contient / commence par / finit par / égal / regex) · `date` (création ou
modification, plus ancien que / plus récent que / avant / après) · `size` · `sourceFolder` ·
`group` (imbrication ET/OU).

Toute condition accepte `"negate": true`.

### Actions v1

`move` · `copy` · `rename` · `delete` (corbeille par défaut) · `runCommand`.

### Tokens

Utilisables dans les gabarits de renommage et les chemins de destination.

| Token | Alias français | Résultat |
|---|---|---|
| `{name}` | `{nom}` | nom sans extension |
| `{filename}` | `{fichier}` | nom avec extension |
| `{ext}` | `{extension}` | extension sans le point |
| `{folder}` | `{dossier}` | nom du dossier parent |
| `{path}` | `{chemin}` | chemin complet |
| `{counter:000}` | `{compteur:000}` | compteur de désambiguïsation |
| `{created:F}` | `{date:F}`, `{creation:F}` | date de création, format .NET `F` (défaut `yyyy-MM-dd`), heure locale |
| `{modified:F}` | `{modification:F}` | date de dernière modification |
| `{now:F}` | `{aujourdhui:F}` | date courante |

Les deux graphies sont valides et le resteront. Une interface en français qui impose d'écrire
`{name}` n'est simple qu'en apparence — mais les fichiers de règles déjà écrits ne doivent pas
cesser de fonctionner pour autant. La casse est ignorée. `{{` et `}}` produisent une accolade
littérale.

## Apparence

Trois modes, sélectionnables dans **Paramètres → Apparence** : **Windows** (défaut), **clair**,
**sombre**. Le choix est persisté dans `theme` et s'applique immédiatement, sans redémarrage.

En mode Windows, l'application ne lit ni ne surveille le registre : elle laisse son variant sur
`ThemeVariant.Default`, et Avalonia se repeint tout seul quand l'utilisateur bascule le réglage
système, application ouverte.

Une seule palette, deux valeurs par clé
([`Themes/Palette.axaml`](src/App.UI/Themes/Palette.axaml)) ; les styles
([`Themes/Theme.axaml`](src/App.UI/Themes/Theme.axaml)) ne connaissent que les clés et sont
écrits une seule fois. Toutes les couleurs passent par `DynamicResource` — avec
`StaticResource`, la bascule à chaud ne repeindrait rien.

L'accent est assombri en clair (`#0B72AE` au lieu de `#4CC2FF`) : le bleu du thème sombre passe
sous le seuil de contraste sur fond blanc.

## Arrière-plan

Banog surveille sans fenêtre. Fermer la fenêtre ne quitte pas : la surveillance continue
sous l'icône de la zone de notification, qui ouvre la fenêtre, met la surveillance en
marche ou en pause, et quitte réellement. Le plateau est le seul visage de l'application
quand elle tourne en arrière-plan — aucune vue ni viewmodel n'est construit tant que la
fenêtre n'a pas été demandée.

Une seule instance tourne à la fois : deux processus qui surveillent le même dossier
traiteraient chaque fichier deux fois. Un second lancement sans option demande donc à la
première instance de rouvrir sa fenêtre, puis s'efface.

`Banog.exe --background` (alias `--daemon`) démarre sans fenêtre et met la surveillance en
marche immédiatement. C'est ainsi que la clé de démarrage lance Banog.

Le démarrage avec Windows est obligatoire : la clé `Run` de l'utilisateur pointe vers
l'exécutable courant avec `--background`. Elle est réalignée à chaque lancement sur
l'exécutable qui tourne, ce qui suit un déplacement d'installation.

L'icône du plateau n'est pas embarquée : un carré bleu portant un dossier blanc, dessiné en
pixels au démarrage.

## Parti pris d'interface

L'outil vise des gens qui croulent sous leurs téléchargements, pas des développeurs. Quatre
règles guident l'interface.

**Un espace, une intention.** Une barre latérale sépare trois espaces, et un seul est visible à
la fois : **Surveillance** (ce qui tourne, les règles en place, ce qui s'est passé),
**Règles** (les dossiers surveillés et l'écriture des règles), **Paramètres** (apparence,
détection, emplacement du fichier). Regarder n'est pas modifier : la surveillance n'affiche
aucun champ éditable, seulement un bouton « Modifier » qui bascule vers l'espace d'édition sur
la bonne règle. Ce qui vaut pour l'application entière — l'état marche/pause et « Ranger
maintenant » — vit dans la barre latérale, et nulle part ailleurs. Les règles et les flowcharts
sont sauvegardés automatiquement après une courte pause de saisie, même lorsqu'ils sont
incomplets ; le bouton « Enregistrer maintenant » reste disponible pour forcer l'écriture.

**Aucun vocabulaire d'implémentation à l'écran.** Les valeurs d'énumération du modèle
(`IsOneOf`, `BaseName`, `Any`, `GreaterThan`) restent stables dans le JSON, mais ne sont
jamais affichées telles quelles : un convertisseur unique
([Labels.cs](src/App.UI/Localization/Labels.cs)) les traduit, branché sur tous les sélecteurs
par une seule classe de style.

**Une règle se lit comme une phrase**, pas comme un formulaire. « SI *toutes* de ces
conditions sont remplies : le nom — contient — facture ». Chaque éditeur porte ses propres
mots de liaison ; la case « inverser » est devenue « sauf si ». La liste des règles affiche un
résumé généré (« le type est pdf et le nom contient « facture » → déplacer vers D:\Compta »),
pour qu'on sache ce que fait une règle sans l'ouvrir.

**Rien ne s'exécute sans qu'on ait pu le voir avant.** Le bouton « Essayer sur un fichier… »
prend un fichier réel, dit si la règle s'appliquerait et décrit le résultat — nom final,
dossier d'arrivée — sans toucher au disque. C'était la lacune la plus coûteuse : écrire une
règle de suppression demandait jusqu'ici de la tester sur de vrais fichiers. Une action
irréversible (suppression sans corbeille) encadre en outre sa carte de rouge et l'annonce.

Le reste tient à des détails qui évitent la page blanche : chaque liste vide explique l'action
suivante au lieu d'afficher un cadre vide, les colonnes sont numérotées (dossiers, puis
règles), les chemins se choisissent avec « Parcourir… » plutôt qu'en les tapant, et la barre
d'état — commune aux trois espaces — dit quoi faire tant que rien n'est configuré.

L'espace surveillance chiffre l'exercice en cours : fichiers rangés, erreurs, règles actives,
dossiers surveillés, et un compteur par règle. Une règle qui n'a jamais rien traité est
presque toujours une erreur d'écriture ; sans ce compteur, rien ne la distingue d'une règle qui
travaille. Ces compteurs valent pour la session et ne sont pas persistés. Le journal affiche
une ligne par règle déclenchée, actions enchaînées dans le message (« facture.pdf — Factures :
déplacé vers D:\Compta, puis renommé en 2026-08-05_facture.pdf »), plutôt qu'une ligne par
action.

## Modèle de menace

La frontière de confiance passe entre deux choses qu'il serait facile de confondre :

- **le gabarit d'une règle** est écrit par l'utilisateur — donnée de confiance ;
- **le nom du fichier traité** ne l'est pas. Il vient de ce que quelqu'un a déposé dans le
  dossier surveillé : un téléchargement, une pièce jointe, un partage réseau.

Or les tokens injectent la seconde dans la première. Tout ce qui suit protège cette jointure.
Les tests correspondants sont dans [SecurityTests.cs](tests/App.Core.Tests/SecurityTests.cs).

**Injection d'arguments de commande.** `&`, `^`, `|` et les espaces sont des caractères
valides dans un nom de fichier Windows. Concaténés dans une ligne de commande, ils enchaînent
une seconde commande. Le gabarit d'arguments est donc découpé **avant** l'expansion des
tokens, et les arguments sont remis au processus un par un via `ArgumentList` — jamais
concaténés. Une valeur de token ne peut donc pas créer un argument supplémentaire, quel que
soit son contenu. `UseShellExecute` reste à `false` : ni associations de fichiers, ni verbes
shell.

> Pointer `Executable` sur `cmd.exe` ou `powershell.exe` réintroduit un interpréteur qui
> refait sa propre analyse de `/c`. C'est un choix explicite de l'utilisateur, pas un défaut
> du moteur — mais il annule la protection ci-dessus.

**Évasion de chemin.** Les valeurs de tokens sont contraintes à un segment unique dans tout
contexte de chemin (`TokenScope.Path` / `FileName`) : séparateurs, deux-points et jokers sont
neutralisés, `.` et `..` remplacés. Sans cela `{path}` dans une destination produisait un
chemin enraciné, et `Path.Join` aurait purement et simplement écrasé le dossier cible. Les
séparateurs écrits littéralement dans le gabarit, eux, restent intacts. Une destination qui se
développe en chemin relatif est refusée : elle dépendrait du répertoire courant du processus.

**Jonctions et liens symboliques.** La réanalyse d'un dossier exclut les points d'analyse
(`FileAttributes.ReparsePoint`). Sans ça, une jonction déposée dans un dossier surveillé
faisait sortir le parcours de l'arborescence — jusqu'à `C:\Windows` — et un lien circulaire le
faisait tourner indéfiniment.

**Déni de service par expression régulière.** Les motifs sont évalués avec un délai maximal de
250 ms, et un motif invalide ne matche pas au lieu de casser la règle. Un nom de fichier
construit pour faire exploser un motif du type `^(a+)+$` ne bloque donc pas le moteur.

**Bornes mémoire.** La file de stabilisation est plafonnée (100 000 entrées) et le canal de
traitement est borné (50 000). Un dossier qui déverse des millions d'entrées fait ralentir le
producteur au lieu de faire gonfler le processus sans limite.

### Risques assumés, non corrigés

- **Le fichier de règles vaut exécution de code.** Une règle `runCommand` s'exécute avec les
  droits de l'utilisateur. `%APPDATA%` est protégé par les ACL par défaut ; quiconque peut y
  écrire peut déjà faire bien pire. Aucune signature de configuration n'est prévue.
- **L'essai porte sur un fichier à la fois.** « Essayer sur un fichier… » couvre le cas d'une
  règle qu'on écrit, mais il n'existe pas d'aperçu global du type « voici les 300 fichiers que
  cette règle déplacerait si je l'activais ».
- **TOCTOU sur la destination.** Entre le test d'existence et le déplacement, un tiers peut
  créer la cible. En politique `Rename` ou `Skip` l'opération échoue proprement ; en
  `Overwrite`, elle écrase — ce qui est le comportement demandé.

## Performances

Ce que le moteur traite par fichier n'est pas censé être mesurable à côté des I/O disque du
watcher. L'objectif n'était donc pas la vitesse brute mais **la pression sur le GC** : un
utilitaire résident qui digère un dossier de 100 000 fichiers ne doit pas déclencher des
dizaines de collectes.

Mesures sur 200 000 fichiers, une règle, quatre conditions
(`GC.GetAllocatedBytesForCurrentThread`, Release) :

| | avant | après |
|---|---|---|
| Test d'extension seul | 96 o/fichier | **0 o** |
| Extraction des composants du chemin | 213 o/fichier | **80 o** (l'objet `FileContext` lui-même) |
| Expansion de tokens | 868 o/fichier, 13 GC gen0 | **96 o, 1 GC gen0** |

Comment :

- **`FileContext` découpe le chemin une fois** à la construction et expose nom, base,
  extension et dossier en `ReadOnlySpan<char>`. Les exposer en `string` allouait à *chaque
  lecture de propriété*, donc à chaque condition sur chaque fichier — et `Extension` ajoutait
  un `ToLowerInvariant`.
- **`ValueStringBuilder`** compose les noms et chemins dans un `stackalloc` de 320 caractères,
  avec repli sur `ArrayPool` au débordement. Le cas courant n'alloue que la chaîne finale.
  Les nombres et dates sont écrits par `TryFormat` directement dans le tampon.
- **Aiguillage des tokens par `switch` sur span** contre littéraux : le compilateur en fait un
  saut par longueur puis par caractère. La chaîne de comparaisons insensibles à la casse ne
  sert plus qu'aux graphies non canoniques.
- **Un `Regex` par condition**, conservé dans une table à clés faibles. Les surcharges
  statiques de `Regex` repassent par un cache global — hachage du motif compris — à chaque
  appel.
- **Résolution des règles en O(1).** Le contrôleur indexe les règles par dossier surveillé et
  les pré-trie au chargement de la configuration. Avant, chaque fichier déclenchait un
  balayage des dossiers **puis** un `Contains` sur la liste d'identifiants de règles, soit un
  coût en O(dossiers + règles × identifiants) par événement.
- **Le moteur ne trie plus par fichier** : il vérifie en O(n) que les règles sont déjà
  ordonnées et ne trie que si elles ne le sont pas — ce qui n'arrive jamais en production.

Ce qui a été **écarté** après mesure :

- **Table de hachage pour la liste d'extensions.** Une règle en compte moins de dix ; le
  balayage linéaire sur spans gagne (pas de hachage insensible à la casse, pas
  d'indirection, tout tient en ligne de cache) et n'alloue rien.
- **Gain en temps sur l'expansion de tokens.** Les allocations chutent d'un facteur neuf,
  mais le temps mesuré reste à parité — la machine de test est trop bruitée pour affirmer
  mieux, et ce chemin n'est de toute façon emprunté que par les fichiers qui matchent.

## Choix d'architecture

**Native AOT dès le premier commit, pas ajouté après coup.** Les analyseurs trimming/AOT sont
actifs sur toute la solution via `Directory.Build.props`. Conséquences assumées dans le code :
XAML et bindings compilés, JSON par source generators, P/Invoke par `LibraryImport`,
convertisseurs XAML exposés en statiques plutôt qu'instanciés par réflexion, regex interprété.
La solution compile aujourd'hui sans un seul avertissement IL2xxx/IL3xxx.

**Polymorphisme JSON par registre, pas par `[JsonDerivedType]`.** Les attributs auraient figé
la liste des types dérivés dans `App.Core`. `RuleTypeRegistry` associe un discriminant à un
`JsonTypeInfo` : un module futur (conditions de contenu, OCR, classification IA) enregistre ses
propres types avec son propre contexte généré, sans recompiler le coeur ni invalider les
fichiers de règles existants. Un discriminant inconnu lève une erreur explicite plutôt que
d'être ignoré silencieusement — un fichier écrit par une version ultérieure ne doit jamais être
chargé amputé.

**L'évaluation des conditions est asynchrone alors que toutes les conditions v1 sont
synchrones.** C'est délibéré : une condition de contenu (lecture, OCR, appel LLM local)
s'ajoutera comme un `IConditionEvaluator` de plus, sans changer une signature ni toucher au
moteur.

**Surveillance par événements, jamais par polling.** `ReadDirectoryChangesW` en appel bloquant
sur un thread dédié par dossier, annulation par `CancelIoEx`. Le débordement du buffer noyau
est détecté et déclenche une réanalyse du dossier, plutôt que de perdre des fichiers en
silence.

**Debounce + stabilisation.** Une copie de fichier produit une rafale d'événements. Le
`FileStabilizer` coalesce par chemin, attend une période de calme, puis vérifie que le fichier
est réellement ouvrable et que sa taille a cessé de bouger — sinon on traiterait un
téléchargement en cours.

**Traitement sérialisé.** Les fichiers stabilisés passent par un `Channel` à consommateur
unique : deux règles ne peuvent pas manipuler le même fichier simultanément.

## Hors périmètre v1

Pas d'OCR ni de lecture de contenu, pas de tags, pas d'intégration cloud, pas de
multiplateforme. L'architecture est faite pour les accueillir sans réécriture ; elle ne les
anticipe pas par du code mort.

## Limites connues

- L'UI n'expose pas l'édition de groupes de conditions imbriqués. Le format les supporte et le
  moteur les évalue (`ConditionGroup`) ; l'éditeur v1 s'en tient au ET/OU au niveau de la règle.
- L'association règle → dossier existe dans le modèle (`WatchedFolder.RuleIds`) mais l'éditeur
  ne la propose pas encore : toutes les règles s'appliquent à tous les dossiers surveillés.
- L'essai à blanc ne prend qu'un fichier à la fois : pas encore d'aperçu de l'effet d'une
  règle sur tout un dossier.

## Nom

`Banog` est le nom de travail du dossier. Pistes cohérentes avec une identité
minimaliste/cyberpunk et l'idée d'automatisation silencieuse : **Quiet**, **Undertow**,
**Nocturne**, **Silt**, **Drift**. À trancher avant la première diffusion — le nom est présent
dans `AssemblyName`, le chemin `%APPDATA%`, et le manifeste.
