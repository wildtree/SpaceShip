namespace SpaceShip;

// ==================== Constants ====================
internal static class C
{
    public const float SPACE_SIZE       = 1024.0f;
    public const float SHIP_RADIUS      = 8.0f;
    public const float RING_RADIUS      = 32.0f;
    public const float RING_TUBE_RADIUS = 2.0f;
    public const float MAIN_ACCEL       = 40.0f;
    public const float BRAKE_ACCEL      = 40.0f;
    public const float ROTATION_SPEED   = 1.8f;
    public const float DRAG_K           = 0.0f;
    public const float FUEL_MAIN        = 20.0f;
    public const float FUEL_BRAKE       = 40.0f;
    public const float FUEL_ROTATE      = 4.0f;
    public const float INITIAL_FUEL     = 1000.0f;
    public const int   STAR_COUNT       = 800;
    public const int   RING_SEGMENTS    = 64;
    public const int   TUBE_SEGMENTS    = 10;

    public const float RING_TIME_LIMIT  = 30.0f;
    public const int   RINGS_PER_STAGE  = 5;
    public const int   RING_BASE_SCORE  = 100;
    public const int   RING_TIME_BONUS  = 5;
    public const float EXPLODE_DURATION = 2.0f;
    public const float STAGE_CLEAR_WAIT = 3.0f;
    public const float TITLE_FLIP_SEC   = 5.0f;
    public const int   HISCORE_COUNT    = 5;
    public const float COUNTDOWN_START  = 5.0f;

    public const int   WINDOW_WIDTH     = 640;
    public const int   WINDOW_HEIGHT    = 400;
    public const float FOV_DEG          = 75.0f;
    public const float NEAR_PLANE       = 0.5f;
    public const float FAR_PLANE        = 2000.0f;

    public const float WARN_HORIZON     = 5.0f;
    public const float WARN_DT_STEP     = 0.05f;
}

// ==================== Vec3 ====================
internal struct Vec3
{
    public float X, Y, Z;

    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static Vec3 Add(Vec3 a, Vec3 b)   => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 Sub(Vec3 a, Vec3 b)   => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 Scale(Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
    public static float Dot(Vec3 a, Vec3 b)  => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static float Len(Vec3 a) => MathF.Sqrt(Dot(a, a));

    public static Vec3 Norm(Vec3 a)
    {
        float l = Len(a);
        return (l < 1e-6f) ? new Vec3(0, 0, 1) : Scale(a, 1.0f / l);
    }

    // Rodrigues rotation: rotate v around axis by angle radians
    public static Vec3 Rotate(Vec3 v, Vec3 axis, float angle)
    {
        Vec3 n = Norm(axis);
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return Add(Add(Scale(v, c), Scale(Cross(n, v), s)),
                   Scale(n, Dot(n, v) * (1.0f - c)));
    }

    // Wrap coordinates into [0, SPACE_SIZE)
    public static Vec3 Wrap(Vec3 p)
    {
        float ss = C.SPACE_SIZE;
        p.X = p.X % ss; if (p.X < 0) p.X += ss;
        p.Y = p.Y % ss; if (p.Y < 0) p.Y += ss;
        p.Z = p.Z % ss; if (p.Z < 0) p.Z += ss;
        return p;
    }

    // Shortest-path delta in toroidal space
    public static Vec3 TorusDelta(Vec3 from, Vec3 to)
    {
        float hs = C.SPACE_SIZE * 0.5f;
        Vec3 d = Sub(to, from);
        if (d.X >  hs) d.X -= C.SPACE_SIZE;
        if (d.X < -hs) d.X += C.SPACE_SIZE;
        if (d.Y >  hs) d.Y -= C.SPACE_SIZE;
        if (d.Y < -hs) d.Y += C.SPACE_SIZE;
        if (d.Z >  hs) d.Z -= C.SPACE_SIZE;
        if (d.Z < -hs) d.Z += C.SPACE_SIZE;
        return d;
    }
}

// ==================== Ring ====================
internal struct Ring
{
    public Vec3  Pos;
    public Vec3  Normal;
    public Vec3  Up;
    public float RotSpeed;
    public Vec3  RotAxis;
    public float MoveSpeed;
    public Vec3  MoveDir;
    public int   ColorType;  // 0=gold, 1=cyan(rotating), 2=magenta(moving)
}

// ==================== Enums ====================
internal enum GameMode
{
    Easy   = 0,
    Normal = 1,
    Hard   = 2
}

internal enum ShipType
{
    Standard = 0,
    Agile    = 1,
    Boost    = 2
}

internal enum GameStateEnum
{
    Title,
    Ranking,
    StageSelect,  // コナミコマンド後のステージ選択
    ModeSelect,
    ShipSelect,
    BonusIntro,   // ボーナスステージ開始前の説明画面
    Countdown,
    Playing,
    Exploding,
    Entry,
    GameOver,
    StageClear
}

// ==================== HiScore ====================
internal struct HiScore
{
    public string Initials; // 3 chars
    public int    Score;
    public int    Stage;
}
