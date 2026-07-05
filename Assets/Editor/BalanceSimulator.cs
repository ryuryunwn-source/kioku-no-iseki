using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using KiokuNoIseki;

// メニュー「Kioku / Run Balance Simulation」で AI vs AI を大量に自動対戦し、
// 勝利条件の内訳・平均ターン数・先攻勝率・カード別勝率などを集計して
// プロジェクト直下 BalanceReport.txt に出力する（画面なし・高速）。
//
// 目的：感想ではなくデータでバランスを評価する（盟約が飾りになっていないか等）。
public static class BalanceSimulator
{
    const int Games = 200;          // 対戦回数
    const int MaxPlayerTurns = 200; // 1試合の手番数上限（無限ループ保険）

    // 盟約パラメータ（破壊回数×必要体数）を総当たりで回し、盟約勝率を比較する。
    [MenuItem("Kioku/Sweep Pact Balance")]
    public static void SweepPact()
    {
        int origEntry = GameEngine.MemoryEntryEngraving;
        int origCount = GameEngine.PactWinCount;
        int origSurv = GameEngine.EngraveOnSurvive;
        bool origBank = GameEngine.BankToOwner;

        var sb = new StringBuilder();
        sb.AppendLine("=== 古き盟約 パラメータ探索（各設定で200戦） ===");
        sb.AppendLine("目標：盟約勝率が5〜10%に乗り、HP勝利が主軸のまま（盟約が現実的な第2の道になる）。");
        sb.AppendLine("成熟経路：盤面で生き延びた守護者が刻印を蓄積し、完全刻印で死ぬと持ち主の記憶領域へ。");
        sb.AppendLine();
        sb.AppendLine("┌生存刻印┬破壊回数┬必要体数┬─盟約勝率─┬─HP勝率─┬─枯渇─┬─リーチ─┬平均T─");

        int[] survs = { 0, 1 };        // 1ターン生存で得る刻印
        int[] entries = { 3, 4, 5 };   // 完全刻印（記憶領域入り）までの刻印数
        int[] counts = { 2, 3 };       // 盟約に必要な体数
        GameEngine.BankToOwner = true; // 自分で狙える経路として持ち主banking
        foreach (int surv in survs)
        foreach (int entry in entries)
        foreach (int count in counts)
        {
            GameEngine.EngraveOnSurvive = surv;
            GameEngine.MemoryEntryEngraving = entry;
            GameEngine.PactWinCount = count;

            int pact = 0, hp = 0, exhaust = 0, reach = 0, tsum = 0;
            for (int gi = 0; gi < Games; gi++)
            {
                var g = new GameEngine(seed: gi + 1);
                g.NewGame(true);
                bool r = false; int safety = 0;
                while (g.result == GameResult.Ongoing && safety++ < MaxPlayerTurns)
                {
                    AIController.TakeTurn(g);
                    if (PactCount(g.players[0]) >= count - 1 || PactCount(g.players[1]) >= count - 1) r = true;
                }
                tsum += g.turnNumber;
                if (r) reach++;
                if (g.players[0].hp <= 0 || g.players[1].hp <= 0) hp++;
                else if (PactCount(g.players[0]) >= count || PactCount(g.players[1]) >= count) pact++;
                else if (g.deck.Count == 0) exhaust++;
            }
            sb.AppendLine($"│  {surv,2}    │  {entry,2}    │  {count,2}体  │  {Pct(pact, Games),6}  │ {Pct(hp, Games),6} │{Pct(exhaust, Games),5} │ {Pct(reach, Games),6} │ {(double)tsum / Games:0.0}");
        }
        sb.AppendLine("└────────┴────────┴────────┴──────────┴────────┴──────┴────────┴──────");
        sb.AppendLine();
        sb.AppendLine("※ 現行は 生存刻印0・4回・3体＝盟約0%。生存刻印1にすると自分で成熟を狙える。");
        sb.AppendLine("※ 盟約5〜10%かつHP勝利が主軸(80%以上)を保つ行が理想。");

        GameEngine.MemoryEntryEngraving = origEntry;
        GameEngine.PactWinCount = origCount;
        GameEngine.EngraveOnSurvive = origSurv;
        GameEngine.BankToOwner = origBank;

        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../PactSweep.txt"));
        System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[BalanceSimulator] 盟約探索完了。\n\n" + sb);
        EditorUtility.RevealInFinder(path);
    }

