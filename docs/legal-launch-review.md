# Legal & launch-readiness review (2026-11-15)

**Not legal advice.** This is a developer's checklist from reading the project and public sources. Before you
charge money for the game, spend an hour with a games/IP lawyer. Many offer flat-fee launch reviews, and this
document should make that hour cheap.

## TL;DR

| Area | Status | What to do |
|---|---|---|
| Art assets (KayKit, Quaternius, Kenney) | ✅ Clear | All CC0; license files are kept in the repo. Credits are optional but listed in `THIRD_PARTY_NOTICES.md`. |
| Code libraries | ⚠️ Fixed today | Facepunch.Steamworks is MIT, which **requires shipping its notice**. Builds now include `THIRD_PARTY_NOTICES.txt`. |
| Sounds, shaders, terrain, procedural models | ✅ Clear | All made in this project (synthesized SFX, our own shaders and generators). |
| Steam App ID | ❌ Must change | `480` is Valve's public *Spacewar* test app. You need your own App ID ($100 Steam Direct fee per game). |
| "Hamzilla" boss name | ⚠️ Fixed today | Toho aggressively polices "-zilla" names (cease-and-desist letters to a mobile game, lawsuits over a beer, invalidated "Guzzilla"). Renamed to **Hamageddon the Hog**. |
| "Uzi" weapon name | ⚠️ Fixed today | "Uzi" is a real firearm trademark. Games usually win these fights (Humvee v. Activision), but renaming costs nothing. Now the **Bullet Hose**. |
| Lucky Lodge (slots / roulette / scratchers) | ⚠️ Decide | No real money, so it's not gambling law. But **PEGI rates simulated casino gambling 18+** (since 2020). See below. |
| Voice + text chat | ⚠️ Add before launch | Need per-player mute (added today), a short privacy note, and no recording/storage (we don't). |
| Game name "FriendSlop Hunting" | ⚠️ Check | "Friendslop" is a community slang term with no trademark found, but some devs consider it derogatory. Do a USPTO/EUIPO search on your final title. |
| Unity license | ✅ Clear | Unity Personal is free up to $200k revenue + funding a year. The runtime fee was cancelled, and the splash screen is optional in Unity 6. |

## 1. Copyright: what's in the build

- **3D art.** KayKit Adventurers (Kay Lousberg), Quaternius Stylized Nature + Medieval Village (free versions) and
  Kenney Nature Kit are all **CC0 1.0** (public domain dedication). You can use, modify and sell games with them, with
  no attribution required. You just can't resell the raw asset packs themselves. Each pack's `License.txt` is in
  `Assets/_Game/ThirdParty/*`. Keep them.
- **Everything else is original.** That covers animals (built procedurally by `CritterBuilder`), weapons
  (`WeaponModelFactory`), fish, the casino machines, terrain and ground textures (generated), water/rock/cloud/sky
  shaders, and **every sound effect** (synthesized in `Sfx.cs`; no audio files).
- **Research.** Design ideas came from reading talk transcripts (GMTK, Extra Credits and dev tutorials). Game mechanics
  and ideas aren't copyrightable, and no code, art or text was copied from them.
- **Code comments mention other games** ("How to Fish style", "Lethal-Company style"). That's fine internally. **Don't
  use other games' names, art or screenshots on your store page or trailer.** "If you like X…" in a description is
  a grey zone; skip it.

## 2. Trademarks

- **Other people's marks in the game.** We removed the two obvious ones (Hamzilla → Hamageddon, Uzi → Bullet Hose).
  Revolver, Bazooka, Minigun, Shotgun and so on are generic words. Animal species are fine. "Big Doug the Elk" is fine.
  - Internal asset file names still say `Hamzilla.prefab` / `Uzi.asset`. Players never see them, so it's not an
    issue, but rename them during any big refactor for tidiness.
- **Your own mark.** Before spending money on marketing:
  1. Search USPTO and EUIPO for your title.
  2. Check that the Steam store name, domain and social handles are free.
  3. Consider registering it once you're sure, at roughly $350 per class in the US.
- **"Friendslop".** No trademark was found. It's a fan-coined label and fine to use in tags, but think about whether you
  want it in the title.

## 3. Steam

1. **Own App ID.** Pay the $100 Steam Direct fee (recoupable after $1,000 of revenue). Then replace `480` in
   `NetBootstrap`/`SteamService` and `BuildTools` (the line that writes `steam_appid.txt`). Shipping on 480 isn't
   possible through Steam anyway. Builds handed to friends on 480 are fine for testing.
2. **Steam Distribution Agreement + tax interview** are required to get paid.
3. **Content survey.** Answer honestly. Expect to disclose: frequent cartoon violence (hunting, ragdolls),
   simulated gambling (Lucky Lodge), user-generated communication (voice/text chat).
4. **No real-money gambling, ever.** Steam's rules prohibit gambling. We're safe *because*:
   - team money can't be bought;
   - it can't be cashed out;
   - it can't be traded.

   Keep it that way. Never add a store for team money, bucks or "spins", and never add paid random rewards. Paid loot
   boxes are illegal in Belgium, and from June 2026 PEGI rates any game selling paid random items 16+.

## 4. Age ratings and the Lucky Lodge

- Steam uses IARC for many regions. **PEGI rates games that simulate casino gambling (slots, roulette) 18+**, even with
  no real money. ESRB adds a "Simulated Gambling" descriptor, usually at Teen or higher.
- Your options:
  - **Keep it** and accept an 18+ PEGI rating. Probably fine for a friendslop audience, but it can limit
    console/storefront reach later.
  - **Reskin it** to look less like a casino, for example a "Lucky Lodge Raffle" (wheel of fortune, carnival games),
    and check with the rating questionnaire.
  - **Let hosts turn the Lodge off** as a lobby option. That doesn't change the rating, but it's nice for parents and
    streamers.
- Hunting/animal violence is cartoonish. Expect PEGI 12 / ESRB T without the casino.

## 5. Voice & text chat (privacy and safety)

- **What we do.** Voice is captured only while push-to-talk is held (or open-mic is on and you're speaking). It's
  relayed through the host to players in the session and **never recorded or stored**. Text chat is shown in-session
  only. Profiles (`profile.json`) stay on each player's PC. No analytics or telemetry.
- **Before launch:**
  - A short **privacy notice** on the store page or a website, saying what's transmitted, that nothing is stored, and
    that Steam names are shown.
  - **Per-player mute** for voice and text (added today in the pause menu).
  - A note that the game isn't directed at children under 13 (COPPA in the US, GDPR-K in the EU). Don't market
    to kids.
  - Optionally, a profanity filter toggle for chat.
- Don't add voice recording, clips or replays without explicit consent and a privacy policy update.

## 6. Business side (quick list)

- **Who owns the game?** If friends contributed ideas, art or code, put a short written agreement in place
  (contribution + IP assignment + revenue split) **before** it makes money.
- **Form an LLC** (or your country's equivalent) to keep personal assets separate. Use it to sign the Steamworks
  agreement.
- **Refunds and EULA.** Steam's standard EULA and refund policy cover most indies. A custom EULA is optional.
- **Accessibility statements** aren't legally required for games in most places, but the remappable controls and
  shake/bob toggles help.

## 7. Done in the repo today

- `THIRD_PARTY_NOTICES.md` (shipped in every build as `THIRD_PARTY_NOTICES.txt`).
- Hamzilla → **Hamageddon the Hog** (with an asset migration).
- Uzi → **Bullet Hose** (with an asset migration).
- Comment on the `steam_appid.txt` line reminding us to swap in the real App ID.
- Per-player mute for voice and text chat (pause menu).

## Sources

- Steam Direct fee & App IDs: https://partner.steamgames.com/doc/gettingstarted/appfee, https://partner.steamgames.com/steamdirect
- Steam content survey: https://partner.steamgames.com/doc/gettingstarted/contentsurvey
- Toho "-zilla" enforcement: https://www.techdirt.com/2011/04/27/trademarkzilla-strikes-again-complains-that-fingerzilla-may-confuse-people-into-thinking-its-godzilla/ ,
  https://www.managingip.com/IssueArticle/3871298/Archive/Japan-IP-High-Court-finds-free-riding-and-dilution-by-GUZZILLA-trademark.html ,
  https://www.claimsjournal.com/news/west/2014/05/13/248788.htm
- Real-product trademarks in games (Humvee v. Activision; Bell v. EA): https://www.thedrive.com/tech/32872/you-have-a-first-amendment-right-to-humvees-in-call-of-duty-games-judge-rules.md ,
  https://www.yorku.ca/osgoode/iposgoode/?p=40269
- PEGI simulated gambling 18+ and loot-box rules: https://www.screenhub.com.au/news/games/loot-boxes-pegi-16-age-rating-2695980/ ,
  https://www.osborneclarke.com/insights/games-loot-boxes-get-16-plus-age-rating-europe-under-pegi-overhaul ,
  https://blog.promise.legal/loot-box-laws-game-developers/
- Unity Personal terms: https://unity.com/blog/news/plan-pricing-and-packaging-updates , https://unity.com/blog/unity-is-canceling-the-runtime-fee
- "Friendslop" term: https://en.wikipedia.org/wiki/Friendslop
- CC0 asset licenses: each pack's License.txt; https://creativecommons.org/publicdomain/zero/1.0/
