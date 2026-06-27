#pragma warning disable CS0618  // Silk.NET.OpenGL.Legacy の固定機能API は意図的に使用

using System;
using Silk.NET.OpenGL.Legacy;
using static SDL3.SDL;

// Top-level entry point
SpaceShip.Game.Run();

namespace SpaceShip
{

internal static unsafe class Game
{
    private static readonly Vec3[] Stars = new Vec3[C.STAR_COUNT];

    private static void InitStars()
    {
        for (int i = 0; i < C.STAR_COUNT; i++)
        {
            Stars[i] = new Vec3(
                (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)),
                (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)),
                (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)));
        }
    }

    private static void SpawnRing(ref Ring ring, int ringNum, int stage)
    {
        ring.Pos = new Vec3(
            (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)),
            (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)),
            (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)));

        float theta = (float)Random.Shared.NextDouble() * 2.0f * MathF.PI;
        float phi   = MathF.Acos(2.0f * (float)Random.Shared.NextDouble() - 1.0f);
        ring.Normal = Vec3.Norm(new Vec3(
            MathF.Sin(phi) * MathF.Cos(theta),
            MathF.Sin(phi) * MathF.Sin(theta),
            MathF.Cos(phi)));

        Vec3 arb = (MathF.Abs(ring.Normal.Y) < 0.9f) ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
        ring.Up = Vec3.Norm(Vec3.Cross(Vec3.Norm(Vec3.Cross(ring.Normal, arb)), ring.Normal));

        bool shouldRotate = (stage >= 2) && (ringNum >= 7 - stage);
        ring.RotSpeed = shouldRotate ? (MathF.PI / 30.0f) : 0.0f;
        ring.RotAxis  = shouldRotate
            ? Vec3.Norm(Vec3.Cross(ring.Normal, ring.Up))
            : new Vec3(0, 1, 0);

        bool shouldMove = (stage >= 7) && (ringNum >= 12 - stage);
        ring.MoveSpeed = shouldMove ? 50.0f : 0.0f;
        if (shouldMove)
        {
            float mt = (float)Random.Shared.NextDouble() * 2.0f * MathF.PI;
            float mp = MathF.Acos(2.0f * (float)Random.Shared.NextDouble() - 1.0f);
            ring.MoveDir = Vec3.Norm(new Vec3(
                MathF.Sin(mp) * MathF.Cos(mt),
                MathF.Sin(mp) * MathF.Sin(mt),
                MathF.Cos(mp)));
        }
        else
        {
            ring.MoveDir = new Vec3(0, 0, 0);
        }
        ring.ColorType = shouldMove ? 2 : (shouldRotate ? 1 : 0);
    }

    private static int CheckRingPass(GameState gs)
    {
        Vec3  dCurr    = Vec3.TorusDelta(gs.Pos,     gs.Ring.Pos);
        Vec3  dPrev    = Vec3.TorusDelta(gs.PrevPos, gs.Ring.Pos);
        float sideCurr = Vec3.Dot(dCurr, gs.Ring.Normal);
        float sidePrev = Vec3.Dot(dPrev, gs.Ring.Normal);
        if ((sideCurr >= 0.0f) == (sidePrev >= 0.0f)) return 0;

        float t       = sidePrev / (sidePrev - sideCurr);
        Vec3  crossPt = Vec3.Add(gs.PrevPos, Vec3.Scale(Vec3.Sub(gs.Pos, gs.PrevPos), t));
        Vec3  toRing  = Vec3.TorusDelta(crossPt, gs.Ring.Pos);
        float along   = Vec3.Dot(toRing, gs.Ring.Normal);
        Vec3  inPlane = Vec3.Sub(toRing, Vec3.Scale(gs.Ring.Normal, along));
        float d       = Vec3.Len(inPlane);
        if (d > C.RING_RADIUS) return 0;
        return (d <= 8.0f) ? 2 : 1;
    }

    private static bool CheckRingHit(GameState gs)
    {
        Vec3  raw  = Vec3.Sub(gs.Ring.Pos, gs.Pos);
        float hitR = C.RING_TUBE_RADIUS + C.SHIP_RADIUS;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            Vec3 d = new(raw.X + dx * C.SPACE_SIZE,
                         raw.Y + dy * C.SPACE_SIZE,
                         raw.Z + dz * C.SPACE_SIZE);
            if (Vec3.Len(d) > C.SPACE_SIZE * 0.6f) continue;
            Vec3  s      = Vec3.Scale(d, -1.0f);
            float alongN = Vec3.Dot(s, gs.Ring.Normal);
            Vec3  ip     = Vec3.Sub(s, Vec3.Scale(gs.Ring.Normal, alongN));
            float r      = Vec3.Len(ip);
            float dist   = MathF.Sqrt((r - C.RING_RADIUS) * (r - C.RING_RADIUS) + alongN * alongN);
            if (dist < hitR) return true;
        }
        return false;
    }

    private static bool PredictCollision(GameState gs)
    {
        float hitR = C.RING_TUBE_RADIUS + C.SHIP_RADIUS;
        for (float t = C.WARN_DT_STEP; t <= C.WARN_HORIZON; t += C.WARN_DT_STEP)
        {
            Vec3 spos  = Vec3.Wrap(Vec3.Add(gs.Pos, Vec3.Scale(gs.Vel, t)));
            Vec3 rpos  = gs.Ring.Pos;
            Vec3 rnorm = gs.Ring.Normal;
            if (gs.Ring.MoveSpeed > 0.0f)
                rpos = Vec3.Wrap(Vec3.Add(rpos, Vec3.Scale(gs.Ring.MoveDir, gs.Ring.MoveSpeed * t)));
            if (gs.Ring.RotSpeed != 0.0f)
                rnorm = Vec3.Norm(Vec3.Rotate(rnorm, gs.Ring.RotAxis, gs.Ring.RotSpeed * t));

            Vec3 raw = Vec3.Sub(rpos, spos);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Vec3  d    = new(raw.X + dx * C.SPACE_SIZE, raw.Y + dy * C.SPACE_SIZE, raw.Z + dz * C.SPACE_SIZE);
                if (Vec3.Len(d) > C.SPACE_SIZE * 0.6f) continue;
                Vec3  s    = Vec3.Scale(d, -1.0f);
                float an   = Vec3.Dot(s, rnorm);
                float r    = Vec3.Len(Vec3.Sub(s, Vec3.Scale(rnorm, an)));
                float dist = MathF.Sqrt((r - C.RING_RADIUS) * (r - C.RING_RADIUS) + an * an);
                if (dist < hitR) return true;
            }
        }
        return false;
    }

    public static int Run()
    {
        HiScoreManager.Load();
        AudioSystem.Init();

        if (!Init(InitFlags.Video))
        {
            Console.Error.WriteLine($"SDL_Init: {GetError()}");
            return 1;
        }

        GLSetAttribute(GLAttr.ContextMajorVersion, 2);
        GLSetAttribute(GLAttr.ContextMinorVersion, 1);
        GLSetAttribute(GLAttr.DoubleBuffer, 1);
        GLSetAttribute(GLAttr.DepthSize, 24);

        var window = CreateWindow("SpaceShip", C.WINDOW_WIDTH, C.WINDOW_HEIGHT,
            WindowFlags.OpenGL | WindowFlags.Resizable);
        if (window == IntPtr.Zero)
        {
            Console.Error.WriteLine($"SDL_CreateWindow: {GetError()}");
            Quit();
            return 1;
        }

        var glCtx = GLCreateContext(window);
        if (glCtx == IntPtr.Zero)
        {
            Console.Error.WriteLine($"SDL_GL_CreateContext: {GetError()}");
            DestroyWindow(window);
            Quit();
            return 1;
        }
        GLSetSwapInterval(1);

        // Init Silk.NET GL
        Renderer.Gl = GL.GetApi(proc => GLGetProcAddress(proc));
        var Gl = Renderer.Gl;

        Gl.Enable(EnableCap.DepthTest);
        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Game state init
        var gs = new GameState();
        gs.State      = GameStateEnum.Title;
        gs.TitleTimer = C.TITLE_FLIP_SEC;

        InitStars();
        SpawnRing(ref gs.Ring, 1, 1);

        ulong prevTick = GetTicks();
        bool  running  = true;

        const string ENTRY_SET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.- ";
        const int    ENTRY_N   = 39;

        while (running)
        {
            ulong now = GetTicks();
            float dt  = (float)(now - prevTick) * 0.001f;
            prevTick  = now;
            if (dt > 0.05f) dt = 0.05f;

            // ---- Events ----
            bool anyKey  = false;
            bool keyUp   = false, keyDown  = false;
            bool keyLeft = false, keyRight = false, keyEnter = false;

            while (PollEvent(out Event ev))
            {
                if (ev.Type == (uint)EventType.Quit)
                    running = false;
                if (ev.Type == (uint)EventType.KeyDown)
                {
                    switch (ev.Key.Key)
                    {
                        case Keycode.Escape:  running  = false; break;
                        case Keycode.Up:      keyUp    = true; anyKey = true; break;
                        case Keycode.Down:    keyDown  = true; anyKey = true; break;
                        case Keycode.Left:    keyLeft  = true; anyKey = true; break;
                        case Keycode.Right:   keyRight = true; anyKey = true; break;
                        case Keycode.Return:
                        case Keycode.KpEnter: keyEnter = true; anyKey = true; break;
                        default:              anyKey = true; break;
                    }
                }
            }

            // ---- Title / Ranking ----
            if (gs.State == GameStateEnum.Title || gs.State == GameStateEnum.Ranking)
            {
                gs.TitleTimer -= dt;
                if (gs.TitleTimer <= 0.0f)
                {
                    gs.TitleTimer = C.TITLE_FLIP_SEC;
                    if (gs.State == GameStateEnum.Title)
                    {
                        gs.State = GameStateEnum.Ranking;
                        gs.RankingModeIdx++;
                    }
                    else
                    {
                        gs.State = GameStateEnum.Title;
                    }
                }
                if (anyKey)
                {
                    gs.State   = GameStateEnum.ModeSelect;
                    gs.ModeSel = (int)GameMode.Normal;
                }
                goto doRender;
            }

            // ---- Mode select ----
            if (gs.State == GameStateEnum.ModeSelect)
            {
                if (keyUp)   gs.ModeSel = (gs.ModeSel + 2) % 3;
                if (keyDown) gs.ModeSel = (gs.ModeSel + 1) % 3;
                if (keyEnter)
                {
                    gs.State   = GameStateEnum.ShipSelect;
                    gs.ShipSel = (int)ShipType.Standard;
                }
                goto doRender;
            }

            // ---- Ship select ----
            if (gs.State == GameStateEnum.ShipSelect)
            {
                if (keyUp)   gs.ShipSel = (gs.ShipSel + 2) % 3;
                if (keyDown) gs.ShipSel = (gs.ShipSel + 1) % 3;
                if (keyEnter)
                {
                    GameMode chosenMode = (GameMode)gs.ModeSel;
                    ShipType chosenShip = (ShipType)gs.ShipSel;
                    int      rmi        = gs.RankingModeIdx;
                    gs = new GameState();
                    gs.Pos              = new Vec3(512, 512, 512);
                    gs.Fwd              = new Vec3(0, 0, 1);
                    gs.Up               = new Vec3(0, 1, 0);
                    gs.Fuel             = C.INITIAL_FUEL;
                    gs.PrevPos          = gs.Pos;
                    gs.RingTimer        = C.RING_TIME_LIMIT;
                    gs.Stage            = 1;
                    gs.CountdownVal     = C.COUNTDOWN_START;
                    gs.CountdownLastN   = (int)C.COUNTDOWN_START + 1;
                    gs.Mode             = chosenMode;
                    gs.Ship             = chosenShip;
                    gs.RankingModeIdx   = rmi;
                    gs.State            = GameStateEnum.Countdown;
                    SpawnRing(ref gs.Ring, 1, 1);
                }
                goto doRender;
            }

            // ---- Countdown ----
            if (gs.State == GameStateEnum.Countdown)
            {
                gs.CountdownVal -= dt;
                int cn = (int)MathF.Ceiling(gs.CountdownVal);
                if (cn >= 1 && cn < gs.CountdownLastN)
                {
                    AudioSystem.PlayPip();
                    gs.CountdownLastN = cn;
                }
                if (gs.CountdownVal <= 0.0f && gs.CountdownLastN > 0)
                {
                    AudioSystem.PlayGong();
                    gs.CountdownLastN = 0;
                }
                if (gs.CountdownVal <= -1.0f) gs.State = GameStateEnum.Playing;
                goto doRender;
            }

            // ---- Exploding ----
            if (gs.State == GameStateEnum.Exploding)
            {
                gs.ExplodeTimer -= dt;
                if (gs.ExplodeTimer <= 0.0f)
                {
                    if (HiScoreManager.Qualifies(gs.Score, (int)gs.Mode))
                    {
                        gs.EntryCh[0] = gs.EntryCh[1] = gs.EntryCh[2] = 'A';
                        gs.EntryCur   = 0;
                        gs.State      = GameStateEnum.Entry;
                    }
                    else
                    {
                        gs.State = GameStateEnum.GameOver;
                    }
                }
                goto doRender;
            }

            // ---- Initials entry ----
            if (gs.State == GameStateEnum.Entry)
            {
                if (keyUp || keyDown)
                {
                    char cur = gs.EntryCh[gs.EntryCur];
                    int idx  = ENTRY_SET.IndexOf(cur);
                    if (idx < 0) idx = 0;
                    if (keyUp)   idx = (idx + 1) % ENTRY_N;
                    if (keyDown) idx = (idx + ENTRY_N - 1) % ENTRY_N;
                    gs.EntryCh[gs.EntryCur] = ENTRY_SET[idx];
                }
                if (keyRight && gs.EntryCur < 2) gs.EntryCur++;
                if (keyLeft  && gs.EntryCur > 0) gs.EntryCur--;
                if (keyEnter)
                {
                    string initials = new string(gs.EntryCh);
                    HiScoreManager.Add(initials, gs.Score, gs.Stage, (int)gs.Mode);
                    HiScoreManager.Save();
                    gs.State = GameStateEnum.GameOver;
                }
                goto doRender;
            }

            // ---- Game over ----
            if (gs.State == GameStateEnum.GameOver)
            {
                if (anyKey)
                {
                    gs.State      = GameStateEnum.Title;
                    gs.TitleTimer = C.TITLE_FLIP_SEC;
                }
                goto doRender;
            }

            // ---- Stage clear ----
            if (gs.State == GameStateEnum.StageClear)
            {
                if (anyKey)
                {
                    gs.RingsDone      = 0;
                    gs.RingTimer      = C.RING_TIME_LIMIT;
                    gs.Fuel           = C.INITIAL_FUEL;
                    gs.CountdownVal   = C.COUNTDOWN_START;
                    gs.CountdownLastN = (int)C.COUNTDOWN_START + 1;
                    gs.State          = GameStateEnum.Countdown;
                    SpawnRing(ref gs.Ring, 1, gs.Stage);
                }
                goto doRender;
            }

            // ---- Playing ----
            if (gs.State == GameStateEnum.Playing)
            {
                var keys = GetKeyboardState(out _);

                float shipRotMul   = (gs.Ship == ShipType.Agile) ? 2.0f : 1.0f;
                float shipAccelMul = (gs.Ship == ShipType.Boost) ? 2.0f : 1.0f;

                Vec3  right   = Vec3.Norm(Vec3.Cross(gs.Fwd, gs.Up));
                float rot     = C.ROTATION_SPEED * shipRotMul * dt;
                bool  rotating = false;

                if (keys[(int)Scancode.Up])
                {
                    gs.Fwd = Vec3.Rotate(gs.Fwd, right, rot);
                    gs.Up  = Vec3.Rotate(gs.Up,  right, rot);
                    rotating = true;
                }
                if (keys[(int)Scancode.Down])
                {
                    gs.Fwd = Vec3.Rotate(gs.Fwd, right, -rot);
                    gs.Up  = Vec3.Rotate(gs.Up,  right, -rot);
                    rotating = true;
                }
                if (keys[(int)Scancode.Left])
                {
                    gs.Fwd = Vec3.Rotate(gs.Fwd, gs.Up, -rot);
                    rotating = true;
                }
                if (keys[(int)Scancode.Right])
                {
                    gs.Fwd = Vec3.Rotate(gs.Fwd, gs.Up, rot);
                    rotating = true;
                }
                if (rotating)
                {
                    gs.ReOrthogonalize();
                    if (gs.Fuel > 0.0f) gs.Fuel -= C.FUEL_ROTATE * dt;
                    if (gs.Mode == GameMode.Easy)
                    {
                        float spd = Vec3.Len(gs.Vel);
                        if (spd > 1e-4f) gs.Vel = Vec3.Scale(gs.Fwd, spd);
                    }
                }

                bool thrusting = (keys[(int)Scancode.Z] || keys[(int)Scancode.A]) && gs.Fuel > 0.0f;
                if (thrusting)
                {
                    gs.Vel  = Vec3.Add(gs.Vel, Vec3.Scale(gs.Fwd, C.MAIN_ACCEL * shipAccelMul * dt));
                    gs.Fuel -= C.FUEL_MAIN * dt;
                }

                bool braking = gs.Mode != GameMode.Hard
                            && (keys[(int)Scancode.X] || keys[(int)Scancode.B])
                            && gs.Fuel > 0.0f;
                if (braking)
                {
                    float spd = Vec3.Len(gs.Vel);
                    if (spd > 1e-4f)
                    {
                        float dv = C.BRAKE_ACCEL * dt;
                        if (dv >= spd)
                            gs.Vel = new Vec3(0, 0, 0);
                        else
                            gs.Vel = Vec3.Add(gs.Vel, Vec3.Scale(gs.Vel, -dv / spd));
                    }
                    gs.Fuel -= C.FUEL_BRAKE * dt;
                }

                AudioSystem.Thruster(thrusting || braking || rotating);

                gs.CollisionWarning = PredictCollision(gs) ? 1 : 0;
                AudioSystem.WarningTone(gs.CollisionWarning != 0);

                gs.FuelWarning = (gs.Fuel <= C.INITIAL_FUEL * 0.30f) ? 1 : 0;
                gs.TimeWarning = (gs.RingTimer <= 10.0f) ? 1 : 0;
                int statLevel  = gs.TimeWarning != 0 ? 2 : gs.FuelWarning != 0 ? 1 : 0;
                AudioSystem.StatusWarning(statLevel);

                if (gs.Fuel < 0.0f) gs.Fuel = 0.0f;

                // DRAG_K = 0 のため速度減衰なし

                // Ring update
                if (gs.Ring.RotSpeed != 0.0f)
                {
                    float angle    = gs.Ring.RotSpeed * dt;
                    gs.Ring.Normal = Vec3.Norm(Vec3.Rotate(gs.Ring.Normal, gs.Ring.RotAxis, angle));
                    gs.Ring.Up     = Vec3.Norm(Vec3.Rotate(gs.Ring.Up,     gs.Ring.RotAxis, angle));
                }
                if (gs.Ring.MoveSpeed > 0.0f)
                    gs.Ring.Pos = Vec3.Wrap(Vec3.Add(gs.Ring.Pos, Vec3.Scale(gs.Ring.MoveDir, gs.Ring.MoveSpeed * dt)));

                gs.PrevPos = gs.Pos;
                gs.Pos     = Vec3.Wrap(Vec3.Add(gs.Pos, Vec3.Scale(gs.Vel, dt)));

                if (gs.ExcellentTimer > 0.0f) gs.ExcellentTimer -= dt;

                gs.RingTimer -= dt;
                if (gs.RingTimer <= 0.0f)
                {
                    gs.State        = GameStateEnum.Exploding;
                    gs.ExplodeTimer = C.EXPLODE_DURATION;
                    AudioSystem.PlayExplosion();
                    goto doRender;
                }

                if (CheckRingHit(gs))
                {
                    gs.State        = GameStateEnum.Exploding;
                    gs.ExplodeTimer = C.EXPLODE_DURATION;
                    AudioSystem.PlayExplosion();
                    goto doRender;
                }

                int passResult = CheckRingPass(gs);
                if (passResult != 0)
                {
                    int baseScore = (gs.Ring.ColorType == 2) ? 400
                                  : (gs.Ring.ColorType == 1) ? 200
                                  : C.RING_BASE_SCORE;
                    int ringScore = baseScore + (int)gs.RingTimer * C.RING_TIME_BONUS;
                    if (passResult == 2)
                    {
                        ringScore         += 50;
                        gs.ExcellentTimer  = 2.0f;
                    }
                    gs.Score     += ringScore;
                    gs.RingsDone++;
                    gs.RingTimer = C.RING_TIME_LIMIT;

                    if (gs.RingsDone >= C.RINGS_PER_STAGE)
                    {
                        int fuelBonus     = (int)gs.Fuel;
                        gs.Score         += fuelBonus;
                        gs.StageFuelBonus = fuelBonus;
                        gs.Stage++;
                        gs.State = GameStateEnum.StageClear;
                    }
                    else
                    {
                        SpawnRing(ref gs.Ring, gs.RingsDone + 1, gs.Stage);
                    }
                }
            }

            doRender:
            // Non-playing: silence audio
            if (gs.State != GameStateEnum.Playing)
            {
                AudioSystem.Thruster(false);
                AudioSystem.WarningTone(false);
                AudioSystem.StatusWarning(0);
            }

            // ==================== Render ====================
            GetWindowSize(window, out int ww, out int wh);
            if (wh == 0) wh = 1;

            Gl.Viewport(0, 0, (uint)ww, (uint)wh);
            Gl.ClearColor(0.0f, 0.0f, 0.015f, 1.0f);
            Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // --- Perspective projection ---
            Gl.MatrixMode(MatrixMode.Projection);
            Gl.LoadIdentity();
            float fovRad = C.FOV_DEG * MathF.PI / 180.0f;
            float f      = 1.0f / MathF.Tan(fovRad * 0.5f);
            float asp    = (float)ww / (float)wh;
            float nr     = C.NEAR_PLANE, fr = C.FAR_PLANE;
            float[] proj = {
                f / asp, 0,  0,                          0,
                0,       f,  0,                          0,
                0,       0,  (fr + nr) / (nr - fr),     -1,
                0,       0,  2.0f * fr * nr / (nr - fr), 0
            };
            fixed (float* pp = proj) Gl.LoadMatrix(pp);

            // --- View matrix ---
            Gl.MatrixMode(MatrixMode.Modelview);
            Gl.LoadIdentity();
            Vec3 fv = gs.Fwd;
            Vec3 rv = Vec3.Norm(Vec3.Cross(fv, gs.Up));
            Vec3 uv = Vec3.Norm(Vec3.Cross(rv, fv));
            float[] view = {
                 rv.X,  uv.X, -fv.X, 0,
                 rv.Y,  uv.Y, -fv.Y, 0,
                 rv.Z,  uv.Z, -fv.Z, 0,
                 0,     0,     0,    1
            };
            fixed (float* pv = view) Gl.LoadMatrix(pv);

            Renderer.RenderStars(gs.Pos, Stars);
            Renderer.RenderRing(ref gs.Ring, gs.Pos);
            Renderer.RenderHud(gs, ww, wh);

            GLSwapWindow(window);
        }

        AudioSystem.Cleanup();
        GLDestroyContext(glCtx);
        DestroyWindow(window);
        Quit();
        return 0;
    }
}

} // namespace SpaceShip