    [MenuItem("Kioku/Run Balance Simulation")]
    public static void Run()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        int hpWins = 0, pactWins = 0, exhaustWins = 0, timeouts = 0;
        int firstPlayerWins = 0;
        int reachGames = 0; // 盟約リーチ(2体)が一度でも発生した試合数
        var turnCounts = new List<int>();

        // カード別：勝者側/敗者側の盤面に出現した回数
        var cardWin = new Dictionary<string, int>();
        var cardLose = new Dictionary<string, int>();
        var cardName = new Dictionary<string, string>();

        for (int gi = 0; gi < Games; gi++)
        {
            var g = new GameEngine(seed: gi + 1);
            g.NewGame(player1IsAI: true); // 両者ともAIとして回す（人間入力なし）

            var seen0 = new HashSet<string>();
            var seen1 = new HashSet<string>();
            bool reached = false;
            int safety = 0;

            while (g.result == GameResult.Ongoing && safety++ < MaxPlayerTurns)
            {
                AIController.TakeTurn(g);

                RecordBoard(g.players[0], seen0, cardName);
                RecordBoard(g.players[1], seen1, cardName);
                int reachAt = GameEngine.PactWinCount - 1;
                if (PactCount(g.players[0]) >= reachAt || PactCount(g.players[1]) >= reachAt) reached = true;
            }

            turnCounts.Add(g.turnNumber);
            if (reached) reachGames++;

            // 勝敗の分類
            string outcome;
            if (g.result == GameResult.Ongoing) { timeouts++; outcome = "timeout"; }
            else if (g.players[0].hp <= 0 || g.players[1].hp <= 0) { hpWins++; outcome = "hp"; }
            else if (PactCount(g.players[0]) >= GameEngine.PactWinCount || PactCount(g.players[1]) >= GameEngine.PactWinCount) { pactWins++; outcome = "pact"; }
            else if (g.deck.Count == 0) { exhaustWins++; outcome = "exhaust"; }
            else { timeouts++; outcome = "unknown"; }

            if (g.result == GameResult.Player0Win) firstPlayerWins++;

            // カード別集計（勝者側で見たカード=勝ち、敗者側=負け）
            if (g.result != GameResult.Ongoing && outcome != "unknown")
            {
                bool p0won = g.result == GameResult.Player0Win;
                Tally(p0won ? seen0 : seen1, cardWin);
                Tally(p0won ? seen1 : seen0, cardLose);
            }
        }

        sw.Stop();
        string report = BuildReport(hpWins, pactWins, exhaustWins, timeouts, firstPlayerWins,
            reachGames, turnCounts, cardWin, cardLose, cardName, sw.ElapsedMilliseconds);

