# 『記憶の遺跡』 v1 実装

ルールブック（`記憶の遺跡_ルールブック.md`）の **v1スコープ** をUnity 6 (C#) で実装したもの。

## 遊び方
1. Unityエディタにフォーカスを当てる（スクリプトが自動コンパイルされる）。
2. 任意のシーンで **Play ▶** を押すだけ。`GameUI` が `RuntimeInitializeOnLoadMethod` で
   自動的にCanvas・EventSystemを生成するので、シーン編集は不要。
3. 「あなた vs AI」のローカル対戦が始まる。

### 操作
- **手札カードをクリック**：プレイ（守護者は召喚／魔法・魔法石は即解決）。
  対象が必要な効果は15-4の「デフォルト対象」を自動採用（タップ操作を単純化）。
- **自分の場の守護者をクリック**：選択。右下に「技」「攻撃」ボタンが出る。
  - 技：`TechniqueActivator.TryActivate` を呼ぶ（16章）。
  - 攻撃：相手守護者をクリックして対象指定、または「本体を直接攻撃」。
- **刻む**：ボタンを押してから捧げる手札をクリック（7-3）。
- **ターン終了**：終了フェイズ（風化処理）→AIの番が自動進行。

## 実装済み（v1）
| 章 | 内容 | 実装ファイル |
|---|---|---|
| 6-12 | コア対戦ロジック（5フェイズ／共有遺構デッキ／風化／転生／刻む／勝利条件3種） | `GameEngine.cs` `GameTypes.cs` |
| 13 | 固定48枚（守護者30＋魔法14＋魔法石4） | `CardDatabase.cs` |
| 15-4 | 効果解決（全EffectId、自動対象） | `EffectResolver.cs` |
| 16 | タップ技発動（集約点 `TechniqueActivator`） | `GameEngine.cs` |
| 17 | ヒューリスティックAI | `AIController.cs` |
| 14 | 枠（系統別5色）＋イラスト窓（単色プレースホルダー）UI | `GameUI.cs` |

## 未実装（理由あり）
- **18章 オンライン対戦**：Netcode for GameObjects パッケージ追加＋Unity Dashboard での
  Authentication/Relay 有効化（手作業設定）が前提のため未着手。設計方針と手順は `OnlineStub.cs` に記載。
  `GameEngine` の行動メソッドをホスト権威でラップする設計になっているので差し込み可能。
- **v2機能（写し身生成15章・音声詠唱16-5章）**：スコープ外。`CardInstance` は将来の
  差し込みを想定した構造のまま（v1では固定カードのみ使用）。

## 設計メモ
- カードは ScriptableObject ではなくコード定義（`CardDatabase`）にした。
  ヘッドレスで自動生成でき、`.asset` の手作業作成が不要なため。必要なら後でSO化可能。
- 日本語表示：`Font.CreateDynamicFontFromOSFont`（Yu Gothic等のOSフォント）を実行時ロード。
- イラスト差し替え：`GameUI.MakeCard` の "Art" 子オブジェクト（現状は単色）を
  `Assets/Resources/CardArt/guardian_001.png` 等に差し替える想定（14-3）。
