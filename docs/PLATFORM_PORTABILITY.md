# Portage macOS / Linux — état des lieux

Notes pour la reprise du projet sur une autre plateforme. Le choix Rust + Avalonia
(voir la mémoire du projet, décision du 2026-09-03) visait justement cette portabilité ;
ce document liste précisément ce qui est déjà acquis et ce qui, dans le travail réalisé
depuis, est spécifique à Windows et à réécrire.

## Déjà portable

- **`core/almatter-core` + `core/almatter-ffi`** : Rust pur (reqwest, rusqlite, tokio,
  tokio-tungstenite, serde). Aucun code spécifique à un OS. `#![forbid(unsafe_code)]`
  sur `almatter-core` ; tout ce qui touche la frontière FFI est confiné à `almatter-ffi`.
  Devrait se recompiler tel quel pour macOS/Linux (à vérifier : la feature `native-tls`
  de `tokio-tungstenite` s'appuie sur la brique TLS de l'OS — Secure Transport sur macOS,
  OpenSSL sur Linux — donc juste une dépendance système à avoir en place au build, pas
  de code à changer).
- **`app/Almatter.App/Views/*.axaml` + ViewModels + Models** : XAML Avalonia, liaisons de
  données, commandes — rendu par le moteur cross-platform d'Avalonia (Skia), aucune
  dépendance Windows dans ces fichiers eux-mêmes.
- **`NativeCore.cs`** : le nom de bibliothèque P/Invoke est `"almatter_ffi"` **sans
  extension** (`app/Almatter.App/Interop/NativeCore.cs:21`) — .NET résout automatiquement
  `almatter_ffi.dll` / `libalmatter_ffi.so` / `libalmatter_ffi.dylib` selon l'OS. Il reste
  seulement à produire le bon binaire natif par plateforme et à adapter sa copie dans le
  csproj (actuellement un `<None Include="...\target\debug\almatter_ffi.dll">` codé en
  dur pour Windows).

## Spécifique à Windows (ajouté depuis le début du projet)

Le projet cible encore `<TargetFramework>net10.0-windows</TargetFramework>` ; un vrai
portage doit d'abord cibler `net10.0` (ou multi-cibler). **Windows Forms n'est plus utilisé
depuis le 2026-09-14** (`UseWindowsForms` retiré) : il coûtait ~13 Mo pour une icône de
notification et le presse-papiers, et n'existe pas hors Windows. Ce qui reste propre à
Windows est isolé ci-dessous.

| Fonctionnalité | Fichier | Mécanisme Windows | Piste macOS / Linux |
|---|---|---|---|
| Notifications de mention (+ icône de la zone de notification) | `Services/DesktopNotifier.cs` (`WindowsTrayNotifier`), utilisé via l'interface `IDesktopNotifier` | `Shell_NotifyIconW` (shell32, P/Invoke sans `unsafe`) ; le clic revient comme message fenêtre, reçu par `Win32Properties.AddWndProcHookCallback` d'Avalonia | Écrire une implémentation de `IDesktopNotifier` par OS et la brancher dans `DesktopNotifier.Create` — hors Windows, elle renvoie aujourd'hui une implémentation vide (l'app marche, sans notifications). Linux : `org.freedesktop.Notifications` (D-Bus), qui gère aussi le clic (signal `ActionInvoked`). macOS : `UNUserNotificationCenter`. |
| Barre de titre sombre | `Views/MainWindow.axaml.cs` (`ApplyTitleBarTheme`, P/Invoke `dwmapi.dll`) | `DwmSetWindowAttribute` (Windows uniquement) | macOS : `NSWindow.appearance` (interop Cocoa/Avalonia natif). Linux : dépend du gestionnaire de fenêtres, généralement non pilotable depuis l'appli — probablement à laisser tomber ou no-op. |
| Badge sur l'icône (barre des tâches) | `Services/TaskbarBadge.cs` (interop COM `ITaskbarList3`, `CreateIconIndirect`) | Shell Windows uniquement. Le **dessin** de la pastille (`RenderPixels`) passe par Skia et est portable ; seule sa pose sur le bouton est propre à Windows | macOS : `NSDockTile.badgeLabel` (interop Cocoa). Linux : pas d'équivalent standard (quelques DE supportent l'API Unity Launcher, la plupart non) — no-op probable. |
| Session persistée de façon sécurisée | `Services/SessionStore.cs` | `System.Security.Cryptography.ProtectedData` (DPAPI, Windows uniquement) — paquet NuGet explicite depuis le retrait de Windows Forms, qui l'apportait implicitement | macOS : Keychain (interop Security.framework). Linux : Secret Service / libsecret (D-Bus), avec repli sur un fichier à permissions restreintes si indisponible. |

### Installation

`installer/` (script Inno Setup `Almatter.iss` + `build-installer.ps1`) est entièrement
propre à Windows : publication `win-x64` dépendante du runtime, installation par
utilisateur, téléchargement et installation du runtime .NET 10 s'il manque (détecté dans
`Program Files\dotnet\shared\Microsoft.NETCore.App\10.*`). Rien n'y est réutilisable
tel quel. Pistes : macOS, un `.app` signé et notarié dans un `.dmg` (runtime embarqué,
pas d'installeur de dépendances sur ce système) ; Linux, AppImage ou Flatpak, où le
runtime .NET peut être embarqué ou pris dans le SDK Flatpak freedesktop/dotnet.
Le script de publication devra aussi viser le bon RID (`osx-arm64`, `linux-x64`) et
copier `libalmatter_ffi.dylib`/`.so` (voir le point 4 ci-dessous).
`installer/test-in-sandbox.ps1` teste l'installation sur un Windows vierge via Windows
Sandbox ; l'équivalent ailleurs serait une VM ou un conteneur propre.

`core/.cargo/config.toml` lie le runtime Visual C++ en statique dans `almatter_ffi.dll`
(section limitée à la cible `x86_64-pc-windows-msvc`, sans effet sur les autres OS). Sur
Linux, l'équivalent à surveiller est la version de glibc de la machine de build : compiler
sur une distribution trop récente rend le `.so` inutilisable sur les machines plus
anciennes visées par le projet.

## Déjà cross-platform, à ne pas confondre

- **Collage qui conserve les liens** (`Services/LinkAwarePaste.cs`) : passe par le
  presse-papiers d'Avalonia. Le nom du format HTML est choisi par OS (`HTML Format` sous
  Windows, `text/html` sous Linux, `public.html` sous macOS) et lu en octets. Vérifié sous
  Windows avec du HTML écrit comme par un navigateur (UTF-8, accents, tirets, émoji). **Non
  testé** sous Linux/macOS : l'extraction du fragment se replie sur le HTML entier quand les
  marqueurs `StartFragment` propres à Windows sont absents, ce qui devrait suffire, à vérifier.
