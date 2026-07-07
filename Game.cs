using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MonsterHunt;

public enum GameState { Menu, Playing, Paused, GameOver, Victory, Controls, About }

public sealed class Game : GameWindow
{
    // --- mundo ---
    const float ArenaRadius = 44f;
    const int MaxLevel = 12;
    readonly record struct Prop(Vector3 Pos, float TrunkH, float Foliage, bool IsRock, float Rot);
    readonly record struct Star(Vector3 Pos, float Size, Vector3 Color);
    readonly List<Prop> _props = new();
    readonly List<Star> _stars = new();
    readonly List<Monster> _monsters = new();
    readonly Random _rng = new();

    // --- armas ---
    readonly record struct WeaponDef(string Name, float Damage, float Range, float Cooldown, float Knockback, Vector3 Color);
    static readonly WeaponDef[] Weapons =
    {
        new("ESPADA", 34, 3.2f, 0.50f, 1.1f, new Vector3(0.78f, 0.80f, 0.85f)),
        new("MACHADO", 60, 3.4f, 0.72f, 1.7f, new Vector3(0.75f, 0.72f, 0.70f)),
        new("LAMINA SOMBRIA", 85, 3.9f, 0.55f, 1.5f, new Vector3(0.60f, 0.28f, 0.85f)),
        new("MARTELO DE GUERRA", 130, 3.6f, 0.95f, 3.2f, new Vector3(0.62f, 0.65f, 0.72f)),
    };
    int _weapon;
    sealed class GroundWeapon { public int Index; public Vector3 Pos; }
    readonly List<GroundWeapon> _groundWeapons = new();
    int _nearWeapon = -1;        // arma ao alcance do jogador (-1 = nenhuma)
    float _pickupMsgTimer;

    // --- jogador ---
    Vector3 _playerPos;          // posição dos pés
    float _velY;
    float _yaw, _pitch;
    float _hp = 100f;
    float _attackCd;
    float _swingTimer;
    float _damageFlash;
    double _lastDamageTime = -100;
    GameState _state = GameState.Menu;
    bool _skipMouse;             // ignora o delta do mouse no 1º frame após capturar o cursor
    int _kills;

    // --- níveis ---
    int _level;
    int _toSpawn;                // monstros que ainda vão surgir neste nível
    float _spawnTimer;
    float _levelBreak;
    double _time;                // tempo de jogo (congela no pause/menu)
    double _realTime;            // tempo real (anima o menu)
    double _titleTimer;

    // --- áudio ---
    AudioEngine _audio = null!;

    // --- gráficos ---
    Shader _shader = null!;
    Shader _hud = null!;
    int _cubeVao, _cubeVbo, _quadVao, _quadVbo;
    float _aspect = 16f / 9f;

    static readonly Vector3 SkyColor = new(0.015f, 0.025f, 0.06f);

    // botões (x, y, largura, altura — frações da tela, origem embaixo/esquerda)
    static readonly Vector4 BtnMenu1 = new(0.37f, 0.50f, 0.26f, 0.088f);
    static readonly Vector4 BtnMenu2 = new(0.37f, 0.39f, 0.26f, 0.088f);
    static readonly Vector4 BtnMenu3 = new(0.37f, 0.28f, 0.26f, 0.088f);
    static readonly Vector4 BtnMenu4 = new(0.37f, 0.17f, 0.26f, 0.088f);
    static readonly Vector4 BtnPrimary = new(0.37f, 0.40f, 0.26f, 0.095f);
    static readonly Vector4 BtnSecondary = new(0.37f, 0.27f, 0.26f, 0.095f);
    static readonly Vector4 BtnBack = new(0.37f, 0.08f, 0.26f, 0.088f);

    const float EyeHeight = 1.6f;
    Vector3 EyePos => _playerPos + new Vector3(0, EyeHeight, 0);
    Vector3 Front => new(
        MathF.Sin(_yaw) * MathF.Cos(_pitch),
        MathF.Sin(_pitch),
        -MathF.Cos(_yaw) * MathF.Cos(_pitch));

    public Game(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws) { }

    protected override void OnLoad()
    {
        base.OnLoad();
        VSync = VSyncMode.On;
        CursorState = CursorState.Normal;

        GL.ClearColor(SkyColor.X, SkyColor.Y, SkyColor.Z, 1f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.Multisample);

        _shader = new Shader(VertexSrc, FragmentSrc);
        _hud = new Shader(HudVertexSrc, HudFragmentSrc);
        BuildCube();
        BuildQuad();
        BuildWorld();
        Restart();

        _audio = new AudioEngine();
        _audio.SetMusicVolume(0.30f);   // mais baixinha no menu
        _audio.PlayMusic();
    }

    void Restart()
    {
        _playerPos = Vector3.Zero;
        _velY = 0;
        _yaw = 0; _pitch = 0;
        _hp = 100f;
        _attackCd = 0; _swingTimer = 0; _damageFlash = 0;
        _kills = 0;
        _level = 0;
        _toSpawn = 0;
        _levelBreak = 1.5f;
        _weapon = 0;
        _pickupMsgTimer = 0;
        _nearWeapon = -1;
        _monsters.Clear();

        // as armas melhores ficam flutuando pelo mapa desde o início
        _groundWeapons.Clear();
        for (int i = 1; i < Weapons.Length; i++)
        {
            float ang = (i - 1) * (MathHelper.TwoPi / 3f) + (float)_rng.NextDouble() * 1.5f;
            float r = 16f + (float)_rng.NextDouble() * 22f;
            _groundWeapons.Add(new GroundWeapon
            {
                Index = i,
                Pos = new Vector3(MathF.Sin(ang) * r, 0, MathF.Cos(ang) * r),
            });
        }
    }

    void StartGame()
    {
        Restart();
        _state = GameState.Playing;
        CursorState = CursorState.Grabbed;
        _skipMouse = true;
        _audio.SetMusicVolume(0.55f);
    }

    void Resume()
    {
        _state = GameState.Playing;
        CursorState = CursorState.Grabbed;
        _skipMouse = true;
        _audio.SetMusicVolume(0.55f);
    }

    void Pause()
    {
        _state = GameState.Paused;
        CursorState = CursorState.Normal;
        _audio.SetMusicVolume(0.25f);
    }

    void ToMenu()
    {
        _state = GameState.Menu;
        CursorState = CursorState.Normal;
        Restart();
        _audio.SetMusicVolume(0.30f);
    }

