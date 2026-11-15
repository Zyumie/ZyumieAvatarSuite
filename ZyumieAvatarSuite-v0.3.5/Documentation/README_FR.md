# Zyumie Avatar Suite 0.3.0

Package Unity Editor pour projets VRChat sous Unity 2022.3 LTS.

## Installation

1. Décompresse l'archive.
2. Dans Unity : Window > Package Manager > `+` > Add package from disk.
3. Sélectionne `package.json` à la racine de l'archive.
4. Ouvre `Zyumie > Avatar Suite`.
5. Glisse la racine de l'avatar dans `Avatar Root`.

Le SDK VRChat doit être installé séparément. Le package inspecte `VRCAvatarDescriptor` sans référence de compilation directe au SDK.

## Nouveautés 0.3.0

### Optimisation de textures sur copie

- Crée une copie de l'avatar dans la scène.
- Clone les matériaux utilisés par cette copie.
- Copie les textures source prises en charge vers `Assets/ZyumieAvatarSuite/Generated/<Avatar>/Textures`.
- Applique une résolution maximale configurable et une compression.
- Configure aussi les paramètres Android des textures copiées.
- Réassigne les matériaux clonés uniquement sur la copie de l'avatar.
- Ne modifie pas les matériaux ni textures originaux.
- Ignore les textures non prises en charge ou sans asset source importé.

Formats sources traités : PNG, JPG/JPEG, TGA, PSD, TIF/TIFF, BMP, EXR et HDR. Les RenderTextures, Texture2DArray, textures intégrées à des modèles et ressources non prises en charge sont ignorées.

La taille finale dépend du format, de l'importeur Unity, de la plateforme et de la configuration de build. La mémoire estimée du rapport n'est pas la taille finale de l'upload.

### Analyse

- Triangles et sommets.
- Skinned Meshes, Basic Meshes, slots matériaux, os référencés et BlendShapes.
- Bounds estimées, textures uniques et mémoire brute estimée.
- Animator, PhysBones, PhysBone Colliders, Contacts, contraintes, lights, particules, AudioSources, line/trail renderers et Missing Scripts.
- Repères mobiles affichés à titre indicatif uniquement.

Le rang final doit être vérifié dans le VRChat SDK Builder.

### Préparation plateforme non destructive

- Création d'une copie Windows / PCVR.
- Création d'une copie Android / Quest / Steam Frame autonome.
- Aucun Collider, Rigidbody, Light, AudioSource, PhysBone, Contact ou autre composant n'est supprimé automatiquement.
- Le SDK VRChat Builder reste responsable du build et de la validation.

VRChat utilise deux cibles de build : Windows et Android. Quest et Steam Frame autonome utilisent Android ; Steam Frame en PC streaming utilise Windows.

### Tracking

- Inspection des champs du Descriptor contenant Eye, Eyelid, Lip, Face ou Tracking.
- Détection indicative d'os des yeux par leur nom.
- Comptage indicatif des BlendShapes liées aux yeux, paupières et bouche.
- Désactivation optionnelle des booléens sérialisés contenant `faceTracking`.
- Aucun os, BlendShape, visème, menu ou Animator Controller n'est supprimé par le diagnostic.

## Limites connues

- Pas de réduction polygonale automatique : Unity 2022.3 ne fournit pas de décimateur générique fiable pour les Skinned Meshes avec BlendShapes.
- Pas de fusion automatique de meshes ni d'atlas UV.
- Pas de garantie de compatibilité des shaders PC avec Android.
- Pas de calcul du rang officiel VRChat.
- Pas de build ni d'upload automatique.
- Les copies de scène et les assets générés doivent être vérifiés avant publication.

## Sauvegarde

Fais un commit Git ou une sauvegarde du projet avant tout test. Les assets générés sont stockés sous `Assets/ZyumieAvatarSuite/Generated/`. Vérifie qu'ils ne sont plus référencés avant de les supprimer.
