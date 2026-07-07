using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;

namespace MonsterHunt;

public enum Sfx { Swing, Hit, MonsterDie, PlayerHurt, Growl, Roar, Spawn, Pickup }

/// <summary>
/// Toca uma trilha de terror em loop e efeitos sonoros posicionais (3D),
/// tudo sintetizado em memória — sem arquivos de áudio.
/// Se o OpenAL não estiver disponível, o jogo continua funcionando sem som.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public bool Enabled { get; }

    ALDevice _device;
    ALContext _context;
    int _musicSource;
    readonly List<int> _buffers = new();
    readonly Dictionary<Sfx, int> _sfx = new();
    readonly int[] _pool = new int[10];
    int _poolIdx;
    readonly float[] _orientation = new float[6];

    const int SampleRate = 22050;

    public AudioEngine()
    {
        try
        {
            _device = ALC.OpenDevice(null);
            if (_device == ALDevice.Null) return;
            _context = ALC.CreateContext(_device, new ALContextAttributes());
            ALC.MakeContextCurrent(_context);

            int musicBuffer = MakeBuffer(ComposeHorrorMusic());
            _musicSource = AL.GenSource();
            AL.Source(_musicSource, ALSourcei.Buffer, musicBuffer);
            AL.Source(_musicSource, ALSourceb.Looping, true);
            AL.Source(_musicSource, ALSourceb.SourceRelative, true);
            AL.Source(_musicSource, ALSourcef.Gain, 0.4f);

            _sfx[Sfx.Swing] = MakeBuffer(MakeSwing());
            _sfx[Sfx.Hit] = MakeBuffer(MakeHit());
            _sfx[Sfx.MonsterDie] = MakeBuffer(MakeDie());
            _sfx[Sfx.PlayerHurt] = MakeBuffer(MakeHurt());
            _sfx[Sfx.Growl] = MakeBuffer(MakeGrowl());
            _sfx[Sfx.Roar] = MakeBuffer(MakeRoar());
            _sfx[Sfx.Spawn] = MakeBuffer(MakeSpawn());
            _sfx[Sfx.Pickup] = MakeBuffer(MakePickup());

            for (int i = 0; i < _pool.Length; i++)
            {
                _pool[i] = AL.GenSource();
                AL.Source(_pool[i], ALSourcef.ReferenceDistance, 4f);
                AL.Source(_pool[i], ALSourcef.MaxDistance, 80f);
                AL.Source(_pool[i], ALSourcef.RolloffFactor, 1.2f);
            }
            Enabled = true;
        }
        catch
        {
            Enabled = false;
        }
    }

    int MakeBuffer(short[] data)
    {
        int b = AL.GenBuffer();
        AL.BufferData(b, ALFormat.Mono16, data, SampleRate);
        _buffers.Add(b);
        return b;
    }

    public void PlayMusic()
    {
        if (Enabled) AL.SourcePlay(_musicSource);
    }

    public void SetMusicVolume(float gain)
    {
        if (Enabled) AL.Source(_musicSource, ALSourcef.Gain, gain);
    }

    public void UpdateListener(Vector3 pos, Vector3 front, Vector3 up)
    {
        if (!Enabled) return;
        AL.Listener(ALListener3f.Position, pos.X, pos.Y, pos.Z);
        _orientation[0] = front.X; _orientation[1] = front.Y; _orientation[2] = front.Z;
        _orientation[3] = up.X; _orientation[4] = up.Y; _orientation[5] = up.Z;
        AL.Listener(ALListenerfv.Orientation, ref _orientation[0]);
    }

    /// <summary>Efeito sonoro posicionado no mundo (fica mais alto/direcional conforme a distância).</summary>
    public void PlaySfx(Sfx s, Vector3 pos, float gain = 1f, float pitch = 1f) =>
        PlayInternal(s, pos, false, gain, pitch);

    /// <summary>Efeito sonoro "no ouvido" do jogador (não posicional).</summary>
    public void PlaySfx2D(Sfx s, float gain = 1f, float pitch = 1f) =>
        PlayInternal(s, Vector3.Zero, true, gain, pitch);

    void PlayInternal(Sfx s, Vector3 pos, bool relative, float gain, float pitch)
    {
        if (!Enabled) return;
        int src = _pool[_poolIdx];
        _poolIdx = (_poolIdx + 1) % _pool.Length;
        AL.SourceStop(src);
        AL.Source(src, ALSourcei.Buffer, _sfx[s]);
        AL.Source(src, ALSourceb.SourceRelative, relative);
        AL.Source(src, ALSource3f.Position, pos.X, pos.Y, pos.Z);
        AL.Source(src, ALSourcef.Gain, gain);
        AL.Source(src, ALSourcef.Pitch, pitch);
        AL.SourcePlay(src);
    }

    public void Dispose()
    {
        if (_device == ALDevice.Null) return;
        if (_musicSource != 0) AL.SourceStop(_musicSource);
        foreach (var s in _pool) if (s != 0) { AL.SourceStop(s); AL.DeleteSource(s); }
        if (_musicSource != 0) AL.DeleteSource(_musicSource);
        foreach (var b in _buffers) AL.DeleteBuffer(b);
        if (_context != ALContext.Null) { ALC.MakeContextCurrent(ALContext.Null); ALC.DestroyContext(_context); }
        ALC.CloseDevice(_device);
    }

    // ------------------------------------------------------------------
    //  Música de terror — loop de 16 s
    //  Drones graves desafinados + vento + batida de coração + notas fantasma
    // ------------------------------------------------------------------

    static short[] ComposeHorrorMusic()
    {
        const double len = 16.0;
        var mix = new float[(int)(len * SampleRate)];
        var rng = new Random(666);

        // drones contínuos (nº inteiro de ciclos em 16 s → loop sem clique)
        AddDrone(mix, 55.0, 0.13f);       // Lá grave
        AddDrone(mix, 55.6875, 0.09f);    // quase igual → batimento sinistro
        AddDrone(mix, 82.6875, 0.05f);    // quinta levemente desafinada

        AddWind(mix, 0.055f, rng);

        // batida de coração: "tum-tum" a cada 1,6 s
        for (double t = 0; t < len - 0.5; t += 1.6)
        {
            AddHeartThump(mix, (int)(t * SampleRate), 0.42f);
            AddHeartThump(mix, (int)((t + 0.30) * SampleRate), 0.26f);
        }

        // notas fantasmagóricas esparsas (com trítono para dar tensão)
        AddGhost(mix, 2.0, 440.00, 2.4, 0.060f);
        AddGhost(mix, 5.2, 466.16, 1.8, 0.050f);
        AddGhost(mix, 7.6, 622.25, 2.6, 0.055f);
        AddGhost(mix, 10.6, 415.30, 1.6, 0.048f);
        AddGhost(mix, 12.8, 440.00, 2.4, 0.055f);

        return Master(mix);
    }

    static void AddDrone(float[] mix, double freq, float vol)
    {
        double phase = 0, step = freq / SampleRate;
        for (int i = 0; i < mix.Length; i++)
        {
            double t = i / (double)SampleRate;
            phase += step;
            float tri = 4f * MathF.Abs((float)(phase % 1.0) - 0.5f) - 1f;
            float lfo = 0.8f + 0.2f * MathF.Sin((float)(t * Math.PI * 2.0 / 8.0));
            mix[i] += tri * vol * lfo;
        }
    }

    static void AddWind(float[] mix, float vol, Random rng)
    {
        float lp = 0;
        for (int i = 0; i < mix.Length; i++)
        {
            double t = i / (double)SampleRate;
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += 0.04f * (n - lp);
            // envelope zera nas pontas do loop → emenda invisível
            float env = 0.5f - 0.5f * MathF.Cos((float)(t * Math.PI * 2.0 / 16.0));
            float gust = 0.6f + 0.4f * MathF.Sin((float)(t * Math.PI * 2.0 / 4.0));
            mix[i] += lp * vol * env * gust;
        }
    }

    static void AddHeartThump(float[] mix, int start, float vol)
    {
        int n = (int)(0.16 * SampleRate);
        double phase = 0;
        for (int i = 0; i < n && start + i < mix.Length; i++)
        {
            double t = i / (double)SampleRate;
            double f = 34.0 + 70.0 * Math.Exp(-t * 26.0);
            phase += f / SampleRate;
            mix[start + i] += MathF.Sin((float)(phase * Math.PI * 2.0)) * vol * (float)Math.Exp(-t * 14.0);
        }
    }

    static void AddGhost(float[] mix, double start, double freq, double dur, float vol)
    {
        int s0 = (int)(start * SampleRate);
        int n = (int)(dur * SampleRate);
        double phase = 0;
        for (int i = 0; i < n && s0 + i < mix.Length; i++)
        {
            double t = i / (double)SampleRate;
            double f = freq * (1.0 + 0.008 * Math.Sin(t * Math.PI * 2.0 * 4.3));
            phase += f / SampleRate;
            float env = MathF.Sin((float)(Math.PI * Math.Min(1.0, t / dur)));
            mix[s0 + i] += MathF.Sin((float)(phase * Math.PI * 2.0)) * vol * env;
        }
    }

    // ------------------------------------------------------------------
    //  Efeitos sonoros
    // ------------------------------------------------------------------

    static short[] MakeSwing()
    {
        int n = (int)(0.20 * SampleRate);
        var mix = new float[n];
        var rng = new Random(1);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            double p = i / (double)n;
            float cutoff = (float)Math.Min(0.5, 0.06 + 0.5 * Math.Sin(Math.PI * p));
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += cutoff * (x - lp);
            mix[i] += lp * 0.9f * MathF.Sin((float)(Math.PI * p));
        }
        return Master(mix);
    }

    static short[] MakeHit()
    {
        int n = (int)(0.14 * SampleRate);
        var mix = new float[n];
        var rng = new Random(2);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            phase += 95.0 / SampleRate;
            mix[i] += MathF.Sin((float)(phase * Math.PI * 2.0)) * 0.8f * (float)Math.Exp(-t * 22.0);
            mix[i] += (float)(rng.NextDouble() * 2.0 - 1.0) * 0.45f * (float)Math.Exp(-t * 45.0);
        }
        return Master(mix);
    }

    static short[] MakeDie()
    {
        int n = (int)(0.55 * SampleRate);
        var mix = new float[n];
        var rng = new Random(3);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double f = 50.0 + 240.0 * Math.Exp(-t * 6.0);
            phase += f / SampleRate;
            float sq = (phase % 1.0) < 0.5 ? 1f : -1f;
            mix[i] += sq * 0.4f * (float)Math.Exp(-t * 5.0);
            mix[i] += (float)(rng.NextDouble() * 2.0 - 1.0) * 0.18f * (float)Math.Exp(-t * 9.0);
        }
        return Master(mix);
    }

    static short[] MakeHurt()
    {
        int n = (int)(0.30 * SampleRate);
        var mix = new float[n];
        double p1 = 0, p2 = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double f = 70.0 + 60.0 * Math.Exp(-t * 10.0);
            p1 += f / SampleRate;
            p2 += f * 1.04 / SampleRate;
            float sq = ((p1 % 1.0) < 0.5 ? 1f : -1f) + ((p2 % 1.0) < 0.5 ? 0.7f : -0.7f);
            mix[i] += sq * 0.30f * (float)Math.Exp(-t * 7.0);
        }
        return Master(mix);
    }

    static short[] MakeGrowl()
    {
        int n = (int)(1.1 * SampleRate);
        var mix = new float[n];
        var rng = new Random(4);
        double phase = 0;
        float lp = 0, lpn = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double f = 58.0 + 12.0 * Math.Sin(t * Math.PI * 2.0 * 2.1) + 6.0 * Math.Sin(t * Math.PI * 2.0 * 0.7);
            phase += f / SampleRate;
            float saw = (float)(2.0 * (phase % 1.0) - 1.0);
            lp += 0.22f * (saw - lp);
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            lpn += 0.10f * (x - lpn);
            float env = MathF.Pow(MathF.Sin((float)(Math.PI * Math.Min(1.0, t / 1.1))), 0.8f);
            float trem = 0.72f + 0.28f * MathF.Sin((float)(t * Math.PI * 2.0 * 8.6));
            mix[i] += (lp * 0.85f + lpn * 0.5f) * env * trem;
        }
        return Master(mix);
    }

    static short[] MakeRoar()
    {
        int n = (int)(1.5 * SampleRate);
        var mix = new float[n];
        var rng = new Random(5);
        double phase = 0;
        float lp = 0, lpn = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double f = 45.0 + 100.0 * Math.Exp(-t * 1.6) + 8.0 * Math.Sin(t * Math.PI * 2.0 * 3.3);
            phase += f / SampleRate;
            float saw = (float)(2.0 * (phase % 1.0) - 1.0);
            lp += 0.30f * (saw - lp);
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            lpn += 0.16f * (x - lpn);
            float attack = MathF.Min(1f, (float)(t / 0.06));
            float env = attack * (float)Math.Exp(-Math.Max(0.0, t - 0.15) * 2.2);
            float trem = 0.75f + 0.25f * MathF.Sin((float)(t * Math.PI * 2.0 * 6.2));
            mix[i] += (lp + lpn * 0.6f) * env * trem;
        }
        return Master(mix);
    }

    static short[] MakeSpawn()
    {
        int n = (int)(0.7 * SampleRate);
        var mix = new float[n];
        var rng = new Random(6);
        double phase = 0;
        float lpn = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double p = t / 0.7;
            phase += (150.0 + 400.0 * p * p) / SampleRate;
            float env = (float)(Math.Pow(p, 1.4) * Math.Exp(-Math.Max(0.0, t - 0.58) * 35.0));
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            lpn += 0.3f * (x - lpn);
            mix[i] += MathF.Sin((float)(phase * Math.PI * 2.0)) * 0.4f * env + lpn * 0.25f * env;
        }
        return Master(mix);
    }

    static short[] MakePickup()
    {
        int n = (int)(0.4 * SampleRate);
        var mix = new float[n];
        (double f, double t0)[] notes = { (523.25, 0.0), (783.99, 0.09), (1046.5, 0.18) };
        foreach (var (f, t0) in notes)
        {
            int s0 = (int)(t0 * SampleRate);
            double phase = 0;
            for (int i = s0; i < n; i++)
            {
                double t = (i - s0) / (double)SampleRate;
                phase += f / SampleRate;
                mix[i] += MathF.Sin((float)(phase * Math.PI * 2.0)) * 0.28f * (float)Math.Exp(-t * 12.0);
            }
        }
        return Master(mix);
    }

    static short[] Master(float[] mix)
    {
        var data = new short[mix.Length];
        for (int i = 0; i < mix.Length; i++)
            data[i] = (short)(Math.Tanh(mix[i]) * 0.9 * short.MaxValue);
        return data;
    }
}
