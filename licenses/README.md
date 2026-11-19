# Third-party licences

The project's own code is under the MIT License (see `LICENSE` at the root).
Bundled third-party assets keep their own licences, reproduced here:

- `OpenSans-OFL.txt` — Open Sans (`app/Almatter.App/Assets/Fonts/OpenSans.ttf`),
  SIL Open Font License 1.1. Open Sans is the official Mattermost client's body
  font; Almatter ships it so the default look matches, and offers the platform
  UI font as an alternative in the settings.
- `Lato-OFL.txt` — Lato (`app/Almatter.App/Assets/Fonts/Lato-Regular.ttf` and
  `Lato-Bold.ttf`), SIL Open Font License 1.1. Offered as a third typeface in
  the settings.
- `Geist-OFL.txt` — Geist and Geist Mono (`app/Almatter.App/Assets/GeistFont/`
  and `app/Almatter.App/Assets/GeistMonoFont/`), Copyright The Geist Project
  Authors, SIL Open Font License 1.1, from https://github.com/vercel/geist-font
  (release `v1.9.2`, static TTF files, unchanged). The typefaces of the Ark
  theme: Geist for the text, Geist Mono for small labels and times.
- `Twemoji-CC-BY-4.0.txt` — Twemoji graphics
  (`app/Almatter.App/Assets/EmojiFont/TwemojiAlmatter.ttf`), Copyright Twitter,
  Inc and other contributors, licensed under CC-BY 4.0
  (https://creativecommons.org/licenses/by/4.0/). Taken from the community fork
  https://github.com/jdecked/twemoji (tag `v17.0.3`). Changed: the SVG images
  were converted into a COLRv0 colour font with nanoemoji, by
  `.github/workflows/build-twemoji-font.yml`. Offered in the settings as an
  alternative to the system's own emoji.
- `emoji-data-MIT.txt` — emoji-data (https://github.com/iamcal/emoji-data),
  MIT. No file of it is shipped: `tools/generate-emoji-shortcodes.js` reads its
  emoji list and writes `app/Almatter.App/Models/EmojiShortcodesData.cs`, so
  what Almatter carries is the generated shortcode table. That list is the one
  Mattermost itself is built on, which is what makes ":smirk_cat:" draw the
  same emoji here as in the official client.
- `theme-palettes-MIT.txt` — the colour values of seven named themes
  (Catppuccin, Catppuccin Latte, Nord, Rosé Pine, Rosé Pine Moon, Rosé Pine
  Dawn, Tokyo Night), taken from fastframe-theme
  (https://github.com/crmne/fastframe) and the original schemes it adapts, all
  MIT. No file of theirs is shipped: the values are copied into
  `app/Almatter.App/Models/NamedThemes.cs`. fastframe's eighth palette,
  Ristretto, is left out on purpose: its colours are Monokai Pro's, whose
  licence forbids redistribution.
