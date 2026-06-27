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

    public static void Init()
    {
        if (!InitSubSystem(InitFlags.Audio)) return;

        var spec = new AudioSpec { Format = AudioFormat.AudioF32LE, Channels = 1, Freq = 22050 };

        s_thrusterStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, ref spec, null, IntPtr.Zero);
        if (s_thrusterStream != IntPtr.Zero) ResumeAudioStreamDevice(s_thrusterStream);

        s_warningStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, ref spec, null, IntPtr.Zero);
        if (s_warningStream != IntPtr.Zero) ResumeAudioStreamDevice(s_warningStream);

        s_statusStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, ref spec, null, IntPtr.Zero);
        if (s_statusStream != IntPtr.Zero) ResumeAudioStreamDevice(s_statusStream);

        s_countdownStream = OpenAudioDeviceStream(AudioDeviceDefaultPlayback, ref spec, null, IntPtr.Zero);
        if (s_countdownStream != IntPtr.Zero) ResumeAudioStreamDevice(s_countdownStream);
    }

    public static void Cleanup()
    {
        if (s_thrusterStream  != IntPtr.Zero) { DestroyAudioStream(s_thrusterStream);  s_thrusterStream  = IntPtr.Zero; }
        if (s_warningStream   != IntPtr.Zero) { DestroyAudioStream(s_warningStream);   s_warningStream   = IntPtr.Zero; }
        if (s_statusStream    != IntPtr.Zero) { DestroyAudioStream(s_statusStream);    s_statusStream    = IntPtr.Zero; }
        if (s_countdownStream != IntPtr.Zero) { DestroyAudioStream(s_countdownStream); s_countdownStream = IntPtr.Zero; }
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
