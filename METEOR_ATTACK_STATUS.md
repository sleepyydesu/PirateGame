# Boss Meteor Attack: Goals and Status

## Goal

The boss should hold its spell-casting pose while a timed meteor attack runs. Meteors should appear above and around the player, descend at a configurable speed, and trigger their impact VFX and damage when they reach the ground.

The attack should expose controls for:

- Number of meteors in each wave
- Time between waves
- Total wave-spawning duration
- Meteor height and fall speed
- Scatter radius around the player
- Impact damage and radius

For example, 10 meteors per wave, a 1-second interval, and a 3-second duration should create three waves at approximately 0, 1, and 2 seconds: 30 meteors total in phase one. Later phases can increase the wave size using the configured multiplier.

## Current Setup

The spell is configured in `Assets/ScriptableObjects/BossData/MeteorSpell.asset` and currently uses the configured copy:

`Assets/Prefabs/Presentations/Impact/Red energy explosion 1.prefab`

Current phase-one settings in the asset:

| Setting | Value |
| --- | ---: |
| Meteors per wave | 10 |
| Cast duration | 3 seconds |
| Wave interval | 1 second |
| Spawn height | 10 units |
| Fall speed | 10 units/second |
| Impact delay | 2 seconds |
| Prefab lifetime after impact | 3 seconds |
| Direct-hit chance | 0.5 per wave (at most one direct hit) |
| Scatter radius | 5–10 units |
| Blast radius | 1 unit |

With these height and speed values, the fall itself takes about 1 second. `Impact Delay` is a minimum spawn-to-impact time: the script waits before falling only when the configured delay is longer than the calculated fall time. At the current values, impact is expected about 2 seconds after a meteor spawns.

## Changes Made So Far

- `BossMeteorCaster` spawns a fixed number of waves based on duration and interval, with the first wave spawned immediately. It keeps the cast active until the spawned meteors finish their falls.
- `BossAI` waits for the caster before completing the attack, so the cast pose can be held through the attack.
- Each wave has at most one meteor aimed directly at the player. The other meteors scatter around the player's position at the time that wave is spawned.
- `Meteor` keeps the prefab root and marker at the landing point and moves the `Core` child down from the configured local height.
- `MeteorParts` on the configured prefab assigns the Core, Circle marker, flight particle systems, impact effect objects, and root impact particle system.
- The configured prefab's 11 particle systems have Play On Awake disabled. The script explicitly starts the marker and flight effects, then starts impact effects on landing.
- The configured impact particle bursts were changed from 1 second after playback to time zero. The Core formation burst was left at its authored timing.
- The caster's public start method was renamed to `StartCast` to avoid collisions with unrelated animation events named `Begin`. The casting clip should call `AE_SpellStart`.

## Problems Observed During Testing

Several Play Mode tests have shown inconsistent-looking results:

- Sometimes only a subset of the expected meteors was visible.
- Some impacts appeared late or seemed to belong to a later wave.
- The marker and Core sometimes appeared together at ground level.
- In later tests, markers and Cores appeared correctly, but impact effects were missing, incomplete, or delayed.
- Some tests appeared to show four or five groups, even though the current phase-one schedule is three waves.

Investigations found and fixed specific causes: particle systems playing on prefab spawn, the marker not being restarted after particle clearing, impact bursts being delayed by one second, and direct-hit meteors overlapping at the same point. These changes have not yet been confirmed in a new Unity Play Mode test after the latest prefab edits.

If five distinct wave groups still appear after reimporting and testing, check whether the animation event or attack is starting the caster more than once. The caster currently schedules three phase-one waves for the saved 3-second / 1-second settings.

## Next Unity Test

1. Let Unity finish importing/recompiling the changed scripts and prefab.
2. Open the `MeteorSpell` asset and verify the Meteor Prefab is `Red energy explosion 1` from `Assets/Prefabs/Presentations/Impact`.
3. Confirm `Prefab Plays Itself` is enabled and the prefab root's `MeteorParts` references still point to Core, Circle, flight effects, and impact effects.
4. Temporarily reduce the wave size to 1 or 2 and scatter radius to a small range, so individual meteors are easy to observe.
5. Test one cast and verify that the marker appears at ground level, the Core starts above it and descends, and impact particles appear on the same frame that the Core reaches the ground.
6. After the single-meteor test works, restore 10 meteors per wave and test that the three waves occur at approximately 0, 1, and 2 seconds.

If the impact is still delayed, inspect the **runtime-instantiated prefab** in the Hierarchy and verify that the root and impact particle systems are playing at landing. This will help distinguish a remaining particle-system timing issue from multiple spell casts or an Inspector reference to a different prefab.

## Validation

The gameplay C# project built successfully after the latest script changes. The attack and VFX still require verification in Unity Play Mode; a successful code build does not confirm the visual timing.
