# Mining（采矿）

> 实体 ID：`MelvorBase:Mining` | 组件：`Action` | 矿点：28
> **重要机制**：采矿每次挥镐耗时 `BaseInterval`，但**只有 `BaseProductChance` 的概率敲出矿石**（期望单矿耗时 ≈ `interval / chance`，实际受工具与全局加成提升）。

| ID | 名称 | 分类 | 等级 | 经验 | 挥镐间隔(s) | 破岩率 | 期望单矿(s) | 产物 | 额外/稀有掉落 |
|---|---|---|---|---|---|---|---|---|---|
| `MelvorBase:Stone` | Stone | Ore | 1 | 3 | 1.5 | 40.0% | 3.8 | Stone |  |
| `MelvorBase:Clay` | Clay | Ore | 3 | 4 | 1.5 | 33.3% | 4.5 | Clay |  |
| `MelvorBase:CopperOre` | Copper Ore | Ore | 5 | 6 | 1.5 | 30.0% | 5.0 | Copper Ore | Geode (0.25%) |
| `MelvorBase:RuneEssence` | Rune Essence | Essence | 8 | 5 | 1.5 | 33.3% | 4.5 | 2× Rune Essence |  |
| `MelvorBase:IronOre` | Iron Ore | Ore | 10 | 10 | 1.5 | 24.0% | 6.2 | Iron Ore | Geode (0.25%) |
| `MelvorBase:StoneDebris` | Stone Debris | Quests | 10 | 25 | 1.5 | 2.0% | 75.0 | 20× Stone |  |
| `MelvorBase:Topaz` | Topaz Vein | Gem | 12 | 29 | 1.5 | 8.0% | 18.8 | Uncut Topaz |  |
| `MelvorBase:SilverOre` | Silver Ore | Ore | 18 | 12 | 1.5 | 22.0% | 6.8 | Silver Ore | Geode (0.25%) |
| `MelvorBase:Sulfur` | Sulfur | Quests | 18 | 13 | 1.5 | 10.0% | 15.0 | Sulfur |  |
| `MelvorBase:Coal` | Coal | Ore | 25 | 10 | 1.5 | 33.0% | 4.5 | Coal |  |
| `MelvorBase:FallenStar` | Fallen Star | Ore | 30 | 13 | 1.5 | 25.0% | 6.0 | Stardust |  |
| `MelvorBase:Sapphire` | Sapphire Vein | Gem | 32 | 46 | 1.5 | 7.0% | 21.4 | Uncut Sapphire |  |
| `MelvorBase:MithrilOre` | Mithril Ore | Ore | 35 | 22 | 1.5 | 15.0% | 10.0 | Mithril Ore | Geode (0.25%) |
| `MelvorBase:GoldOre` | Gold Ore | Ore | 38 | 20 | 1.5 | 18.0% | 8.3 | Gold Ore | Geode (0.25%) |
| `MelvorBase:AttunedEssence` | Attuned Essence | Essence | 42 | 13 | 1.5 | 20.0% | 7.5 | Attuned Essence |  |
| `MelvorBase:AdamantiteOre` | Adamantite Ore | Ore | 45 | 34 | 1.5 | 12.5% | 12.0 | Adamantite Ore | Geode (0.25%) |
| `MelvorBase:Ruby` | Ruby Vein | Gem | 47 | 92 | 1.5 | 5.0% | 30.0 | Uncut Ruby |  |
| `MelvorBase:GoldenFallenStar` | Golden Fallen Star | Ore | 50 | 24 | 1.5 | 20.0% | 7.5 | Golden Stardust |  |
| `MelvorBase:RuniteOre` | Runite Ore | Ore | 55 | 50 | 1.5 | 10.0% | 15.0 | Runite Ore | Geode (0.25%) |
| `MelvorBase:IcyDoorway` | Icy Doorway | Quests | 62 | 200 | 1.5 | 1.0% | 150.0 | Stone |  |
| `MelvorBase:Emerald` | Emerald Vein | Gem | 65 | 153 | 1.5 | 4.0% | 37.5 | Uncut Emerald |  |
| `MelvorBase:DawnsteelOre` | Dawnsteel Ore | Ore | 70 | 62 | 1.5 | 9.0% | 16.7 | Dawnsteel Ore | Geode (0.25%) |
| `MelvorBase:MeteoriteOre` | Meteorite Ore | Ore | 75 | 64 | 1.5 | 8.0% | 18.8 | Meteorite Ore | Scorched Geode (0.25%) |
| `MelvorBase:DenseCoal` | Dense Coal | Ore | 80 | 49 | 1.5 | 24.0% | 6.2 | 3× Coal |  |
| `MelvorBase:PureEssence` | Pure Essence | Essence | 80 | 29 | 1.5 | 20.0% | 7.5 | 2× Pure Essence |  |
| `MelvorBase:DragoniteOre` | Dragonite Ore | Ore | 85 | 93 | 1.5 | 7.0% | 21.4 | Dragonite Ore | Geode (0.25%) |
| `MelvorBase:Diamond` | Diamond Vein | Gem | 90 | 210 | 1.5 | 3.0% | 50.0 | Uncut Diamond |  |
| `MelvorBase:Jadestone` | Jadestone Vein | Gem | 97 | 333 | 1.5 | 2.0% | 75.0 | Uncut Jadestone |  |
