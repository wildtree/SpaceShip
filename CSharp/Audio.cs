using System;
using static SDL3.SDL;

namespace SpaceShip;

internal static class AudioSystem
{
    private static IntPtr s_thrusterStream  = IntPtr.Zero;
    private static float   s_noiseLpf       = 0.0f;

    private static IntPtr s_warningStream   = IntPtr.Zero;
    private static float   s_warnSinePhase  = 0.0f;
    private static float   s_warnModPhase   = 0.0f;

    private static IntPtr s_statusStream    = IntPtr.Zero;
    private static float   s_statSinePhase  = 0.0f;
    private static float   s_statModPhase   = 0.0f;
    private static int     s_statPrevLevel  = 0;

    private static IntPtr s_countdownStream = IntPtr.Zero;
    private static IntPtr s_ringStream      = IntPtr.Zero; // リング通過SE専用
    private static IntPtr s_jingleStream   = IntPtr.Zero; // ジングル専用
    private static IntPtr s_bonusStream    = IntPtr.Zero; // ボーナスジングル専用 (s_jingleStreamに割り込まれない)
    private static IntPtr s_uiStream       = IntPtr.Zero; // メニューUI操作音専用

    public static void Init()
    {
        if (!InitSubSystem(InitFlags.Audio)) return;

        var spec = new AudioSpec { Format = AudioFormat.AudioF32LE, Channels = 1, Freq = 22050 };

        s_thrusterStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_thrusterStream != IntPtr.Zero) ResumeAudioStreamDevice(s_thrusterStream);

