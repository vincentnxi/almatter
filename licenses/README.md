# Third-party licences

The project's own code is under the MIT License (see `LICENSE` at the root).
Bundled third-party assets keep their own licences, reproduced here:

- `OpenSans-OFL.txt` — Open Sans (`app/Almatter.App/Assets/Fonts/OpenSans.ttf`),
  SIL Open Font License 1.1. Open Sans is the official Mattermost client's body
  font; Almatter ships it so the default look matches, and offers the platform
  UI font as an alternative in the settings.
- `emoji-data-MIT.txt` — emoji-data (https://github.com/iamcal/emoji-data),
  MIT. No file of it is shipped: `tools/generate-emoji-shortcodes.js` reads its
  emoji list and writes `app/Almatter.App/Models/EmojiShortcodesData.cs`, so
  what Almatter carries is the generated shortcode table. That list is the one
  Mattermost itself is built on, which is what makes ":smirk_cat:" draw the
  same emoji here as in the official client.
