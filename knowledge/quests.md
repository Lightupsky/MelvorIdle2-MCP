# Melvor Idle 2 任务系统（Quests）全览

> 离线提取自 `MelvorBase.json`，共 **37** 个任务（含 Act 1 / Act 2 主线、后续主线占位、支线任务）。
> 主线任务按 Act 1 (`MQ1`→`MQ8`) → Act 2 (`MQ9`→`MQ17`) 串行推进；支线任务 (`SideQuests`) 多无前置任务，可与技能练级并行推进。

## Act 1（8 个）

### `MelvorBase:MQ1` — Pointy Preparation / 准备尖锐的武器
- **提示/推荐要求**：Must be able to defeat a Combat Level 1 Enemy using Ranged Combat Style
- **奖励**：2,000 Gold Pieces, 150× Stone Arrows, 50× Shrimp
  - **Step 0** (`0`): 提交材料/费用: 24× Stone Arrows *(说明: Just putting some pointy rocks on some wood, nothing too tough.)*
  - **Step 1** (`1`): 击杀 3× Archery Target [Ranged] *(说明: If I didn't know better, I'd say they were aiming at me.)*

### `MelvorBase:MQ2` — Fishin' for Trouble / 钓鱼钓出大麻烦
- **前置条件**：Quest:Pointy Preparation(MQ1)
- **提示/推荐要求**：Must be able to defeat a Combat Level 4 Enemy multiple times; Fishing Lv.6; Smithing Lv.6
- **奖励**：5,000 Gold Pieces, 100× Copper Bar
  - **Step 0** (`0`): 提交材料/费用: 3× Copper Sword *(说明: Swords are plenty sharp, that'll do.)*
  - **Step 1** (`1`): 通过 Fishing 获取 15× Raw Sardine *(说明: Just catch some fish and don't look suspicious. Easy.)*
  - **Step 2** (`2`): 击杀 5× Golbin Ambusher *(说明: I'm not going to let [Companion Name] beat me!)*

### `MelvorBase:MQ3` — Boarish Bonfire / 生篝火烤野猪
- **前置条件**：Quest:Fishin' for Trouble(MQ2)
- **提示/推荐要求**：Must be able to defeat a Combat Level 8 Enemy; Woodcutting Lv.10; Mining Lv.10; Fishing Lv.23; Cooking Lv.27
- **奖励**：10,000 Gold Pieces, 150× Trout, 15× Bird Nest, 15× Geode, 5× Oak Tree Seed
  - **Step 0** (`0`): 伐木 1× WoodDebris | 采矿 1× StoneDebris *(说明: A little cleanup could go a long way to making things feel a bit more put together.)*
  - **Step 1** (`1`): 提交材料/费用: 50× Mackerel Bait → 通过 Fishing 获取 15× Raw Trout *(说明: Nothing too flashy, just gotta pull up enough fish to show them how it's done.)*
  - **Step 2** (`2`): 击杀 1× Enraged Boar *(说明: There's no helping it, then!)*

### `MelvorBase:MQ4` — Misty Reconnaissance / 迷雾里探路
- **前置条件**：Quest:Boarish Bonfire(MQ3)
- **提示/推荐要求**：Must be able to defeat a Combat Level 8 Enemy; Smithing Lv.12; Crafting Lv.12
- **奖励**：20,000 Gold Pieces, 250× Leather, 250× Iron Bar, 250× Steel Bar
  - **Step 0** (`0`): Smithing 制作 6× ClimbingHook | Crafting 制作 3× ClimbingGloves | Crafting 制作 3× ClimbingBoots | Crafting 制作 3× ClimbingRope | Crafting 制作 3× ClimbingHarness | 提交材料/费用: 6× Climbing Hook, 3× Climbing Gloves, 3× Climbing Boots, 3× Climbing Rope, 3× Climbing Harness *(说明: I should be able to follow Gloob's schematics easy enough.)*

### `MelvorBase:MQ5` — Rocky Rumble / 乱石大混战
- **前置条件**：Quest:Misty Reconnaissance(MQ4)
- **提示/推荐要求**：Must be able to defeat a Combat Level 15 Dungeon Boss multiple times; Must be able to defeat a Combat Level 25 Enemy multiple times; Mining Lv.18; Crafting Lv.25; Runecrafting Lv.20; Smithing Lv.22
- **奖励**：25,000 Gold Pieces, 2× Hobforge Gloves, 500× Coal, 500× Rune Essence, 150× Uncut Topaz, 150× Uncut Sapphire
  - **Step 0** (`0`): Crafting 制作 1× SulfurProtectionGloves → 提交材料/费用: 25× Volatile Fire Rune *(说明: Chisel first, then make them into volatile bombs. Easy.)*
  - **Step 1** (`1`): 击杀 5× Grumpy Golem *(说明: I don't suppose they're interested in talking this one out.)*
  - **Step 2** (`2`): 提交材料/费用: 10× Steel Rock Anchor → Crafting 制作 10× CanyonSupport *(说明: Maybe I should ask someone in the mines..? Nah, what's the worst that could happen?)*

### `MelvorBase:MQ6` — Dungeon Breakout / 逃出地牢
- **前置条件**：Quest:Rocky Rumble(MQ5)
- **提示/推荐要求**：Woodcutting Lv.25; Smithing Lv.25
- **奖励**：30,000 Gold Pieces, 250× Oak Log, 200× Willow Log
  - **Step 0** (`0`): 提交材料/费用: 10× Silver Bar *(说明: Gold coins aren't good enough, eh?)*
  - **Step 1** (`1`): 伐木 1× FortressWall1 → Smithing 制作 1× ReinforcedWoodcuttersAxe → 伐木 1× FortressWall2 *(说明: Just press it into the wall and cut, no problem.)*

### `MelvorBase:MQ7` — Tree Trimming / 疯狂砍树
- **前置条件**：Quest:Dungeon Breakout(MQ6)
- **提示/推荐要求**：Must be able to defeat a Combat Level 35 Miniboss; Woodcutting Lv.30; Firemaking Lv.30; Cooking Lv.23; Farming Lv.21
- **奖励**：35,000 Gold Pieces, 100× Strawberry Cake, 20× Bird Nest
  - **Step 0** (`0`): 提交材料/费用: 5× Strawberry Cake *(说明: Good food always makes you feel better.)*
  - **Step 1** (`1`): 击杀 1× Tainted Treant *(说明: Time for a little bit of payback.)*
  - **Step 2** (`2`): 通过 Woodcutting 获取 25× Treant Remains → 生火燃烧 25× TreantRemains *(说明: Let's make them a bit smaller, turned into ash.)*

### `MelvorBase:MQ8` — In the Thick of It / 陷入激战
- **前置条件**：Quest:Tree Trimming(MQ7)
- **提示/推荐要求**：Must be able to defeat a Combat Level 36 Enemy multiple times; Must be able to defeat a Combat Level 39 Pinnacle Boss; Woodcutting Lv.39; Firemaking Lv.39
- **奖励**：80,000 Gold Pieces, 200× Banana Pancake, 2000× Mithril Javelin, 3000× Air Rune, 3000× Water Rune, 3000× Earth Rune, 3000× Fire Rune, 1500× Chaos Rune
  - **Step 0** (`0`): 生火燃烧 1× Brazier1 | 生火燃烧 1× Brazier2 | 生火燃烧 1× Brazier3 *(说明: If I want any supplies, I should take them from the cart before the attack starts in earnest.)*
  - **Step 1** (`1`): 伐木 1× FortGate *(说明: We have to get through this gate!)*
  - **Step 2** (`2`): 击杀 1× Ringleader *(说明: We will not fall. Not this time.)*

## Act 2（9 个）

### `MelvorBase:MQ9` — Sand in the Wind / 风中的沙
- **前置条件**：Quest:In the Thick of It(MQ8)
- **提示/推荐要求**：Smithing Lv.35; Hunting Lv.35; Cooking Lv.40
- **奖励**：60,000 Gold Pieces, 250× Mithril Bar, 250× Chicken Sandwich, 3× Teak Tree Seed
  - **Step 0** (`0`): 狩猎捕获 1× PrisonerWagon *(说明: They travelled a long, long way.)*
  - **Step 1** (`1`): 提交材料/费用: 75× Teak Log, 25× Steel Sword, 25× Steel Shield, 25× Steel Platebody *(说明: I should provide some basic supplies to help.)*
  - **Step 2** (`2`): 提交材料/费用: 5× Relief Rations (Meat), 5× Relief Rations (Seafood), 5× Relief Rations (Veggie) *(说明: Wow, they're well stocked. That's helpful, at least.)*

### `MelvorBase:MQ10` — The Path to Melvor / 通往梅尔沃之路
- **前置条件**：Quest:Sand in the Wind(MQ9)
- **提示/推荐要求**：Must be able to defeat a Combat Level 40 Enemy multiple times; Must be able to defeat a Combat Level 42 Enemy multiple times; Must be able to defeat a Combat Level 40 Miniboss; Crafting Lv.45; Woodcutting Lv.45; Beastcrafting Lv.20; Hunting Lv.45; Firemaking Lv.42; Thieving Lv.32
- **奖励**：80,000 Gold Pieces, 2× Careful Extractor Gloves, 250× Mithril Lantern (Lit), 1000× Lockpick, 250× Gold Bar, 150× Salmon; Unlocks the Silted Oasis Combat Area.
  - **Step 0** (`0`): 击杀 1× Silkback Scarab | 通过 Combat 获取 1× Silkback Fibre | 击杀 1× Siltspinner | 通过 Combat 获取 1× Silt Webbing | 通过 Crafting 获取 1× Cacti Filter | 通过 Crafting 获取 1× Filtered Vial → 伐木 1× WarpedCacti | 通过 Woodcutting 获取 1× Warped Cacti Water *(说明: With enough of these, we should be able to make it to Clearwater!)*
  - **Step 1** (`1`): 击杀 1× Dancing Cactus *(说明: That hurt, but I guess it's fair.)*
  - **Step 2** (`2`): 通过 Beastcrafting 获取 1× Stonehopper Gloves → 狩猎捕获 5× StonehopperWhelp *(说明: Individually, these aren't too bad.)*
  - **Step 3** (`3`): 生火燃烧 3× StonehopperSmokeOut *(说明: Hopefully this will persuade them to come out and say hello.)*
  - **Step 4** (`4`): 通过 Crafting 获取 1× Gas Mask → 击杀 1× Mature Stonehopper *(说明: You'd really better taste good.)*
  - **Step 5** (`5`): 窃取 1× CapitalGuard → 通过 Thieving 获取 2× Confiscated Visitor Pass *(说明: I mean... They won't mind if we borrow them, right?)*

### `MelvorBase:MQ11` — The Big City / 大城市
- **前置条件**：Quest:The Path to Melvor(MQ10)
- **提示/推荐要求**：Must be able to defeat a Combat Level 45 Enemy multiple times; Fletching Lv.45; Hunting Lv.45; Herblore Lv.45
- **奖励**：90,000 Gold Pieces, 250× Adamantite Bar, 250× Maple Log; Unlocks the Sea Cliffside Combat Area
  - **Step 0** (`0`): 击杀 10× Sea Griffin *(说明: Here they come!)*
  - **Step 1** (`1`): 通过 Fletching 获取 100× Sea Griffin Arrow → 提交材料/费用: 100× Sea Griffin Arrow *(说明: He'd better win.)*
  - **Step 2** (`2`): 通过 Hunting 获取 4× Toxic Butterfly in a Jar → 通过 Herblore 获取 1× True-Shot Potion | 提交材料/费用: True-Shot Potion *(说明: None of this looks too rare, thankfully.)*

### `MelvorBase:MQ12` — The Ivory Athenaeum / 象牙藏书院
- **前置条件**：Quest:The Big City(MQ11)
- **提示/推荐要求**：Must be able to defeat a Combat Level 45 Enemy multiple times; Must be able to defeat a Combat Level 50 Dungeon Boss; Runecrafting Lv.50; Thieving Lv.50; Smithing Lv.50; Agility Lv.45
- **奖励**：110,000 Gold Pieces; (Coming Soon) Unlocks the Summoning Skill (Coming Soon) Unlocks the Ivory Athenaeum Thieving Location (Summoning themed)
  - **Step 0** (`0`): 通过 Runecrafting 获取 1× Ivory Athenaeum Hood | 通过 Runecrafting 获取 1× Ivory Athenaeum Robe | 通过 Runecrafting 获取 1× Ivory Athenaeum Bottoms → 窃取 1× Archivist | 通过 Thieving 获取 1× Ivory Athenaeum Key → 窃取 1× LowerFloors | 通过 Thieving 获取 1× Map of Lower Floors | 通过 Thieving 获取 1× Abyssal Vault Key Mould *(说明: This mission might be next to impossible.)*
  - **Step 1** (`1`): 通过 Smithing 获取 1× Abyssal Bar | 通过 Smithing 获取 1× Abyssal Vault Key | 窃取 1× AbyssalVaultDoor *(说明: The vision proved particularly useful, didn't it?)*
  - **Step 2** (`2`): 通关 1× AbyssalVault *(说明: I don't think he's going to let us out of here.)*
  - **Step 3** (`3`): 敏捷完成 1× AbyssalVaultEscape *(说明: We need to escape, NOW!)*

### `MelvorBase:MQ13` — Unfortunate Teller / 倒霉的占卜师
- **前置条件**：Quest:The Ivory Athenaeum(MQ12)
- **提示/推荐要求**：Must be able to defeat a Combat Level 55 Enemy multiple times; Hunting Lv.40; Beastcrafting Lv.45; Herblore Lv.45; Firemaking Lv.55
- **奖励**：120,000 Gold Pieces, 100× Reinforced Snare, 100× Reinforced Small Bird Catcher
  - **Step 0** (`0`): 狩猎捕获 1× YellowtailBird | 通过 Hunting 获取 1× Dreamleaf | 通过 Beastcrafting 获取 3× Ground Dreamleaf | 通过 Herblore 获取 1× Seer's Incense → 生火燃烧 1× SeersIncense *(说明: I doubt Talm will let us take his equipment after this. The recipe won't do us any good afterwards.)*
  - **Step 1** (`1`): 击杀 3× Soulsmith Novice | 击杀 3× Soulsmith Whelp *(说明: We can take them.)*

### `MelvorBase:MQ14` — Starlit Sky / 星光满天
- **前置条件**：Quest:Unfortunate Teller(MQ13)
- **提示/推荐要求**：Must be able to defeat a Combat Level 55 Enemy multiple times; AgilityTech:Climbing Lv.20; AgilityTech:Jumping Lv.20; AgilityTech:Evading Lv.20; Astrology Lv.55
- **奖励**：130,000 Gold Pieces, 500× Stardust, 100× Cherry Pie
  - **Step 0** (`0`): 条件:AgilityTech:Climbing Lv.20 | 条件:AgilityTech:Jumping Lv.20 | 条件:AgilityTech:Evading Lv.20 *(说明: I need to improve my techniques.)*
  - **Step 1** (`1`): 击杀 6× Soulsmith Novice | 击杀 6× Soulsmith Whelp *(说明: Not these again...)*
  - **Step 2** (`2`): 占星观测 1× MQ14Ceedyr | 占星观测 1× MQ14Iridan | 占星观测 1× MQ14Syllia *(说明: What does Era wish for us to see?)*

### `MelvorBase:MQ15` — Cold Plunge / 寒地探秘
- **前置条件**：Quest:Starlit Sky(MQ14)
- **提示/推荐要求**：Must be able to defeat a Combat Level 60 Miniboss; Crafting Lv.55; Firemaking Lv.60; Mining Lv.62
- **奖励**：160,000 Gold Pieces, 350× Yew Log, 2500× Coal, 100× Emerald
  - **Step 0** (`0`): 通过 Crafting 获取 1× Warm Hat | 通过 Crafting 获取 1× Warm Body | 通过 Crafting 获取 1× Warm Legs | 通过 Crafting 获取 1× Warm Boots | 通过 Crafting 获取 1× Warm Gloves → 生火燃烧 3× MapleLog → 采矿 1× IcyDoorway → 生火燃烧 3× MahoganyLog → 采矿 1× IcyDoorway → 生火燃烧 3× YewLog → 采矿 1× IcyDoorway *(说明: Melting alone won't do it. We'll need to hack away at the ice, too.)*
  - **Step 1** (`1`): 击杀 1× Ice Yeti *(说明: Good morning?)*

### `MelvorBase:MQ16` — Ship Shape / 修船出海
- **前置条件**：Quest:Cold Plunge(MQ15)
- **提示/推荐要求**：Farming Lv.63; Smithing Lv.65; Crafting Lv.65
- **奖励**：160,000 Gold Pieces, 25× Eagle Nest, 250× Elk Hide, 250× Runite Bar
  - **Step 0** (`0`): 农耕种植 1× Corn | 农耕种植 1× Watermelon | 农耕种植 1× Tomato *(说明: Head inland enough, should be able to grow something.)*
  - **Step 1** (`1`): Smithing 制作 1× ShipKeelRepair *(说明: I'll need to make something that'll help it weather the rocky shore.)*
  - **Step 2** (`2`): Crafting 制作 1× ShipSailRepair *(说明: It can't be that hard, can it?)*

### `MelvorBase:MQ17` — The Sunken Tower / 沉没之塔
- **前置条件**：Quest:Ship Shape(MQ16)
- **提示/推荐要求**：Must be able to defeat a Combat Level 67 Enemy multiple times; Must be able to defeat a Combat Level 69 Pinnacle Boss; AstrologyMajorNodes≥2; Herblore Lv.69; Agility Lv.60; Woodcutting Lv.69; Fishing Lv.69
- **奖励**：300,000 Gold Pieces, 2000× Stardust, 500× Dawnsteel Bar, 400× Lasagna, 100× Sporlyle Herb Seed, 2000× Dawnsteel Arrows, 4000× Decay Rune
  - **Step 0** (`0`): 条件:AstrologyMajorNodes≥2 *(说明: Astrology (Following stars through rocks))*
  - **Step 1** (`1`): 击杀 10× Melee Amalgamation | 击杀 10× Ranged Amalgamation | 击杀 10× Magic Amalgamation *(说明: I think we're doing them a favor.)*
  - **Step 2** (`2`): 通过 Herblore 获取 1× Potion of Rebirth *(说明: This feels... right.)*
  - **Step 3** (`3`): 敏捷完成 1× FleshboundPassage → 敏捷完成 1× SinewChasm *(说明: This is going to be extremely dangerous.)*
  - **Step 4** (`4`): 击杀 1× Talm *(说明: What happened to you..?)*

## Main Quests（10 个）

### `MelvorBase:MQ18` — MQ18
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: What can you tell me? (Divination))*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: Bring me back to what you wish to show me. (Divination))*

### `MelvorBase:MQ19` — MQ19
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: Damn him.)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: The Dreamers... what can they teach me?)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: [Exclusive Content Agility] X/X [Exclusive Content Agility])*
  - **Step 3** (`3`): 提交材料/费用: 1 Gold Pieces *(说明: This 'ought to be good!)*

### `MelvorBase:MQ20` — MQ20
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: I've got first watch, I suppose.)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: It looks like a whole pack has come out to greet us.)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: This is some of the toughest meat I've worked with...)*
  - **Step 3** (`3`): 提交材料/费用: 1 Gold Pieces *(说明: It's almost like they don't want people coming up here!)*

### `MelvorBase:MQ21` — MQ21
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: It it a pull door..?)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: Let's see what this dwarf has got!)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: Don't have much of a choice now, do we?)*
  - **Step 3** (`3`): 提交材料/费用: 1 Gold Pieces *(说明: Nothing too scary, then?)*

### `MelvorBase:MQ22` — MQ22
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: I've been working on the Stoneeeeway~)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: Come on... Someone has to be out of place...)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: Oh no...)*

### `MelvorBase:MQ23` — MQ23
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: I'll focus on breaking up the bigger rocks.)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: Please don't blow up, please don't blow up, please don't blo-)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: They have to pay.)*

### `MelvorBase:MQ24` — MQ24
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: Her house simply isn't enough. How about a proper cookhouse? Maybe she'll be able to get some help then, too?)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: Do we just plant these in the ground or..?)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: This feels a little unorthodox.)*
  - **Step 3** (`3`): 提交材料/费用: 1 Gold Pieces *(说明: A bit of this, and a bit of that...)*

### `MelvorBase:MQ25` — MQ25
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: Should come to a point here, maybe a handle made of...)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: I wonder how Flamberian bodies handle stardust...)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: Just... stay still!)*

### `MelvorBase:MQ26` — MQ26
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: What's going on up here?)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: Who's a good monster?)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: I feel soggy and miserable.)*
  - **Step 3** (`3`): 提交材料/费用: 1 Gold Pieces *(说明: They started it.)*
  - **Step 4** (`4`): 提交材料/费用: 1 Gold Pieces *(说明: Has to be what we're looking for.)*

### `MelvorBase:MQ27` — MQ27
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: 1 Gold Pieces *(说明: I can't wait to see them in action.)*
  - **Step 1** (`1`): 提交材料/费用: 1 Gold Pieces *(说明: I just have to reel them in before the shadows do.)*
  - **Step 2** (`2`): 提交材料/费用: 1 Gold Pieces *(说明: Today, the beast dies.)*

## Side Quests（10 个）

### `MelvorBase:SeedsofSustenance` — Seeds of Sustenance / 维持生计的种子
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Only the best for Lira. I'll need to harvest some fresh produce, then hand it over.)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: It's a large fireplace, it'll take a lot of wood to fill, and a decent bit of effort to get it all burning.)*

### `MelvorBase:PackingLunch` — Packing Lunch / 准备午餐
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: What would a man like him even like? Fish, maybe?)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I need to make sure the bolts are well-made so they'll hold a man of that size, only way to do that is to do it myself.)*

### `MelvorBase:MorallyEthicalGraverobbing` — Morally Ethical Graverobbing / 合乎伦理的盗墓
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Some steel for the heads, and willow logs for the shafts should be plenty strong enough.)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I have the feeling I'm going to be here a while...)*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Morlin really isn't holding back on asking away, is he?)*

