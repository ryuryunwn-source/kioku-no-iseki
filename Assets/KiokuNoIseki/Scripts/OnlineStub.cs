namespace KiokuNoIseki
{
    // 18章 オンライン対戦（Unity Relay + Netcode for GameObjects）について
    //
    // v1のこのビルドでは未実装。理由：
    //  - com.unity.netcode.gameobjects パッケージの追加が必要
    //  - Unity Dashboard で Authentication / Relay サービスの有効化が必要（プロジェクトのリンク）
    // これらはエディタ／ダッシュボード側の手作業設定を伴うため、コードだけでは完結できない。
    //
    // 実装方針はルールブック18章のとおり「ホスト権威モデル」を採用する：
    //  - 正規の状態（GameEngine）はホスト側のみが保持
    //  - クライアントは ServerRpc で操作要求を送り、ホストが GameEngine の行動メソッドを実行
    //  - 相手の手札・遺構デッキの内容は非所有クライアントへ送らない（枚数のみ同期）
    //
    // GameEngine.PlayCard / Attack / Inscribe / TechniqueActivator.TryActivate を
    // そのままホスト上で呼ぶラッパとして NetworkGameController を実装すればよい設計になっている。
    //
    // 追加手順（オンラインを有効化する場合）:
    //  1. Package Manager で Netcode for GameObjects と Multiplayer Services (Relay/Auth) を追加
    //  2. Unity Dashboard でプロジェクトをリンクし Authentication・Relay を有効化
    //  3. NetworkGameController（NetworkBehaviour）を作成し GameEngine をラップ
}
