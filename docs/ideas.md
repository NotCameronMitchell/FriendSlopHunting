# Ideas parking lot

Things we want, but **not until the core loop is done**. Anything new that isn't in the current milestone goes here first (scope rule from `01-discovery-and-scope.md`).

## From Cameron (2026-11-14)
| Idea | Notes / overlap with the scope doc |
|---|---|
| **Proximity voice chat** | Already planned for M4 (Steam voice → 3D AudioSource). Needs a push-to-talk/open-mic setting. |
| **Gambling**: roulette, slot machine, scratchers, paid with **animals** | Bigger than the original "cosmetic crate" idea. Paying with carcasses is a fun sink: a high-value juggled rabbit becomes a betting chip. Decide what payouts are (money? cosmetics? upgrades?). |
| **Weapon and character upgrades + leveling** that unlocks purchasable upgrades | Pairs with the M2 shop. Decide: team level vs. per-player level, and whether it's saved per host. |
| **Day/night cycle, cabin and beds** for sleeping, **larger map with a border** (on top of a mountain) | Day/night + spooky nights are already M3/M4. New: cabin, beds to skip the night, and the mountaintop map with a natural edge (cliffs) instead of invisible walls. |
| **Better skybox** | Cheap visual win. Fits the juice/polish pass or M5. |
| **Progression and harder mobs** | Regions/legendaries already in the plan. New: difficulty that scales with progression. |
| **Mobs that fight back** | Already a pillar ("dumb animals with attitude": goose, boar). Bump it up when we add M2 animals. |
| **Toggle-able friendly fire**, set per lobby at creation | Add a lobby settings screen when hosting: friendly fire on/off (stored in Steam lobby data + a NetworkVariable). M3 friendly-fire ragdolls should respect it. |

## Design direction: difficulty by distance from camp (Cameron, 2026-11-14)
"The area around camp and the shop should have friendly/weaker mobs; on a larger map the stronger and aggressive mobs should show up more."

**Done now (current map):**
- A `SafeZone` component on camp (24 m radius).
- Fighters never spawn within it plus a 16 m buffer.
- They won't aggro players standing in camp, and they give up a chase when the target gets back.
- Any fighter that wanders in walks back out.
- Spawn distance bands already put skittish animals closest and predators farthest.

**Plan for the bigger mountaintop map:** replace per-species min/max spawn distance with **difficulty rings** (or hand-painted regions) radiating out from camp. Each ring has a spawn table and level:

| Ring | Distance | Feel | Typical spawns |
|---|---|---|---|
| 0: Camp | 0–25 m | Safe | none hostile; ducks wander in |
| 1: Meadow | 25–80 m | Easy money | rabbits, ducks, deer, the odd goose |
| 2: Woods | 80–180 m | Some bite | deer, boars, geese, lone wolves |
| 3: Deep forest | 180–300 m | Dangerous | wolf packs, moose, boars |
| 4: Peak / ridge | 300 m+ | Deadly, best payouts | bears, big packs, legendary totems |

- **Implementation sketch:**
  - A `DifficultyMap` (rings around camp, or a painted texture/volumes) that returns a ring for any position.
  - A `SpawnTable` asset per ring (species + weights + pack sizes + caps).
  - A single `RingSpawner` that keeps each ring populated.
  - Species keep their own stats; rings decide *where* they live. Ring level could also scale health, damage and payout for the same species ("Woods Boar" vs "Peak Boar").
- **Payoff:** the economy naturally scales with risk. Far = dangerous = expensive carcasses, but you have to haul them back, and heavy carcasses slow you down.
- **Ties into:** the bigger map with a border (mountaintop), day/night (night pushes danger inward one ring?), progression (rings unlock as the team levels), and bosses (one totem per outer ring).

## Promoted to MVP (see the scope doc §4 re-plan)
Weapons + shop (pistol starter, unlimited ammo), many more mobs and bosses, options menu (keybinds, volume, push-to-talk, mute), character customization with outfits, better art (CC0 packs).

## Deferred from the weapon roster
- **Drivable tank**: needs a vehicle system (driver seat, networked vehicle physics, cannon). Natural fit with the planned truck.

## Earlier parking lot
- Bow (silent), dynamite, tranquilizer, harpoon
- Tree stand, trail cam, ghillie suit, bait
- Daily bounties
- Truck bed / sled / zipline for hauling
