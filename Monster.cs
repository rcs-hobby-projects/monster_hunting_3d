using OpenTK.Mathematics;

namespace MonsterHunt;

public enum MonsterKind { Slime, Imp, Brute }

public sealed class Monster
{
    public MonsterKind Kind;
    public Vector3 Pos;
    public float Hp;
    public float MaxHp;
    public float Speed;
    public float Scale;
    public int Damage;
    public Vector3 Color;

    public float Yaw;
    public float AttackCd;
    public float HitFlash;
    public float WanderTimer;
    public float WanderAngle;
    public float BobPhase;
    public float GrowlTimer;     // tempo até o próximo rosnado
    public float SpawnScale = 1; // 0→1 enquanto "brota" do chão ao surgir do nada

    // -1 = vivo; >= 0 = animação de morte (conta até zero e some)
    public float DeathTimer = -1f;
    public bool Dying => DeathTimer >= 0f;

    public static Monster Create(MonsterKind kind, Vector3 pos, Random rng)
    {
        var m = new Monster
        {
            Kind = kind,
            Pos = pos,
            WanderAngle = (float)(rng.NextDouble() * MathHelper.TwoPi),
            BobPhase = (float)(rng.NextDouble() * MathHelper.TwoPi),
            GrowlTimer = 1.5f + (float)rng.NextDouble() * 5f,
        };
        switch (kind)
        {
            case MonsterKind.Slime:
                m.MaxHp = 60; m.Speed = 2.4f; m.Damage = 8; m.Scale = 0.85f;
                m.Color = new Vector3(0.35f, 0.75f, 0.30f);
                break;
            case MonsterKind.Imp:
                m.MaxHp = 35; m.Speed = 4.4f; m.Damage = 6; m.Scale = 0.65f;
                m.Color = new Vector3(0.85f, 0.25f, 0.20f);
                break;
            case MonsterKind.Brute:
                m.MaxHp = 150; m.Speed = 1.7f; m.Damage = 18; m.Scale = 1.55f;
                m.Color = new Vector3(0.55f, 0.25f, 0.70f);
                break;
        }
        m.Hp = m.MaxHp;
        return m;
    }
}
