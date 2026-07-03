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
    // コナミコマンド: ↑↑↓↓←→←→BA
    // 0=Up 1=Down 2=Left 3=Right 4=A(keyKA) 5=B(keyKB)
    private static readonly int[] KonamiSeq = { 0, 0, 1, 1, 2, 3, 2, 3, 5, 4 };

    private static readonly Vec3[] Stars = new Vec3[C.STAR_COUNT];
    private static IntPtr s_gamepad = IntPtr.Zero;  // 現在使用中のゲームパッド
    private const short PAD_DEAD = 8000;            // スティックのデッドゾーン (~25%)

    // 接続中のゲームパッドから最初の1つを開く
    private static void OpenFirstGamepad()
    {
        if (s_gamepad != IntPtr.Zero) return;

        // SDL のゲームパッドデータベースに登録済みのデバイスを優先
        var ids = GetGamepads(out int count);
        if (ids != null && count > 0) { s_gamepad = OpenGamepad(ids[0]); return; }

        // 未登録ジョイスティックに汎用マッピングを追加してゲームパッドとして使う
        var jids = GetJoysticks(out int jcount);
        if (jids == null || jcount == 0) return;
        for (int i = 0; i < jcount; i++)
        {
            uint jid = jids[i];
            if (IsGamepad(jid)) continue; // 既に登録済み

            // GUIDを文字列化してマッピング文字列を構築
            var guid = GetJoystickGUIDForID(jid);
            byte[] guidBuf = new byte[33];
            GUIDToString(guid, guidBuf, guidBuf.Length);
            string guidStr = System.Text.Encoding.ASCII.GetString(guidBuf, 0, 32);
            string jname   = GetJoystickNameForID(jid) ?? "Joystick";

            // 汎用マッピング: USB gamepad (081F:E401) クローン向け
            // b0=X b1=A b2=B b3=Y  十字キー=a0(横)/a1(縦) アナログスティックなし
            string mapping = $"{guidStr},{jname},platform:Linux," +
                             "a:b1,b:b2,x:b0,y:b3,back:b8,start:b9," +
                             "leftshoulder:b4,rightshoulder:b5," +
                             "lefttrigger:b6,righttrigger:b7," +
                             "dpup:-a1,dpdown:+a1,dpleft:-a0,dpright:+a0," +
                             "leftx:a0,lefty:a1," +
                             "leftstick:b10,rightstick:b11";
            AddGamepadMapping(mapping);

            if (IsGamepad(jid)) { s_gamepad = OpenGamepad(jid); return; }
        }
    }

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

    private static void SpawnRing(ref Ring ring, int ringNum, int stage,
                                   bool hasNeutronStar = false, Vec3 nsPos = default)
    {
        const float NS_MIN_DIST = 200.0f;
        do
        {
            ring.Pos = new Vec3(
                (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)),
                (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)),
                (float)(Random.Shared.Next(0, (int)C.SPACE_SIZE)));
        }
        while (hasNeutronStar && Vec3.Len(Vec3.TorusDelta(ring.Pos, nsPos)) < NS_MIN_DIST);

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

    private static void SpawnNeutronStar(GameState gs)
    {
        if (gs.Stage < 16) { gs.HasNeutronStar = false; return; }
        gs.NeutronStarPos = new Vec3(
            (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
            (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
            (float)Random.Shared.Next(0, (int)C.SPACE_SIZE));
        gs.HasNeutronStar = true;
    }

    // ボーナスアイテムをスポーン (リングと中性子星から200px以上離れた場所)
    private static void SpawnBonusItem(GameState gs)
    {
        // 30%=時間アイテム(1), 70%=燃料アイテム(2)
        gs.ItemType = (Random.Shared.NextDouble() < 0.30) ? 1 : 2;

        const float MIN_DIST = 200.0f;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            Vec3 pos = new Vec3(
                (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
                (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
                (float)Random.Shared.Next(0, (int)C.SPACE_SIZE));
            if (Vec3.Len(Vec3.TorusDelta(pos, gs.Ring.Pos)) < MIN_DIST) continue;
            if (gs.HasNeutronStar && Vec3.Len(Vec3.TorusDelta(pos, gs.NeutronStarPos)) < MIN_DIST) continue;
            gs.ItemPos = pos;
            AudioSystem.PlayItemSpawn(gs.ItemType);
            return;
        }
        // 200回試行で見つからなければ制約を無視して配置
        gs.ItemPos = new Vec3(
            (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
            (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
            (float)Random.Shared.Next(0, (int)C.SPACE_SIZE));
        AudioSystem.PlayItemSpawn(gs.ItemType);
    }

    // ボーナスステージ用リング: bonusNum 1=固定, 2=回転, 3+=回転+移動
    private static void SpawnBonusRing(ref Ring ring, int bonusNum, bool hasNS, Vec3 nsPos)
    {
        const float NS_MIN_DIST = 200.0f;
        do
        {
            ring.Pos = new Vec3(
                (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
                (float)Random.Shared.Next(0, (int)C.SPACE_SIZE),
                (float)Random.Shared.Next(0, (int)C.SPACE_SIZE));
        }
        while (hasNS && Vec3.Len(Vec3.TorusDelta(ring.Pos, nsPos)) < NS_MIN_DIST);

        float theta = (float)Random.Shared.NextDouble() * 2.0f * MathF.PI;
        float phi   = MathF.Acos(2.0f * (float)Random.Shared.NextDouble() - 1.0f);
        ring.Normal = Vec3.Norm(new Vec3(
            MathF.Sin(phi) * MathF.Cos(theta),
            MathF.Sin(phi) * MathF.Sin(theta),
            MathF.Cos(phi)));
        Vec3 arb = (MathF.Abs(ring.Normal.Y) < 0.9f) ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
        ring.Up = Vec3.Norm(Vec3.Cross(Vec3.Norm(Vec3.Cross(ring.Normal, arb)), ring.Normal));

        bool rot  = bonusNum >= 2;
        bool move = bonusNum >= 3;
        ring.RotSpeed = rot ? (MathF.PI / 30.0f) : 0.0f;
        ring.RotAxis  = rot ? Vec3.Norm(Vec3.Cross(ring.Normal, ring.Up)) : new Vec3(0, 1, 0);
        ring.MoveSpeed = move ? 50.0f : 0.0f;
        if (move)
        {
            float mt = (float)Random.Shared.NextDouble() * 2.0f * MathF.PI;
            float mp = MathF.Acos(2.0f * (float)Random.Shared.NextDouble() - 1.0f);
            ring.MoveDir = Vec3.Norm(new Vec3(
                MathF.Sin(mp) * MathF.Cos(mt),
                MathF.Sin(mp) * MathF.Sin(mt),
                MathF.Cos(mp)));
        }
        else { ring.MoveDir = new Vec3(0, 0, 0); }
        ring.ColorType = move ? 2 : (rot ? 1 : 0);
    }

    // ボーナスステージ失敗: 得点なしでステージ終了
    private static void FailBonusStage(GameState gs)
    {
        gs.BonusFailed     = true;
        gs.SpeedBonusScore = 0;
        gs.StageFuelBonus  = 0;
        gs.ItemType        = 0;
        gs.HasTimeItem     = false;
        gs.HasFuelItem     = false;
        // ボーナスはステージ番号を消費しない (gs.Stage はそのまま)
        gs.StageClearTimer = 30f; // キー待ちなし: 30秒表示後に自動進行
        gs.State = GameStateEnum.StageClear;
        AudioSystem.PlayJingleBonusFailed();
    }

    // 次ステージを開始: ボーナスステージ判定 → BonusIntro or Countdown
    private static void StartNextStage(GameState gs)
    {
        gs.BonusFailed = false;
        gs.Vel         = new Vec3(0, 0, 0); // 各ステージ開始時に速度リセット
        SpawnNeutronStar(gs);
        if (gs.PendingBonus)
        {
            gs.PendingBonus  = false;
            gs.IsBonusStage  = true;
            gs.BonusStageNum++;
            gs.WaitRelease   = true; // 直前のキー入力を引き継がないよう解放待ち
            SpawnBonusRing(ref gs.Ring, gs.BonusStageNum, gs.HasNeutronStar, gs.NeutronStarPos);
            gs.State = GameStateEnum.BonusIntro;
            AudioSystem.PlayJingleBonusStart();
        }
        else
        {
            gs.IsBonusStage = false;
            SpawnRing(ref gs.Ring, 1, gs.Stage, gs.HasNeutronStar, gs.NeutronStarPos);
            gs.State = GameStateEnum.Countdown;
            AudioSystem.PlayJingleStart();
        }
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

        if (!Init(InitFlags.Video | InitFlags.Gamepad))
        {
            Console.Error.WriteLine($"SDL_Init: {GetError()}");
            return 1;
        }
        OpenFirstGamepad(); // 起動時に接続済みのゲームパッドを開く

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
            bool keyKA   = false, keyKB    = false; // コナミコマンド用 A/B ボタン

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
                        case Keycode.Z:
                        case Keycode.A:       keyKA    = true; anyKey = true; break;
                        case Keycode.X:
                        case Keycode.B:       keyKB    = true; anyKey = true; break;
                        default:              anyKey = true; break;
                    }
                }

                // ゲームパッド接続/切断
                if (ev.Type == (uint)EventType.JoystickAdded)
                {
                    // 未登録デバイスの場合も OpenFirstGamepad 内でマッピングを追加して開く
                    if (s_gamepad == IntPtr.Zero) OpenFirstGamepad();
                }
                if (ev.Type == (uint)EventType.GamepadAdded)
                {
                    if (s_gamepad == IntPtr.Zero)
                        s_gamepad = OpenGamepad(ev.GDevice.Which);
                }
                if (ev.Type == (uint)EventType.GamepadRemoved)
                {
                    if (s_gamepad != IntPtr.Zero && GetGamepadID(s_gamepad) == ev.GDevice.Which)
                    {
                        CloseGamepad(s_gamepad);
                        s_gamepad = IntPtr.Zero;
                        OpenFirstGamepad(); // 他に繋がっているものを試みる
                    }
                }

                // ゲームパッドボタン → メニュー操作
                if (ev.Type == (uint)EventType.GamepadButtonDown)
                {
                    var btn = (GamepadButton)ev.GButton.Button;
                    switch (btn)
                    {
                        case GamepadButton.DPadUp:    keyUp    = true; anyKey = true; break;
                        case GamepadButton.DPadDown:  keyDown  = true; anyKey = true; break;
                        case GamepadButton.DPadLeft:  keyLeft  = true; anyKey = true; break;
                        case GamepadButton.DPadRight: keyRight = true; anyKey = true; break;
                        case GamepadButton.East:      keyEnter = true; keyKA = true; anyKey = true; break;
                        case GamepadButton.Start:     keyEnter = true; anyKey = true; break;
                        case GamepadButton.South:     keyKB    = true; anyKey = true; break;
                        default:                      anyKey = true; break;
                    }
                }
                if (ev.Type == (uint)EventType.GamepadAxisMotion)
                {
                    var axis = (GamepadAxis)ev.GAxis.Axis;
                    var value = ev.GAxis.Value;
                    switch (axis)
                    {
                        case GamepadAxis.LeftX:
                            if (value < 0)
                            {
                                keyLeft = true; anyKey = true;        
                            }
                            if (value > 0)
                            {
                                keyRight = true; anyKey = true;        
                            }
                            break;
                        case GamepadAxis.LeftY:
                            if (value < 0)
                            {
                                keyUp = true; anyKey = true;        
                            }
                            if (value > 0)
                            {
                                keyDown = true; anyKey = true;
                            }
                            break;
                        default:  
                            anyKey = true; break;      
                    }        
                }
            }

            // ---- キー全解放待機 ----
            // ボーナスステージ前後はアクセルを押しっぱなしの可能性があるため、
            // 全キーが物理的に離されるまで anyKey を無効化する
            if (gs.WaitRelease)
            {
                var ks = GetKeyboardState(out _);
                bool held = ks[(int)Scancode.Z]      || ks[(int)Scancode.A]      ||
                            ks[(int)Scancode.X]      || ks[(int)Scancode.B]      ||
                            ks[(int)Scancode.Space]  || ks[(int)Scancode.Return]  ||
                            ks[(int)Scancode.Up]     || ks[(int)Scancode.Down]    ||
                            ks[(int)Scancode.Left]   || ks[(int)Scancode.Right];
                if (s_gamepad != IntPtr.Zero)
                    held |= GetGamepadButton(s_gamepad, GamepadButton.South) ||
                            GetGamepadButton(s_gamepad, GamepadButton.East)  ||
                            GetGamepadButton(s_gamepad, GamepadButton.Start) ||
                            GetGamepadButton(s_gamepad, GamepadButton.DPadUp)   ||
                            GetGamepadButton(s_gamepad, GamepadButton.DPadDown) ||
                            GetGamepadButton(s_gamepad, GamepadButton.DPadLeft) ||
                            GetGamepadButton(s_gamepad, GamepadButton.DPadRight);
                if (held) anyKey = false;
                else      gs.WaitRelease = false;
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
                    int pressed = keyUp    ? 0 : keyDown  ? 1 : keyLeft  ? 2 :
                                  keyRight ? 3 : keyKA    ? 4 : keyKB    ? 5 : -1;
                    if (pressed >= 0 && KonamiSeq[gs.KonamiStep] == pressed)
                    {
                        gs.KonamiStep++;
                        if (gs.KonamiStep >= KonamiSeq.Length)
                        {
                            // コナミコマンド完成 → ステージセレクトへ
                            gs.KonamiStep  = 0;
                            gs.StageSel0   = 0;
                            gs.StageSel1   = 1;
                            gs.StageSelCur = 0;
                            gs.State       = GameStateEnum.StageSelect;
                            AudioSystem.PlayUiConfirm();
                        }
                        // else: タイトルに留まり続きを待つ
                    }
                    else
                    {
                        gs.KonamiStep = 0;
                        gs.State      = GameStateEnum.ModeSelect;
                        gs.ModeSel    = (int)GameMode.Normal;
                    }
                }
                goto doRender;
            }

            // ---- Stage select (コナミコマンド後) ----
            if (gs.State == GameStateEnum.StageSelect)
            {
                if (keyUp)
                {
                    if (gs.StageSelCur == 0) gs.StageSel0 = (gs.StageSel0 + 1) % 10;
                    else                      gs.StageSel1 = (gs.StageSel1 + 1) % 10;
                    AudioSystem.PlayUiSelect();
                }
                if (keyDown)
                {
                    if (gs.StageSelCur == 0) gs.StageSel0 = (gs.StageSel0 + 9) % 10;
                    else                      gs.StageSel1 = (gs.StageSel1 + 9) % 10;
                    AudioSystem.PlayUiSelect();
                }
                if (keyRight && gs.StageSelCur == 0) { gs.StageSelCur = 1; AudioSystem.PlayUiSelect(); }
                if (keyLeft  && gs.StageSelCur == 1) { gs.StageSelCur = 0; AudioSystem.PlayUiSelect(); }
                if (keyEnter || keyKA)
                {
                    int stageNum = gs.StageSel0 * 10 + gs.StageSel1;
                    gs.StageStart = Math.Max(1, Math.Min(99, stageNum));
                    gs.State      = GameStateEnum.ModeSelect;
                    gs.ModeSel    = (int)GameMode.Normal;
                    AudioSystem.PlayUiConfirm();
                }
                goto doRender;
            }

            // ---- Mode select ----
            if (gs.State == GameStateEnum.ModeSelect)
            {
                if (keyUp)   { gs.ModeSel = (gs.ModeSel + 2) % 3; AudioSystem.PlayUiSelect(); }
                if (keyDown) { gs.ModeSel = (gs.ModeSel + 1) % 3; AudioSystem.PlayUiSelect(); }
                if (keyEnter || keyKA)
                {
                    gs.State   = GameStateEnum.ShipSelect;
                    gs.ShipSel = (int)ShipType.Standard;
                    AudioSystem.PlayUiConfirm();
                }
                goto doRender;
            }

            // ---- Ship select ----
            if (gs.State == GameStateEnum.ShipSelect)
            {
                if (keyUp)   { gs.ShipSel = (gs.ShipSel + 2) % 3; AudioSystem.PlayUiSelect(); }
                if (keyDown) { gs.ShipSel = (gs.ShipSel + 1) % 3; AudioSystem.PlayUiSelect(); }
                if (keyEnter || keyKA)
                {
                    AudioSystem.PlayUiConfirm();
                    GameMode chosenMode   = (GameMode)gs.ModeSel;
                    ShipType chosenShip   = (ShipType)gs.ShipSel;
                    int      rmi          = gs.RankingModeIdx;
                    int      stageStart   = gs.StageStart;
                    gs = new GameState();
                    gs.InitialFuel      = chosenMode == GameMode.Easy ? 500.0f : C.INITIAL_FUEL;
                    gs.TimeLimit        = chosenMode == GameMode.Hard ? 45.0f  : C.RING_TIME_LIMIT;
                    gs.Pos              = new Vec3(512, 512, 512);
                    gs.Fwd              = new Vec3(0, 0, 1);
                    gs.Up               = new Vec3(0, 1, 0);
                    gs.Fuel             = gs.InitialFuel;
                    gs.PrevPos          = gs.Pos;
                    gs.RingTimer        = gs.TimeLimit;
                    gs.Stage            = stageStart;
                    gs.StageStart       = stageStart;
                    gs.CountdownVal     = C.COUNTDOWN_START;
                    gs.CountdownLastN   = (int)C.COUNTDOWN_START + 1;
                    gs.Mode             = chosenMode;
                    gs.Ship             = chosenShip;
                    gs.RankingModeIdx   = rmi;
                    StartNextStage(gs);
                }
                goto doRender;
            }

            // ---- Bonus stage intro ----
            if (gs.State == GameStateEnum.BonusIntro)
            {
                if (anyKey)
                {
                    gs.State = GameStateEnum.Countdown;
                    AudioSystem.PlayUiConfirm();
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
                if (gs.CountdownVal <= -1.0f)
                {
                    gs.State = GameStateEnum.Playing;
                    //AudioSystem.PlayJingleStart();
                }
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
                        AudioSystem.PlayJingleNewRecord();
                    }
                    else
                    {
                        gs.State = GameStateEnum.GameOver;
                        AudioSystem.PlayJingleGameOver();
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
                if ((keyRight || keyKA) && gs.EntryCur < 2) gs.EntryCur++;
                if ((keyLeft || keyKB)  && gs.EntryCur > 0) gs.EntryCur--;
                if (keyEnter || (keyKA && gs.EntryCur == 2))
                {
                    string initials = new string(gs.EntryCh);
                    HiScoreManager.Add(initials, gs.Score, gs.Stage, (int)gs.Mode);
                    HiScoreManager.Save();
                    gs.State = GameStateEnum.GameOver;
                    AudioSystem.PlayJingleGameOver();
                }
                goto doRender;
            }

            // ---- Game over ----
            if (gs.State == GameStateEnum.GameOver)
            {
                if (anyKey)
                {
                    gs.StageStart = 1;
                    gs.State      = GameStateEnum.Title;
                    gs.TitleTimer = C.TITLE_FLIP_SEC;
                }
                goto doRender;
            }

            // ---- Stage clear ----
            if (gs.State == GameStateEnum.StageClear)
            {
                bool advance;
                if (gs.IsBonusStage)
                {
                    // ボーナスサマリーはキー待ちなし: タイマーで自動進行
                    gs.StageClearTimer -= dt;
                    advance = gs.StageClearTimer <= 0f;
                }
                else
                {
                    advance = anyKey;
                }
                if (advance)
                {
                    gs.RingsDone      = 0;
                    gs.ItemType       = 0;  // フィールドアイテム消去 (保持アイテムは持ち越し)
                    gs.RingTimer      = gs.TimeLimit;
                    gs.Fuel           = gs.InitialFuel;
                    gs.CountdownVal   = C.COUNTDOWN_START;
                    gs.CountdownLastN = (int)C.COUNTDOWN_START + 1;
                    StartNextStage(gs);
                }
                goto doRender;
            }

            // ---- Playing ----
            if (gs.State == GameStateEnum.Playing)
            {
                var keys = GetKeyboardState(out _);

                // ゲームパッドの入力を取得
                bool padUp = false, padDown = false, padLeft = false, padRight = false;
                bool padThrust = false, padBrake = false;
                if (s_gamepad != IntPtr.Zero)
                {
                    short ly = GetGamepadAxis(s_gamepad, GamepadAxis.LeftY);
                    short lx = GetGamepadAxis(s_gamepad, GamepadAxis.LeftX);
                    padUp    = ly < -PAD_DEAD || GetGamepadButton(s_gamepad, GamepadButton.DPadUp);
                    padDown  = ly >  PAD_DEAD || GetGamepadButton(s_gamepad, GamepadButton.DPadDown);
                    padLeft  = lx < -PAD_DEAD || GetGamepadButton(s_gamepad, GamepadButton.DPadLeft);
                    padRight = lx >  PAD_DEAD || GetGamepadButton(s_gamepad, GamepadButton.DPadRight);
                    padThrust = GetGamepadButton(s_gamepad, GamepadButton.South); // A ボタン
                    padBrake  = GetGamepadButton(s_gamepad, GamepadButton.East);  // B ボタン
                }

                float shipRotMul   = (gs.Ship == ShipType.Agile) ? 2.0f : 1.0f;
                float shipAccelMul = (gs.Ship == ShipType.Boost) ? 2.0f : 1.0f;

                Vec3  right   = Vec3.Norm(Vec3.Cross(gs.Fwd, gs.Up));
                float rot     = C.ROTATION_SPEED * shipRotMul * dt;
                bool  rotating = false;

                if (keys[(int)Scancode.Up] || padUp)
                {
                    gs.Fwd = Vec3.Rotate(gs.Fwd, right, rot);
                    gs.Up  = Vec3.Rotate(gs.Up,  right, rot);
                    rotating = true;
                }
                if (keys[(int)Scancode.Down] || padDown)
                {
                    gs.Fwd = Vec3.Rotate(gs.Fwd, right, -rot);
                    gs.Up  = Vec3.Rotate(gs.Up,  right, -rot);
                    rotating = true;
                }
                if (keys[(int)Scancode.Left] || padLeft)
                {
                    gs.Fwd = Vec3.Rotate(gs.Fwd, gs.Up, -rot);
                    rotating = true;
                }
                if (keys[(int)Scancode.Right] || padRight)
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

                bool thrusting = (keys[(int)Scancode.Z] || keys[(int)Scancode.A] || padThrust) && gs.Fuel > 0.0f;
                if (thrusting)
                {
                    gs.Vel  = Vec3.Add(gs.Vel, Vec3.Scale(gs.Fwd, C.MAIN_ACCEL * shipAccelMul * dt));
                    gs.Fuel -= C.FUEL_MAIN * dt;
                }

                bool braking = gs.Mode != GameMode.Hard
                            && (keys[(int)Scancode.X] || keys[(int)Scancode.B] || padBrake)
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

                gs.FuelWarning = (gs.Fuel <= gs.InitialFuel * 0.30f) ? 1 : 0;
                gs.TimeWarning = (gs.RingTimer <= 10.0f) ? 1 : 0;
                int statLevel  = gs.TimeWarning != 0 ? 2 : gs.FuelWarning != 0 ? 1 : 0;
                AudioSystem.StatusWarning(statLevel);

                if (gs.Fuel < 0.0f)
                {
                    if (gs.HasFuelItem)    { gs.HasFuelItem = false; gs.Fuel = gs.InitialFuel; }
                    else if (gs.IsBonusStage) { FailBonusStage(gs); goto doRender; }
                    else                   { gs.Fuel = 0.0f; }
                }

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

                // 中性子星重力 (ステージ16以降)
                if (gs.HasNeutronStar)
                {
                    Vec3  nsDelta = Vec3.TorusDelta(gs.Pos, gs.NeutronStarPos);
                    float nsDist  = Vec3.Len(nsDelta);
                    if (nsDist > 0.001f)
                    {
                        float r    = nsDist / 100.0f;
                        float gAcc = 36.0f / (r * r);
                        gs.Vel = Vec3.Add(gs.Vel, Vec3.Scale(Vec3.Norm(nsDelta), gAcc * dt));
                    }
                }

                gs.PrevPos = gs.Pos;
                gs.Pos     = Vec3.Wrap(Vec3.Add(gs.Pos, Vec3.Scale(gs.Vel, dt)));

                if (gs.ExcellentTimer > 0.0f) gs.ExcellentTimer -= dt;
                if (gs.StageClearPending && gs.ExcellentTimer <= 0f)
                {
                    gs.StageClearPending = false;
                    if (gs.IsBonusStage) gs.StageClearTimer = 30f;
                    gs.State = GameStateEnum.StageClear;
                }

                // ボーナスアイテム接触 (アイテム半径8 + 船体半径8 = 16px)
                if (gs.ItemType != 0)
                {
                    float itemDist = Vec3.Len(Vec3.TorusDelta(gs.Pos, gs.ItemPos));
                    if (itemDist < 8.0f + C.SHIP_RADIUS)
                    {
                        AudioSystem.PlayItemGet(gs.ItemType);
                        if (gs.ItemType == 1) // 時間アイテム
                        {
                            if (gs.HasTimeItem) gs.Score += 300;
                            else                gs.HasTimeItem = true;
                        }
                        else                   // 燃料アイテム
                        {
                            if (gs.HasFuelItem) gs.Score += 100;
                            else                gs.HasFuelItem = true;
                        }
                        gs.ItemType = 0;
                    }
                }

                // 中性子星への衝突 (半径4ピクセル)
                if (gs.HasNeutronStar)
                {
                    float nsDist2 = Vec3.Len(Vec3.TorusDelta(gs.Pos, gs.NeutronStarPos));
                    if (nsDist2 < 4.0f + C.SHIP_RADIUS)
                    {
                        if (gs.IsBonusStage) { FailBonusStage(gs); goto doRender; }
                        gs.State        = GameStateEnum.Exploding;
                        gs.ExplodeTimer = C.EXPLODE_DURATION;
                        AudioSystem.PlayExplosion();
                        goto doRender;
                    }
                }

                gs.RingTimer -= dt;
                if (gs.RingTimer <= 0.0f)
                {
                    if (gs.HasTimeItem)    { gs.HasTimeItem = false; gs.RingTimer = gs.TimeLimit; }
                    else if (gs.IsBonusStage) { FailBonusStage(gs); goto doRender; }
                    else
                    {
                        gs.State        = GameStateEnum.Exploding;
                        gs.ExplodeTimer = C.EXPLODE_DURATION;
                        AudioSystem.PlayExplosion();
                        goto doRender;
                    }
                }

                if (CheckRingHit(gs))
                {
                    if (gs.IsBonusStage) { FailBonusStage(gs); goto doRender; }
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
                    AudioSystem.PlayRingPass(passResult == 2);
                    gs.Score     += ringScore;
                    gs.RingsDone++;
                    gs.RingTimer = gs.TimeLimit;

                    int ringsNeeded = gs.IsBonusStage ? 1 : C.RINGS_PER_STAGE;
                    if (gs.RingsDone >= ringsNeeded)
                    {
                        if (gs.IsBonusStage)
                        {
                            // ボーナスはステージ番号を消費しない
                            float spd = Vec3.Len(gs.Vel);
                            gs.SpeedBonusScore = spd >= 400 ? 1000
                                               : spd >= 300 ? 700
                                               : spd >= 200 ? 500
                                               : spd >= 150 ? 200
                                               : spd >= 100 ? 100
                                               : 0;
                            gs.Score += gs.SpeedBonusScore;
                        }
                        else
                        {
                            // 通常ステージ: ステージ番号を進め、5の倍数ならボーナスを予約
                            if (gs.Stage % 5 == 0) gs.PendingBonus = true;
                            gs.Stage++;
                        }
                        int fuelBonus     = (int)gs.Fuel;
                        gs.Score         += fuelBonus;
                        gs.StageFuelBonus = fuelBonus;
                        if (gs.IsBonusStage) AudioSystem.PlayJingleBonusClear();
                        else                 AudioSystem.PlayJingleClear();
                        if (gs.ExcellentTimer > 0f)
                        {
                            gs.StageClearPending = true; // Excellent表示後にStageClearへ
                        }
                        else
                        {
                            if (gs.IsBonusStage) gs.StageClearTimer = 30f;
                            gs.State = GameStateEnum.StageClear;
                        }
                    }
                    else
                    {
                        SpawnRing(ref gs.Ring, gs.RingsDone + 1, gs.Stage, gs.HasNeutronStar, gs.NeutronStarPos);
                        if (!gs.IsBonusStage)
                        {
                            if (gs.RingsDone == 2) SpawnBonusItem(gs);
                            if (gs.RingsDone == 3) gs.ItemType = 0;
                        }
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
            Renderer.RenderBonusItem(gs, gs.Pos, wh);
            Renderer.RenderNeutronStar(gs, gs.Pos);
            Renderer.RenderHud(gs, ww, wh);

            GLSwapWindow(window);
        }

        AudioSystem.Cleanup();
        if (s_gamepad != IntPtr.Zero) CloseGamepad(s_gamepad);
        GLDestroyContext(glCtx);
        DestroyWindow(window);
        Quit();
        return 0;
    }
}

} // namespace SpaceShip
