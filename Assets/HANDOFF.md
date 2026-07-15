# 引き継ぎメモ（マイモン / kioku-no-iseki）

別PCで作業を継続するための情報。最終更新: 2026-07-15。
リポジトリ: https://github.com/ryuryunwn-source/kioku-no-iseki

---

## 1. これは何のプロジェクト？
Unity 製の1対1対戦カードゲーム『マイモン』（旧『記憶の遺跡』）。
- 対戦モード: オフライン(AI) / オンライン(Relay + Netcode)
- 写真からカードを生成する「写し身（マイモン）」機能あり
- UIは**シーンに置かず全てコードで生成**（GameUI / OnlineController / SettingsOverlay 等が起動時に自動生成）
- 音声操作(「いけ」「くらえ」「ターンエンド」)は**Windows専用**（Androidでは#ifで無効化済み）

## 2. 開き方
- **Unity バージョンは `6000.4.4f1`**（同じHubのEditorで開くこと）
- `git clone` 後、Unity Hub で「開く」→ プロジェクトフォルダを指定
- 初回はLibrary再生成で少し時間がかかる

## 3. APIキー（重要・Gitに入っていない）
セキュリティのため `Assets/Resources/writeshi_api.txt` は**gitignore**されている。
写し身（写真カード生成）を使うなら、別PCでこのファイルを手動で作成する:
- 1行目: Google Vision APIキー
- 2行目: Groq APIキー
（キーが無くても対戦自体は動く。写し身の命名/生成だけ無効）

## 4. ビルド手順
メニュー **Kioku → Build Android APK / Build Windows EXE**、またはバッチ:
```
Unity.exe -batchmode -quit -projectPath <proj> -buildTarget Android -executeMethod BuildScript.BuildAndroid -logFile <log>
Unity.exe -batchmode -quit -projectPath <proj> -buildTarget Win64  -executeMethod BuildScript.BuildWindows -logFile <log>
```
出力: `Build/Android/KiokuNoIseki.apk` / `Build/Windows/KiokuNoIseki.exe`
（Windowsは exe 単体でなく **Windowsフォルダごと**配布すること）

### Androidビルドの落とし穴（★このPC固有。別PCのパスがASCIIなら不要な対処あり）
1. **プロジェクトの実パスに日本語があるとAndroidは失敗する**（Gradleが非ASCIIパス不可）。
   → ASCIIのジャンクションを作り、そのパス経由でビルドする:
   `New-Item -ItemType Junction -Path C:\Users\<user>\mymon -Target "<日本語を含む実パス>"`
   別PCのプロジェクトパスが英数字だけなら**この対処は不要**、直接ビルドでOK。
2. **NDK未導入だと失敗**（`android-ndk-r27c`）。Hub CLIで追加:
   `"Unity Hub.exe" -- --headless install-modules --version 6000.4.4f1 -m android-sdk-ndk-tools --childModules`
3. Editorが「NDK未検出」をキャッシュする件は、`BuildScript.EnsureAndroidToolPaths()` が
   毎回ビルド前に同梱SDK/NDK/JDKパスを設定して回避済み（コード側で対策済み）。

## 5. 直近の変更（最新コミット順）
- オンライン: 攻撃/技も「不可なら理由をグレー表示」に統一（攻撃済み/ゲージ不足/技使用済み）
- オンライン: 生贄済みのターンは生贄ボタンをグレー表示（1ターン1回）
- オンライン: カード詳細パネルを画面左端へ移動（カードに被って押せない問題を解消）
- オンライン: Netcodeのシーン管理を無効化（接続時のSubstring例外を解消）
- AudioListener欠如の警告を解消（AudioManagerが保証用リスナーを付与）
- Android対応: 同梱SDK/NDK/JDKパスをビルド前に明示設定

## 6. 残タスク / 既知の課題
- [ ] PC版(.exe)を最新コードで再ビルド（オンライン修正を反映した配布物）
- [ ] マルチプレイヤーPlayModeの Player2 で出ていたネイティブクラッシュ
      （`imeLogCallback` / BurstRuntime系）が、シーン管理無効化後も再発するか要確認
- [ ] スマホ実機での画面レイアウト微調整（縦横比ごと）
- [ ] オン라인は**ホスト/クライアント両方を同じバージョン**にすること（プロトコル整合）

## 7. 設計方針メモ
- 勝敗はHP削り主軸（ビジョンA）。「記憶領域リワーク」は不採用で撤去済み。
- UI/機能変更は**オフラインとオンライン両モードを必ず両方**更新する。
- 動作確認/見た目チェックはユーザーが自分で行う。
