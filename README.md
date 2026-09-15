<p align="center">
  <img src="app/Almatter.App/Assets/almatter-logo.png" alt="Almatter logo" width="96">
</p>

<h1 align="center">Almatter</h1>

<p align="center">
  A lightweight native desktop client for <a href="https://mattermost.com">Mattermost</a>, Windows first.<br>
  Shared Rust core, Avalonia UI.
</p>

<p align="center">
  <b>English</b> · <a href="#francais">Français</a>
</p>

---

<a id="english"></a>

Almatter is a personal, independent Mattermost client. It exists because the official
desktop app is an Electron wrapper that routinely uses 500–600 MB of memory; Almatter
aims to do the everyday work — reading, writing, threads, search, notifications — in a
fraction of that, and to stay usable on older machines.

> **Status: early pre-release (0.5.0).** It is used daily, but expect rough edges.
> The interface is currently **in French only**.

## Features

- **Fast channel switching** — messages are cached locally (SQLite), so a channel opens
  instantly from the cache and then catches up with the server.
- **Real-time updates** over WebSocket: new messages, edits, reactions, typing indicators,
  presence, and read state synced with your other devices.
- **Messaging** — Markdown rendering with a formatting toolbar, edit and delete, threads
  in a side panel, reactions and an emoji picker, `@mentions` with autocomplete, pinned
  messages.
- **Attachments** — attach files, or paste images and files straight into the composer.
  Pasting from a web page keeps its links.
- **Link previews**, with a setting to turn them off.
- **Search** messages, browse and join channels, start direct messages.
- **Offline outbox** — messages written while disconnected are queued and sent when the
  connection comes back.
- **Windows integration** — mention notifications from the notification area, an unread
  badge on the taskbar icon, a title bar that follows the theme.
- **Appearance** — light and dark themes (or follow Windows), a reduced-contrast mode,
  adjustable message font size, and a choice between the official client's Open Sans
  and the system UI font.

### Not supported (yet)

- Sign-in with **multi-factor authentication or SSO** (GitLab, SAML, Office 365…).
  Only username/e-mail and password work today.
