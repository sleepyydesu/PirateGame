# Quest System

Data-driven quests: a quest is a `QuestDefinition` asset (a list of stages), and the
world talks to it by reporting **event keys** and setting **flags**. Most new quests need
no new code — just an asset plus a few scene components.

Test scene: `Assets/Scenes/QuestSystemTestScene.unity`
(rebuild any time with **Tools > Pirate Game > Quests > Build Quest Test Scene**).

## How it fits together

```
QuestDefinition (asset)          QuestManager (scene)             World components
  requirements  ──unlock──▶      status per quest                 QuestZone        → "entered:<id>"
  start mode                     current stage + progress         QuestItemPickup  → "item_collected:<id>"
  stages[] ── completeOnEvent ◀── Report("event:key")  ◀────────  CombatEncounter  → "enemy_defeated:<id>" …
            ── targetId ───────▶ QuestGuidance ──▶ marker + trail  QuestNpc (dialogue) → Accept / Complete / Report
  rewards[]                      flags ("storm_done")  ──────────▶ QuestStateBinding shows/hides objects
```

Lifecycle: **Locked → Available → (Discovered) → Active[stage…] → Completed**.
The design-doc state names (`Active_Search`, `Storm_Triggered`…) are stage ids / labels,
shown in the Quest Log and the QA panel.

## Adding a quest

1. **Create > Pirate Game > Quests > Quest.** Fill title/description, requirements
   (quest completed / flag set), start mode:
   - `AcceptFromNpc` — an NPC conversation has an *Accept* choice.
   - `DiscoverOnEvent` — reporting `discoverOnEvent` pops the *Accept / Not Now* offer.
   - `AutoStart` — goes Active as soon as it unlocks.
2. Add **stages**. Each has an objective, a `targetId` (where the marker/trail points),
   `completeOnEvent` + `requiredCount`, and optional fallbacks (`revertOnEvent`,
   `requiredItem` → reverts if the player loses the item).
3. Add **rewards** (Create > Pirate Game > Quests > Rewards). Tick `rewardIsChoice` for
   "pick one". New reward types: subclass `QuestReward`.
4. Add the quest to the scene's **QuestManager** list.
5. In the scene:
   - `QuestTarget` (id = stage `targetId`; set `areaRadius` for "search this area").
   - Something that reports your events: `QuestZone`, `QuestItemPickup`,
     `CombatEncounter`, `QuestNpc` choices, or `QuestManager.Instance.Report("...")` from code.
   - `QuestStateBinding` to show/hide objects by quest status/stage/flags.
   - `QuestNpc` conversations are picked top-down: the first whose condition passes is used.

## Built-in event keys

| Component | Reports |
|---|---|
| QuestZone | `entered:<zoneId>`, `exited:<zoneId>` |
| QuestItemPickup | `item_collected:<itemId>` |
| SearchableCrate | `searched:debris` |
| CombatEncounter | `encounter_started/enemy_defeated/encounter_cleared/encounter_reset:<id>` |
| MerchantShop | `bought:<itemId>` |
| StormEvent | `storm_started`, `storm_finished` (+ flags `storm_started`, `storm_done`) |

## Controls

E talk/interact · Q pick up · J quest log · T toggle guide trail · F1 QA panel
(dialogue: E/Space continue, Tab skip, 1-4 choose).

Note: Q is also bound to *Defense* in `PlayerControls`; blocking only happens with the
sword drawn. Change `QuestItemPickup.key` if that becomes a problem.

## QA panel (F1)

Force any quest to any state, step stages, skip/start the storm, give/drop quest items,
kill the encounter, teleport to the objective, and watch flags / recent events live.

## Placeholders

Audio is synthesised by `QuestAudio` until real clips are dropped into its override list.
The merchant uses the Kevin Iglesias humanoid with Idle/Talk/Damage clips (no dedicated
"thank" or "tied up" animations yet). The ship-break is a procedural split of `Ship.fbx`.