    Vector2 MouseUv => new(
        MousePosition.X / MathF.Max(1, ClientSize.X),
        1f - MousePosition.Y / MathF.Max(1, ClientSize.Y));

    static bool Contains(Vector4 r, Vector2 p) =>
        p.X >= r.X && p.X <= r.X + r.Z && p.Y >= r.Y && p.Y <= r.Y + r.W;

    bool Clicked(Vector4 r) =>
        MouseState.IsButtonPressed(MouseButton.Left) && Contains(r, MouseUv);

    void BuildWorld()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 52; i++)
        {
            float ang = (float)(rng.NextDouble() * MathHelper.TwoPi);
            float r = 9f + (float)rng.NextDouble() * (ArenaRadius - 6f);
            var pos = new Vector3(MathF.Sin(ang) * r, 0, MathF.Cos(ang) * r);
            bool rock = rng.NextDouble() < 0.3;
            _props.Add(new Prop(
                pos,
                TrunkH: 2.2f + (float)rng.NextDouble() * 1.8f,
                Foliage: 1.6f + (float)rng.NextDouble() * 1.2f,
                IsRock: rock,
                Rot: (float)(rng.NextDouble() * MathHelper.TwoPi)));
        }

        // céu estrelado
        for (int i = 0; i < 110; i++)
        {
            float ang = (float)(rng.NextDouble() * MathHelper.TwoPi);
            float elev = 0.12f + (float)rng.NextDouble() * 0.85f;
            var dir = new Vector3(
                MathF.Cos(elev) * MathF.Sin(ang),
                MathF.Sin(elev),
                MathF.Cos(elev) * MathF.Cos(ang));
            var col = rng.NextDouble() < 0.85
                ? new Vector3(0.85f, 0.88f, 1.0f)
                : new Vector3(1.0f, 0.85f, 0.65f);
            _stars.Add(new Star(dir * 190f, 0.35f + (float)rng.NextDouble() * 0.75f, col));
        }
    }

    void StartLevel(int lvl)
    {
        _level = lvl;
        _toSpawn = 3 + lvl;               // nível 1 = 4 monstros ... nível 12 = 15
        _spawnTimer = 0.6f;
    }

    // monstros surgem do nada, perto do jogador, com um som arrepiante
    void SpawnMonster()
    {
        float ang = (float)(_rng.NextDouble() * MathHelper.TwoPi);
        float dist = 9f + (float)_rng.NextDouble() * 10f;
        var pos = _playerPos + new Vector3(MathF.Sin(ang) * dist, 0, MathF.Cos(ang) * dist);
        var xz = new Vector2(pos.X, pos.Z);
        if (xz.Length > ArenaRadius - 2) { xz = xz.Normalized() * (ArenaRadius - 2); pos = new Vector3(xz.X, 0, xz.Y); }

        MonsterKind kind;
        double roll = _rng.NextDouble();
        if (_level == 1) kind = MonsterKind.Slime;
        else if (_level >= 3 && roll < 0.06 + 0.02 * _level) kind = MonsterKind.Brute;
        else if (roll < 0.5) kind = MonsterKind.Imp;
        else kind = MonsterKind.Slime;

        var m = Monster.Create(kind, pos, _rng);
        m.MaxHp *= 1f + (_level - 1) * 0.07f;   // ficam mais fortes a cada nível
        m.Hp = m.MaxHp;
        m.SpawnScale = 0f;
        _monsters.Add(m);

        _audio.PlaySfx(Sfx.Spawn, pos + new Vector3(0, 1, 0), 0.9f, 0.85f + (float)_rng.NextDouble() * 0.3f);
    }

    protected override void OnUpdateFrame(FrameEventArgs a)
    {
        base.OnUpdateFrame(a);
        float dt = MathF.Min((float)a.Time, 0.05f);
        _realTime += a.Time;

        switch (_state)
        {
            case GameState.Menu:
                if (KeyboardState.IsKeyPressed(Keys.Escape)) { Close(); return; }
                if (KeyboardState.IsKeyPressed(Keys.Enter) || KeyboardState.IsKeyPressed(Keys.Space) || Clicked(BtnMenu1))
                    StartGame();
                else if (Clicked(BtnMenu2)) _state = GameState.Controls;
                else if (Clicked(BtnMenu3)) _state = GameState.About;
                else if (Clicked(BtnMenu4)) { Close(); return; }
                UpdateTitle();
                return;

            case GameState.Controls:
            case GameState.About:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || Clicked(BtnBack))
                    _state = GameState.Menu;
                UpdateTitle();
                return;

            case GameState.Paused:
                if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keys.P) || Clicked(BtnPrimary))
                    Resume();
                else if (Clicked(BtnSecondary))
                    ToMenu();
                UpdateTitle();
                return;

            case GameState.GameOver:
            case GameState.Victory:
                if (KeyboardState.IsKeyPressed(Keys.R) || Clicked(BtnPrimary))
                    StartGame();
                else if (KeyboardState.IsKeyPressed(Keys.Escape) || Clicked(BtnSecondary))
                    ToMenu();
                UpdateTitle();
                return;
        }

        // --- jogando ---
        _time += a.Time;
        if (KeyboardState.IsKeyPressed(Keys.Escape) || KeyboardState.IsKeyPressed(Keys.P)) { Pause(); return; }

        // --- câmera ---
        const float sens = 0.0022f;
        if (_skipMouse)
        {
            _skipMouse = false;
        }
        else
        {
            _yaw += MouseState.Delta.X * sens;
            _pitch = Math.Clamp(_pitch - MouseState.Delta.Y * sens, -1.55f, 1.55f);
        }

        // --- movimento ---
        var forward = new Vector3(MathF.Sin(_yaw), 0, -MathF.Cos(_yaw));
        var right = new Vector3(MathF.Cos(_yaw), 0, MathF.Sin(_yaw));
        var wish = Vector3.Zero;
        if (KeyboardState.IsKeyDown(Keys.W)) wish += forward;
        if (KeyboardState.IsKeyDown(Keys.S)) wish -= forward;
        if (KeyboardState.IsKeyDown(Keys.D)) wish += right;
        if (KeyboardState.IsKeyDown(Keys.A)) wish -= right;
        float speed = KeyboardState.IsKeyDown(Keys.LeftShift) ? 9f : 6f;
        if (wish.LengthSquared > 0) _playerPos += wish.Normalized() * speed * dt;

        // pulo e gravidade
        bool grounded = _playerPos.Y <= 0.001f;
        if (grounded && KeyboardState.IsKeyPressed(Keys.Space)) _velY = 5.4f;
        _velY -= 14f * dt;
        _playerPos.Y = MathF.Max(0, _playerPos.Y + _velY * dt);
        if (_playerPos.Y <= 0) _velY = MathF.Max(0, _velY);

        // colisão com árvores/pedras e limite da arena
        foreach (var p in _props)
        {
            var d = new Vector2(_playerPos.X - p.Pos.X, _playerPos.Z - p.Pos.Z);
            float min = p.IsRock ? 1.1f : 0.75f;
            if (d.LengthSquared < min * min && d.LengthSquared > 0.0001f)
            {
                d = d.Normalized() * min;
                _playerPos.X = p.Pos.X + d.X;
                _playerPos.Z = p.Pos.Z + d.Y;
            }
        }
        var pxz = new Vector2(_playerPos.X, _playerPos.Z);
        if (pxz.Length > ArenaRadius) { pxz = pxz.Normalized() * ArenaRadius; _playerPos.X = pxz.X; _playerPos.Z = pxz.Y; }

        // --- trocar de arma com E (a atual fica flutuando no lugar da outra) ---
        _nearWeapon = -1;
        for (int i = 0; i < _groundWeapons.Count; i++)
        {
            var d = new Vector2(_playerPos.X - _groundWeapons[i].Pos.X, _playerPos.Z - _groundWeapons[i].Pos.Z);
            if (d.Length < 2.2f) { _nearWeapon = i; break; }
        }
        if (_nearWeapon >= 0 && KeyboardState.IsKeyPressed(Keys.E))
        {
            var gw = _groundWeapons[_nearWeapon];
            (gw.Index, _weapon) = (_weapon, gw.Index);
            _pickupMsgTimer = 3f;
            _audio.PlaySfx2D(Sfx.Pickup, 0.8f);
        }
        _pickupMsgTimer = MathF.Max(0, _pickupMsgTimer - dt);

        // --- ataque ---
        var wpn = Weapons[_weapon];
        _attackCd = MathF.Max(0, _attackCd - dt);
        _swingTimer = MathF.Max(0, _swingTimer - dt);
        bool attackPressed = MouseState.IsButtonPressed(MouseButton.Left) || KeyboardState.IsKeyPressed(Keys.F);
        if (attackPressed && _attackCd <= 0)
        {
            _attackCd = wpn.Cooldown;
            _swingTimer = 0.28f;
            _audio.PlaySfx2D(Sfx.Swing, 0.45f, 0.9f + (float)_rng.NextDouble() * 0.2f);
            var front = Front;
            foreach (var m in _monsters)
            {
                if (m.Dying) continue;
                var center = m.Pos + new Vector3(0, 0.6f * m.Scale, 0);
                var to = center - EyePos;
                float dist = to.Length;
                if (dist > wpn.Range + m.Scale * 0.5f) continue;
                if (Vector3.Dot(to.Normalized(), front) < 0.55f) continue;

                m.Hp -= wpn.Damage;
                m.HitFlash = 0.18f;
                var push = new Vector3(to.X, 0, to.Z);
                if (push.LengthSquared > 0.001f) m.Pos += push.Normalized() * wpn.Knockback;
                if (m.Hp <= 0)
                {
                    m.DeathTimer = 0.35f;
                    _kills++;
                    _audio.PlaySfx(Sfx.MonsterDie, center, 0.8f, m.Kind == MonsterKind.Brute ? 0.7f : 1f);
                }
                else
                {
                    _audio.PlaySfx(Sfx.Hit, center, 0.7f, 0.9f + (float)_rng.NextDouble() * 0.2f);
                }
            }
        }

        UpdateMonsters(dt);

        // regeneração
        if (_time - _lastDamageTime > 4.0 && _hp > 0)
            _hp = MathF.Min(100f, _hp + 8f * dt);
        _damageFlash = MathF.Max(0, _damageFlash - dt * 2.5f);

        // --- níveis: monstros pingam "do nada" até completar a cota ---
        if (_toSpawn > 0)
        {
            _spawnTimer -= dt;
            if (_spawnTimer <= 0)
            {
                SpawnMonster();
                _toSpawn--;
                _spawnTimer = MathF.Max(1.2f, 4.5f - _level * 0.25f) * (0.6f + (float)_rng.NextDouble() * 0.8f);
            }
        }
        else if (_monsters.Count == 0)
        {
            _levelBreak -= dt;
            if (_levelBreak <= 0)
            {
                if (_level >= MaxLevel)
                {
                    _state = GameState.Victory;
                    CursorState = CursorState.Normal;
                    _audio.SetMusicVolume(0.20f);
                }
                else
                {
                    StartLevel(_level + 1);
                }
                _levelBreak = 4f;
            }
        }

        if (_hp <= 0)
        {
            _hp = 0;
            _state = GameState.GameOver;
            CursorState = CursorState.Normal;
            _audio.SetMusicVolume(0.20f);
        }

        _audio.UpdateListener(EyePos, Front, Vector3.UnitY);
        UpdateTitle();
    }

    void UpdateMonsters(float dt)
    {
        for (int i = _monsters.Count - 1; i >= 0; i--)
        {
            var m = _monsters[i];
            m.HitFlash = MathF.Max(0, m.HitFlash - dt);

            if (m.Dying)
            {
                m.DeathTimer -= dt;
                if (m.DeathTimer <= 0) _monsters.RemoveAt(i);
                continue;
            }

            // ainda "brotando" do chão
            if (m.SpawnScale < 1f)
            {
                m.SpawnScale = MathF.Min(1f, m.SpawnScale + dt * 2.2f);
                continue;
            }

            var toPlayer = _playerPos - m.Pos;
            toPlayer.Y = 0;
            float dist = toPlayer.Length;

            // rosnados para assustar (posicionais — dá para ouvir de onde vêm)
            m.GrowlTimer -= dt;
            if (m.GrowlTimer <= 0)
            {
                m.GrowlTimer = 4f + (float)_rng.NextDouble() * 7f;
                if (dist < 35f)
                {
                    var mouth = m.Pos + new Vector3(0, 0.7f * m.Scale, 0);
                    if (m.Kind == MonsterKind.Brute)
                        _audio.PlaySfx(Sfx.Roar, mouth, 1f, 0.8f + (float)_rng.NextDouble() * 0.15f);
                    else
                        _audio.PlaySfx(Sfx.Growl, mouth, 0.85f,
                            (m.Kind == MonsterKind.Imp ? 1.25f : 0.9f) + (float)_rng.NextDouble() * 0.2f);
                }
            }

            Vector3 dir;
            if (dist < 28f)
            {
                dir = dist > 0.01f ? toPlayer / dist : Vector3.Zero;
            }
            else
            {
                m.WanderTimer -= dt;
                if (m.WanderTimer <= 0)
                {
                    m.WanderTimer = 1.5f + (float)_rng.NextDouble() * 2.5f;
                    m.WanderAngle = (float)(_rng.NextDouble() * MathHelper.TwoPi);
                }
                dir = new Vector3(MathF.Sin(m.WanderAngle), 0, MathF.Cos(m.WanderAngle)) * 0.35f;
            }

            float stopDist = 0.55f * m.Scale + 0.55f;
            if (dist > stopDist)
                m.Pos += dir * m.Speed * dt;

            if (dir.LengthSquared > 0.001f)
                m.Yaw = MathF.Atan2(-dir.X, -dir.Z);

            // manter dentro da arena
            var mxz = new Vector2(m.Pos.X, m.Pos.Z);
            if (mxz.Length > ArenaRadius) { mxz = mxz.Normalized() * ArenaRadius; m.Pos = new Vector3(mxz.X, m.Pos.Y, mxz.Y); }

            // ataque no jogador
            m.AttackCd = MathF.Max(0, m.AttackCd - dt);
            if (dist < stopDist + 0.35f && m.AttackCd <= 0 && _playerPos.Y < 1.2f)
            {
                m.AttackCd = 1.0f;
                _hp -= m.Damage;
                _damageFlash = 1f;
                _lastDamageTime = _time;
                _audio.PlaySfx2D(Sfx.PlayerHurt, 0.7f);
                var push = -toPlayer;
                push.Y = 0;
                if (push.LengthSquared > 0.001f) _playerPos -= push.Normalized() * 0.8f;
            }
        }
    }

    void UpdateTitle()
    {
        _titleTimer -= 0.016;
        if (_titleTimer > 0) return;
        _titleTimer = 0.25;
        Title = _state switch
        {
            GameState.Menu or GameState.Controls or GameState.About => "Caçador de Monstros 3D",
            GameState.Paused => $"Caçador de Monstros 3D — PAUSADO | HP {(int)_hp} | Nível {_level}/{MaxLevel} | Mortes {_kills}",
            GameState.GameOver => $"Caçador de Monstros 3D — VOCÊ MORREU! Mortes: {_kills}",
            GameState.Victory => $"Caçador de Monstros 3D — VITÓRIA! Mortes: {_kills}",
            _ => $"Caçador de Monstros 3D — HP {(int)_hp} | Nível {_level}/{MaxLevel} | Monstros {_monsters.Count(x => !x.Dying)} | Mortes {_kills}",
        };
    }

    // ------------------------------------------------------------------
    //  Renderização
    // ------------------------------------------------------------------

    protected override void OnRenderFrame(FrameEventArgs a)
    {
        base.OnRenderFrame(a);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();
        var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(75f), _aspect, 0.05f, 300f);

        bool menuLike = _state is GameState.Menu or GameState.Controls or GameState.About;
        Vector3 eye;
        Matrix4 view;
        if (menuLike)
        {
            // câmera orbitando a arena como pano de fundo do menu
            float t = (float)_realTime * 0.12f;
            eye = new Vector3(MathF.Sin(t) * 20f, 7f, MathF.Cos(t) * 20f);
            view = Matrix4.LookAt(eye, new Vector3(0, 2.5f, 0), Vector3.UnitY);
        }
        else
        {
            eye = EyePos;
            view = Matrix4.LookAt(eye, eye + Front, Vector3.UnitY);
        }
        _shader.Set("uView", view);
        _shader.Set("uProj", proj);
        _shader.Set("uCamPos", eye);
        GL.BindVertexArray(_cubeVao);

        // lua e estrelas
        Draw(Matrix4.CreateScale(11f) * Matrix4.CreateTranslation(65f, 95f, -120f),
            new Vector3(0.92f, 0.93f, 0.84f), emissive: 1f);
        foreach (var s in _stars)
            Draw(Matrix4.CreateScale(s.Size) * Matrix4.CreateTranslation(s.Pos), s.Color, emissive: 1f);

        // chão
        Draw(Matrix4.CreateScale(300f, 1f, 300f) * Matrix4.CreateTranslation(0, -0.5f, 0),
            new Vector3(0.30f, 0.52f, 0.24f), noise: 1f);

        // árvores e pedras
        foreach (var p in _props)
        {
            if (p.IsRock)
            {
                Draw(Matrix4.CreateScale(1.6f, 1.1f, 1.4f) * Matrix4.CreateRotationY(p.Rot) *
                     Matrix4.CreateTranslation(p.Pos + new Vector3(0, 0.35f, 0)),
                     new Vector3(0.48f, 0.48f, 0.50f), noise: 1f);
            }
            else
            {
                Draw(Matrix4.CreateScale(0.38f, p.TrunkH, 0.38f) *
                     Matrix4.CreateTranslation(p.Pos + new Vector3(0, p.TrunkH * 0.5f, 0)),
                     new Vector3(0.42f, 0.28f, 0.15f));
                Draw(Matrix4.CreateScale(p.Foliage, p.Foliage * 0.9f, p.Foliage) *
                     Matrix4.CreateRotationY(p.Rot) *
                     Matrix4.CreateTranslation(p.Pos + new Vector3(0, p.TrunkH + p.Foliage * 0.3f, 0)),
                     new Vector3(0.13f, 0.42f, 0.16f), noise: 1f);
            }
        }

        // pilares de pedra no limite da arena
        for (int i = 0; i < 20; i++)
        {
            float ang = i / 20f * MathHelper.TwoPi;
            var pos = new Vector3(MathF.Sin(ang) * (ArenaRadius + 2.5f), 3.0f, MathF.Cos(ang) * (ArenaRadius + 2.5f));
            Draw(Matrix4.CreateScale(2.6f, 7.5f, 2.6f) * Matrix4.CreateRotationY(ang) * Matrix4.CreateTranslation(pos),
                 new Vector3(0.42f, 0.43f, 0.48f), noise: 1f);
        }

        // armas flutuando no mapa (feixe de luz + arma girando)
        foreach (var gw in _groundWeapons)
        {
            var wc = Weapons[gw.Index].Color;
            Draw(Matrix4.CreateScale(0.16f, 9f, 0.16f) * Matrix4.CreateTranslation(gw.Pos + new Vector3(0, 4.5f, 0)),
                 wc, emissive: 0.9f);
            float bobY = 1.2f + MathF.Sin((float)_time * 2f + gw.Index) * 0.25f;
            Draw(Matrix4.CreateScale(0.10f, 0.20f, 0.95f) *
                 Matrix4.CreateRotationY((float)_time * 1.5f + gw.Index * 2f) *
                 Matrix4.CreateTranslation(gw.Pos + new Vector3(0, bobY, 0)),
                 wc, emissive: 0.6f);
        }

        // monstros
        foreach (var m in _monsters)
            DrawMonster(m);

        // arma em primeira pessoa (espaço da câmera, com depth limpo)
        if (_state is GameState.Playing or GameState.Paused)
        {
            GL.Clear(ClearBufferMask.DepthBufferBit);
            _shader.Set("uView", Matrix4.Identity);
            _shader.Set("uCamPos", Vector3.Zero);
            DrawWeapon();
        }

        DrawHud();
        SwapBuffers();
    }

    void Draw(Matrix4 model, Vector3 color, float noise = 0f, float emissive = 0f)
    {
        _shader.Set("uModel", model);
        _shader.Set("uColor", color);
        _shader.Set("uNoise", noise);
        _shader.Set("uEmissive", emissive);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
    }

    void DrawMonster(Monster m)
    {
        float s = m.Scale * MathF.Max(0.02f, m.SpawnScale);
        if (m.Dying) s *= MathF.Max(0.01f, m.DeathTimer / 0.35f);

        float bob = MathF.Abs(MathF.Sin((float)_time * 4f + m.BobPhase)) * 0.12f * s;
        var baseMat = Matrix4.CreateRotationY(m.Yaw) * Matrix4.CreateTranslation(m.Pos + new Vector3(0, bob, 0));

        var color = Vector3.Lerp(m.Color, Vector3.One, MathF.Min(1f, m.HitFlash * 6f));
        var eyeColor = m.Kind switch
        {
            MonsterKind.Imp => new Vector3(1.0f, 0.25f, 0.12f),
            MonsterKind.Brute => new Vector3(0.85f, 0.30f, 1.0f),
            _ => new Vector3(0.70f, 0.95f, 0.30f),
        };

        // corpo
        Draw(Matrix4.CreateScale(1.0f * s, 0.95f * s, 0.85f * s) *
             Matrix4.CreateTranslation(0, 0.55f * s, 0) * baseMat, color,
             emissive: m.HitFlash > 0 ? 0.6f : 0f);
        // olhos brilhando no escuro (frente = -Z)
        Draw(Matrix4.CreateScale(0.14f * s, 0.20f * s, 0.10f * s) *
             Matrix4.CreateTranslation(-0.20f * s, 0.68f * s, -0.44f * s) * baseMat, eyeColor, emissive: 1f);
        Draw(Matrix4.CreateScale(0.14f * s, 0.20f * s, 0.10f * s) *
             Matrix4.CreateTranslation(0.20f * s, 0.68f * s, -0.44f * s) * baseMat, eyeColor, emissive: 1f);

        // barra de vida flutuante
        if (!m.Dying && m.Hp < m.MaxHp)
        {
            float frac = m.Hp / m.MaxHp;
            float w = 1.0f * s;
            Draw(Matrix4.CreateScale(w, 0.09f, 0.02f) *
                 Matrix4.CreateTranslation(m.Pos + new Vector3(0, 1.25f * s + bob, 0)),
                 new Vector3(0.12f, 0.05f, 0.05f), emissive: 0.8f);
            Draw(Matrix4.CreateScale(w * frac, 0.10f, 0.03f) *
                 Matrix4.CreateTranslation(m.Pos + new Vector3(-w * (1 - frac) * 0.5f, 1.25f * s + bob, 0)),
                 new Vector3(0.85f, 0.15f, 0.12f), emissive: 0.8f);
        }
    }

    void DrawWeapon()
    {
        // animação do golpe
        float swing = 0f;
        if (_swingTimer > 0)
        {
            float p = 1f - _swingTimer / 0.28f;
            swing = MathF.Sin(p * MathF.PI) * -1.15f;
        }
        var rot = Matrix4.CreateRotationX(0.22f + swing) * Matrix4.CreateRotationY(-0.14f);
        var place = rot * Matrix4.CreateTranslation(0.36f, -0.38f, -0.55f);

        var wpn = Weapons[_weapon];
        var wood = new Vector3(0.35f, 0.22f, 0.10f);
        var gold = new Vector3(0.80f, 0.65f, 0.20f);
        var darkWood = new Vector3(0.20f, 0.12f, 0.08f);

        switch (_weapon)
        {
            case 0: // espada
                Draw(Matrix4.CreateScale(0.045f, 0.09f, 0.85f) * Matrix4.CreateTranslation(0, 0, -0.50f) * place, wpn.Color);
                Draw(Matrix4.CreateScale(0.22f, 0.05f, 0.06f) * Matrix4.CreateTranslation(0, 0, -0.05f) * place, gold);
                Draw(Matrix4.CreateScale(0.05f, 0.06f, 0.22f) * Matrix4.CreateTranslation(0, 0, 0.10f) * place, wood);
                break;
            case 1: // machado
                Draw(Matrix4.CreateScale(0.05f, 0.05f, 0.95f) * Matrix4.CreateTranslation(0, 0, -0.35f) * place, wood);
                Draw(Matrix4.CreateScale(0.06f, 0.32f, 0.22f) * Matrix4.CreateTranslation(0, 0.02f, -0.76f) * place, wpn.Color);
                break;
            case 2: // lâmina sombria
                Draw(Matrix4.CreateScale(0.05f, 0.10f, 1.05f) * Matrix4.CreateTranslation(0, 0, -0.60f) * place, wpn.Color, emissive: 0.45f);
                Draw(Matrix4.CreateScale(0.22f, 0.05f, 0.06f) * Matrix4.CreateTranslation(0, 0, -0.05f) * place, new Vector3(0.25f, 0.10f, 0.35f));
                Draw(Matrix4.CreateScale(0.05f, 0.06f, 0.22f) * Matrix4.CreateTranslation(0, 0, 0.10f) * place, darkWood);
                break;
            case 3: // martelo de guerra
                Draw(Matrix4.CreateScale(0.06f, 0.06f, 0.90f) * Matrix4.CreateTranslation(0, 0, -0.30f) * place, darkWood);
                Draw(Matrix4.CreateScale(0.30f, 0.20f, 0.20f) * Matrix4.CreateTranslation(0, 0, -0.76f) * place, wpn.Color);
                break;
        }
    }

    // ------------------------------------------------------------------
    //  HUD e menus
    // ------------------------------------------------------------------

    void DrawHud()
    {
        GL.Disable(EnableCap.DepthTest);
        _hud.Use();
        GL.BindVertexArray(_quadVao);

        if (_state is GameState.Playing or GameState.Paused)
        {
            if (_state == GameState.Playing)
            {
                // mira
                Rect(0.5f - 0.009f, 0.5f - 0.0022f, 0.018f, 0.0044f, new Vector4(1, 1, 1, 0.85f));
                Rect(0.5f - 0.0028f, 0.5f - 0.014f, 0.0056f, 0.028f, new Vector4(1, 1, 1, 0.85f));
            }

            // barra de vida
            float frac = Math.Clamp(_hp / 100f, 0, 1);
            var hpColor = Vector4.Lerp(new Vector4(0.85f, 0.15f, 0.1f, 0.95f), new Vector4(0.2f, 0.8f, 0.25f, 0.95f), frac);
            Rect(0.028f, 0.045f, 0.304f, 0.040f, new Vector4(0, 0, 0, 0.55f));
            Rect(0.030f, 0.050f, 0.30f * frac, 0.030f, hpColor);

            // recarga do ataque
            float cd = 1f - Math.Clamp(_attackCd / Weapons[_weapon].Cooldown, 0, 1);
            Rect(0.030f, 0.024f, 0.30f * cd, 0.012f, new Vector4(0.95f, 0.85f, 0.25f, 0.9f));

            // arma atual e nível
            Text(Weapons[_weapon].Name, 0.84f, 0.055f, 0.0032f, new Vector4(0.9f, 0.88f, 0.8f, 0.9f));
            Text($"NIVEL {_level} DE {MaxLevel}", 0.5f, 0.955f, 0.0038f, new Vector4(0.85f, 0.82f, 0.9f, 0.9f));

            // um quadradinho por monstro vivo (canto superior esquerdo)
            int alive = _monsters.Count(x => !x.Dying);
            for (int i = 0; i < alive && i < 30; i++)
                Rect(0.028f + i * 0.024f, 0.905f, 0.016f, 0.028f, new Vector4(0.85f, 0.2f, 0.15f, 0.9f));

            // aviso para trocar de arma quando estiver perto de uma
            if (_state == GameState.Playing && _nearWeapon >= 0)
                Text($"E - PEGAR {Weapons[_groundWeapons[_nearWeapon].Index].Name}", 0.5f, 0.40f, 0.0045f,
                    new Vector4(0.95f, 0.85f, 0.3f, 0.7f + 0.3f * MathF.Sin((float)_realTime * 6f)));

            // aviso de arma nova coletada
            if (_pickupMsgTimer > 0)
            {
                float alpha = MathF.Min(1f, _pickupMsgTimer);
                Text($"NOVA ARMA - {Weapons[_weapon].Name}", 0.5f, 0.62f, 0.005f,
                    new Vector4(0.95f, 0.85f, 0.3f, alpha));
            }

            // flash de dano
            if (_state == GameState.Playing && _damageFlash > 0)
                Rect(0, 0, 1, 1, new Vector4(0.8f, 0.05f, 0.05f, _damageFlash * 0.35f));
        }

        switch (_state)
        {
            case GameState.Menu:
                Rect(0, 0, 1, 1, new Vector4(0.02f, 0.03f, 0.08f, 0.45f));
                Text("CACADOR DE", 0.5f, 0.86f, 0.010f, new Vector4(0.85f, 0.20f, 0.15f, 1f));
                Text("MONSTROS 3D", 0.5f, 0.75f, 0.010f, new Vector4(0.90f, 0.90f, 0.95f, 1f));
                DrawButton(BtnMenu1, "INICIAR");
                DrawButton(BtnMenu2, "COMANDOS");
                DrawButton(BtnMenu3, "SOBRE");
                DrawButton(BtnMenu4, "SAIR");
                break;

            case GameState.Controls:
            {
                Rect(0, 0, 1, 1, new Vector4(0.02f, 0.03f, 0.08f, 0.75f));
                Text("COMANDOS", 0.5f, 0.88f, 0.009f, new Vector4(0.95f, 0.85f, 0.3f, 1f));
                string[] lines =
                {
                    "WASD - ANDAR",
                    "MOUSE - OLHAR",
                    "SHIFT ESQUERDO - CORRER",
                    "ESPACO - PULAR",
                    "CLIQUE OU F - ATACAR",
                    "E - TROCAR DE ARMA",
                    "ESC OU P - PAUSAR",
                    "SIGA OS FEIXES DE LUZ",
                };
                for (int i = 0; i < lines.Length; i++)
                    Text(lines[i], 0.5f, 0.76f - i * 0.075f, 0.0048f, new Vector4(0.92f, 0.92f, 0.95f, 1f));
                DrawButton(BtnBack, "VOLTAR");
                break;
            }

            case GameState.About:
            {
                Rect(0, 0, 1, 1, new Vector4(0.02f, 0.03f, 0.08f, 0.75f));
                Text("SOBRE", 0.5f, 0.88f, 0.009f, new Vector4(0.95f, 0.85f, 0.3f, 1f));
                string[] lines =
                {
                    "CACADOR DE MONSTROS 3D",
                    "UMA AVENTURA DE TERROR NOTURNA",
                    "SOBREVIVA AOS 12 NIVEIS",
                    "MONSTROS SURGEM DO NADA",
                    "ARMAS PODEROSAS TE ESPERAM",
                    "FEITO EM C SHARP COM OPENTK",
                    "MUSICA E SONS GERADOS POR CODIGO",
                };
                for (int i = 0; i < lines.Length; i++)
                    Text(lines[i], 0.5f, 0.76f - i * 0.075f, 0.0042f, new Vector4(0.92f, 0.92f, 0.95f, 1f));
                DrawButton(BtnBack, "VOLTAR");
                break;
            }

            case GameState.Paused:
                Rect(0, 0, 1, 1, new Vector4(0.02f, 0.02f, 0.05f, 0.5f));
                Text("PAUSADO", 0.5f, 0.66f, 0.013f, new Vector4(0.97f, 0.97f, 1f, 1f));
                DrawButton(BtnPrimary, "CONTINUAR");
                DrawButton(BtnSecondary, "SAIR");
                break;

            case GameState.GameOver:
                Rect(0, 0, 1, 1, new Vector4(0.25f, 0f, 0f, 0.6f));
                Text("VOCE MORREU", 0.5f, 0.70f, 0.012f, new Vector4(0.97f, 0.92f, 0.92f, 1f));
                Text($"MORTES {_kills}  NIVEL {_level}", 0.5f, 0.595f, 0.006f, new Vector4(0.95f, 0.85f, 0.75f, 1f));
                DrawButton(BtnPrimary, "REINICIAR");
                DrawButton(BtnSecondary, "SAIR");
                break;

            case GameState.Victory:
                Rect(0, 0, 1, 1, new Vector4(0.02f, 0.10f, 0.03f, 0.6f));
                Text("VOCE VENCEU", 0.5f, 0.70f, 0.012f, new Vector4(0.75f, 0.95f, 0.55f, 1f));
                Text($"12 NIVEIS - {_kills} MONSTROS MORTOS", 0.5f, 0.595f, 0.005f, new Vector4(0.85f, 0.95f, 0.80f, 1f));
                DrawButton(BtnPrimary, "JOGAR DE NOVO");
                DrawButton(BtnSecondary, "SAIR");
                break;
        }

        GL.Enable(EnableCap.DepthTest);
    }

    void DrawButton(Vector4 r, string label)
    {
        bool hov = Contains(r, MouseUv);
        float bx = 0.0035f, by = 0.0035f * _aspect;
        Rect(r.X - bx, r.Y - by, r.Z + 2 * bx, r.W + 2 * by, new Vector4(0.9f, 0.9f, 0.95f, hov ? 0.9f : 0.35f));
        Rect(r.X, r.Y, r.Z, r.W, hov ? new Vector4(0.92f, 0.74f, 0.18f, 0.97f) : new Vector4(0.12f, 0.13f, 0.19f, 0.92f));

        float px = MathF.Min(r.Z * 0.75f / (label.Length * 6f), r.W * 0.5f / (5f * _aspect));
        var tcol = hov ? new Vector4(0.08f, 0.08f, 0.10f, 1f) : new Vector4(0.95f, 0.95f, 0.97f, 1f);
        Text(label, r.X + r.Z / 2f, r.Y + r.W / 2f, px, tcol);
    }

    // desenha texto com a fonte pixelada 5x5 (cx/cy = centro, px = tamanho do "pixel")
    void Text(string s, float cx, float cy, float px, Vector4 color)
    {
        float py = px * _aspect;
        float width = s.Length * 6f * px - px;
        float x0 = cx - width / 2f;
        float y0 = cy - 2.5f * py;
        for (int i = 0; i < s.Length; i++)
        {
            if (!Font.TryGetValue(s[i], out var g)) continue;
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 5; col++)
                    if (g[row][col] == '1')
                        Rect(x0 + i * 6f * px + col * px, y0 + (4 - row) * py, px * 0.92f, py * 0.92f, color);
        }
    }

    static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = new[] { "01110", "10001", "11111", "10001", "10001" },
        ['B'] = new[] { "11110", "10001", "11110", "10001", "11110" },
        ['C'] = new[] { "01111", "10000", "10000", "10000", "01111" },
        ['D'] = new[] { "11110", "10001", "10001", "10001", "11110" },
        ['E'] = new[] { "11111", "10000", "11110", "10000", "11111" },
        ['F'] = new[] { "11111", "10000", "11110", "10000", "10000" },
        ['G'] = new[] { "01111", "10000", "10011", "10001", "01110" },
        ['H'] = new[] { "10001", "10001", "11111", "10001", "10001" },
        ['I'] = new[] { "11111", "00100", "00100", "00100", "11111" },
        ['J'] = new[] { "00111", "00010", "00010", "10010", "01100" },
        ['K'] = new[] { "10010", "10100", "11000", "10100", "10010" },
        ['L'] = new[] { "10000", "10000", "10000", "10000", "11111" },
        ['M'] = new[] { "10001", "11011", "10101", "10001", "10001" },
        ['N'] = new[] { "10001", "11001", "10101", "10011", "10001" },
        ['O'] = new[] { "01110", "10001", "10001", "10001", "01110" },
        ['P'] = new[] { "11110", "10001", "11110", "10000", "10000" },
        ['Q'] = new[] { "01110", "10001", "10001", "10010", "01101" },
        ['R'] = new[] { "11110", "10001", "11110", "10010", "10001" },
        ['S'] = new[] { "01111", "10000", "01110", "00001", "11110" },
        ['T'] = new[] { "11111", "00100", "00100", "00100", "00100" },
        ['U'] = new[] { "10001", "10001", "10001", "10001", "01110" },
        ['V'] = new[] { "10001", "10001", "10001", "01010", "00100" },
        ['W'] = new[] { "10001", "10001", "10101", "10101", "01010" },
        ['X'] = new[] { "10001", "01010", "00100", "01010", "10001" },
        ['Y'] = new[] { "10001", "01010", "00100", "00100", "00100" },
        ['Z'] = new[] { "11111", "00010", "00100", "01000", "11111" },
        ['-'] = new[] { "00000", "00000", "01110", "00000", "00000" },
        ['0'] = new[] { "01110", "10001", "10101", "10001", "01110" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "01110" },
        ['2'] = new[] { "11110", "00001", "01110", "10000", "11111" },
        ['3'] = new[] { "11110", "00001", "01110", "00001", "11110" },
        ['4'] = new[] { "00010", "00110", "01010", "11111", "00010" },
        ['5'] = new[] { "11111", "10000", "11110", "00001", "11110" },
        ['6'] = new[] { "01110", "10000", "11110", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "00100" },
        ['8'] = new[] { "01110", "10001", "01110", "10001", "01110" },
        ['9'] = new[] { "01110", "10001", "01111", "00001", "01110" },
    };

    void Rect(float x, float y, float w, float h, Vector4 color)
    {
        _hud.Set("uRect", new Vector4(x, y, w, h));
        _hud.Set("uColor", color);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        if (e.Height > 0) _aspect = e.Width / (float)e.Height;
    }

    protected override void OnUnload()
    {
        _audio.Dispose();
        _shader.Dispose();
        _hud.Dispose();
        GL.DeleteBuffer(_cubeVbo);
        GL.DeleteBuffer(_quadVbo);
        GL.DeleteVertexArray(_cubeVao);
        GL.DeleteVertexArray(_quadVao);
        base.OnUnload();
    }

    // ------------------------------------------------------------------
    //  Geometria e shaders
    // ------------------------------------------------------------------

    void BuildCube()
    {
        float[] v =
        {
            // posição            // normal
            -0.5f,-0.5f,-0.5f,  0f, 0f,-1f,
             0.5f,-0.5f,-0.5f,  0f, 0f,-1f,
             0.5f, 0.5f,-0.5f,  0f, 0f,-1f,
             0.5f, 0.5f,-0.5f,  0f, 0f,-1f,
            -0.5f, 0.5f,-0.5f,  0f, 0f,-1f,
            -0.5f,-0.5f,-0.5f,  0f, 0f,-1f,

            -0.5f,-0.5f, 0.5f,  0f, 0f, 1f,
             0.5f,-0.5f, 0.5f,  0f, 0f, 1f,
             0.5f, 0.5f, 0.5f,  0f, 0f, 1f,
             0.5f, 0.5f, 0.5f,  0f, 0f, 1f,
            -0.5f, 0.5f, 0.5f,  0f, 0f, 1f,
            -0.5f,-0.5f, 0.5f,  0f, 0f, 1f,

            -0.5f, 0.5f, 0.5f, -1f, 0f, 0f,
            -0.5f, 0.5f,-0.5f, -1f, 0f, 0f,
            -0.5f,-0.5f,-0.5f, -1f, 0f, 0f,
            -0.5f,-0.5f,-0.5f, -1f, 0f, 0f,
            -0.5f,-0.5f, 0.5f, -1f, 0f, 0f,
            -0.5f, 0.5f, 0.5f, -1f, 0f, 0f,

             0.5f, 0.5f, 0.5f,  1f, 0f, 0f,
             0.5f, 0.5f,-0.5f,  1f, 0f, 0f,
             0.5f,-0.5f,-0.5f,  1f, 0f, 0f,
             0.5f,-0.5f,-0.5f,  1f, 0f, 0f,
             0.5f,-0.5f, 0.5f,  1f, 0f, 0f,
             0.5f, 0.5f, 0.5f,  1f, 0f, 0f,

            -0.5f,-0.5f,-0.5f,  0f,-1f, 0f,
             0.5f,-0.5f,-0.5f,  0f,-1f, 0f,
             0.5f,-0.5f, 0.5f,  0f,-1f, 0f,
             0.5f,-0.5f, 0.5f,  0f,-1f, 0f,
            -0.5f,-0.5f, 0.5f,  0f,-1f, 0f,
            -0.5f,-0.5f,-0.5f,  0f,-1f, 0f,

            -0.5f, 0.5f,-0.5f,  0f, 1f, 0f,
             0.5f, 0.5f,-0.5f,  0f, 1f, 0f,
             0.5f, 0.5f, 0.5f,  0f, 1f, 0f,
             0.5f, 0.5f, 0.5f,  0f, 1f, 0f,
            -0.5f, 0.5f, 0.5f,  0f, 1f, 0f,
            -0.5f, 0.5f,-0.5f,  0f, 1f, 0f,
        };
        _cubeVao = GL.GenVertexArray();
        GL.BindVertexArray(_cubeVao);
        _cubeVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _cubeVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
    }

    void BuildQuad()
    {
        float[] v = { 0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1 };
        _quadVao = GL.GenVertexArray();
        GL.BindVertexArray(_quadVao);
        _quadVbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
    }

    const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProj;
        out vec3 vNormal;
        out vec3 vWorldPos;
        void main()
        {
            vec4 world = vec4(aPos, 1.0) * uModel;
            vWorldPos = world.xyz;
            vNormal = aNormal * mat3(uModel);
            gl_Position = world * uView * uProj;
        }
        """;

    const string FragmentSrc = """
        #version 330 core
        in vec3 vNormal;
        in vec3 vWorldPos;
        uniform vec3 uColor;
        uniform vec3 uCamPos;
        uniform float uNoise;
        uniform float uEmissive;
        out vec4 FragColor;
        void main()
        {
            vec3 n = normalize(vNormal);

            // luar frio e fraco
            vec3 moonDir = normalize(vec3(-0.35, 1.0, 0.25));
            float diff = max(dot(n, moonDir), 0.0);
            vec3 col = uColor * (0.10 + 0.26 * diff) * vec3(0.55, 0.65, 0.95);

            // lanterna do jogador (luz quente que enfraquece com a distancia)
            vec3 toCam = uCamPos - vWorldPos;
            float d = length(toCam);
            float lantern = min(1.3, 6.0 / (1.0 + 0.30 * d * d));
            float facing = max(dot(n, normalize(toCam)), 0.0);
            col += uColor * lantern * (0.25 + 0.75 * facing) * vec3(1.0, 0.82, 0.55);

            col *= 1.0 + uNoise * 0.12 * sin(vWorldPos.x * 1.7) * sin(vWorldPos.z * 1.9);

            float fog = smoothstep(18.0, 70.0, d);
            col = mix(col, vec3(0.015, 0.025, 0.06), fog);

            // objetos emissivos (lua, estrelas, olhos) ignoram luz e nevoa
            col = mix(col, uColor, uEmissive);
            FragColor = vec4(col, 1.0);
        }
        """;

    const string HudVertexSrc = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        uniform vec4 uRect;
        void main()
        {
            vec2 p = uRect.xy + aPos * uRect.zw;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    const string HudFragmentSrc = """
        #version 330 core
        uniform vec4 uColor;
        out vec4 FragColor;
        void main() { FragColor = uColor; }
        """;
}