- Multiple servers or teams at once, calls, boards, playbooks and plugins.
- macOS and Linux — see [Other platforms](#other-platforms-en).

## Install

**Requirements:** Windows 10 or 11, 64-bit (x64).

1. Download `Almatter-Setup-<version>.exe` from the
   [Releases page](https://github.com/vincentnxi/almatter/releases).
2. Run it. Almatter installs for your user only and needs no administrator rights.

Almatter runs on Microsoft's .NET 10 runtime. If it isn't already on your computer, the
installer downloads it (about 30 MB, checked against a pinned SHA-256) and installs it —
that single step asks for Windows' permission.

> The installer is not code-signed yet, so Windows SmartScreen may warn about an
> unknown publisher. Choose *More info → Run anyway* to continue.

Updating is the same: run the newer installer over the existing install.

**Uninstalling** from Windows Settings removes the program and asks whether to also
delete your data (saved sign-in, settings, cached messages). That data lives in
`%LOCALAPPDATA%\Almatter` and `%APPDATA%\Almatter`.

## Privacy and security

- There is **no telemetry, analytics or third-party service**. Almatter talks to the
  Mattermost server you sign in to, with one exception: a link preview's image is
  downloaded from the linked website itself, like a browser would. Turning link previews
  off in the settings stops that.
- Your password is never stored. The session token is saved encrypted with Windows' data
  protection API (DPAPI), readable only by your Windows account.
- Messages are cached on your disk so they open instantly and remain readable offline.

## How it's built

```
core/                  Rust workspace
  almatter-core/       Mattermost API client, SQLite cache, WebSocket, offline outbox
  almatter-ffi/        C-compatible library (almatter_ffi.dll) exposing the core
app/Almatter.App/      Desktop app: Avalonia UI, MVVM, calls the core through P/Invoke
installer/             Inno Setup script, build and Windows Sandbox test scripts
design/                Interface mockups the UI was designed from
docs/                  Project notes (platform portability)
licenses/              Licences of bundled third-party assets
```

All networking, caching and synchronisation happen in the Rust core, which contains no
`unsafe` code (`#![forbid(unsafe_code)]`) and no operating-system-specific code. The UI
exchanges JSON with it through a single FFI entry point. The C# side keeps OS-specific
code to a few isolated integration points.

## Building from source

### Prerequisites

- [Rust](https://rustup.rs) (stable, MSVC toolchain — `rustup` offers to install the
  Visual Studio C++ build tools it needs)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — only to build the installer

### Run a development build

```powershell
# 1. Build the Rust core (produces core\target\debug\almatter_ffi.dll)
cd core
cargo build -p almatter-ffi

# 2. Build and start the app (the csproj copies the DLL next to the executable)
cd ..
dotnet run --project app\Almatter.App
```

For a release build, use `cargo build --release -p almatter-ffi` and
`dotnet build -c Release`: each configuration picks up the matching Rust build.

> If Almatter is already running (including minimised to the notification area), Windows
> locks its files and the new build won't replace them. Quit it first.

### Run the tests

```powershell
cd core
cargo test
```

### Build the installer

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

This builds the Rust core in release mode, publishes the app for `win-x64`, and compiles
`installer\output\Almatter-Setup-<version>.exe`. The version comes from `<Version>` in
`app/Almatter.App/Almatter.App.csproj`.

`installer\test-in-sandbox.ps1` then installs that setup in a clean
[Windows Sandbox](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview)
and checks that the app starts. Expect about six minutes there: the .NET runtime install
is much slower in the Sandbox than on a real PC.

<a id="other-platforms-en"></a>

## Other platforms

Almatter targets Windows only for now, but it was built to be portable: the Rust core and
the Avalonia views run on macOS and Linux as they are. What remains Windows-specific
(notifications, taskbar badge, title bar colour, secure session storage, the installer) is
listed in [docs/PLATFORM_PORTABILITY.md](docs/PLATFORM_PORTABILITY.md) (in French), with a
suggested approach for each OS.

## Licence

Almatter is released under the [MIT License](LICENSE).

It bundles the Open Sans font under the SIL Open Font License 1.1 — see
[licenses/](licenses/).

Almatter is an independent project, not affiliated with or endorsed by Mattermost, Inc.
Mattermost is a trademark of Mattermost, Inc.

<br>

---

<a id="francais"></a>

# Almatter — en français

<p align="center">
  <a href="#english">English</a> · <b>Français</b>
</p>

Almatter est un client Mattermost personnel et indépendant. Il est né d'un constat :
l'application de bureau officielle repose sur Electron et occupe couramment 500 à 600 Mo
de mémoire. Almatter vise à assurer l'usage quotidien — lire, écrire, suivre les fils,
chercher, recevoir les notifications — pour une fraction de cette consommation, et à
rester utilisable sur des machines anciennes.

> **État : préversion (0.5.0).** Utilisé au quotidien, mais tout n'est pas encore poli.
> L'interface est pour l'instant **uniquement en français**.

## Fonctionnalités

- **Changement de canal instantané** — les messages sont gardés en cache sur le disque
  (SQLite) : un canal s'ouvre tout de suite depuis le cache, puis se met à jour auprès
  du serveur.
- **Mises à jour en temps réel** par WebSocket : nouveaux messages, modifications,
  réactions, indicateur de saisie, présence, et état de lecture synchronisé avec vos
  autres appareils.
- **Messagerie** — rendu Markdown avec barre de mise en forme, modification et
  suppression, fils de discussion dans un panneau latéral, réactions et sélecteur
  d'émojis, `@mentions` avec autocomplétion, messages épinglés.
- **Pièces jointes** — joindre des fichiers, ou coller directement images et fichiers
  dans la zone de saisie. Un texte collé depuis une page web garde ses liens.
- **Aperçus de liens**, désactivables dans les réglages.
- **Recherche** dans les messages, parcours et ajout de canaux, messages directs.
- **File d'envoi hors ligne** — les messages écrits sans connexion sont mis en attente
  et envoyés au retour de celle-ci.
- **Intégration à Windows** — notifications de mention depuis la zone de notification,
  pastille de non-lus sur l'icône de la barre des tâches, barre de titre assortie au
  thème.
- **Apparence** — thèmes clair et sombre (ou celui de Windows), mode contraste réduit,
  taille du texte des messages réglable, et choix entre Open Sans (la police du client
  officiel) et la police système.

### Pas encore pris en charge

- La connexion avec **double authentification ou SSO** (GitLab, SAML, Office 365…).
  Seuls l'identifiant (ou l'e-mail) et le mot de passe fonctionnent aujourd'hui.
- Plusieurs serveurs ou équipes à la fois, les appels, Boards, Playbooks et les plugins.
- macOS et Linux — voir [Autres plateformes](#autres-plateformes).

## Installation

**Configuration requise :** Windows 10 ou 11, 64 bits (x64).

1. Téléchargez `Almatter-Setup-<version>.exe` depuis la
   [page des versions](https://github.com/vincentnxi/almatter/releases).
2. Lancez-le. Almatter s'installe pour votre seul compte utilisateur, sans droits
   d'administrateur.

Almatter fonctionne avec le moteur .NET 10 de Microsoft. S'il n'est pas déjà présent sur
l'ordinateur, l'installeur le télécharge (environ 30 Mo, vérifié par une empreinte
SHA-256 fixée à l'avance) et l'installe — c'est la seule étape qui demande l'autorisation
de Windows.

> L'installeur n'est pas encore signé : Windows SmartScreen peut signaler un éditeur
> inconnu. Cliquez sur *Informations complémentaires → Exécuter quand même*.

Pour mettre à jour, il suffit de lancer le nouvel installeur par-dessus l'installation
existante.

**Désinstaller** depuis les Paramètres de Windows supprime le programme et propose de
supprimer aussi vos données (connexion enregistrée, réglages, messages en cache). Ces
données se trouvent dans `%LOCALAPPDATA%\Almatter` et `%APPDATA%\Almatter`.

## Confidentialité et sécurité

- **Aucune télémétrie, aucune mesure d'audience, aucun service tiers.** Almatter
  communique avec le serveur Mattermost auquel vous vous connectez, à une exception
  près : l'image d'un aperçu de lien est téléchargée depuis le site du lien lui-même,
  comme le ferait un navigateur. Désactiver les aperçus de liens dans les réglages
  supprime ces téléchargements.
- Votre mot de passe n'est jamais enregistré. Le jeton de session est chiffré avec la
  protection de données de Windows (DPAPI) et lisible uniquement par votre compte
  Windows.
- Les messages sont mis en cache sur le disque pour s'ouvrir instantanément et rester
  lisibles hors ligne.

## Organisation du code

```
core/                  Espace de travail Rust
  almatter-core/       Client de l'API Mattermost, cache SQLite, WebSocket, file hors ligne
  almatter-ffi/        Bibliothèque compatible C (almatter_ffi.dll) qui expose le cœur
app/Almatter.App/      Application de bureau : interface Avalonia, MVVM, appelle le cœur par P/Invoke
installer/             Script Inno Setup, scripts de fabrication et de test en Windows Sandbox
design/                Maquettes à partir desquelles l'interface a été conçue
docs/                  Notes du projet (portabilité)
licenses/              Licences des ressources tierces embarquées
```

Le réseau, le cache et la synchronisation sont entièrement pris en charge par le cœur
Rust, qui ne contient ni code `unsafe` (`#![forbid(unsafe_code)]`) ni code propre à un
système d'exploitation. L'interface échange du JSON avec lui par un point d'entrée FFI
unique. Côté C#, le code propre à Windows est cantonné à quelques points d'intégration
isolés.

## Compiler depuis les sources

### Prérequis

- [Rust](https://rustup.rs) (stable, chaîne MSVC — `rustup` propose d'installer les
  outils de compilation C++ de Visual Studio nécessaires)
- [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — uniquement pour fabriquer
  l'installeur

### Lancer une version de développement

```powershell
# 1. Compiler le cœur Rust (produit core\target\debug\almatter_ffi.dll)
cd core
cargo build -p almatter-ffi

# 2. Compiler et lancer l'application (le csproj copie la DLL à côté de l'exécutable)
cd ..
dotnet run --project app\Almatter.App
```

Pour une version Release, utilisez `cargo build --release -p almatter-ffi` et
`dotnet build -c Release` : chaque configuration reprend la compilation Rust
correspondante.

> Si Almatter est déjà ouvert (y compris réduit dans la zone de notification), Windows
> verrouille ses fichiers et la nouvelle compilation ne les remplace pas. Quittez-le
> d'abord.

### Lancer les tests

```powershell
cd core
cargo test
```

### Fabriquer l'installeur

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Le script compile le cœur Rust en mode release, publie l'application pour `win-x64` et
produit `installer\output\Almatter-Setup-<version>.exe`. Le numéro de version provient de
`<Version>` dans `app/Almatter.App/Almatter.App.csproj`.

`installer\test-in-sandbox.ps1` installe ensuite ce fichier dans une
[Windows Sandbox](https://learn.microsoft.com/fr-fr/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview)
vierge et vérifie que l'application démarre. Comptez environ six minutes : l'installation
de .NET y est beaucoup plus lente que sur un vrai PC.

<a id="autres-plateformes"></a>

## Autres plateformes

Almatter ne vise que Windows pour l'instant, mais il a été conçu pour être portable : le
cœur Rust et les vues Avalonia fonctionnent tels quels sous macOS et Linux. Ce qui reste
propre à Windows (notifications, pastille de la barre des tâches, couleur de la barre de
titre, stockage sécurisé de la session, installeur) est recensé dans
[docs/PLATFORM_PORTABILITY.md](docs/PLATFORM_PORTABILITY.md), avec une piste pour chaque
système.

## Licence

Almatter est distribué sous [licence MIT](LICENSE).

Il embarque la police Open Sans, sous SIL Open Font License 1.1 — voir
[licenses/](licenses/).

Almatter est un projet indépendant, sans lien avec Mattermost, Inc. ni approuvé par
elle. Mattermost est une marque de Mattermost, Inc.