        string path = System.IO.Path.Combine(Application.dataPath, "../BalanceReport.txt");
        path = System.IO.Path.GetFullPath(path);
        System.IO.File.WriteAllText(path, report, new UTF8Encoding(false));
        Debug.Log($"[BalanceSimulator] {Games}戦完了（{sw.ElapsedMilliseconds}ms）。レポート: {path}\n\n" + report);
        EditorUtility.RevealInFinder(path);
    }

    static void RecordBoard(RecallerState p, HashSet<string> seen, Dictionary<string, string> names)
    {
        foreach (var c in p.board)
        {
            seen.Add(c.definition.id);
            names[c.definition.id] = c.definition.trueName;
        }
    }

    static void Tally(HashSet<string> ids, Dictionary<string, int> dst)
    {
        foreach (var id in ids) dst[id] = dst.TryGetValue(id, out var v) ? v + 1 : 1;
    }

    static int PactCount(RecallerState p) => p.memoryZone.Count(c => c.engravingCount >= 3);

    static string BuildReport(int hp, int pact, int exhaust, int timeout, int firstWins,
        int reachGames, List<int> turns, Dictionary<string, int> win, Dictionary<string, int> lose,
        Dictionary<string, string> names, long ms)
    {
        int total = Games;
        var sb = new StringBuilder();
        sb.AppendLine("=== 記憶の遺跡 バランスシミュレーション ===");
        sb.AppendLine($"対戦数: {total}（AI vs AI, 各試合 固定シード）  所要: {ms}ms");
        sb.AppendLine();
        sb.AppendLine("■ 勝利条件の内訳");
        sb.AppendLine($"  HP削り勝利   : {hp,4}  ({Pct(hp, total)})");
        sb.AppendLine($"  古き盟約勝利 : {pact,4}  ({Pct(pact, total)})   ← 5〜10%が理想。0%なら飾り");
        sb.AppendLine($"  遺構枯渇勝利 : {exhaust,4}  ({Pct(exhaust, total)})");
        sb.AppendLine($"  引き分け/上限: {timeout,4}  ({Pct(timeout, total)})");
        sb.AppendLine();
        sb.AppendLine("■ 盟約の脅威度");
        sb.AppendLine($"  リーチ(完全刻印2体)が発生した試合: {reachGames}  ({Pct(reachGames, total)})   ← 50%前後だと駆け引きが機能");
        sb.AppendLine();
        sb.AppendLine("■ テンポ");
        turns.Sort();
        sb.AppendLine($"  平均ターン数: {turns.Average():0.0}  中央値: {turns[turns.Count / 2]}  最短: {turns.First()}  最長: {turns.Last()}");
        sb.AppendLine($"  （目安: 10〜15ターン。短すぎ=大味 / 長すぎ=だれる）");
        sb.AppendLine();
        sb.AppendLine("■ 先攻/後攻");
        sb.AppendLine($"  先攻(player0)勝率: {Pct(firstWins, total)}   ← 55%超なら先攻有利ゲー");
        sb.AppendLine();
        sb.AppendLine("■ カード別 勝率（そのカードが自分の盤面に出た試合の勝率。出現数が少ないものは参考値）");
        sb.AppendLine("   勝率が高い=強い / 低い=弱い or 使いづらい。50%から大きく外れるカードが調整候補。");
        sb.AppendLine("   ┌─勝率─┬─出現─┬─カード────");

        var ids = win.Keys.Union(lose.Keys).ToList();
        var rows = new List<(string id, double rate, int n)>();
        foreach (var id in ids)
        {
            int w = win.TryGetValue(id, out var wv) ? wv : 0;
            int l = lose.TryGetValue(id, out var lv) ? lv : 0;
            int n = w + l;
            if (n == 0) continue;
            rows.Add((id, (double)w / n, n));
        }
        foreach (var r in rows.OrderByDescending(r => r.rate))
        {
            string nm = names.TryGetValue(r.id, out var s) ? s : r.id;
            sb.AppendLine($"   │ {r.rate * 100,4:0}% │ {r.n,4} │ {r.id} {nm}");
        }
        sb.AppendLine("   └──────┴──────┴───────────");
        sb.AppendLine();
        sb.AppendLine("※ カード勝率は「盤面に出た試合の勝敗」の近似（プレイ履歴の厳密な追跡ではない）。");
        sb.AppendLine("※ AIはヒューリスティックのため、読み合いの深さまでは測れない。構造的バランスの一次評価として使うこと。");
        return sb.ToString();
    }

    static string Pct(int n, int total) => total == 0 ? "0%" : $"{100.0 * n / total:0.0}%";
}
