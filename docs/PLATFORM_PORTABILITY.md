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

Tout ce qui suit vient de `Almatter.App.csproj` : `<TargetFramework>net10.0-windows</TargetFramework>`
et `<UseWindowsForms>true</UseWindowsForms>` — un vrai portage doit d'abord cibler
`net10.0` (ou multi-cibler) et retirer/remplacer chaque usage de WinForms listé ici.

| Fonctionnalité | Fichier | Mécanisme Windows | Piste macOS / Linux |
|---|---|---|---|
| Icône barre système + notifications | `Views/MainWindow.axaml.cs` (`SetupTrayIcon`, `ShowMentionNotification`) | `System.Windows.Forms.NotifyIcon` + `ShowBalloonTip` | Avalonia a son **propre** `TrayIcon` cross-platform (`Avalonia.Controls.TrayIcon`) — à utiliser à la place plutôt que du code par OS. Pour les notifications elles-mêmes, pas d'équivalent Avalonia intégré : notification native macOS (`UNUserNotificationCenter` via interop) et `org.freedesktop.Notifications` (D-Bus) sur Linux. |
| Presse-papiers lors du collage de liens | `Views/MainWindow.axaml.cs` (`TryHandleLinkAwarePaste`, `ExtractHtmlFragment`) | `System.Windows.Forms.Clipboard`, format `CF_HTML` (en-tête StartFragment/EndFragment propre à Windows) | Utiliser l'`IClipboard`/`TopLevel.Clipboard` d'Avalonia (déjà cross-platform) ; l'extraction du fragment HTML doit être adaptée, macOS/Linux n'exposent pas le même format d'en-tête que CF_HTML. |
| Barre de titre sombre | `Views/MainWindow.axaml.cs` (`ApplyTitleBarTheme`, P/Invoke `dwmapi.dll`) | `DwmSetWindowAttribute` (Windows uniquement) | macOS : `NSWindow.appearance` (interop Cocoa/Avalonia natif). Linux : dépend du gestionnaire de fenêtres, généralement non pilotable depuis l'appli — probablement à laisser tomber ou no-op. |
| Badge sur l'icône (barre des tâches) | `Services/TaskbarBadge.cs` (interop COM `ITaskbarList3`) | Shell Windows uniquement | macOS : `NSDockTile.badgeLabel` (interop Cocoa). Linux : pas d'équivalent standard (quelques DE supportent l'API Unity Launcher, la plupart non) — no-op probable. |
| Session persistée de façon sécurisée | `Services/SessionStore.cs` | `System.Security.Cryptography.ProtectedData` (DPAPI, Windows uniquement) | macOS : Keychain (interop Security.framework). Linux : Secret Service / libsecret (D-Bus), avec repli sur un fichier à permissions restreintes si indisponible. |

## Déjà cross-platform, à ne pas confondre

- **Suivi du thème clair/sombre du système** (`Services/SystemTheme.cs`) : passe par
  `PlatformSettings.GetColorValues()` d'Avalonia, implémenté sur les trois OS. Seule
  la *teinte de la barre de titre* qui en découle est propre à Windows (ligne ci-dessus).
- **Police de l'interface** : la pile `Segoe UI Variable, Segoe UI, Inter` se replie sur
  Inter (embarqué via `Avalonia.Fonts.Inter`) partout où Segoe est absent, donc macOS et
  Linux rendent correctement sans police à embarquer. Si un rendu vraiment natif est
  souhaité plus tard, il suffira d'ajouter `-apple-system`/`Cantarell` en tête de pile :
  la police n'est déclarée qu'à deux endroits, l'attribut `FontFamily` de `MainWindow.axaml`
  et celui de `LoginWindow.axaml`, dont tout le reste hérite.

## Point d'entrée pour la reprise

1. Faire cibler `net10.0` (multi-ciblage `net10.0-windows`/`net10.0` si Windows doit
   rester supporté en parallèle) et retirer `UseWindowsForms`.
2. Remplacer le tray icon par `Avalonia.Controls.TrayIcon` (fonctionne déjà sur les
   trois OS) ; ne garder du code par-OS que pour les notifications et le badge/dock,
   qui n'ont pas d'API commune.
3. Encapsuler chaque bloc du tableau ci-dessus derrière une vérification de plateforme
   (`OperatingSystem.IsWindows()` / `IsMacOS()` / `IsLinux()`) avec une implémentation
   (ou un no-op assumé) par OS.
4. Adapter le csproj pour copier le bon binaire natif (`.dll`/`.so`/`.dylib`) selon la
   cible de build.

Rien de ce qui précède ne touche au cœur Rust ni à la logique métier des ViewModels —
le travail de portage est localisé à ces quelques points d'intégration OS.
