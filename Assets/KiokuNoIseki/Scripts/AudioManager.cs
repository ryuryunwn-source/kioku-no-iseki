using UnityEngine;
using System.Collections.Generic;

namespace KiokuNoIseki
{
    // BGM・効果音を一元管理する常駐マネージャ（自動起動）。
    //  - BGMは Resources/Audio/ から読み込み、タイトル/戦闘で切り替える（同じ曲なら鳴らし直さない）。
    //  - 実際の音量 = ユーザー音量(0〜1) × トラック係数。戦闘BGMは係数0.7（同じ%でも30%小さく）。
    //  - ユーザー音量0%＝完全無音。
    //  - 効果音は該当ファイルがあれば鳴る。無ければ黙って無視（後から追加可能）。
    // 使い方：AudioManager.Title() / AudioManager.Battle() / AudioManager.Sfx("sfx_attack")
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        AudioSource bgm;
        AudioSource sfx;
        string currentBgm;

        float bgmUserVol = 0.5f;   // 設定画面のスライダー値（0〜1）
        float bgmTrackMul = 1f;    // 曲ごとの係数（タイトル=1.0 / 戦闘=0.7）
        bool bgmMuted;
        float sfxUserVol = 0.8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("KiokuNoIseki_Audio");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AudioManager>();
            Instance.Init();
        }

        void Init()
        {
            bgm = gameObject.AddComponent<AudioSource>();
            bgm.loop = true; bgm.playOnAwake = false;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.loop = false; sfx.playOnAwake = false; sfx.volume = sfxUserVol;

            // シーンに AudioListener が1つも無いと「no audio listeners」警告が出て音が鳴らない。
            // 当ゲームの音は全て2D（BGM/効果音）なので位置は不問。無ければ常駐マネージャに付けて必ず1つ確保する
            //（既に存在する場合は二重にしない＝multiple listeners警告を避ける）。
            if (Object.FindFirstObjectByType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();

            ApplyBgm();
        }

        // 実際のBGM音量を反映（0%やミュートで完全無音）。
        void ApplyBgm()
        {
            if (bgm == null) return;
            bgm.volume = bgmMuted ? 0f : Mathf.Clamp01(bgmUserVol) * bgmTrackMul;
        }

        // BGMを切り替える。trackMul=曲ごとの音量係数（戦闘は0.7）。同じ曲なら鳴らし直さず係数だけ更新。
        public void PlayBgm(string name, float trackMul = 1f)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (currentBgm == name && bgm != null && bgm.isPlaying)
            {
                bgmTrackMul = trackMul; ApplyBgm(); // 同じ曲：係数だけ反映
                return;
            }
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null)
            {
                Debug.LogWarning($"🎵 BGM未配置: Resources/Audio/{name}（対応形式のファイルを置いてください）");
                currentBgm = null; if (bgm != null) bgm.Stop();
                return;
            }
            currentBgm = name; bgmTrackMul = trackMul;
            ApplyBgm();
            bgm.clip = clip; bgm.Play();
        }

        public void StopBgm() { currentBgm = null; if (bgm != null) bgm.Stop(); }

        // 効果音を鳴らす。Resources/Audio/{name} が無ければ黙って無視（未決定でも安全）。
        public void PlaySfx(string name, float volume = 1f)
        {
            if (string.IsNullOrEmpty(name) || sfx == null) return;
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null) return;
            sfx.PlayOneShot(clip, Mathf.Clamp01(volume)); // sfx.volume（ユーザー音量）×これ
        }

        // Resources/Audio/{folder}/ 内の全効果音からランダムに1つ鳴らす（フォルダに足すだけで候補が増える）。
        readonly Dictionary<string, AudioClip[]> sfxPools = new Dictionary<string, AudioClip[]>();
        public void PlayRandomSfx(string folder, float volume = 1f)
        {
            if (sfx == null || string.IsNullOrEmpty(folder)) return;
            if (!sfxPools.TryGetValue(folder, out var clips))
            {
                clips = Resources.LoadAll<AudioClip>("Audio/" + folder);
                sfxPools[folder] = clips;
            }
            if (clips == null || clips.Length == 0) return;
            var clip = clips[Random.Range(0, clips.Length)];
            sfx.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        public void SetBgmVolume(float v) { bgmUserVol = Mathf.Clamp01(v); ApplyBgm(); }
        public void SetSfxVolume(float v) { sfxUserVol = Mathf.Clamp01(v); if (sfx != null) sfx.volume = sfxUserVol; }
        public float BgmVolume => bgmUserVol;   // 設定画面にはユーザー音量（係数をかける前）を見せる
        public float SfxVolume => sfxUserVol;
        public bool BgmMuted => bgmMuted;
        public void ToggleBgmMute() { bgmMuted = !bgmMuted; ApplyBgm(); }

        // ── 便利ショートカット（Instanceが無くても安全）──
        public static void Title() { Instance?.PlayBgm("bgm_title", 1f); }
        public static void Battle() { Instance?.PlayBgm("bgm_battle", 0.4f); } // 戦闘BGMは係数0.4（さらに小さく）
        public static void Sfx(string name, float v = 1f) { Instance?.PlaySfx(name, v); }
        public static void SfxRandom(string folder, float v = 1f) { Instance?.PlayRandomSfx(folder, v); }
    }
}