        s_warningStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_warningStream != IntPtr.Zero) ResumeAudioStreamDevice(s_warningStream);

        s_statusStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_statusStream != IntPtr.Zero) ResumeAudioStreamDevice(s_statusStream);

        s_countdownStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_countdownStream != IntPtr.Zero) ResumeAudioStreamDevice(s_countdownStream);

        s_ringStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_ringStream != IntPtr.Zero) ResumeAudioStreamDevice(s_ringStream);

        s_jingleStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_jingleStream != IntPtr.Zero) ResumeAudioStreamDevice(s_jingleStream);

        s_bonusStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_bonusStream != IntPtr.Zero) ResumeAudioStreamDevice(s_bonusStream);

        s_uiStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, in spec, null, IntPtr.Zero);
        if (s_uiStream != IntPtr.Zero) ResumeAudioStreamDevice(s_uiStream);
    }

    public static void Cleanup()
    {
        if (s_thrusterStream  != IntPtr.Zero) { DestroyAudioStream(s_thrusterStream);  s_thrusterStream  = IntPtr.Zero; }
        if (s_warningStream   != IntPtr.Zero) { DestroyAudioStream(s_warningStream);   s_warningStream   = IntPtr.Zero; }
        if (s_statusStream    != IntPtr.Zero) { DestroyAudioStream(s_statusStream);    s_statusStream    = IntPtr.Zero; }
        if (s_countdownStream != IntPtr.Zero) { DestroyAudioStream(s_countdownStream); s_countdownStream = IntPtr.Zero; }
        if (s_ringStream      != IntPtr.Zero) { DestroyAudioStream(s_ringStream);      s_ringStream      = IntPtr.Zero; }
        if (s_jingleStream    != IntPtr.Zero) { DestroyAudioStream(s_jingleStream);    s_jingleStream    = IntPtr.Zero; }
        if (s_bonusStream     != IntPtr.Zero) { DestroyAudioStream(s_bonusStream);     s_bonusStream     = IntPtr.Zero; }
        if (s_uiStream        != IntPtr.Zero) { DestroyAudioStream(s_uiStream);        s_uiStream        = IntPtr.Zero; }
    }

    private static unsafe void PushFloats(IntPtr stream, float[] buf, int count)
    {
        fixed (float* ptr = buf)
            PutAudioStreamData(stream, (IntPtr)ptr, count * sizeof(float));
    }

    // White noise -> one-pole LPF (α=0.65) -> vol 0.45, keep 80ms buffer
    public static void Thruster(bool on)
    {
        if (s_thrusterStream == IntPtr.Zero) return;
        if (!on)
        {
            ClearAudioStream(s_thrusterStream);
            s_noiseLpf = 0.0f;
            return;
        }
        const int   SR           = 22050;
        const int   TARGET_BYTES = (int)(SR * 0.08f) * sizeof(float);
        int queued = GetAudioStreamQueued(s_thrusterStream);
        if (queued >= TARGET_BYTES) return;
        int n = (TARGET_BYTES - queued) / sizeof(float);

        float[] buf = new float[1024];
        while (n > 0)
        {
            int batch = n < 1024 ? n : 1024;
            for (int i = 0; i < batch; i++)
            {
                float r = ((float)Random.Shared.NextDouble()) * 2.0f - 1.0f;
                s_noiseLpf = 0.65f * r + 0.35f * s_noiseLpf;
                buf[i] = s_noiseLpf * 0.45f;
            }
            PushFloats(s_thrusterStream, buf, batch);
            n -= batch;
        }
    }

    // 880Hz sine x 4Hz square AM, vol 0.35
    public static void WarningTone(bool on)
    {
        if (s_warningStream == IntPtr.Zero) return;
        if (!on)
        {
            ClearAudioStream(s_warningStream);
            s_warnSinePhase = 0.0f;
            s_warnModPhase  = 0.0f;
            return;
        }
        const int   SR       = 22050;
        const float FREQ     = 880.0f;
        const float MOD_FREQ = 4.0f;
        const int   TARGET   = (int)(SR * 0.08f) * sizeof(float);
        int queued = GetAudioStreamQueued(s_warningStream);
        if (queued >= TARGET) return;
        int n = (TARGET - queued) / sizeof(float);

        float[] buf = new float[1024];
        while (n > 0)
        {
            int batch = n < 1024 ? n : 1024;
            for (int i = 0; i < batch; i++)
            {
                float s   = MathF.Sin(s_warnSinePhase * 2.0f * MathF.PI);
                s_warnSinePhase += FREQ / SR;
                if (s_warnSinePhase >= 1.0f) s_warnSinePhase -= 1.0f;
                float mod = (s_warnModPhase < 0.5f) ? 1.0f : 0.0f;
                s_warnModPhase += MOD_FREQ / SR;
                if (s_warnModPhase >= 1.0f) s_warnModPhase -= 1.0f;
                buf[i] = s * mod * 0.35f;
            }
            PushFloats(s_warningStream, buf, batch);
            n -= batch;
        }
    }

    // level 0=off, 1=fuel warning 440Hz/2Hz, 2=time warning 1100Hz/6Hz, vol 0.3
    public static void StatusWarning(int level)
    {
        if (s_statusStream == IntPtr.Zero) return;
        if (level != s_statPrevLevel)
        {
            ClearAudioStream(s_statusStream);
            s_statSinePhase = 0.0f;
            s_statModPhase  = 0.0f;
            s_statPrevLevel = level;
        }
        if (level == 0) return;
        const int SR    = 22050;
        float freq  = (level == 2) ? 1100.0f : 440.0f;
        float mfreq = (level == 2) ?    6.0f :   2.0f;
        const int TARGET = (int)(SR * 0.08f) * sizeof(float);
        int queued = GetAudioStreamQueued(s_statusStream);
        if (queued >= TARGET) return;
        int n = (TARGET - queued) / sizeof(float);

        float[] buf = new float[1024];
        while (n > 0)
        {
            int batch = n < 1024 ? n : 1024;
            for (int i = 0; i < batch; i++)
            {
                float s2 = MathF.Sin(s_statSinePhase * 2.0f * MathF.PI);
                s_statSinePhase += freq / SR;
                if (s_statSinePhase >= 1.0f) s_statSinePhase -= 1.0f;
                float mod = (s_statModPhase < 0.5f) ? 1.0f : 0.0f;
                s_statModPhase += mfreq / SR;
                if (s_statModPhase >= 1.0f) s_statModPhase -= 1.0f;
                buf[i] = s2 * mod * 0.3f;
            }
            PushFloats(s_statusStream, buf, batch);
            n -= batch;
        }
    }

    // sine + harmonic x exp(-decayRate*t)
    private static void PushTone(float freq, float dur, float vol, float decayRate, float harmRatio)
    {
        if (s_countdownStream == IntPtr.Zero) return;
        const int SR = 22050;
        int total = (int)(SR * dur);
        float[] buf = new float[512];
        for (int pushed = 0; pushed < total; )
        {
            int batch = total - pushed;
            if (batch > 512) batch = 512;
            for (int i = 0; i < batch; i++)
            {
                float t   = (float)(pushed + i) / SR;
                float env = MathF.Exp(-decayRate * t);
                float s   = MathF.Sin(t * freq * 2.0f * MathF.PI)
                          + harmRatio * MathF.Sin(t * freq * 4.0f * MathF.PI);
                buf[i] = s * env * vol;
            }
            PushFloats(s_countdownStream, buf, batch);
            pushed += batch;
        }
    }

    public static void PlayPip()  => PushTone(1100.0f, 0.10f, 0.55f, 18.0f, 0.0f);
    public static void PlayGong() => PushTone(550.0f,  0.70f, 0.60f,  3.5f, 0.25f);

    // ---- ジングル共通ヘルパー ----

    // ノート1音をジングルストリームに追加 (サイン波 + 5度倍音 + 減衰エンベロープ + 末尾無音ギャップ)
    private static void PushJingleNote(float freq, float dur, float vol, float decay,
                                        float harmRatio = 0.25f, float gapSec = 0.015f)
    {
        if (s_jingleStream == IntPtr.Zero) return;
        const int SR = 22050;
        int noteSamples = (int)(SR * dur);
        int gapSamples  = (int)(SR * gapSec);
        float[] buf = new float[512];

        // ノート本体
        float phase = 0f;
        for (int pushed = 0; pushed < noteSamples; )
        {
            int batch = Math.Min(noteSamples - pushed, 512);
            for (int i = 0; i < batch; i++)
            {
                float t   = (float)(pushed + i) / SR;
                float env = MathF.Exp(-decay * t);
                // 基音 + 完全5度 (×1.5) でブラス的な音色に
                float s   = MathF.Sin(phase       * 2f * MathF.PI)
                          + harmRatio * MathF.Sin(phase * 3f * MathF.PI);
                phase += freq / SR;
                if (phase >= 1f) phase -= 1f;
                buf[i] = s * env * vol;
            }
            PushFloats(s_jingleStream, buf, batch);
            pushed += batch;
        }

        // 無音ギャップ
        for (int pushed = 0; pushed < gapSamples; )
        {
            int batch = Math.Min(gapSamples - pushed, 512);
            Array.Clear(buf, 0, batch);
            PushFloats(s_jingleStream, buf, batch);
            pushed += batch;
        }
    }

    // ボーナスジングル専用ノートヘルパー (s_bonusStream に書き込む)
    private static void PushBonusNote(float freq, float dur, float vol, float decay,
                                       float harmRatio = 0.25f, float gapSec = 0.015f)
    {
        if (s_bonusStream == IntPtr.Zero) return;
        const int SR = 22050;
        int noteSamples = (int)(SR * dur);
        int gapSamples  = (int)(SR * gapSec);
        float[] buf = new float[512];

        float phase = 0f;
        for (int pushed = 0; pushed < noteSamples; )
        {
            int batch = Math.Min(noteSamples - pushed, 512);
            for (int i = 0; i < batch; i++)
            {
                float t   = (float)(pushed + i) / SR;
                float env = MathF.Exp(-decay * t);
                float s   = MathF.Sin(phase       * 2f * MathF.PI)
                          + harmRatio * MathF.Sin(phase * 3f * MathF.PI);
                phase += freq / SR;
                if (phase >= 1f) phase -= 1f;
                buf[i] = s * env * vol;
            }
            PushFloats(s_bonusStream, buf, batch);
            pushed += batch;
        }

        for (int pushed = 0; pushed < gapSamples; )
        {
            int batch = Math.Min(gapSamples - pushed, 512);
            Array.Clear(buf, 0, batch);
            PushFloats(s_bonusStream, buf, batch);
            pushed += batch;
        }
    }

    // ステージ開始: C5→E5→G5→C6 上昇ファンファーレ (元気に)
    public static void PlayJingleStart()
    {
        if (s_jingleStream == IntPtr.Zero) return;
        ClearAudioStream(s_jingleStream);
        PushJingleNote(523.3f,  0.08f, 0.50f, 16f);
        PushJingleNote(659.3f,  0.08f, 0.50f, 16f);
        PushJingleNote(784.0f,  0.08f, 0.50f, 16f);
        PushJingleNote(1046.5f, 0.35f, 0.60f,  5f, gapSec: 0f);
    }

    // ステージクリア: C5→G5→C5→G5→C6 勝利ファンファーレ
    public static void PlayJingleClear()
    {
        if (s_jingleStream == IntPtr.Zero) return;
        ClearAudioStream(s_jingleStream);
        PushJingleNote(523.3f,  0.10f, 0.50f, 12f, gapSec: 0.01f);
        PushJingleNote(784.0f,  0.10f, 0.50f, 12f, gapSec: 0.01f);
        PushJingleNote(523.3f,  0.10f, 0.50f, 12f, gapSec: 0.01f);
        PushJingleNote(784.0f,  0.10f, 0.55f, 12f, gapSec: 0.01f);
        PushJingleNote(1046.5f, 0.55f, 0.65f,  3f, gapSec: 0f);
    }

    // ニューレコード: G4→B4→D5→G5→D5→G5→B5 勝利ファンファーレ (クリアとは別フレーズ)
    public static void PlayJingleNewRecord()
    {
        if (s_jingleStream == IntPtr.Zero) return;
        ClearAudioStream(s_jingleStream);
        PushJingleNote(392.0f, 0.07f, 0.45f, 20f, 0.20f, 0.005f);
        PushJingleNote(493.9f, 0.07f, 0.45f, 20f, 0.20f, 0.005f);
        PushJingleNote(587.3f, 0.07f, 0.45f, 20f, 0.20f, 0.005f);
        PushJingleNote(784.0f, 0.07f, 0.50f, 20f, 0.20f, 0.005f);
        PushJingleNote(587.3f, 0.07f, 0.48f, 20f, 0.20f, 0.005f);
        PushJingleNote(784.0f, 0.08f, 0.50f, 18f, 0.20f, 0.010f);
        PushJingleNote(987.8f, 0.50f, 0.62f,  3f, 0.25f, 0.000f);
    }

    // ゲームオーバー: A4→F4→C4→A3 暗く下降するフレーズ
    public static void PlayJingleGameOver()
    {
        if (s_jingleStream == IntPtr.Zero) return;
        ClearAudioStream(s_jingleStream);
        if (s_bonusStream != IntPtr.Zero) ClearAudioStream(s_bonusStream); // ボーナスジングルも止める
        PushJingleNote(440.0f, 0.28f, 0.50f, 4f, harmRatio: 0.15f, gapSec: 0.03f);
        PushJingleNote(349.2f, 0.28f, 0.50f, 4f, harmRatio: 0.15f, gapSec: 0.03f);
        PushJingleNote(261.6f, 0.28f, 0.50f, 4f, harmRatio: 0.15f, gapSec: 0.03f);
        PushJingleNote(220.0f, 0.80f, 0.55f, 2f, harmRatio: 0.15f, gapSec: 0f);
    }

    // ボーナスステージクリア: G4→B4→D5→G5→B5→D6→G6 盛大なGメジャーファンファーレ
    // ※ s_bonusStream を使うため PlayJingleStart の ClearAudioStream に割り込まれない
    public static void PlayJingleBonusClear()
    {
        if (s_bonusStream == IntPtr.Zero) return;
        ClearAudioStream(s_bonusStream);
        PushBonusNote(392.0f,  0.08f, 0.55f, 18f, 0.15f, 0.005f); // G4
        PushBonusNote(493.9f,  0.08f, 0.57f, 18f, 0.15f, 0.005f); // B4
        PushBonusNote(587.3f,  0.08f, 0.59f, 18f, 0.15f, 0.005f); // D5
        PushBonusNote(784.0f,  0.08f, 0.62f, 18f, 0.18f, 0.005f); // G5
        PushBonusNote(987.8f,  0.08f, 0.65f, 18f, 0.20f, 0.005f); // B5
        PushBonusNote(1174.7f, 0.08f, 0.68f, 18f, 0.22f, 0.005f); // D6
        PushBonusNote(1568.0f, 1.00f, 0.80f,  2f, 0.28f, 0.000f); // G6 (ビッグホールド)
    }

    // ボーナスステージ失敗: A4→G4→E4→D4→A3 物悲しい短調の下降フレーズ
    // ※ s_bonusStream を使うため他のジングルに割り込まれない
    public static void PlayJingleBonusFailed()
    {
        if (s_bonusStream == IntPtr.Zero) return;
        ClearAudioStream(s_bonusStream);
        PushBonusNote(440.0f, 0.28f, 0.45f, 5f, 0.22f, 0.04f); // A4
        PushBonusNote(392.0f, 0.28f, 0.42f, 5f, 0.22f, 0.04f); // G4
        PushBonusNote(329.6f, 0.28f, 0.40f, 5f, 0.22f, 0.04f); // E4
        PushBonusNote(293.7f, 0.30f, 0.38f, 4f, 0.22f, 0.04f); // D4
        PushBonusNote(220.0f, 0.95f, 0.45f, 2f, 0.28f, 0.000f); // A3 (長いホールド)
    }

    // ボーナスステージ開始: C5→E5→G5→C6→E6→G6 高速上昇ファンファーレ
    // ※ s_bonusStream を使うため PlayJingleClear の余韻と同時に鳴り、互いを消さない
    public static void PlayJingleBonusStart()
    {
        if (s_bonusStream == IntPtr.Zero) return;
        ClearAudioStream(s_bonusStream);
        PushBonusNote(523.3f,  0.08f, 0.50f, 18f, 0.15f, 0.005f); // C5
        PushBonusNote(659.3f,  0.08f, 0.55f, 18f, 0.15f, 0.005f); // E5
        PushBonusNote(784.0f,  0.08f, 0.58f, 18f, 0.15f, 0.005f); // G5
        PushBonusNote(1046.5f, 0.08f, 0.62f, 18f, 0.18f, 0.005f); // C6
        PushBonusNote(1318.5f, 0.08f, 0.65f, 18f, 0.20f, 0.010f); // E6
        PushBonusNote(1568.0f, 0.80f, 0.78f,  3f, 0.25f, 0.000f); // G6 (ビッグホールド)
    }

    // ---- メニューUI操作音 ----

    // サイン波1音をUIストリームに追加する内部ヘルパー
    private static void PushUiNote(float freq, float dur, float vol, float decay)
    {
        if (s_uiStream == IntPtr.Zero) return;
        const int SR = 22050;
        int total = (int)(SR * dur);
        float[] buf = new float[512];
        float phase = 0f;
        for (int pushed = 0; pushed < total; )
        {
            int batch = Math.Min(total - pushed, 512);
            for (int i = 0; i < batch; i++)
            {
                float t = (float)(pushed + i) / SR;
                buf[i] = MathF.Sin(phase * 2f * MathF.PI) * MathF.Exp(-decay * t) * vol;
                phase += freq / SR;
                if (phase >= 1f) phase -= 1f;
            }
            PushFloats(s_uiStream, buf, batch);
            pushed += batch;
        }
    }

    // 上下選択: 短い「ティック」音 (660Hz 40ms)
    public static void PlayUiSelect()
    {
        if (s_uiStream == IntPtr.Zero) return;
        ClearAudioStream(s_uiStream);
        PushUiNote(660f, 0.04f, 0.30f, 40f);
    }

    // 決定: 上昇2音チャイム (660Hz→990Hz)
    public static void PlayUiConfirm()
    {
        if (s_uiStream == IntPtr.Zero) return;
        ClearAudioStream(s_uiStream);
        PushUiNote(660f, 0.07f, 0.38f, 22f);
        PushUiNote(990f, 0.13f, 0.42f, 12f);
    }

    // アイテム出現: 取得音と対比させた下降チャイム (静かめ)
    public static void PlayItemSpawn(int itemType)
    {
        if (s_uiStream == IntPtr.Zero) return;
        ClearAudioStream(s_uiStream);

        // リング通過音(~200ms)と重ならないよう220msの無音パディングを先頭に挿入
        const int SR = 22050;
        int silenceSamples = (int)(SR * 0.22f);
        float[] silBuf = new float[512];
        for (int pushed = 0; pushed < silenceSamples; )
        {
            int batch = Math.Min(silenceSamples - pushed, 512);
            Array.Clear(silBuf, 0, batch);
            PushFloats(s_uiStream, silBuf, batch);
            pushed += batch;
        }

        if (itemType == 1)
        {
            PushUiNote(784.0f, 0.07f, 0.22f, 14f); // G5
            PushUiNote(523.3f, 0.12f, 0.18f,  8f); // C5
        }
        else
        {
            PushUiNote(466.2f, 0.07f, 0.22f, 14f); // Bb4
            PushUiNote(311.1f, 0.12f, 0.18f,  8f); // Eb4
        }
    }

    // アイテム取得: 時間(1)=高音チャイム / 燃料(2)=中音チャイム
    public static void PlayItemGet(int itemType)
    {
        if (s_uiStream == IntPtr.Zero) return;
        ClearAudioStream(s_uiStream);
        if (itemType == 1)
        {
            PushUiNote(659.3f, 0.06f, 0.42f, 20f); // E5
            PushUiNote(987.8f, 0.14f, 0.48f,  9f); // B5
        }
        else
        {
            PushUiNote(392.0f, 0.06f, 0.40f, 20f); // G4
            PushUiNote(587.3f, 0.14f, 0.46f,  9f); // D5
        }
    }

    // リング通過SE: 周波数が上昇するスイープ音
    // excellent=false: 500→1100Hz / 200ms (通常通過)
    // excellent=true : 700→2000Hz + オクターブ倍音 / 300ms (Excellent)
    public static void PlayRingPass(bool excellent)
    {
        if (s_ringStream == IntPtr.Zero) return;
        ClearAudioStream(s_ringStream);
        const int SR = 22050;
        float f0    = excellent ? 700f  : 500f;
        float f1    = excellent ? 2000f : 1100f;
        float dur   = excellent ? 0.30f : 0.20f;
        float vol   = excellent ? 0.55f : 0.45f;
        float decay = excellent ? 5.0f  : 9.0f;
        float harm  = excellent ? 0.30f : 0.0f; // オクターブ倍音の音量比

        int total = (int)(SR * dur);
        float phase = 0f;
        float[] buf = new float[512];
        for (int pushed = 0; pushed < total; )
        {
            int batch = Math.Min(total - pushed, 512);
            for (int i = 0; i < batch; i++)
            {
                float t    = (float)(pushed + i) / SR;
                float freq = f0 + (f1 - f0) * (t / dur); // 線形スイープ
                float env  = MathF.Exp(-decay * t);
                float s    = MathF.Sin(phase * 2f * MathF.PI)
                           + harm * MathF.Sin(phase * 4f * MathF.PI); // オクターブ倍音
                phase += freq / SR;
                if (phase >= 1f) phase -= 1f;
                buf[i] = s * env * vol;
            }
            PushFloats(s_ringStream, buf, batch);
            pushed += batch;
        }
    }

    public static void PlayExplosion()
    {
        if (s_countdownStream == IntPtr.Zero) return;
        ClearAudioStream(s_countdownStream);
        const int   SR    = 22050;
        const float DUR   = 1.6f;
        const float DECAY = 3.5f;
        int total = (int)(SR * DUR);
        float lpf = 0.0f;
        float[] buf = new float[512];
        for (int pushed = 0; pushed < total; )
        {
            int batch = total - pushed;
            if (batch > 512) batch = 512;
            for (int i = 0; i < batch; i++)
            {
                float t   = (float)(pushed + i) / SR;
                float env = MathF.Exp(-DECAY * t);
                float r   = ((float)Random.Shared.NextDouble()) * 2.0f - 1.0f;
                lpf = 0.55f * r + 0.45f * lpf;
                float boom = MathF.Sin(t * 80.0f * 2.0f * MathF.PI);
                buf[i] = (lpf * 0.55f + boom * 0.45f) * env * 0.75f;
            }
            PushFloats(s_countdownStream, buf, batch);
            pushed += batch;
        }
    }
}
