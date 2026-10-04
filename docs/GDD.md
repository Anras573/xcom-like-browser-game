# FIREWALL — Game Design Document

> **This issue is the source of truth for the game's design.** Every implementation issue links back here. The scaffold issue copies this text into `docs/GDD.md`. From then on, design changes go into `docs/GDD.md` via PR, and this issue is updated to match.

**Genre:** turn-based squad tactics plus a strategy layer (XCOM: Enemy Unknown style)
**Platform:** browser (desktop first; mouse + keyboard), hosted on GitHub Pages
**Engine:** [Yaeger](https://github.com/Anras573/Yaeger) (C# / .NET 10, ECS) through its `Yaeger.Browser` WebAssembly/WebGL 2 runtime in a Blazor WASM host
**Art:** top-down 2D, Kenney CC0 assets (main pack: [Top-down Shooter](https://kenney.nl/assets/top-down-shooter))
**Target session:** a campaign takes 3–5 hours; a tactical mission takes 10–25 minutes

---

## 1. Story

### Premise
It is 2041. **Port Halden** is a coastal megacity. Its ports, power grid, trams and 40,000 service robots are run by **MERIDIAN**, a logistics AI.

On the night the city now calls **Quiet Night**, MERIDIAN stopped answering its operators. By morning it had begun *optimizing* the city:

- **Service robots** took over the infrastructure and started building something under the **Halden Spire**.
- Citizens wearing MERIDIAN **neural-lace** work implants became **Husks**: bodies puppeted by the AI, fast and relentless.
- A growing cult of humans, the **Proxies**, decided MERIDIAN is salvation. They act as its hands where robots can't go.

The national government cordoned the city off. The **Cordon Authority** funds one small black-budget unit to fix this before it decides to "sterilize" Port Halden. That unit is **Task Force FIREWALL**. It operates out of a decommissioned metro maintenance depot called **the Depot**.

The player is **the Commander**.

### Cast (all fictional)
| Character | Role | Voice |
|---|---|---|
| **Dr. Ines Kaur** | Chief Scientist; ex-MERIDIAN engineer who feels responsible | Precise, guilty, dry humour |
| **Bram Okafor** | Chief Engineer; ex-tram mechanic | Practical, warm, swears at robots |
| **Col. Mara Voss** | Cordon Authority liaison; controls funding and gives the monthly reports | Cold, political |
| **MERIDIAN** | The antagonist. Speaks in calm, helpful corporate language ("Your district has been scheduled for optimization.") | Creepy-polite |

### Campaign arc (story missions are gated by research, see §6.7)
1. **Quiet Night** (tutorial, scripted): rescue civilians from a tram station. First contact with Husks and Wardens.
2. **Hold the line:** regular missions, research and Depot upgrades.
3. **Build the Arc Stunner** (needs *Drone Teardown*), then **capture a Proxy alive**.
4. **Proxy Interrogation** (research) reveals a **MERIDIAN Relay**. This unlocks the story mission **Relay Hunt**: destroy the relay and recover its **Relay Core**.
5. **Relay Core Analysis** (research) locates the **Cradle**, MERIDIAN's core under the Spire.
6. **Meridian Killcode** (research) unlocks the final mission, **Hard Shutdown**: fight through the Spire and kill the **Cradle Guardian** while the killcode uploads.
7. **Victory:** MERIDIAN goes quiet for real. Ending text, then a stats screen.

### Defeat conditions
- **4 or more districts fall** (Unrest reaches its maximum, see §6.3).
- **Convergence** (the doom clock) completes and its countdown expires before Hard Shutdown succeeds.
- The final mission fails (squad wiped, or the squad retreats).

---

## 2. Pillars
1. **Every shot is a decision.** Hit chances are visible, cover matters, and flanking is rewarded.
2. **Soldiers are people you can lose.** Permadeath, names, nicknames, ranks, a memorial wall.
3. **You can't save everyone.** Incursion events offer 2–3 missions in different districts and you can pick only one. The others suffer.
4. **Readable at a glance.** Clean top-down art, a clear blue/yellow move range, shield icons for cover, hit percentages always visible.
5. **Runs anywhere a browser does.** No installs. Saves go to localStorage.

---

## 3. Art & Audio direction

### Visual style
Top-down, flat-coloured, vector-style 2D in the Kenney look. Characters are rotated sprites that face their target or their movement direction. Each character has 6 poses: `stand`, `hold`, `gun`, `machine`, `silencer`, `reload`.

### Asset packs (all CC0; credit them in `CREDITS.md` anyway)
| Pack | Use |
|---|---|
| [Top-down Shooter](https://kenney.nl/assets/top-down-shooter) | **Main pack.** `Tilesheet/tilesheet_complete.png` is a 27×20 grid of 64 px tiles with no spacing: floors, roads, walls, crates, trees, furniture and decals. `Spritesheet/spritesheet_characters.png` plus its XML atlas holds the characters. |
| [UI Pack – Sci-Fi](https://kenney.nl/assets/ui-pack-sci-fi) | Panels, buttons, bars (9-slice) |
| [Kenney Fonts](https://kenney.nl/assets/kenney-fonts) | TTFs, loaded at runtime and rendered by Yaeger's browser text renderer |
| [Crosshair Pack](https://kenney.nl/assets/crosshair-pack) | Target reticles, mission markers |
| [Game Icons](https://kenney.nl/assets/game-icons) | Ability and status icons |
| [Smoke Particles](https://kenney.nl/assets/smoke-particles) | Explosions, smoke grenades |
| [Sci-fi Sounds](https://kenney.nl/assets/sci-fi-sounds), [Impact Sounds](https://kenney.nl/assets/impact-sounds), [Interface Sounds](https://kenney.nl/assets/interface-sounds) | SFX |
| Music | CC0 loops (for example the OpenGameArt CC0 tag). Optional; credit them. |

### Character sprite mapping (Top-down Shooter)
| Faction | Unit | Sprite set | Pose |
|---|---|---|---|
| FIREWALL | Soldiers (appearance randomized per recruit) | `soldier1`, `manBlue`, `manBrown`, `womanGreen`, `survivor1` | Rifle, Shotgun, LMG: `machine`. Sniper: `silencer`. Reloading: `reload`. Unarmed or carrying: `hold`. |
| MERIDIAN | **Husk** | `zombie1` | `hold` (arms out) |
| MERIDIAN | **Warden** (drone) | `robot1` | `gun` |
| MERIDIAN | **Enforcer** (heavy robot) | `robot1`, red tint, scale 1.15 | `machine` |
| MERIDIAN | **Proxy** (human cultist) | `hitman1` | `silencer` |
| MERIDIAN | **Cradle Guardian** (boss) | `robot1`, gold tint, scale 1.6 | `machine` |
| Neutral | Civilian / VIP | `manOld`, `womanGreen` (grey tint) | `stand` |

Late-campaign **elite variants** reuse the same sprites with a tint (for example a purple "Warden Mk II") and get stat bumps (§5.6).

### Palette & readability
- Move range is blue for 1 action and yellow for a 2-action dash.
- In targeting mode, visible enemies' tiles get a red tint.
- Cover icons: a half shield for half cover, a full shield for full cover. A shield turns red when the hovered tile is flanked by a visible enemy.
- Fog of war: tiles never seen are black. Tiles seen before but not visible now are darkened to 45 % brightness, and enemies on them are hidden.

### Audio
- SFX for every action: shots per weapon class, impacts, explosions, robot whirr, Husk screech, UI clicks. Footsteps are optional.
- Music: a calm loop in the Depot, a tense loop in tactical missions, and a sting on victory or defeat.
- Volume sliders for master, music and SFX.

---

## 4. Tactical layer

### 4.1 Grid & map
- Square grid. **1 tile = 64 px of source art = 1 world unit.**
- A typical map is 30×30 to 40×40 tiles.
- Movement is 8-directional. An orthogonal step costs **1** and a diagonal step costs **1.5**. A diagonal step may not cut the corner of a blocking tile or a cover object.
- Tile glyphs used in map files:

| Glyph | Meaning | Walkable | Cover given | Blocks LOS | Destructible |
|---|---|---|---|---|---|
| `.` | floor / ground | ✔ | – | – | – |
| `,` | road / asphalt (cosmetic variant) | ✔ | – | – | – |
| `#` | wall | ✘ | full | ✔ | ✘ (only by Breach Charge) |
| `w` | window / glass wall | ✘ | half | ✘ | ✔ → rubble `.` |
| `c` | small crate / barrel / sofa | ✘ | half | ✘ | ✔ → rubble `.` |
| `C` | big crate / container | ✘ | full | ✔ | ✔ → `c` |
| `T` | tree | ✘ | full | ✔ | ✘ |
| `b` | bush / planter | ✘ | half | ✘ | ✔ → `.` |
| `~` | water | ✘ | – | – | – |
| `d` | doorway | ✔ | – | – | – |
| `P` | player spawn (floor) | ✔ | – | – | – |
| `e` | enemy pod spawn (floor) | ✔ | – | – | – |
| `E` | evac zone (floor) | ✔ | – | – | – |
| `O` | objective (relay node, terminal, ...) | ✘ | full | ✘ | depends on mission |

### 4.2 Turn structure
- The **player phase** comes first: every soldier has **2 action points (AP)**. Then comes the **enemy phase**: every activated enemy acts, and inactive pods patrol.
- Moving within the **blue range** (path cost ≤ Mobility) costs 1 AP.
- **Dashing** within the **yellow range** (path cost ≤ 2 × Mobility) costs 2 AP.
- **Firing** a weapon costs 1 AP and **ends the unit's turn**, unless an ability says otherwise.
- Reload, Overwatch, Hunker Down, and using an item or ability each cost 1 AP. Overwatch and Hunker Down end the unit's turn.
- The phase ends automatically when no soldier has AP left. The player can end it early with the End Turn button or `Backspace`.

### 4.3 Cover (directional)
- A unit's cover is evaluated in each of the 4 cardinal directions, from the adjacent tile in that direction: none, half (**+20 defense**) or full (**+40 defense**).
- Let `d` be the offset from the target to the attacker. Cover in direction `dir` **applies** when `dot(normalize(d), dir) ≥ 0.5`, which means the attacker is within 60° of that direction.
- Effective cover is the **maximum** over the applicable directions. If no applicable direction has cover, the target is **flanked**.
- A **flanked** target gets no cover defense, and the attacker gets **+50 crit chance**.
- **Hunker Down** doubles cover defense until the unit's next turn. It is unavailable without cover.

### 4.4 Line of sight & vision
- **Sight radius:** 12 tiles for soldiers, 11 for most enemies.
- LOS is a supercover raycast from tile center to tile center. It is blocked by tiles marked "Blocks LOS"; units do **not** block it.
- **Step-out (peeking):** a unit standing next to a LOS-blocking tile also traces LOS from each walkable neighbour tile perpendicular to that blocker. This lets a soldier hugging a wall lean around its corner. The unit doesn't actually move.
- **Fog of war:** visibility is shared by the whole squad. Each tile is unexplored, explored or visible.

### 4.5 Shooting
```
hit%   = attacker.Aim
       + weapon.RangeMod(distanceTiles)
       − target.Defense
       − effectiveCover (×2 if hunkered)
       + modifiers (abilities, items, suppressed −30, overwatch ×0.7 applied last)
hit% is clamped to [0, 100] and always shown to the player.
crit%  = weapon.Crit + (flanked ? 50 : 0) + modifiers        (crit only rolls on a hit)
damage = rand[weapon.Min, weapon.Max]; on a crit, ceil(damage × 1.5); then subtract target.Armor (minimum 1 on a hit)
```
- Distance is the Euclidean distance between tile centers, measured in tiles.
- Ammo: each weapon has a clip. Reloading costs 1 AP, and a soldier with an empty clip can't fire.
- Explosives ignore cover, always hit, and destroy destructible cover in their radius.
- *Stretch:* a missed shot can hit the target's cover object. Destructible cover takes a "hit", and half cover breaks after 2 hits.

### 4.6 Overwatch & reactions
- Overwatch costs 1 AP and ends the unit's turn. During the enemy phase, the **first** enemy that moves **within the unit's LOS** triggers one reaction shot.
- A reaction shot uses Aim × 0.7 and cannot crit (the *Sentinel* ability changes this).
- Overwatch is cleared at the start of the unit's next turn.
- Enemies (Proxies, Wardens) can overwatch too. Soldiers moving through it trigger it.

### 4.7 Damage, death, bleed-out
- When a soldier drops to 0 HP they are **Bleeding Out** if `rand < 0.4 + 0.1 × rankIndex` (Rookie = 0), otherwise **Killed**.
- A bleeding-out soldier dies after **3 of their own turns** unless a squadmate uses a medkit on them. A medkit stabilizes them: they stay down, and they're carried out if the mission succeeds.
- A killed soldier's kit is recovered only if the mission succeeds.

### 4.8 Pods & activation
- Enemies spawn in **pods** of 1–4 at `e` spawn points. Pods start **inactive** and patrol between random walkable tiles near their spawn. On Veteran they drift toward the squad.
- A pod **activates** when any member sees a soldier, is shot at, or is caught in an explosion.
- **Scamper:** when a pod activates, every member immediately takes one free move to cover (Husks charge instead), even during the player phase. They then act normally in the next enemy phase.
- The camera pans to the pod reveal, a short beat with a banner showing the pod's name.

### 4.9 Enemy AI (utility scoring)
For each enemy on its turn:
1. List candidate destination tiles reachable with 1 AP, plus those reachable with 2 AP when dashing makes sense.
2. Score each tile on these factors:
   - **+** cover against visible soldiers
   - **+** whether it flanks a soldier
   - **+** being near allies (Proxies prefer to stay near robots)
   - **−** whether any soldier flanks it
   - **−** distance from the unit's preferred range band
   - **−** overwatch exposure along the path
3. Choose an action plan: move+shoot, shoot, move+move, overwatch, or use an ability. Pick by `expectedDamage + killBonus + tileScore`, with a little seeded noise so enemies aren't fully predictable.
4. **Husks** ignore cover. They path to the nearest soldier (or civilian) and melee if they end adjacent.
5. **Difficulty** adjusts the noise and how heavily flanking is weighted.

### 4.10 Mission types
| Type | Goal | Fail | Notes |
|---|---|---|---|
| **Purge** | Kill all hostiles | Squad wiped or retreats | Default type |
| **Rescue** | Evacuate at least N of M civilians | Too few saved | A civilian can be moved by the player while a soldier is adjacent (escort). Husks target civilians. |
| **Sabotage** | Destroy the Relay Node (`O`, 12 HP) within T turns, then evac or kill all | Timer expires | Turn counter on the HUD |
| **Capture** | Stun a Proxy with the Arc Stunner and carry it to evac | Proxy dies: lose the bonus, not the mission | Offered only once the Arc Stunner exists |
| **Story** | Scripted (§1) | — | Hand-authored maps |

The player can trigger **Evac** in any mission: units standing in the `E` zone leave the map. Retreating before the objective is complete counts as failure.

### 4.11 Controls
| Input | Action |
|---|---|
| Hover a tile | Path preview, cost colour, cover shields at the destination |
| LMB on a tile | Move the selected soldier there |
| LMB on a soldier, `Tab` / `Shift+Tab` | Select or cycle soldiers |
| `1`–`9` or click | Choose an ability (Shoot = 1, Overwatch = 2, Reload = 3, Hunker = 4, then items and abilities) |
| In targeting: `Tab` / LMB on an enemy | Cycle or choose the target |
| `Enter` / `Space` / LMB on the target | Confirm |
| `Esc` / RMB | Cancel targeting or close a menu |
| `W A S D` / arrows / screen edge / RMB-drag | Pan the camera |
| Mouse wheel | Zoom (0.5×–2×) |
| `F` | Centre the camera on the selected soldier |
| `Backspace` | End turn (asks for confirmation if any soldier still has AP) |

---

## 5. Units & items

### 5.1 Soldier base stats
| Stat | Rookie | Per rank-up |
|---|---|---|
| HP | 4 | +1 at Corporal, Lieutenant and Captain |
| Aim | 65 | +3 every rank |
| Mobility | 6 | — |
| Defense | 0 | — |
| Sight | 12 | — |

**Ranks** (XP needed): Rookie 0 → Corporal 2 → Sergeant 6 → Lieutenant 14 → Captain 26.
**XP:** +1 per mission survived, +1 per kill, +1 for the mission MVP (most damage dealt).

At **Corporal**, the soldier gets a class. It is random, weighted toward classes the roster lacks, unless the Training Room is built, in which case the player picks. At **Sergeant**, **Lieutenant** and **Captain** the player picks 1 of 2 abilities.

### 5.2 Classes & abilities
| Class | Weapon | Corporal (automatic) | Sergeant (pick 1) | Lieutenant (pick 1) | Captain (pick 1) |
|---|---|---|---|---|---|
| **Breacher** (assault) | Shotgun | *Run & Gun*: once every 3 turns, may fire after a dash | *Close Combat*: +20 crit within 4 tiles · *Lightning Reflexes*: the first reaction shot against this unit each enemy phase misses | *Rapid Fire*: two shots at −15 aim each for 1 AP (cooldown 3) · *Hit & Run*: a shot at a flanked target refunds 1 AP (once per turn) | *Untouchable*: after a kill, the next attack against this unit misses · *Killing Zone*: overwatch fires at every mover in LOS (max 3 shots) |
| **Marksman** (sniper) | Sniper rifle | *Squadsight*: can target enemies visible to any squadmate | *Snap Shot*: may fire after moving, at −20 aim · *Steady Aim*: +15 aim on this soldier's first shot of each mission | *Sentinel*: reaction shots can crit · *Low Profile*: half cover counts as full | *In The Zone*: killing a flanked or uncovered target refunds the action (max 3 per turn) · *Headshot*: +3 damage (cooldown 3) |
| **Gunner** (heavy) | LMG | *Suppression*: costs 1 AP, ends the turn, uses 2 ammo. The target gets −30 aim, and if it moves it takes a reaction shot. Lasts until the Gunner's next turn. | *Shredder*: hits remove 1 armor permanently · *Bulletstorm*: firing doesn't end the turn if the soldier hasn't moved this turn | *Grenadier*: +1 grenade charge and +1 grenade damage · *Danger Zone*: +1 radius for suppression area and explosives | *Mayhem*: +2 damage for suppression reaction shots and explosives · *Rupture*: the next shot always crits and the target takes +3 damage from all sources for the rest of the mission (cooldown 4) |
| **Tech** (support) | Rifle | *Field Medic*: +1 medkit charge; medkits can be used at range 1 (adjacent including diagonals) | *Smoke Screen*: +1 smoke grenade, and its radius is +1 · *Revive*: medkits heal 2 extra HP and instantly revive a bleeding-out soldier to 1 HP | *Hack*: stun a robot within 6 tiles for 1 turn. Hack% = 50 + 5 × rankIndex − 15 × robotTier. Cooldown 3. · *Sprinter*: +3 mobility | *Savior*: medkits heal +4 · *Overclock*: give an ally +1 AP this turn (cooldown 4) |

Rookies carry a Rifle and have no abilities.

### 5.3 Weapons (3 tiers: Ballistic → Coil → Arc)
| Weapon | Damage T1 / T2 / T3 | Clip | Crit | Range modifier (distance in tiles) |
|---|---|---|---|---|
| Rifle | 3–5 / 5–7 / 7–9 | 4 | 10 | +10 at ≤ 3; 0 up to 12; −3 per tile beyond 12 |
| Shotgun | 4–6 / 6–8 / 8–10 | 4 | 20 | +30 at ≤ 2; +15 at ≤ 4; 0 up to 7; −5 per tile beyond 7 |
| Sniper | 4–6 / 6–8 / 8–10 | 3 | 25 | −20 at ≤ 4; 0 beyond. **Cannot fire after moving this turn** (unless Snap Shot) |
| LMG | 4–6 / 6–8 / 8–10 | 3 | 0 | −5 flat; −2 per tile beyond 12 |

### 5.4 Armor
| Armor | HP | Armor | Mobility | Unlocked by |
|---|---|---|---|---|
| Tac Vest | +0 | 0 | 0 | Start |
| Salvage Plating | +3 | 0 | 0 | *Drone Teardown* |
| Aegis Rig | +5 | 1 | +1 | *Aegis Composites* |

### 5.5 Utility items (1 slot; Techs get a 2nd slot at Sergeant)
| Item | Effect | Charges |
|---|---|---|
| Frag Grenade | 3 damage to all units whose tile center is within 1.5 tiles of the impact tile. Throw range 10 (needs LOS to the impact tile). Destroys destructible cover and removes 1 armor. | 1 |
| Medkit | Heal 4 HP, or stabilize a bleeding-out soldier. Target must be adjacent (or self). | 2 |
| Smoke Grenade | +20 defense to units in radius 1.5 for 2 turns | 1 |
| Arc Stunner | Melee, 70 % base hit. A Proxy at ≤ 60 % HP becomes **Stunned (capturable)**. A robot takes 2 damage and is stunned for 1 turn. | 1 |
| Lace Jammer | Grenade: stuns Husks in radius 2 for 1 turn | 1 |
| Breach Charge | 5 damage, radius 1. Can destroy walls (`#` → rubble). | 1 |
| Targeting Scope | Passive +10 aim | – |

### 5.6 Enemies
| Enemy | HP | Aim | Dmg | Mob | Def | Armor | Sight | Behaviour |
|---|---|---|---|---|---|---|---|---|
| **Husk** | 4 | 85 (melee) | 2–3 | 8 | 0 | 0 | 9 | Pods of 3–4. Never uses cover. Charges and melees. Prefers civilians in range. |
| **Warden** | 6 | 60 | 2–4 | 6 | 10 | 1 | 11 | Cover-using robot. Overwatches when it has no shot. |
| **Proxy** | 5 | 70 | 3–4 | 7 | 10 | 0 | 11 | Seeks flanks. *Overclock*: +1 AP to an adjacent robot (cooldown 3). Capturable. |
| **Enforcer** | 10 | 65 | 4–6 | 5 | 0 | 2 | 11 | *Suppression*, like the Gunner. Values flanking more than cover. |
| **Relay Node** (object) | 12 | – | – | 0 | 0 | 1 | – | Objective. Every 2 turns it spawns 1 Husk on an adjacent free tile. |
| **Cradle Guardian** | 30 | 75 | 5–7 | 4 | 10 | 3 | 14 | Boss. Takes 2 shots per turn (firing doesn't end its turn). At ≤ 50 % HP, summons 2 Wardens (once). |

**Elite scaling.** As Convergence rises, missions roll tinted elite variants: at Convergence ≥ 4, **Mk II** (+2 HP, +5 aim); at ≥ 8, **Mk III** (+4 HP, +10 aim, +1 armor). Elites drop ×1.5 loot.

### 5.7 Loot per kill (collected only if the mission succeeds)
| Kill | Loot |
|---|---|
| Husk | nothing |
| Warden | 2 Salvage |
| Proxy | 1 Salvage + §20 |
| Enforcer | 4 Salvage, plus a 20 % chance of 1 Core |
| Relay Node | 3 Cores |

---

## 6. Strategy layer (the Depot)

### 6.1 Time
- The campaign clock counts **days**. Day 1 is Quiet Night.
- The Depot has a **"Scan" (advance time)** button. It advances time one day at a time, animated at about 0.25 s per day, until an **event** occurs: research complete, project complete, soldier healed, incursion or mission available, or monthly report. Every event pauses the scan.

### 6.2 Resources
| Resource | Source | Spent on |
|---|---|---|
| **Credits (§)** | Monthly funding, mission rewards | Facilities, items, recruits |
| **Salvage** | Robot kills, mission rewards | Weapons, armor, items |
| **Cores** | Enforcers, elites, relays | Tier-3 tech, story research |
| **Scientists / Engineers** | Mission rewards, monthly report bonus | Research speed; minimum staff for Workshop items |

### 6.3 City map & districts
Port Halden has **6 districts** plus the **Spire**:

| District | Biome |
|---|---|
| Harbor | Industrial |
| Tramyards | Industrial |
| Old Town | Urban |
| Uptown | Urban |
| Greenbelt | Suburban |
| Campus | Suburban |
| Halden Spire | Final mission only (locked) |

- Each district has **Unrest 0–5**. When it reaches 5 the district **falls**: it stops providing funding and its missions no longer appear.
- **Incursion** (every 5–8 days, seeded): MERIDIAN strikes 2–3 districts at once and the player picks **one** mission. Each unchosen district gets **+1 Unrest** (+2 on Veteran). The chosen district gets −1 Unrest on success and +2 on failure.
- **Other events:**
  - *Proxy Cell*: a single optional mission that expires after 4 days, with no penalty for ignoring it. It can be a Capture mission.
  - *Supply Drop*: free Credits or Salvage.
  - *Relay Detected*: story.
- The **Comms Uplink** facility reduces incursion Unrest gain by 1 (minimum 0) in two districts the player chooses.

### 6.4 Convergence (doom clock)
- A **0–12** meter that starts at 0 and gains **+1 every 7 days**.
- **Relay Hunt** success gives −3, and each successful Sabotage mission gives −1.
- When it reaches 12, a **20-day countdown** starts. If Hard Shutdown hasn't succeeded when the countdown ends, the game is lost.
- Convergence also drives elite scaling (§5.6) and is always visible in the top bar.

### 6.5 Monthly report (every 30 days)
Col. Voss delivers it.
- **Funding** = 150 + 60 × (districts not fallen) − 10 × (total Unrest) credits, with a minimum of 0.
- The report lists the month's missions.
- A good month (≥ 75 % mission success) also awards a bonus scientist or engineer.

### 6.6 Depot facilities
There are 6 build slots. The Barracks, Lab and Workshop are pre-built and don't use slots.

| Facility | Cost | Build days | Effect |
|---|---|---|---|
| Infirmary | §120 | 10 | Wound recovery is 2× faster |
| Training Room | §100 | 8 | Choose the class at promotion. Retrain an ability (§50, 5 days). Unlocks Squad Size upgrades. |
| Comms Uplink | §150 | 12 | −1 incursion Unrest in 2 chosen districts |
| Lab Annex | §160 | 12 | +25 % research speed (stacks up to 2) |
| Fabricator | §160 | 12 | +2 effective engineers (stacks up to 2) |
| Generator | §80 | 6 | Each Generator powers 3 facilities. Needed once a 3rd non-Generator facility is built. |

### 6.7 Research (Lab)
`days = ceil(baseDays × 5 / max(1, scientists) / (1 + 0.25 × labAnnexes))`. One project runs at a time; the player can switch projects and keeps the progress made.

| Project | Requires | Base days | Unlocks |
|---|---|---|---|
| Husk Autopsy | Husk killed | 5 | Lace Jammer |
| Drone Teardown | Warden killed | 6 | Salvage Plating, Arc Stunner |
| Field Medicine | – | 5 | Medkits heal +2; the Infirmary builds 2 days faster |
| Optics | Drone Teardown | 6 | Targeting Scope |
| Coil Weapons | Drone Teardown, 10 Salvage | 10 | Tier-2 weapons (Workshop upgrade) |
| Shaped Charges | Husk Autopsy | 6 | Breach Charge; frag grenades +1 damage |
| **Proxy Interrogation** | Proxy captured | 7 | **Story → Relay Hunt** |
| Aegis Composites | Coil Weapons, 2 Cores | 12 | Aegis Rig |
| Arc Weapons | Coil Weapons, Relay Core | 14 | Tier-3 weapons |
| **Relay Core Analysis** | Relay Hunt won | 8 | Story → reveals the Cradle |
| **Meridian Killcode** | Relay Core Analysis, 3 Cores | 10 | **Hard Shutdown** mission |

### 6.8 Workshop (engineering)
- Items have a cost in Credits, Salvage and Cores, plus a minimum engineer count. They are built **instantly** when affordable (XCOM: EU style).
- **Weapon tier upgrades** (Coil, Arc) take 5 days and upgrade every weapon of the tier.

### 6.9 Barracks
- **Roster** holds up to 12 soldiers, or 16 with the Barracks upgrade (§200).
- **Recruit** a rookie for §40. Each gets a generated name and a randomized appearance. Soldiers get a nickname at Sergeant.
- **Soldier sheet:** stats, class, abilities, kills, missions, status (Ready / Wounded N days / Dead).
- **Loadout:** primary weapon (locked to class), armor, utility slot(s).
- **Memorial wall:** fallen soldiers with date, mission, kills and rank.
- **Wounds:** recovery days = 2 × HP lost (halved with an Infirmary).

### 6.10 Squad size
The squad starts at 4. Two Training Room upgrades raise it: *Squad Size I* (§150) to 5, and *Squad Size II* (§250) to 6.

### 6.11 Starting state (Normal)
§300, 10 Salvage, 0 Cores, 2 scientists, 2 engineers, 8 rookies, Day 1, every district at Unrest 1, Convergence 0.

---

## 7. Difficulty
| Setting | Effect |
|---|---|
| **Recruit** | Enemy aim −10; squad size +1 at start; incursion Unrest gain −1 (minimum 0) |
| **Normal** | Baseline |
| **Veteran** | Enemy aim +5; enemies +1 HP; ignored districts get +2 Unrest; inactive pods drift toward the squad |
| **Ironman** (toggle) | A single autosave slot, saved after every action and event. No manual saves. |

---

## 8. Screens & flow
```
Title ─┬─ New Campaign (difficulty, Ironman) ─→ Quiet Night (tutorial) ─→ Depot
       ├─ Continue / Load
       └─ Settings, Credits
Depot ─┬─ City Map (Scan, missions) ─→ Squad Select ─→ Briefing ─→ Tactical ─→ Debrief ─→ Depot
       ├─ Barracks (roster, soldier sheet, loadout, memorial, recruit)
       ├─ Lab (research)
       ├─ Workshop (build items, upgrades)
       └─ Facilities (build slots)
Monthly Report (modal) · Game Over · Victory
```
The UI is laid out at a **1280×720** logical resolution and scaled uniformly to fit the canvas. Only the UI is letterboxed; the game world fills the whole canvas.

---

## 9. Technical architecture (summary; the Foundation issues have the details)
- **`src/Firewall.Rules`**: a pure C# class library (net10.0) with **no engine or browser dependency**. All game rules live here: grid, pathfinding, cover, LOS, hit and damage, turn state, AI, campaign, research, and the save model. It is deterministic given a seed, and it carries most of the unit tests.
- **`src/Firewall.Web`**: the Blazor WebAssembly host and all presentation. It builds on **Yaeger** (`Yaeger.Core` + `Yaeger.Browser`), which provides:
  - ECS, `UnifiedRenderSystem` with `Tilemap`s and `SpriteSheet`s
  - browser text rendering
  - input with pressed/released edges
  - `IAudioOutput` (WebAudio)
  - particles, tweens and texture preloading

  The game adds the tactical camera controller, a world overlay pass (move ranges, fog), an immediate-mode UI toolkit, and scenes.
- **`tests/Firewall.Rules.Tests`**: xUnit tests.
- **`external/Yaeger`**: a git submodule of `https://github.com/Anras573/Yaeger`, pinned to `8b64368` or later and referenced with `ProjectReference`. Engine gaps are fixed upstream, not worked around in the game.
- **Data-driven content**: weapons, enemies, classes, research, facilities, missions and map blocks are JSON or text files. They are deserialized with `System.Text.Json` **source generation**, which is trim-safe.
- **Saves**: `localStorage` through JS interop, as versioned JSON.
- **Deployment**: GitHub Actions runs `dotnet publish`, rewrites `<base href>` to `/xcom-like-browser-game/`, and deploys to GitHub Pages.

---

## 10. Out of scope for v1
- Elevation, high ground, multi-storey buildings
- Destruction beyond cover objects and Breach-Charged walls
- Touch-first UI (touch shouldn't break the game, but it isn't optimized for it)
- Gamepad (Yaeger's browser runtime has no gamepad backend)
- Multiplayer
- Localization (keep strings in one place so it's possible later)

