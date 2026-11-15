# Zyumie Avatar Suite 0.2.0

Package Unity Editor pour projets VRChat sous Unity 2022.3 LTS.

## Installation

1. Décompresse l'archive.
2. Dans Unity : Window > Package Manager > bouton `+` > Add package from disk.
3. Sélectionne `package.json`.
4. Ouvre le menu `Zyumie > Avatar Suite`.
5. Glisse la racine de l'avatar dans `Avatar Root`.

Le SDK VRChat doit être installé séparément dans le projet Unity. Le package évite une référence de compilation directe au SDK et inspecte le `VRCAvatarDescriptor` via SerializedObject.

## Modules

### Analyse
- Triangles calculés depuis les sous-meshes en topologie triangles.
- Skinned Meshes, Basic Meshes, slots matériaux, os référencés, BlendShapes.
- Bounds estimées, textures uniques et mémoire brute estimée.
- Animators, PhysBones, PhysBone Colliders, Contacts, contraintes détectées, lights, particules, AudioSources, line/trail renderers, Missing Scripts.
- Repères mobiles VRChat intégrés au rapport.

Les métriques ne sont pas un clone du calcul SDK. Le rang final doit être lu dans le VRChat SDK Builder. Les performances VRChat ne sont pas déterminées uniquement par les triangles.

### Optimisation prudente
- Nettoyage des Missing Scripts avec Undo.
- Aucun mesh, matériau, os, blendshape ou contrôleur d'animation n'est supprimé automatiquement.

### Tracking
- Inspection des champs du Descriptor contenant Eye, Eyelid, Lip, Face ou Tracking.
- Détection indicative d'os des yeux par leur nom.
- Comptage indicatif des BlendShapes liées aux yeux, paupières et bouche.
- Désactivation des booléens sérialisés contenant `faceTracking` sur le Descriptor, si le SDK expose de tels champs.
- Les paramètres Animator, menus, visèmes et BlendShapes ne sont pas supprimés.

### Plateformes
- Création de copies de travail PCVR et Android.
- La copie Android/Quest et la copie Steam Frame appliquent une préparation limitée sur les copies : retrait des Lights, Cloth, AudioSource, Rigidbody et Collider Unity.
- Les copies ne sont pas des builds : utiliser le VRChat SDK Builder pour choisir Windows ou Android et vérifier Build & Test.

## Steam Frame / FrameEyeOSC

Steam Frame autonome doit être considéré comme une cible Android/mobile lorsque le client VRChat utilise la version Android. Il ne s'agit pas d'une troisième plateforme de build VRChat indépendante.

FrameEyeOSC peut transmettre des données de regard à VRChat via OSC selon sa configuration. Les points du debug OSC qui bougent prouvent la réception de données, pas leur application à l'avatar. Vérifier la sortie native VRChat built-in eye tracking dans FrameEyeOSC, activer OSC dans VRChat et éviter plusieurs émetteurs OSC concurrents.

VRChat OSC eye tracking :
https://docs.vrchat.com/docs/osc-eye-tracking

Après 10 secondes sans données eye tracking, VRChat revient au comportement auto-look / auto-blink.

Le Steam Frame / FrameEyeOSC ne fournit pas à lui seul un suivi optique de la bouche. La bouche peut continuer à fonctionner avec les visèmes audio VRChat. Un vrai suivi des lèvres nécessite une autre source de face tracking.

## Ce que cette version ne fait pas

- Ne génère pas de données de tracking matériel.
- Ne fabrique pas de suivi des lèvres à partir du microphone.
- Ne garantit pas la compatibilité Android d'un shader PC.
- Ne fusionne pas les meshes, ne réduit pas automatiquement les polygones, ne génère pas d'atlas textures et ne réécrit pas les Animator Controllers.
- Ne calcule pas le rang officiel VRChat.
- Ne téléverse pas les avatars.

Ces opérations nécessitent une analyse spécifique des assets et une validation visuelle. Une automatisation aveugle risquerait de casser les vêtements, les expressions, les menus ou le rig.

## Sauvegarde / Git

Avant les opérations, sauvegarde le projet ou commit Git. Versionner `Assets`, `Packages/manifest.json` et `Packages/packages-lock.json` selon les règles Unity ; ne pas versionner `Library`, `Temp`, `Obj` ou `Logs`.

## Sources

- VRChat Performance Ranking System: https://creators.vrchat.com/avatars/avatar-performance-ranking-system/
- VRChat OSC Eye Tracking: https://docs.vrchat.com/docs/osc-eye-tracking
- VRChat Avatar Creation: https://creators.vrchat.com/avatars/
