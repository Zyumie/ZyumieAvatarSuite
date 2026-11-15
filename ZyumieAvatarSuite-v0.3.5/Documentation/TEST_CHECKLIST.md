# Checklist de test — v0.3.0

## Préparation

- [ ] Dupliquer le projet Unity avant le test.
- [ ] Vérifier Unity 2022.3 LTS.
- [ ] Installer le VRChat Avatars SDK dans le projet de test.
- [ ] Importer Zyumie Avatar Suite via Package Manager > Add package from disk > `package.json`.
- [ ] Ouvrir `Zyumie > Avatar Suite` et sélectionner un avatar de test.

## Analyse

- [ ] Cliquer sur « Analyser l'avatar ».
- [ ] Vérifier que triangles, sommets, BlendShapes, os et composants sont cohérents avec l'avatar.
- [ ] Exporter un rapport `.txt`.

## Optimisation des textures

- [ ] Faire un commit Git ou une copie de sauvegarde du projet.
- [ ] Cliquer sur « Créer une copie + optimiser les textures ».
- [ ] Vérifier qu'un nouvel avatar `_TextureOptimized` apparaît dans la scène.
- [ ] Vérifier les matériaux générés sous `Assets/ZyumieAvatarSuite/Generated/`.
- [ ] Vérifier les textures copiées et leurs paramètres Android dans l'Inspector.
- [ ] Comparer visuellement l'avatar original et la copie : albedo, transparence, normal maps, emission et détails fins.
- [ ] Confirmer que les matériaux et textures originaux n'ont pas changé.
- [ ] Vérifier qu'aucune référence de matériau n'est devenue `Missing`.

## Tracking et interactions

- [ ] Comparer avant/après le nombre de BlendShapes et les références d'os.
- [ ] Vérifier Eye Look, paupières et visèmes.
- [ ] Vérifier PhysBones, PhysBone Colliders, Contacts, colliders et interactions.
- [ ] Vérifier les menus et Animator Controllers.

## Builds

- [ ] Lancer VRChat SDK Build & Test sur Windows.
- [ ] Lancer VRChat SDK Build & Test sur Android.
- [ ] Vérifier les avertissements SDK et les performances sur chaque cible.
- [ ] Tester l'avatar en jeu avant publication.

## À signaler

Joindre le rapport exporté, les erreurs Unity Console complètes, la version Unity et la plateforme testée. Ne pas publier comme version stable tant que les tests Windows et Android ne sont pas passés.
