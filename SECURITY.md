# Security policy

**English** · [Français](#francais)

## Reporting a vulnerability

Please report security problems privately, through GitHub's
[private vulnerability reporting](https://github.com/vincentnxi/almatter/security/advisories/new)
(the *Security* tab, then *Report a vulnerability*) — not in a public issue.

It helps to include:

- what an attacker can do, and what they need first (a member of the server, the
  server itself, someone on the same network…);
- the steps or the message that shows it, and the Almatter version.

Almatter is a personal project maintained in spare time: expect an answer within a
couple of weeks, a fix in the next release, and credit in its notes if you wish.

## Supported versions

Only the latest release receives fixes. Updating is running the newer installer
over the existing install.

## Scope

Almatter is a client: it trusts the Mattermost server it signs in to for what
that server is responsible for (who may read what). Problems in the server itself
belong to [Mattermost](https://mattermost.com/company/responsible-disclosure/).
What a malicious server, another member of it, or a web page linked in a message
can make Almatter do on your computer is in scope.

<br>

---

<a id="francais"></a>

# Politique de sécurité

## Signaler une faille

Merci de signaler les problèmes de sécurité en privé, avec le
[signalement privé de failles](https://github.com/vincentnxi/almatter/security/advisories/new)
de GitHub (onglet *Security*, puis *Report a vulnerability*) — pas dans un ticket
public.

Il est utile d'indiquer :

- ce qu'un attaquant peut faire, et ce dont il a besoin au départ (être membre du
  serveur, contrôler le serveur, être sur le même réseau…) ;
- les étapes ou le message qui le montrent, et la version d'Almatter.

Almatter est un projet personnel maintenu sur du temps libre : comptez une réponse
sous deux semaines environ, une correction dans la version suivante, et une mention
dans ses notes si vous le souhaitez.

## Versions prises en charge

Seule la dernière version reçoit des corrections. Pour mettre à jour, il suffit de
lancer le nouvel installeur par-dessus l'installation existante.

## Périmètre

Almatter est un client : il s'en remet au serveur Mattermost auquel il se connecte
pour ce qui relève de ce serveur (qui a le droit de lire quoi). Les problèmes du
serveur lui-même relèvent de
[Mattermost](https://mattermost.com/company/responsible-disclosure/). Ce qu'un
serveur malveillant, l'un de ses membres ou une page web liée dans un message peut
faire faire à Almatter sur votre ordinateur entre dans le périmètre.