### `MelvorBase:LifesABeach` — Life's A Beach / 海滩生活
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Maybe I'll reel it in, whatever it is.)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I wonder if I should toss him back..?)*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: How do people even use these things?)*

### `MelvorBase:MageintheMaking` — Mage in the Making / 法师觉醒
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I'm sure nothing could possibly go wrong arming her.)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I never fancied myself a teacher, but let's take a stab at it.)*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Maybe she'll be a bit better now?)*
  - **Step 3** (`3`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Surely... Surely, teaching her to arm herself can't go wrong.)*

### `MelvorBase:HarvestFestival` — Harvest Festival / 丰收节
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I'm not too sure how much they'll need... let's get quite a bit.)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I'm going to need you to grow... a bit faster.)*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Take one, cook it, pass it on. Take on, cook it, pass it on...)*
  - **Step 3** (`3`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I hope these turn out nicely.)*

### `MelvorBase:SettingDownRoots` — Setting Down Roots / 落地生根
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I wonder what ever happened to this place?)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I wonder what Clove might find for dinner.  X/X [Steel Arrows] - [Fletching Tab Link])*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: This stuff grows worse than most weeds.  X/X [Ambervine Bush] - [Woodcutting Tab Link])*
  - **Step 3** (`3`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Hopefully Petal hasn't severely poisoned the ground.  X/X  - [Farming Tab Link])*

### `MelvorBase:AGlitteringTrap` — A Glittering Trap / 闪光的陷阱
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Let's make sure these are sturdy enough to handle... whatever we're fighting.  X/X [Rune Spear] - [Fletching Tab Link])*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I feel like I'm arming Gol for war, I hope whatever it is goes down quickly.  X/X [Rune Javelin] - [Fletching Tab Link])*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Let's hope that payout is as good as he claims.)*
  - **Step 3** (`3`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I'll make this extra messy, so it glows even brighter.)*

### `MelvorBase:TheFourSentries` — The Four Sentries / 四位哨兵
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: They're asking for stuff without even seeing me, now...)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: I'm sure this is what I need to do.)*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: At least I know I'm helping her confidence.)*
  - **Step 3** (`3`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: You know, that old man makes a good point...)*

### `MelvorBase:TheSealedGrove` — The Sealed Grove / 被封印的树林
- **奖励**：69 Gold Pieces
  - **Step 0** (`0`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: These trees almost grow faster than I can chop them down!)*
  - **Step 1** (`1`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Surely Gol will be able to handle... Whatever is in this forest.)*
  - **Step 2** (`2`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: This bait positively reeks, but that's kind of the point.)*
  - **Step 3** (`3`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: Rosemary is normally pretty pleasant. Will it really repel monsters with Petal's solution?)*
  - **Step 4** (`4`): 提交材料/费用: Normal Log → 提交材料/费用: Oak Log *(说明: The runes the old man is asking for... This barrier seems like some nasty work.)*
