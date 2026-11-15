# FriendSlop Hunting: Discovery & Scope

*Working title. Repo: `NotCameronMitchell/FriendSlopHunting` (currently empty). Written 2026-11-14.*

**One-liner:** A silly first-person co-op hunting game for 1–4 friends. You stalk dumb animals, pull off ridiculous trick shots, haul the carcasses back to camp together, sell them to a sketchy trader, and spend the money on worse ideas. It's *How to Fish*, but with hunting.

---

## 1. What the research says

### 1.1 The genre ("friendslop")
- The term showed up in March 2025 for cheap, low-fi 3D co-op games that work mainly as a *scaffold for hanging out*. In 2025, R.E.P.O., PEAK, Schedule I and RV There Yet? were all in Steam's top 10 for copies sold. ([app2top](https://app2top.com/analytics/social-co-op-games-for-friends-are-the-main-indie-trend-of-the-year-how-much-do-they-earn-and-how-crowded-is-the-niche-285073.html), [Wikiwand](https://www.wikiwand.com/en/articles/Friendslop))
- **PEAK interview** (Nick Kaman, Aggro Crab, [PC Gamer video](https://www.youtube.com/watch?v=I16hcvvajGM)):
  - PEAK took **about 4.5 months** to make. "Most players aren't going to notice when you spent 3 weeks tweaking this shader… they're there for the holistic experience."
  - The game had a **clear, hard goal** (reach the peak), and accidental slapstick came out of it (slipping on a banana peel).
  - There's lots of room to "friendslop-ify" a non-horror activity. PEAK proved players want something other than horror.
- **Lethal Company breakdowns** ([DingDarkus](https://www.youtube.com/watch?v=TVsB_iiU9Os), [QuinnandWin](https://www.youtube.com/watch?v=o5Itg8zBq3Q)):
  - Progression shouldn't make the chaos easier. It should give you **more ways to handle the chaos.**
  - **Proximity voice** and **sanctioned trolling** (betrayal among friends) are core to the genre, not extras.
  - Short sessions of one or two hours fit friend groups' schedules. When one friend quits, the whole group drifts away.
- **"What makes a co-op game fun?"** ([flying toad](https://www.youtube.com/watch?v=85axgPTKhd8)):
  - The game should mechanically **need** other players.
  - It needs replayability through variety or procedural generation.
  - You should be able to screw over your friends.
  - Low skill floors matter: losing should cause laughter, not rage.

### 1.2 The reference game: *How to Fish* (Dazed Games, 2 devs, Aug 2026)
- Sold over 1M copies in 2 days. 1–4 players, later patched to 8. ([Dot Esports](https://dotesports.com/indies/news/how-to-fish-game-sales-friendslop-steam-indie-coop), [Kotaku](https://kotaku.com/how-to-fish-spider-crab-murder-2000728082))
- **The loop is catch → kill → sell → upgrade → new island.** From a fishing-game dev's playthrough ([Rye Guy](https://www.youtube.com/watch?v=v7NkbVq07C8)):
  - The fishing itself is basic. The fun comes from what happens after: the creature lands on the dock, runs away, and you **beat it up** with fists, brass knuckles, a pistol or dynamite.
  - **Trickshot multipliers** (headshot, airborne, quick scope, point blank) raise the sale price. This is the signature mechanic, but the game explains it poorly.
  - **Bosses are summoned** with quest items (an empty beer can, leech bait). Beating one unlocks the boat keys and radar, which open the next island.
  - Other systems: hunger (you can eat your catch), beer, a gambling machine that pays out *cosmetics*, and dropping your items when you die.
  - Art: low-poly models with **pixelated textures** and **floating hands**. Rye Guy points out that floating hands are a smart shortcut because real hands are hard.
- **Criticisms to avoid:** the loop wears thin after a few hours, the small creatures feel samey and pose no threat, and the trickshot rules are unclear. ([Vaporlens](https://vaporlens.app/app/4001890/how_to_fish), [gamebrief](https://www.gamebrief.net/blog/how-to-fish-review-2026))

### 1.3 Direct competitor: *Huntin' Buddies* (July 2026, $7.99)
- A 4-player co-op hunting game, but it's **horror-comedy**: meat-quota contracts, demon rabbits, a "headless cyclops horseman," and carcasses that **explode if you stretch them while dragging**. The loop is hunt, sleep, repeat. ([GameDeveloper](https://www.gamedeveloper.com/press-release/huntin-buddies-launches-today-on-steam-head-into-the-park-if-you-dare), [demo playthrough](https://www.youtube.com/watch?v=ZxNP5V-J6ns), [trailer](https://www.youtube.com/watch?v=JthSTEjPq2Y))
- **How we differ:** comedy first and no quota pressure. Days are *How to Fish*-style slapstick: animals with personality that fight back, trick-shot style scoring, legendary boss hunts, and a clear finale. **Nights are spooky, not horror**: an optional risk/reward phase (see 2.5). Huntin' Buddies is horror all the way through. The physics haul is shared ground, so we need our own twist on it (see 2.3).

---

## 2. Game design: the MVP loop

### 2.1 Core loop (one session ≈ 60–90 min)
```
CAMP (trading post) ──buy gear──▶ WOODS ──track/stalk──▶ SHOOT (trick-shot bonus)
     ▲                                                        │
     │                                                  animal ISN'T dead yet:
   SELL ◀── haul carcass (heavy = needs friends) ◀── chase / brawl / finish it
     │
     └─▶ money → better guns / calls / decoys / truck → summon LEGENDARY → new region
```

### 2.2 Pillars (every feature should serve at least one)
1. **Dumb animals with attitude.** Hits rarely kill cleanly. A wounded goose chases you, a turkey tries to escape, and a moose ragdolls down a hill. This is our version of beating up the fish on the dock.
2. **Style pays.** Sale price = base × weight × **style multiplier**. Multipliers: headshot, airborne target, long shot, shooting while jumping, no-scope, double kill, and "it was your friend's kill." Each one is announced on screen right away, which fixes How to Fish's clarity problem.
3. **Big game needs friends.** A rabbit is one hand, a deer needs two people, and a moose needs everyone or the truck. The haul is where the slapstick happens.
4. **Friend-ruining is a feature.** Friendly fire ragdolls your buddy (you don't lose money), duck calls attract a bear, the deer costume gets you shot, and you can steal the kill credit.

### 2.3 MVP content (deliberately small)
| Category | MVP | Later |
|---|---|---|
| Map | 1 hand-built forest (≈ 300×300 m) + camp | Swamp, mountain, tundra regions |
| Animals | Rabbit, Goose (aggressive), Deer, Boar (charges), Moose (huge haul) | Bear, turkey flock, skunk (debuff), fish (crossover gag) |
| Legendary | **"Big Doug" the Elk**, summoned with a salt lick + elk call, drops the truck keys | Final boss: Sasquatch |
| Weapons | Fists, slingshot, bolt rifle, shotgun | Bow (silent), dynamite, tranquilizer, harpoon |
| Tools | Duck/deer call, decoy, binoculars, scent cover, jerky (food) | Tree stand, trail cam, ghillie suit, bait |
| Economy | Trader NPC buys carcasses, shop sells gear | Gambling taxidermy crate (cosmetic hats), daily bounties |
| Social | Text names, emotes, friendly fire ragdoll | Proximity voice, walkie-talkie |
| Haul twist | Carry/drag with physics. Two players can lift big game together | Truck bed, sled, zipline |

### 2.4 Explicitly out of scope (for now)
Matchmaking with strangers, dedicated servers, anti-cheat, procedural maps, realistic ballistics, jump-scare horror, Mac/Linux builds, a Steam store page, and controller support.

### 2.5 Spooky nights (the tone twist)
- **Day/night cycle.** A day lasts about 12 minutes of hunting. At dusk the trader rings the camp bell.
- **Staying out after dark is optional and pays double.** Nocturnal animals come out (owl, raccoon, glowing-eyed "night deer"), and every sale gets a ×2 night multiplier.
- **The threat is Sasquatch,** heard before he's seen: wood knocks, distant howls, and sometimes your carcass is gone. He grabs whoever is alone (that's the Lethal Company lesson: split up and it gets scary) and drops them back at camp, minus whatever they were carrying. Funny-scary, not gory.
- **Lanterns and a campfire** make a safe zone. A flare gun scares him off.
- **The arc:** Sasquatch stalks you through the early game and becomes the finale legendary hunt in M5, so the spooky thread pays off.
- **Scope rule:** M3 adds only the day/night lighting and the bell. Sasquatch stalking comes in M4.

---

## 3. Tech decisions

### Decisions locked (2026-11-14)
- **Engine:** Unity 6 (latest LTS)
- **Connection:** Steam test AppID 480, with Tailscale as the fallback
- **Tone:** comedy by day, spooky nights
- **Local repo:** `C:\Users\cbm20\GitGood\FriendSlopHunting`

### 3.1 Engine: **Unity 6 (chosen)**. The comparison is kept for the record.
| | Godot 4.7 | Unity 6 | Unreal 5 |
|---|---|---|---|
| What the hits used | — | R.E.P.O. (Photon), Lethal Co., PEAK, Content Warning ([Photon](https://blog.photonengine.com/r-e-p-o-multiplayer-success-powered-by-photon/)) | Rare in this genre |
| Fit for low-fi 3D | ✅ Forward+ suits stylized art | ✅ | ⚠️ Overkill; deferred, realistic pipeline |
| Iteration speed | ✅ Instant | ⚠️ Slow compiles/domain reloads ([GiusAb](https://www.youtube.com/watch?v=-o60_LIHVTs)) | ❌ Heavy |
| Build size friends download | ✅ ~100–400 MB | ✅ | ❌ Multi-GB |
| Built-in multiplayer | ✅ Spawner/Synchronizer/RPC | ⚠️ NGO or 3rd party (FishNet/Photon) | ✅ Replication |
| **Can Claude edit it directly?** | ✅ Scenes and scripts are plain text (`.tscn`/`.gd`) and diff cleanly in git | ⚠️ YAML scenes, painful | ❌ Blueprints are binary `.uasset` |
| Your experience | ✅ 4.7.2 installed, 8 builds of your last game shipped | None | None |

**Verdict:** Unity 6. The genre's hits used it, which means the most tutorials, Steam integrations and netcode options exist for it.

**What Unity changes about how we work:**
- **Claude writes the C#** (gameplay, netcode, editor tooling). **You do scene layout and prefab wiring** in the editor. Where wiring is tedious, I'll write `Editor/` scripts that build prefabs and scenes from code, so you only click one menu item.
- Set *Project Settings → Editor → Asset Serialization = Force Text* so scenes and prefabs are YAML that git can diff and I can read.
- Use **Git LFS** for `.fbx/.png/.wav`, plus a standard Unity `.gitignore` (Library/, Temp/, Builds/).
- Unity isn't installed yet. You'll need Unity Hub + the latest Unity 6 LTS with the Windows Build Support module.

### 3.2 Multiplayer without publishing anywhere
| Option | How friends connect | Pros | Cons |
|---|---|---|---|
| **A. Steam with test AppID 480 (chosen)** via Facepunch.Steamworks + an NGO Steam transport | Invite from the Steam friends list / lobby browser | Free NAT punch-through and relay, invites, built-in voice API, no store page, no $100 fee | Friends need Steam running. Your status shows "Spacewar." The 480 lobby list is shared, so lobbies need a unique metadata filter ([BatteryAcidDev](https://www.youtube.com/watch?v=xugYYCz0VHU)). Some people report 480 being flaky ([Godot forum](https://forum.godotengine.org/t/godotsteam-lobbies-not-visible/127733)) |
| B. WebRTC via the "Tube" addon | Share a room code | No accounts at all, works in the browser ([AndrooDev](https://www.youtube.com/watch?v=wgIqB6JNcro)) | No TURN relay means strict-NAT friends can't connect. Relies on public BitTorrent trackers |
| C. ENet + [noray](https://github.com/foxssake/netfox) or Tailscale | Room code / VPN | Full control | You'd host a relay or everyone installs Tailscale |
| D. ENet + port forwarding | IP:port | Simplest | The host must configure their router |

**Unity stack:**
- **Netcode for GameObjects (NGO)**, Unity's official netcode. Lethal Company shipped on NGO + Steam.
- **Facepunch.Steamworks** for lobbies, invites, the overlay and voice.
- **Transport swap:** use **Unity Transport** for local dev, then swap in a **Facepunch/Steam transport** for real play. This swap is *required*: you can't run two Steam clients on one PC, so local testing uses Unity's **Multiplayer Play Mode** (up to 4 virtual players in-editor) over Unity Transport.
- **Fallback:** if 480 misbehaves, Unity Transport over **Tailscale** (option C). It's the same code with a different transport.
- *Considered:* FishNet + FishySteamworks (more features, free) and Photon (paid above 100 CCU). NGO was chosen for its docs, its genre precedent, and being first-party.

### 3.3 Netcode model
- **Co-op means client authority over your own player body**, using NGO `ClientNetworkTransform`. Movement stays smooth and no prediction code is needed. Same idea as the AndrooDev pattern ([Part 1](https://www.youtube.com/watch?v=NvG08tA06xQ)).
- **The host owns animals, carcasses, money, and sales.** Clients send a `ShootServerRpc(target, hitPoint, flags)` and the host validates it and applies damage and multipliers.
- **Physics objects:** the host simulates rigidbodies (`NetworkRigidbody`) and clients interpolate. While someone carries a carcass, **ownership transfers to the carrier** so it follows their hands without lag, then returns to the host on drop.

### 3.4 Distribution: **itch.io (restricted page) + butler**
- You've been passing around separate folders for builds 1–8. With the **itch app**, friends install once and get **automatic updates** every time we push.
- A `tools/release.ps1` script runs a Unity batch-mode Windows build, then `butler push`. Cloud CI (GameCI) needs a Unity license secret, so it's deferred until it's worth the setup. The page stays restricted or unlisted, so nothing is "published" publicly.

### 3.5 Art & audio
- Low-poly + pixelated-texture look (point filtering, low-res albedo) + floating hands, the same as How to Fish.
- CC0 assets first: **Quaternius** animated animals and nature packs, **Kenney** props/UI, freesound.org for SFX. Animals get comedic, exaggerated animations and sounds (goose honk *is* the game).

---

## 4. Milestones
Each milestone ends with a **build your friends actually play**.

### MVP re-plan (2026-11-14, after M0/M1 shipped)
Cameron's MVP priorities, in the agreed build order (**gameplay → mobs → menus → art**):

| # | Milestone | Done when | Status |
|---|---|---|---|
| **M0** | Networking spike | Steam 480 + Direct, two PCs connected | ✅ passed with a friend |
| **M1** | "One Rabbit" slice | Rifle, rabbit AI, carry, sell, shared money, trick shots, juggling | ✅ done (+ sound/juice pass) |
| **M2a** | **Weapons + shop** | **Pistol is the free starter; ammo is always unlimited.** Trader shop sells the arsenal below with team money. Weapon switching (1–9 / scroll). Every weapon works in multiplayer. | 🔨 in progress |
| **M2b** | **Mobs & bosses** | Many more animals (aggressive ones that fight back), plus summonable bosses. Absorbs the old M2 animals and the M3 legendary. | next |
| **M2c** | **Menus** | Main/home menu. **Options:** view and rebind keybinds, volume sliders, push-to-talk toggle, mute/unmute button. **Character customization** with outfits, buyable with in-game money or unlocked by achievements. | |
| **M2d** | **Art upgrade** | CC0 packs (Quaternius/Kenney): better trees, grass, rocks, and a modular animated character for outfits. Each download needs Cameron's OK. | |
| **M3+** | Old M3–M5 | Day/night + spooky nights, proximity voice, save/load, finale. Plus `docs/ideas.md`. | later |

**Weapon roster (M2a).** Ammo is unlimited for everything. Prices are first guesses to tune in playtests.
| Weapon | Type | Feel |
|---|---|---|
| Pistol | Semi-auto hitscan | Free starter |
| Revolver | Semi, heavy | Big damage, slow, great headshots |
| Uzi | Full-auto hitscan, spread | Spray at close range |
| Machine gun | Full-auto hitscan | Accurate sustained fire |
| Minigun | Full-auto, spin-up | Absurd fire rate once spun up |
| Shotgun | Pellet spread | Launches carcasses: the juggling tool |
| Bolt rifle | Semi, long range, scope zoom | The current gun |
| Bow | Arcing projectile | **Silent: doesn't spook animals** |
| Slingshot | Arcing projectile, goofy | Bonks things |
| Boomerang | Thrown, returns | Can hit on the way out *and* back |
| Bazooka | Explosive projectile | Area damage, sends everything flying |
| **Tank (drivable)** | Vehicle | Deferred to after M2a (needs the vehicle system). Tracked in ideas. |

Default decision (change anytime): **purchases unlock the weapon for the whole team**, paid from the shared pot.

### Original milestone plan (kept for reference)
| # | Milestone | Done when | Est.* |
|---|---|---|---|
| **M0** | **Networking spike (go/no-go)** | Unity 6 project + NGO + `NetBootstrap` with a transport swap. Two capsules walk around in Multiplayer Play Mode locally *and* via Steam 480 across two houses. One synced physics box. Build delivered through itch. | 1–1.5 wks |
| **M1** | **"One Rabbit" slice** | FPS controller, rifle, rabbit AI (NavMesh wander/flee), ragdoll, pick up, sell to trader, shared money. All working in multiplayer. | 1–2 wks |
| **M2** | **The loop** | 5 animals, shop with 4 weapons + 4 tools, style multipliers with announcer text, two-person carry, hunger/jerky. **Playtest night #1.** | 2–3 wks |
| **M3** | **Chaos & a goal** | Friendly-fire ragdolls, emotes, Big Doug legendary summon fight, truck, day/night lighting + camp bell. **Playtest #2.** | 2 wks |
| **M4** | **Night & social** | Night animals + ×2 night pay, Sasquatch stalking, proximity voice (Steam voice → 3D AudioSource), host save/load, gambling crate cosmetics. | 2–3 wks |
| **M5** | **Finale & juice** | Sasquatch legendary hunt finale, second region, settings menu, polish from playtest notes. | 2–3 wks |

\*Hobby pace with me writing most of the code. PEAK took about 4.5 months with a full team. **About 11–14 weeks to a "complete" game** is realistic only if we hold the line on scope. Unity's editor-side wiring adds a little over Godot.

### Top risks
1. **Steam 480 flakiness.** M0 tests this first. Fallback is Unity Transport over Tailscale.
2. **Physics sync of carried carcasses feels bad.** Prototype in M0 with a box before any art.
3. **The loop gets stale after 2 hours** (How to Fish's main criticism). Mitigations: a region per milestone, animals that fight back, legendary bosses, and a real ending.
4. **Scope creep.** Anything not in table 2.3 goes into a `docs/ideas.md` parking lot.

---

## 5. Proposed repo layout
```
FriendSlopHunting/
  Assets/
    _Game/
      Scripts/       Net/ (NetBootstrap, SteamLobby, transports), Player/, Animals/, Weapons/, Economy/, UI/
      Prefabs/       Player, Animals/, Weapons/, Carcass
      Scenes/        Boot, Camp_Forest
      Editor/        prefab/scene builder scripts
    ThirdParty/      Facepunch.Steamworks, Quaternius, Kenney (credited in CREDITS.md)
  Packages/          manifest.json (NGO, Multiplayer Play Mode, Input System)
  ProjectSettings/
  docs/              01-discovery-and-scope.md, ideas.md, playtest-notes/
  tools/             release.ps1 (build + butler push)
  .gitignore  .gitattributes (LFS)
```
