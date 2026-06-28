# 守護者30種 カードイラスト生成プロンプト集

ルールブック14-2の方針（30枚で画風を統一）に沿った、画像生成AI用プロンプト。
生成した画像は `Assets/Resources/CardArt/guardian_001.png 〜 guardian_030.png` に配置すると
ゲーム内の枠（イラスト窓）に自動でハマります（コード側実装済み・正方形512×512px推奨）。

## 画風を揃えるための共通スタイル（全カードに付ける）

すべてのプロンプトの末尾に、同じ**スタイル指定**を付けてください。これで30枚の画風が揃います。

```
STYLE: ancient lost civilization relic, weathered stone-tablet texture, muted earthy tones,
semi-realistic painterly fantasy, single centered subject, dramatic rim lighting, dark background,
trading card art, highly detailed, square composition
```

- **Midjourney**：各プロンプト末尾に ` --ar 1:1 --sref <アンカー画像URL>` を付け、30枚すべてで同じ `--sref` を使い回す（色味・質感だけ統一され、キャラは各自変わる）。
- **DALL·E / SD**：上記STYLE文を毎回そのまま末尾に付ける。

系統ごとの配色（プロンプトに含めると統一感UP）:
- 焔=warm reds & oranges, ember glow ／ 森=mossy greens & earthy brown ／ 流=teal & deep blue, water reflections ／ 光=warm gold & white, soft radiance ／ 影=deep purple & indigo, shadow

---

## プロンプト一覧

| ファイル名 | カード名（系統） | 主題プロンプト（+共通STYLE） |
|---|---|---|
| guardian_001 | 焔の偵察兵（焔） | a nimble fire scout with glowing ember sparks, light leather armor, agile pose |
| guardian_002 | 灰塵の狩人（焔） | a hunter cloaked in ash and cinders, drawing a smoldering bow |
| guardian_003 | 焼け跡の番犬（焔） | a charred guard dog wreathed in embers, snarling, glowing cracks in its hide |
| guardian_004 | 灼熱の剣士（焔） | a swordsman wielding a molten glowing blade, heat haze around him |
| guardian_005 | 炎纏う巨兵（焔） | a towering armored giant wrapped in roaring flames |
| guardian_006 | 終焔の王（焔） | a regal king of ending fire, crown and mantle of living flame |
| guardian_007 | 苔むす守り手（森） | a stone defender covered in thick moss, shield raised, calm |
| guardian_008 | 若木の射手（森） | a young archer made of saplings, firing a leaf-fletched arrow |
| guardian_009 | 蔓巻きの罠師（森） | a trapper entangled in living vines, setting a thorn snare |
| guardian_010 | 古木の賢者（森） | an elder sage merged with an ancient gnarled tree, glowing eyes |
| guardian_011 | 森林の巨人（森） | a colossal forest giant of bark, leaves and roots, towering |
| guardian_012 | 千年樹の化身（森） | a luminous avatar of a thousand-year world tree, blossoms |
| guardian_013 | 水面の斥候（流） | a light scout skimming across a water surface, ripples trailing |
| guardian_014 | 潮渡りの剣士（流） | a swordsman walking across crashing tides, water blade |
| guardian_015 | 渦巻く影（流） | a whirlpool wraith, a swirling shadow of dark water |
| guardian_016 | 氷河の守護者（流） | an icy glacier guardian of jagged blue ice, frost aura |
| guardian_017 | 深淵の漂流者（流） | a drifter of the deep-sea abyss, bioluminescent, eerie calm |
| guardian_018 | 大海の支配者（流） | a majestic ruler of the great ocean, crown of coral and pearls |
| guardian_019 | 灯火の巫女（光） | a gentle priestess cupping a small sacred flame, soft glow |
| guardian_020 | 導きの旅人（光） | a hooded traveler holding a guiding lantern of light |
| guardian_021 | 聖なる番兵（光） | a radiant holy sentinel with a glowing shield, stoic |
| guardian_022 | 黎明の祈祷師（光） | a dawn prayer-mage kneeling in soft morning light |
| guardian_023 | 神域の守護騎士（光） | a holy knight guarding a sanctuary gate, golden armor |
| guardian_024 | 天啓の大司祭（光） | a high priest bathed in descending heavenly light, arms raised |
| guardian_025 | 闇に潜む者（影） | a sinister figure lurking in deep shadow, glowing eyes only |
| guardian_026 | 呪縛の使い魔（影） | a cursed familiar wrapped in binding shadow chains |
| guardian_027 | 影渡りの刺客（影） | a shadow-walking assassin mid-leap, twin dark daggers |
| guardian_028 | 忘却の番人（影） | a hooded warden of forgotten memories, fading silhouette |
| guardian_029 | 終夜の伯爵（影） | a vampiric count of eternal night, elegant dark cloak |
| guardian_030 | 深淵より来たる者（影） | an eldritch being rising from the abyss, tentacled, ominous |

---

## 取り込み手順（画像ができたら）※フォルダ・自動取り込みは設定済み

1. 生成画像を `guardian_001.png`〜`guardian_030.png` の名前にする（13章の並び順＝この表の順）。
2. `Assets/Resources/CardArt/` フォルダに**ドロップするだけ**。
   → 専用インポーター(`Assets/Editor/CardArtImporter.cs`)が自動で Sprite 化するので、手動の取り込み設定は不要。
3. Play すると自動で枠にハマる（`GetCardArt` がこの名前を読みに行く）。

※ 画像が無い分は、カードIDから生成される「系統カラーの抽象エンブレム」のままになります（混在OK）。

### 形・解像度のおすすめ
- **横長 4:3** がイラスト窓にきれいに収まります（窓が横長のため）。Midjourneyなら `--ar 4:3`。
- 正方形でもOK（その場合はアスペクト維持で枠内に収めて表示＝左右に石板の余白）。
- 512px以上・PNG。
