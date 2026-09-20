# DesktopTools

![DesktopTools en action](assets/readme/hero.gif)

[English](README.md) · [Русский](README.ru.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md)

⭐ **Vous aimez avoir tous vos outils au même endroit ? [Ajoutez une étoile à DesktopTools sur GitHub](https://github.com/vg2222/DesktopTools) et aidez d’autres personnes à le découvrir.**

![Windows 11 x64](https://img.shields.io/badge/Windows_11-x64-0078D4?style=flat)
[![Dernière version](https://img.shields.io/github/v/release/vg2222/DesktopTools?style=flat&color=2563eb)](https://github.com/vg2222/DesktopTools/releases/latest)
[![Licence MIT](https://img.shields.io/badge/Open_source-MIT-64748b?style=flat)](LICENSE)
[![Traitement local](https://img.shields.io/badge/Processing-local-0f766e?style=flat)](docs/security-checks.md)

## Votre boîte à outils Windows pour les captures d’écran, l’enregistrement, les présentations et les tâches quotidiennes.

## 🚀 Télécharger DesktopTools

[**Télécharger pour Windows →**](https://github.com/vg2222/DesktopTools/releases/latest) &nbsp; · &nbsp; [Version portable](https://github.com/vg2222/DesktopTools/releases/latest) &nbsp; · &nbsp; [Notes de version](https://github.com/vg2222/DesktopTools/releases/latest)

Windows 11 · 64 bits · Aucune installation séparée de .NET

Sécurité : [sommes de contrôle SHA-256](https://github.com/vg2222/DesktopTools/releases/latest/download/SHA256SUMS.txt) · [Vérifier le téléchargement](docs/security-checks.md)

<sub>Le programme d’installation actuel n’est pas signé ; Windows SmartScreen peut afficher un avertissement. L’installation se fait pour votre compte utilisateur et ne nécessite aucun droit d’administrateur.</sub>

---

## ✨ Une seule boîte à outils, moins d’applications

**De la capture rapide au tutoriel complet.** Capturez une zone, mettez en évidence le détail important ou enregistrez une fenêtre avec votre voix. Passez de la démonstration à l’explication sans avoir à réunir toute une collection d’applications distinctes.

**Gardez votre public avec vous.** Dessinez par-dessus votre écran, pointez avec un laser, éclairez le détail important ou gardez le fil grâce au téléprompteur et aux minuteurs.

**Réglez aussi les petites tâches.** Lisez le texte d’une capture d’écran, traduisez localement entre l’anglais et le russe, gardez vos notes à portée de main, générez un code QR ou recadrez une image avant de la partager.

## 📸 Trois façons de commencer

### 🏠 Un espace d’accueil pour vos outils quotidiens

Épinglez les outils que vous utilisez le plus et retrouvez tous les autres au même endroit.

![Écran d’accueil de DesktopTools](assets/readme/home.png)

### ✏️ Expliquez avec une capture d’écran

Ajoutez des flèches, des formes et du texte, puis exportez une copie tout en préservant l’original.

![Éditeur de captures d’écran avec annotations](assets/readme/editor.png)

### 🎬 Enregistrez exactement la bonne vue

Choisissez un écran, une fenêtre ou une zone, puis réglez le son et la qualité avant d’enregistrer.

![Enregistreur d’écran avec une source sélectionnée](assets/readme/recorder.png)

## 🔒 Conçu pour un traitement local

Aucun compte. Aucune télémétrie. Aucun traitement de votre contenu dans le cloud. Vos captures d’écran, notes, reconnaissances de texte, traductions anglais–russe et suppressions d’arrière-plan sont traitées sur votre PC.

Vous choisissez où enregistrer les images et les enregistrements. La vérification automatique des mises à jour contacte GitHub pour obtenir les informations de version ; elle ne téléverse ni vos médias ni vos notes. Modifiez sa fréquence ou désactivez-la dans **Paramètres → Mises à jour**.

Les commandes d’enregistrement et de dessin, les notifications, les notes flottantes et le téléprompteur sont masqués par défaut dans les captures compatibles. Les dessins et les effets destinés au public restent visibles. Réglez chaque outil dans **Paramètres → Confidentialité**. L’exclusion de la capture dépend de l’application utilisée pour enregistrer ou partager : vérifiez la vue reçue avant de vous y fier.

## ⚙️ Adaptez-le à vos besoins

1. Installez DesktopTools ou extrayez l’archive ZIP portable, puis ouvrez l’application.
2. Lors de la configuration initiale, choisissez l’apparence, les outils et les préférences de partage d’écran.
3. Épinglez vos outils favoris et personnalisez leurs raccourcis.

DesktopTools reste dans la zone de notification lorsque vous fermez sa fenêtre principale. Cliquez sur son icône pour le rouvrir ; choisissez **Quitter** dans le menu de l’icône pour fermer l’application.

| Action | Raccourci par défaut |
| --- | --- |
| Dessiner / interagir | Ctrl + Alt + D |
| Capturer une zone | Ctrl + Alt + S |
| Afficher / masquer les commandes de dessin | Ctrl + Alt + H |
| Pointeur laser | Ctrl + Alt + L |
| Projecteur | Ctrl + Alt + O |
| Arrêt sur image | Ctrl + Alt + F |

La page **Raccourcis** répertorie toutes les associations de fonctions. Les raccourcis par défaut sont activés ; les raccourcis facultatifs sont désactivés au départ. Chaque raccourci dispose de son propre interrupteur.

## 💡 Quelques précisions

L’enregistrement d’écran nécessite Microsoft Visual C++ x64 Redistributable et Windows Media Foundation. Les fréquences d’enregistrement jusqu’à 144 IPS sont des objectifs ; les performances réelles dépendent de la source, de l’encodeur et du matériel. Consultez la page [Compatibilité](docs/compatibility.md) pour connaître les limites de capture et la couverture actuelle des tests. Le programme d’installation vérifie ce composant et propose la page de téléchargement de Microsoft si nécessaire.

L’application prend en charge **l’anglais, le russe, l’allemand, le français et l’espagnol**, ainsi que les apparences claire, sombre et système. Les langues de l’interface sont indépendantes de la paire de traduction anglais–russe actuellement prise en charge.

<details>
<summary><strong>🛠️ Compiler et contribuer</strong></summary>

Utilisez Windows 11 x64 et la version du SDK .NET définie dans [global.json](global.json). Récupérez les modèles de traduction dont les sommes de contrôle sont fixées, puis lancez la compilation :

```powershell
./scripts/fetch-translation-models.ps1
./scripts/build.ps1
```

Consultez [Contribuer](CONTRIBUTING.md) pour exécuter les vérifications et proposer des modifications, [Architecture](docs/architecture.md) pour découvrir la structure du projet et [Mises à jour](docs/updates.md) pour préparer les paquets de publication.

</details>

## ⭐ Aidez-nous à améliorer DesktopTools

Si DesktopTools a trouvé sa place sur votre bureau, [ajoutez-lui une étoile](https://github.com/vg2222/DesktopTools). Vous avez repéré un problème ? [Signalez un bug](https://github.com/vg2222/DesktopTools/issues/new?template=bug_report.yml) ou [proposez une fonctionnalité](https://github.com/vg2222/DesktopTools/issues/new?template=feature_request.yml).

[Licence MIT](LICENSE) · [Mentions relatives aux composants tiers](THIRD-PARTY-NOTICES.md) · [Politique de sécurité](SECURITY.md)
