using UnityEngine;

namespace KiokuNoIseki
{
    // BGM・効果音を一元管理する常駐マネージャ（自動起動）。
    //  - BGMは Resources/Audio/ から読み込み、タイトル/戦闘で切り替える（同じ曲なら鳴らし直さない）。
    //  - 効果音は Resources/Audio/ に該当ファイルがあれば鳴る。無ければ黙って無視（後から追加可能）。
    // 使い方：AudioManager.Title() / AudioManager.Battle() / AudioManager.Sfx("sfx_attack")
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        AudioSource bgm;
        AudioSource sfx;
        string currentBgm;

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
            bgm.loop = true; bgm.playOnAwake = false; bgm.volume = 0.5f;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.loop = false; sfx.playOnAwake = false; sfx.volume = 0.8f;
        }

        // BGMを切り替える。同じ曲が既に鳴っていれば何もしない。name例: "bgm_title" / "bgm_battle"
        public void PlayBgm(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (currentBgm == name && bgm != null && bgm.isPlaying) return;
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null)
            {
                Debug.LogWarning($"🎵 BGM未配置: Resources/Audio/{name}（対応形式のファイルを置いてください）");
                currentBgm = null; if (bgm != null) bgm.Stop();
                return;
            }
            currentBgm = name;
            bgm.clip = clip; bgm.Play();
        }

        public void StopBgm() { currentBgm = null; if (bgm != null) bgm.Stop(); }

        // 効果音を鳴らす。Resources/Audio/{name} が無ければ黙って無視（未決定でも安全）。
        public void PlaySfx(string name, float volume = 1f)
        {
            if (string.IsNullOrEmpty(name) || sfx == null) return;
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null) return;
            sfx.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        public void SetBgmVolume(float v) { if (bgm != null) bgm.volume = Mathf.Clamp01(v); }
        public void SetSfxVolume(float v) { if (sfx != null) sfx.volume = Mathf.Clamp01(v); }

        // ── 便利ショートカット（Instanceが無くても安全）──
        public static void Title() { Instance?.PlayBgm("bgm_title"); }
        public static void Battle() { Instance?.PlayBgm("bgm_battle"); }
        public static void Sfx(string name, float v = 1f) { Instance?.PlaySfx(name, v); }
    }
}