- **Collage d'images et de fichiers en pièces jointes** (`Services/ClipboardAttachments.cs`) :
  presse-papiers d'Avalonia (`TryGetFilesAsync`, `TryGetBitmapAsync`), image réenregistrée en
  PNG dans `Path.GetTempPath()/Almatter/pasted-images`. Aucun appel Windows. Vérifié sous
  Windows uniquement. **À tester** sous Linux/macOS : que les gestionnaires de fichiers
  (Nautilus, Dolphin, Finder) exposent bien la copie de fichiers comme fichiers et non
  comme simple texte de chemins, et que les captures d'écran arrivent au format `Bitmap`
  (sous X11/Wayland, `image/png`). La règle « image sans texte = image collée » (pour qu'un
  copier depuis Word/Excel reste du texte) devrait valoir aussi pour LibreOffice.
- **Décodage des images d'aperçu de lien** (`ViewModels/CardImageDecoder.cs`) : SkiaSharp,
  livré avec Avalonia sur les trois OS.
- **Suivi du thème clair/sombre du système** (`Services/SystemTheme.cs`) : passe par
  `PlatformSettings.GetColorValues()` d'Avalonia, implémenté sur les trois OS. Seule
  la *teinte de la barre de titre* qui en découle est propre à Windows (ligne ci-dessus).
- **Police de l'interface** : la pile `Segoe UI Variable, Segoe UI, Inter` se replie sur
  Inter (embarqué via `Avalonia.Fonts.Inter`) partout où Segoe est absent, donc macOS et
  Linux rendent correctement sans police à embarquer. Si un rendu vraiment natif est
  souhaité plus tard, il suffira d'ajouter `-apple-system`/`Cantarell` en tête de pile :
  la police n'est déclarée qu'à deux endroits, l'attribut `FontFamily` de `MainWindow.axaml`
  et celui de `LoginWindow.axaml`, dont tout le reste hérite.
- **Police du code dans les messages** (`Views/MessageInlineText.cs`, `MonospaceFont`) :
  une pile `Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, Courier New`,
  qui prend la première présente — Menlo sur macOS, DejaVu/Liberation sur la plupart des
  Linux. Rien à faire au portage, sauf si une distribution n'a aucune des deux.

## Point d'entrée pour la reprise

1. Faire cibler `net10.0` (multi-ciblage `net10.0-windows`/`net10.0` si Windows doit
   rester supporté en parallèle). `UseWindowsForms` est déjà retiré.
2. Écrire l'implémentation Linux de `IDesktopNotifier` (D-Bus). Si une icône permanente
   dans la zone de notification est voulue sur les autres OS, `Avalonia.Controls.TrayIcon`
   existe — mais il ne sait pas afficher de notification, d'où l'interface.
3. Encapsuler chaque bloc du tableau ci-dessus derrière une vérification de plateforme
   (`OperatingSystem.IsWindows()` / `IsMacOS()` / `IsLinux()`) avec une implémentation
   (ou un no-op assumé) par OS.
4. Adapter le csproj pour copier le bon binaire natif (`.dll`/`.so`/`.dylib`) selon la
   cible de build.

Rien de ce qui précède ne touche au cœur Rust ni à la logique métier des ViewModels —
le travail de portage est localisé à ces quelques points d'intégration OS.
