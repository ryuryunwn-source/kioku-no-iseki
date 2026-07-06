using System;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

namespace KiokuNoIseki
{
    // PC（Windowsビルド）用の画像ファイル選択。NativeGallery はエディタとモバイルにしか対応しないため、
    // Windows 標準のファイルダイアログ(comdlg32)を直接呼ぶ。外部パッケージ不要。
    // Windows 以外では PickImagePath() は null を返す（呼び出し側でフォールバック）。
    public static class StandaloneFilePicker
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int structSize;
            public IntPtr dlgOwner;
            public IntPtr instance;
            public string filter;
            public string customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr custData;
            public IntPtr hook;
            public string templateName;
            public IntPtr reservedPtr;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        public static string PickImagePath()
        {
            var ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = "画像 (*.png;*.jpg;*.jpeg;*.bmp)\0*.png;*.jpg;*.jpeg;*.bmp\0すべて (*.*)\0*.*\0\0";
            ofn.file = new string(new char[1024]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[256]);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.title = "写真を選択";
            // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;
            try
            {
                if (GetOpenFileNameW(ref ofn)) return ofn.file;
            }
            catch (Exception e) { Debug.LogWarning("[FilePicker] 失敗: " + e.Message); }
            return null;
        }
#else
        public static string PickImagePath() => null;
#endif

        // ファイルパスから Texture2D を読み込む（全プラットフォーム共通）。maxSize を超える辺は縮小する。
        public static Texture2D LoadTexture(string path, int maxSize = 512)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;

            var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { raw.LoadImage(System.IO.File.ReadAllBytes(path)); }
            catch (Exception e) { Debug.LogWarning("[FilePicker] 読み込み失敗: " + e.Message); return null; }

            int w = raw.width, h = raw.height;
            if (Mathf.Max(w, h) <= maxSize) return raw;

            // GPUブリットで縮小（大きな写真のJPG肥大とハッシュ負荷を抑える）。
            float s = (float)maxSize / Mathf.Max(w, h);
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * s));
            int nh = Mathf.Max(1, Mathf.RoundToInt(h * s));
            var rt = RenderTexture.GetTemporary(nw, nh);
            var prev = RenderTexture.active;
            Graphics.Blit(raw, rt);
            RenderTexture.active = rt;
            var outTex = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            outTex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(raw);
            return outTex;
        }
    }
}
