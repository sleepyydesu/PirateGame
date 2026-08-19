<div align="center">

# 🏴‍☠️ Pirate Game

**A swashbuckling 3D action-adventure — sword combat on land, cannon battles at sea.**

Built by **[GameDevHeroes](#-about-gamedevheroes)**, the game development community of **Herald College Kathmandu**.

[![Unity](https://img.shields.io/badge/Unity-6000.5.4f1-black?logo=unity&logoColor=white)](https://unity.com/)
[![Render Pipeline](https://img.shields.io/badge/Render%20Pipeline-URP-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)
[![Platform](https://img.shields.io/badge/Platform-PC%20%7C%20Mobile-orange)]()
[![Status](https://img.shields.io/badge/Status-In%20Development-yellow)]()
[![License](https://img.shields.io/badge/License-TBD-lightgrey)]()

</div>

---

## ⚓ About the Game

You're a pirate chasing a single clue toward legendary treasure. Sail your ship into a tutorial skirmish, make landfall, explore, take on a merchant's quest, cross blades with rival pirates, topple a boss, and sail back out for one climactic ship battle before reaching the final shore.

```
CUTSCENE (clue) → SHIP BATTLE #1 (tutorial)
   → ISLAND 1: Explore → Quest A → Pirate fights → Rival fight → Treasure → LAND BOSS
   → Sail (travel only)
   → ISLAND 2: Explore → Quest B → Pirate fights
   → SHIP BATTLE #2 (smuggler, climax)
   → Final island → ENDING
```

Two systems carry the game's identity: **sword combat** on foot and **cannon combat** at sea — everything else is built to support that loop without overreaching scope.

### Core Features

| System | Notes |
|---|---|
| Movement & traversal | Walk / run / swim, third-person camera |
| Sword combat | Attack, block, parry |
| Ship combat | Navigation, cannon aiming & firing, ship-vs-ship battles |
| Exploration | 2 islands built from a shared modular kit |
| Quests | 2 handcrafted quests tied to a recurring merchant NPC |
| Progression | 5 player levels, 3 unlockable abilities, 3 weapons |
| Enemies & boss | Basic pirates, a smuggler variant, and a land boss / rival pirate |
| Narrative | Text-only dialogue, 3 key cutscenes |

## 🛠️ Tech Stack

- **Engine:** Unity `6000.5.4f1`
- **Render Pipeline:** Universal Render Pipeline (URP)
- **Input:** Unity Input System
- **Data:** ScriptableObjects for enemy / weapon / quest definitions
- **Task tracking:** ClickUp (source of truth for tasks, sprints, and bug reports — not GitHub Issues)
- **Version control:** Git + Git LFS for binary assets

## 📁 Project Structure

```
Assets/
├── Art/
│   ├── Characters/       # Player, rival pirate, merchant, enemies
│   ├── Environment/      # Island kit, ship, props
│   └── Weapons/
├── Audio/
│   ├── SFX/
│   ├── Music/
│   └── Ambience/
├── Animations/
├── Materials/
├── Prefabs/
│   ├── Characters/
│   ├── Ship/
│   ├── Enemies/
│   ├── Props/
│   └── UI/
├── Scenes/               # MainMenu, Island1, Island2, ShipBattle
├── Scripts/
│   ├── Player/
│   ├── Combat/
│   ├── Ship/
│   ├── Enemies/
│   ├── Quests/
│   ├── UI/
│   ├── Systems/          # GameManager, SaveSystem, AudioManager
│   └── Data/              # ScriptableObject definitions
├── ScriptableObjects/    # Actual data assets (Data_Enemy_BasicPirate, etc.)
├── UI/
└── Resources/
```

Empty folders are kept in the repo with `.gitkeep` placeholders so the structure is ready for everyone to drop assets into from day one.

## 🚀 Getting Started

1. **Install Git LFS** (once per machine) — large art/audio assets are tracked through it:
   ```bash
   git lfs install
   ```
2. **Clone the repo:**
   ```bash
   git clone https://github.com/<org>/<repo>.git
   cd <repo>
   git lfs pull
   ```
3. **Open in Unity Hub** using editor version `6000.5.4f1` (or let Hub install it automatically when you open the project).
4. Let Unity import the project — this can take a few minutes on first open.

## 🌿 Git Workflow

- `main` — always stable and buildable. Never commit directly.
- `develop` — integration branch. All feature branches merge here first.
- `feature/<short-description>` — one feature, one branch.
- `bugfix/<short-description>` — fixes, branched from `develop`.

```
feature/player-movement
feature/ship-combat
bugfix/ship-collision-clipping
```

Open a PR into `develop` for every change, link the related ClickUp task in the description, and get at least one review from your squad before merging. `develop` merges into `main` at milestone builds.

**Unity-specific rules:**
- Never delete or hand-edit `.meta` files — always move/rename assets inside the Editor.
- One person "owns" a scene or prefab while actively editing it — Unity YAML merges do not resolve cleanly.
- Never commit `Library/`, `Temp/`, `Obj/`, `Build/`, or `Logs/` — they're already in `.gitignore`.

## 👥 Team

Organized across Design, Art, Sound, Programming, QA, and Production — coordinated in ClickUp, built in this repo. Task and sprint status live in ClickUp; this repo is where the actual implementation happens.

## 🎓 About GameDevHeroes

**GameDevHeroes** is the game development community at **Herald College Kathmandu**, bringing together students across programming, art, design, sound, and production to build games together and learn the full pipeline of shipping a project as a team.

## 📄 License

License to be decided by the GameDevHeroes team — until then, all rights reserved by the project's contributors.
