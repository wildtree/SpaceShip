// Silk.NET.OpenGL.Legacy は固定機能パイプライン全体を [Obsolete] としているが、
// このプロジェクトでは意図的に OpenGL 2.1 固定機能を使用するため警告を抑制する。
#pragma warning disable CS0618

using Silk.NET.OpenGL.Legacy;
using static SDL3.SDL;

namespace SpaceShip;

internal static class Renderer
{
    public static GL Gl = null!;

    // ==================== 7-segment font ====================
    private static readonly byte[] SEG7 = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };
    private static readonly byte[] SEG7_ALPHA =
    {
        0x77, // A
        0x7C, // B
        0x39, // C
        0x5E, // D
        0x79, // E
        0x71, // F
        0x7D, // G
        0x76, // H
        0x06, // I
        0x1E, // J
        0x72, // K
        0x38, // L
        0x37, // M
        0x74, // N
        0x3F, // O
        0x73, // P
        0x67, // Q
        0x50, // R
        0x6D, // S
        0x78, // T
        0x3E, // U
        0x1C, // V
        0x3E, // W
        0x76, // X
        0x6E, // Y
        0x5B, // Z
    };

    private static void DrawSeg7Char(float x, float y, float w, float h, char c)
    {
        if (c == ' ') return;
        if (c == '-')
        {
            Gl.Begin(PrimitiveType.Lines);
            Gl.Vertex2(x + 2, y + h * 0.5f); Gl.Vertex2(x + w - 2, y + h * 0.5f);
            Gl.End();
            return;
        }
        if (c == '.')
        {
            Gl.Begin(PrimitiveType.Points);
            Gl.Vertex2(x + w * 0.5f, y + h - 2);
            Gl.End();
            return;
        }
        byte s;
        if (c >= '0' && c <= '9') s = SEG7[c - '0'];
        else if (c >= 'A' && c <= 'Z') s = SEG7_ALPHA[c - 'A'];
        else if (c >= 'a' && c <= 'z') s = SEG7_ALPHA[c - 'a'];
        else return;

        float m = 2.0f;
        if ((s & (1 << 0)) != 0) { Gl.Begin(PrimitiveType.Lines); Gl.Vertex2(x + m, y + m);         Gl.Vertex2(x + w - m, y + m);         Gl.End(); }
        if ((s & (1 << 1)) != 0) { Gl.Begin(PrimitiveType.Lines); Gl.Vertex2(x + w - m, y + m);     Gl.Vertex2(x + w - m, y + h * 0.5f);  Gl.End(); }
        if ((s & (1 << 2)) != 0) { Gl.Begin(PrimitiveType.Lines); Gl.Vertex2(x + w - m, y + h * 0.5f); Gl.Vertex2(x + w - m, y + h - m); Gl.End(); }
        if ((s & (1 << 3)) != 0) { Gl.Begin(PrimitiveType.Lines); Gl.Vertex2(x + m, y + h - m);     Gl.Vertex2(x + w - m, y + h - m);     Gl.End(); }
        if ((s & (1 << 4)) != 0) { Gl.Begin(PrimitiveType.Lines); Gl.Vertex2(x + m, y + h * 0.5f);  Gl.Vertex2(x + m, y + h - m);         Gl.End(); }
        if ((s & (1 << 5)) != 0) { Gl.Begin(PrimitiveType.Lines); Gl.Vertex2(x + m, y + m);         Gl.Vertex2(x + m, y + h * 0.5f);      Gl.End(); }
        if ((s & (1 << 6)) != 0) { Gl.Begin(PrimitiveType.Lines); Gl.Vertex2(x + m, y + h * 0.5f);  Gl.Vertex2(x + w - m, y + h * 0.5f);  Gl.End(); }
    }

    public static void DrawString(float x, float y, float cw, float ch, string s)
    {
        Gl.LineWidth(1.5f);
        Gl.PointSize(3.0f);
        foreach (char c in s)
        {
            DrawSeg7Char(x, y, cw, ch, c);
            x += cw + 1;
        }
        Gl.LineWidth(1.0f);
        Gl.PointSize(1.0f);
    }

    private static void DrawFloatInt(float x, float y, float cw, float ch, float val, int digits)
    {
        int iv   = (int)MathF.Round(val);
        string sign = iv >= 0 ? "+" : "-";
        string num  = Math.Abs(iv).ToString().PadLeft(digits, ' ');
        DrawString(x, y, cw, ch, sign + num);
    }

    // ==================== 3D Rendering ====================
    public static void RenderStars(Vec3 shipPos, Vec3[] stars)
    {
        Gl.PointSize(2.0f);
        Gl.Color3(1.0f, 1.0f, 1.0f);
        Gl.Begin(PrimitiveType.Points);
        foreach (var star in stars)
        {
            Vec3 r = Vec3.TorusDelta(shipPos, star);
            Gl.Vertex3(r.X, r.Y, r.Z);
        }
        Gl.End();
        Gl.PointSize(1.0f);
    }

    public static void RenderRingAt(ref Ring ring, Vec3 rel)
    {
        Vec3  norm   = ring.Normal;
        Vec3  rr     = Vec3.Norm(Vec3.Cross(norm, ring.Up));
        Vec3  up     = ring.Up;
        float radius = ring.Radius > 0f ? ring.Radius : C.RING_RADIUS;
        float tubeR  = radius > C.RING_RADIUS ? C.RING_TUBE_RADIUS * 2.5f : C.RING_TUBE_RADIUS;

        switch (ring.ColorType)
        {
            case 1:  Gl.Color3(0.0f, 1.0f, 1.0f); break;  // cyan
            case 2:  Gl.Color3(1.0f, 0.0f, 1.0f); break;  // magenta
            case 3:  Gl.Color3(0.8f, 0.9f, 1.0f); break;  // white-blue: ドッキングポート
            default: Gl.Color3(1.0f, 0.55f, 0.0f); break; // gold
        }

        for (int i = 0; i < C.RING_SEGMENTS; i++)
        {
            float a0 = (float)i       / C.RING_SEGMENTS * 2.0f * MathF.PI;
            float a1 = (float)(i + 1) / C.RING_SEGMENTS * 2.0f * MathF.PI;

            Vec3 c0 = Vec3.Add(rel, Vec3.Add(Vec3.Scale(rr, MathF.Cos(a0) * radius), Vec3.Scale(up, MathF.Sin(a0) * radius)));
            Vec3 c1 = Vec3.Add(rel, Vec3.Add(Vec3.Scale(rr, MathF.Cos(a1) * radius), Vec3.Scale(up, MathF.Sin(a1) * radius)));

            Gl.Begin(PrimitiveType.TriangleStrip);
            for (int j = 0; j <= C.TUBE_SEGMENTS; j++)
            {
                float b    = (float)j / C.TUBE_SEGMENTS * 2.0f * MathF.PI;
                Vec3 tn0   = Vec3.Norm(Vec3.Add(Vec3.Scale(Vec3.Norm(Vec3.Sub(c0, rel)), MathF.Cos(b)), Vec3.Scale(norm, MathF.Sin(b))));
                Vec3 tn1   = Vec3.Norm(Vec3.Add(Vec3.Scale(Vec3.Norm(Vec3.Sub(c1, rel)), MathF.Cos(b)), Vec3.Scale(norm, MathF.Sin(b))));
                Vec3 v0    = Vec3.Add(c0, Vec3.Scale(tn0, tubeR));
                Vec3 v1    = Vec3.Add(c1, Vec3.Scale(tn1, tubeR));
                Gl.Normal3(tn0.X, tn0.Y, tn0.Z); Gl.Vertex3(v0.X, v0.Y, v0.Z);
                Gl.Normal3(tn1.X, tn1.Y, tn1.Z); Gl.Vertex3(v1.X, v1.Y, v1.Z);
            }
            Gl.End();
        }
    }

    public static void RenderRing(ref Ring ring, Vec3 shipPos)
    {
        Vec3  raw      = Vec3.Sub(ring.Pos, shipPos);
        float drawDist = C.SPACE_SIZE * 0.6f;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            Vec3 rel = new(raw.X + dx * C.SPACE_SIZE,
                           raw.Y + dy * C.SPACE_SIZE,
                           raw.Z + dz * C.SPACE_SIZE);
            if (Vec3.Len(rel) > drawDist) continue;
            RenderRingAt(ref ring, rel);
        }
    }

    // 母艦ボディ: ドッキングポートリングの背後に描画
    public static void RenderMothership(ref Ring ring, Vec3 shipPos)
    {
        Vec3  raw      = Vec3.Sub(ring.Pos, shipPos);
        float drawDist = C.SPACE_SIZE * 0.6f;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            Vec3 rel = new(raw.X + dx * C.SPACE_SIZE,
                           raw.Y + dy * C.SPACE_SIZE,
                           raw.Z + dz * C.SPACE_SIZE);
            if (Vec3.Len(rel) > drawDist) continue;
            RenderMothershipAt(rel, ring.Normal, ring.Up);
        }
    }

    private static void RenderMothershipAt(Vec3 rel, Vec3 norm, Vec3 up)
    {
        Vec3 right = Vec3.Norm(Vec3.Cross(norm, up));

        // ローカル座標 (right, up, -norm) で頂点を生成するヘルパー
        // norm 軸の正方向はプレイヤー側 → 負方向が船体側
        Vec3 L(float r, float u, float n) => new Vec3(
            rel.X + right.X * r + up.X * u + norm.X * n,
            rel.Y + right.Y * r + up.Y * u + norm.Y * n,
            rel.Z + right.Z * r + up.Z * u + norm.Z * n);

        // ---- メインハル (box): 幅120 × 高60 × 奥行130 ----
        Gl.LineWidth(1.5f);
        Gl.Color3(0.45f, 0.72f, 1.0f);
        Gl.Begin(PrimitiveType.Lines);
        float[] xs = { -60f,  60f };
        float[] ys = { -30f,  30f };
        float[] zs = { -10f, -140f }; // -norm 方向が船体奥
        for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
        { var a = L(xs[i], ys[j], zs[0]); var b = L(xs[i], ys[j], zs[1]);
          Gl.Vertex3(a.X, a.Y, a.Z); Gl.Vertex3(b.X, b.Y, b.Z); }
        for (int i = 0; i < 2; i++) for (int k = 0; k < 2; k++)
        { var a = L(xs[i], ys[0], zs[k]); var b = L(xs[i], ys[1], zs[k]);
          Gl.Vertex3(a.X, a.Y, a.Z); Gl.Vertex3(b.X, b.Y, b.Z); }
        for (int j = 0; j < 2; j++) for (int k = 0; k < 2; k++)
        { var a = L(xs[0], ys[j], zs[k]); var b = L(xs[1], ys[j], zs[k]);
          Gl.Vertex3(a.X, a.Y, a.Z); Gl.Vertex3(b.X, b.Y, b.Z); }
        Gl.End();

        // ---- ソーラーパネル (左右各1枚) ----
        Gl.Color3(0.25f, 0.55f, 0.30f);
        Gl.Begin(PrimitiveType.Lines);
        foreach (int side in new[] { -1, 1 })
        {
            float xi = side * 60f;   // 船体端
            float xo = side * 185f;  // パネル外端
            float z0 = -35f;         // 前端 (ドッキングポート側)
            float z1 = -105f;        // 後端
            var p00 = L(xi, 0f, z0); var p10 = L(xo, 0f, z0);
            var p01 = L(xi, 0f, z1); var p11 = L(xo, 0f, z1);
            Gl.Vertex3(p00.X, p00.Y, p00.Z); Gl.Vertex3(p10.X, p10.Y, p10.Z); // 前辺
            Gl.Vertex3(p01.X, p01.Y, p01.Z); Gl.Vertex3(p11.X, p11.Y, p11.Z); // 後辺
            Gl.Vertex3(p00.X, p00.Y, p00.Z); Gl.Vertex3(p01.X, p01.Y, p01.Z); // 内辺
            Gl.Vertex3(p10.X, p10.Y, p10.Z); Gl.Vertex3(p11.X, p11.Y, p11.Z); // 外辺
            // 中央仕切り線
            var pm0 = L((xi + xo) * 0.5f, 0f, z0); var pm1 = L((xi + xo) * 0.5f, 0f, z1);
            Gl.Vertex3(pm0.X, pm0.Y, pm0.Z); Gl.Vertex3(pm1.X, pm1.Y, pm1.Z);
        }
        Gl.End();

        // ---- エンジンノズル (後端2基) ----
        Gl.Color3(1.0f, 0.45f, 0.1f);
        Gl.Begin(PrimitiveType.Lines);
        foreach (float ex in new[] { -28f, 28f })
        {
            var nc = L(ex, 0f, -140f);
            for (int seg = 0; seg < 8; seg++)
            {
                float a0 = seg       * MathF.PI * 2f / 8f;
                float a1 = (seg + 1) * MathF.PI * 2f / 8f;
                var v0 = L(ex + MathF.Cos(a0) * 12f, MathF.Sin(a0) * 12f, -140f);
                var v1 = L(ex + MathF.Cos(a1) * 12f, MathF.Sin(a1) * 12f, -140f);
                Gl.Vertex3(v0.X, v0.Y, v0.Z); Gl.Vertex3(v1.X, v1.Y, v1.Z);
                // ノズルの深さ方向ライン
                var n2 = L(ex + MathF.Cos(a0) * 12f, MathF.Sin(a0) * 12f, -155f);
                Gl.Vertex3(v0.X, v0.Y, v0.Z); Gl.Vertex3(n2.X, n2.Y, n2.Z);
            }
        }
        Gl.End();
        Gl.LineWidth(1.0f);
    }

    // ライバル機: 赤いワイヤーフレーム宇宙船
    public static void RenderRivalShip(ref RivalShip rival, Vec3 shipPos)
    {
        if (!rival.Active) return;
        Vec3 raw = Vec3.Sub(rival.Pos, shipPos);
        float drawDist = C.SPACE_SIZE * 0.6f;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            Vec3 rel = new(raw.X + dx * C.SPACE_SIZE,
                           raw.Y + dy * C.SPACE_SIZE,
                           raw.Z + dz * C.SPACE_SIZE);
            if (Vec3.Len(rel) > drawDist) continue;
            RenderRivalShipAt(rel, rival.Fwd, rival.Up);
        }
    }

    private static void RenderRivalShipAt(Vec3 rel, Vec3 fwd, Vec3 up)
    {
        Vec3 right = Vec3.Norm(Vec3.Cross(fwd, up));
        Vec3 L(float f, float r, float u) => new Vec3(
            rel.X + fwd.X * f + right.X * r + up.X * u,
            rel.Y + fwd.Y * f + right.Y * r + up.Y * u,
            rel.Z + fwd.Z * f + right.Z * r + up.Z * u);
        void V(Vec3 v) { Gl.Vertex3(v.X, v.Y, v.Z); }

        Gl.LineWidth(1.5f);
        Gl.Color3(1.0f, 0.35f, 0.35f); // 赤
        Gl.Begin(PrimitiveType.Lines);

        // 胴体: 4本の縦通材 (-10 ≤ f ≤ +10, ±3 right, ±2 up)
        V(L(-10,-3,-2)); V(L(10,-3,-2));
        V(L(-10, 3,-2)); V(L(10, 3,-2));
        V(L(-10,-3, 2)); V(L(10,-3, 2));
        V(L(-10, 3, 2)); V(L(10, 3, 2));
        // 前断面
        V(L(10,-3,-2)); V(L(10, 3,-2)); V(L(10, 3,-2)); V(L(10, 3, 2));
        V(L(10, 3, 2)); V(L(10,-3, 2)); V(L(10,-3, 2)); V(L(10,-3,-2));
        // 後断面
        V(L(-10,-3,-2)); V(L(-10, 3,-2)); V(L(-10, 3,-2)); V(L(-10, 3, 2));
        V(L(-10, 3, 2)); V(L(-10,-3, 2)); V(L(-10,-3, 2)); V(L(-10,-3,-2));
        // ノーズコーン (4稜線)
        V(L(16,0,0)); V(L(10,-3,-2)); V(L(16,0,0)); V(L(10, 3,-2));
        V(L(16,0,0)); V(L(10, 3, 2)); V(L(16,0,0)); V(L(10,-3, 2));
        // 主翼 (後退翼)
        V(L( 5,-3,0)); V(L(-8,-18,0));  // 左前縁
        V(L(-5,-3,0)); V(L(-8,-18,0));  // 左後縁
        V(L( 5, 3,0)); V(L(-8, 18,0));  // 右前縁
        V(L(-5, 3,0)); V(L(-8, 18,0));  // 右後縁
        V(L( 5,-3,0)); V(L(-5,-3,0));   // 左付け根
        V(L( 5, 3,0)); V(L(-5, 3,0));   // 右付け根
        // 垂直尾翼
        V(L(-10,0, 2)); V(L(-16,0, 7));
        V(L(-10,0,-2)); V(L(-16,0, 7));
        V(L(-10,0, 2)); V(L(-10,0,-2));

        Gl.End();
        Gl.LineWidth(1.0f);
    }

    // 質量弾: 深度テスト無効・グラデーショントレイル＋3軸クロス
    public static void RenderBullet(ref Bullet bullet, Vec3 shipPos)
    {
        if (!bullet.Active) return;
        Vec3 rel = Vec3.TorusDelta(shipPos, bullet.Pos);

        // トレイル: 進行方向に伸びるグラデーションライン
        float spd = Vec3.Len(bullet.Vel);
        if (spd > 0.1f)
        {
            Vec3 dir = Vec3.Scale(bullet.Vel, 1f / spd);
            float trailLen = Math.Min(70f, spd * 0.45f);
            Vec3 tail = Vec3.Sub(rel, Vec3.Scale(dir, trailLen));
            Gl.LineWidth(2.5f);
            Gl.Begin(PrimitiveType.Lines);
            Gl.Color4(1.0f, 0.90f, 0.96f, 1.0f); // 先端: 白ピンク
            Gl.Vertex3(rel.X, rel.Y, rel.Z);
            Gl.Color4(1.0f, 0.35f, 0.60f, 0.0f); // 末端: フェードアウト
            Gl.Vertex3(tail.X, tail.Y, tail.Z);
            Gl.End();
        }

        // 弾頭: 3軸クロス (どの角度からでも見える立体的な星型)
        const float cs = 8.0f;
        Gl.LineWidth(2.5f);
        Gl.Color3(1.0f, 0.80f, 0.92f);
        Gl.Begin(PrimitiveType.Lines);
        Gl.Vertex3(rel.X - cs, rel.Y,      rel.Z     ); Gl.Vertex3(rel.X + cs, rel.Y,      rel.Z     );
        Gl.Vertex3(rel.X,      rel.Y - cs, rel.Z     ); Gl.Vertex3(rel.X,      rel.Y + cs, rel.Z     );
        Gl.Vertex3(rel.X,      rel.Y,      rel.Z - cs); Gl.Vertex3(rel.X,      rel.Y,      rel.Z + cs);
        Gl.End();

        // 中心点
        Gl.PointSize(5f);
        Gl.Color3(1.0f, 1.0f, 1.0f);
        Gl.Begin(PrimitiveType.Points); Gl.Vertex3(rel.X, rel.Y, rel.Z); Gl.End();
        Gl.PointSize(1f);

        Gl.LineWidth(1.0f);
    }

    // ボーナスアイテム: 白(時間) or 緑(燃料) の光点
    public static void RenderBonusItem(GameState gs, Vec3 shipPos, int screenH)
    {
        if (gs.ItemType == 0) return;

        Vec3  delta = Vec3.TorusDelta(shipPos, gs.ItemPos);
        float dist  = MathF.Max(0.5f, Vec3.Len(delta));
        float rx = delta.X, ry = delta.Y, rz = delta.Z;

        // 距離に応じた投影サイズ: radius_world * (screenH/2 / tan(FOV/2)) / dist
        float focalLen = (screenH * 0.5f) / MathF.Tan(C.FOV_DEG * MathF.PI / 360.0f);
        float coreSize  = Math.Clamp(8.0f * focalLen / dist, 2.0f, 120.0f);
        float midSize   = Math.Clamp(coreSize * 1.7f,         3.0f, 120.0f);
        float outerSize = Math.Clamp(coreSize * 2.8f,         4.0f, 120.0f);

        bool isTime = (gs.ItemType == 1);

        Gl.DepthMask(false);
        Gl.Enable(EnableCap.PointSmooth);
        Gl.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);

        // 外側グロー
        Gl.PointSize(outerSize);
        if (isTime) Gl.Color4(0.85f, 0.85f, 1.0f, 0.14f);
        else        Gl.Color4(0.0f,  0.80f, 0.0f, 0.14f);
        Gl.Begin(PrimitiveType.Points); Gl.Vertex3(rx, ry, rz); Gl.End();

        // 中間グロー
        Gl.PointSize(midSize);
        if (isTime) Gl.Color4(0.9f, 0.9f, 1.0f, 0.55f);
        else        Gl.Color4(0.2f, 1.0f, 0.2f, 0.55f);
        Gl.Begin(PrimitiveType.Points); Gl.Vertex3(rx, ry, rz); Gl.End();

        // コア
        Gl.PointSize(coreSize);
        if (isTime) Gl.Color4(1.0f, 1.0f, 1.0f, 1.0f);
        else        Gl.Color4(0.7f, 1.0f, 0.7f, 1.0f);
        Gl.Begin(PrimitiveType.Points); Gl.Vertex3(rx, ry, rz); Gl.End();

        Gl.Disable(EnableCap.PointSmooth);
        Gl.PointSize(1.0f);
        Gl.DepthMask(true);
    }

    // 中性子星: 赤い光点 (最近傍トーラスコピーを1個描画)
    public static void RenderNeutronStar(GameState gs, Vec3 shipPos)
    {
        if (!gs.HasNeutronStar) return;

        Vec3  delta = Vec3.TorusDelta(shipPos, gs.NeutronStarPos);
        float rx = delta.X, ry = delta.Y, rz = delta.Z;

        // 複数レイヤを同一深度で重ね描きするため深度書き込みを停止
        // (深度テストは維持: リングの裏側には描画されない)
        Gl.DepthMask(false);
        Gl.Enable(EnableCap.PointSmooth);
        Gl.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);

        // 外側グロー (大, 半透明赤)
        Gl.PointSize(20.0f);
        Gl.Color4(0.8f, 0.0f, 0.0f, 0.18f);
        Gl.Begin(PrimitiveType.Points); Gl.Vertex3(rx, ry, rz); Gl.End();

        // 中間グロー
        Gl.PointSize(11.0f);
        Gl.Color4(1.0f, 0.12f, 0.0f, 0.60f);
        Gl.Begin(PrimitiveType.Points); Gl.Vertex3(rx, ry, rz); Gl.End();

        // コア (明るい赤白)
        Gl.PointSize(5.0f);
        Gl.Color4(1.0f, 0.85f, 0.85f, 1.0f);
        Gl.Begin(PrimitiveType.Points); Gl.Vertex3(rx, ry, rz); Gl.End();

        Gl.Disable(EnableCap.PointSmooth);
        Gl.PointSize(1.0f);
        Gl.DepthMask(true);
    }

    // ==================== HUD helpers ====================
    private static void RenderCockpit(int w, int h, float panelY)
    {
        float fw  = (float)w;
        float mg  = 16.0f, arm = 55.0f;
        float bot = panelY - 4.0f;

        Gl.LineWidth(2.0f);
        Gl.Color3(0.25f, 0.38f, 0.50f);
        Gl.Begin(PrimitiveType.Lines);
        Gl.Vertex2(mg, mg);       Gl.Vertex2(mg + arm, mg);
        Gl.Vertex2(mg, mg);       Gl.Vertex2(mg, mg + arm);
        Gl.Vertex2(fw - mg, mg);  Gl.Vertex2(fw - mg - arm, mg);
        Gl.Vertex2(fw - mg, mg);  Gl.Vertex2(fw - mg, mg + arm);
        Gl.Vertex2(mg, bot);      Gl.Vertex2(mg + arm, bot);
        Gl.Vertex2(mg, bot);      Gl.Vertex2(mg, bot - arm);
        Gl.Vertex2(fw - mg, bot); Gl.Vertex2(fw - mg - arm, bot);
        Gl.Vertex2(fw - mg, bot); Gl.Vertex2(fw - mg, bot - arm);
        Gl.End();
        Gl.LineWidth(1.0f);
    }

    private static void RenderCrosshair(int w, int h)
    {
        float cx = w * 0.5f, cy = h * 0.5f;
        Gl.LineWidth(1.5f);
        Gl.Color3(0.2f, 1.0f, 0.3f);
        Gl.Begin(PrimitiveType.Lines);
        Gl.Vertex2(cx - 14, cy); Gl.Vertex2(cx - 4,  cy);
        Gl.Vertex2(cx + 4,  cy); Gl.Vertex2(cx + 14, cy);
        Gl.Vertex2(cx, cy - 14); Gl.Vertex2(cx, cy - 4);
        Gl.Vertex2(cx, cy + 4);  Gl.Vertex2(cx, cy + 14);
        Gl.End();
        Gl.LineWidth(1.0f);
    }

    // ==================== HUD ====================
    public static void RenderHud(GameState gs, int w, int h)
    {
        Gl.MatrixMode(MatrixMode.Projection);
        Gl.PushMatrix();
        Gl.LoadIdentity();
        Gl.Ortho(0.0, (double)w, (double)h, 0.0, -1.0, 1.0);
        Gl.MatrixMode(MatrixMode.Modelview);
        Gl.PushMatrix();
        Gl.LoadIdentity();
        Gl.Disable(EnableCap.DepthTest);

        float fw = (float)w, fh = (float)h;
        float panelY = fh - 70.0f;

        // Panel background
        Gl.Color4(0.04f, 0.04f, 0.10f, 0.88f);
        Gl.Begin(PrimitiveType.Quads);
        Gl.Vertex2(0, panelY); Gl.Vertex2(fw, panelY);
        Gl.Vertex2(fw, fh);    Gl.Vertex2(0, fh);
        Gl.End();

        Gl.LineWidth(1.0f);
        Gl.Color3(0.15f, 0.30f, 0.55f);
        Gl.Begin(PrimitiveType.Lines);
        Gl.Vertex2(0, panelY); Gl.Vertex2(fw, panelY);
        Gl.End();
        Gl.Color3(0.08f, 0.18f, 0.35f);
        Gl.Begin(PrimitiveType.Lines);
        Gl.Vertex2(0, panelY + 1); Gl.Vertex2(fw, panelY + 1);
        Gl.End();

        Gl.Color3(0.10f, 0.18f, 0.30f);
        Gl.Begin(PrimitiveType.Lines);
        Gl.Vertex2(232, panelY + 4); Gl.Vertex2(232, fh - 4);
        Gl.Vertex2(522, panelY + 4); Gl.Vertex2(522, fh - 4);
        Gl.End();

        RenderCrosshair(w, h);
        RenderCockpit(w, h, panelY);

        float cw = 7.0f, ch = 11.0f;

        // ===== Left block: velocity =====
        Vec3 rgt3d = Vec3.Norm(Vec3.Cross(gs.Fwd, gs.Up));
        float vFwd = Vec3.Dot(gs.Vel, gs.Fwd);
        float vRgt = Vec3.Dot(gs.Vel, rgt3d);
        float vUp  = Vec3.Dot(gs.Vel, gs.Up);
        float speed = Vec3.Len(gs.Vel);

        float lx   = 6.0f,  ly   = panelY + 6.0f;
        float col2  = 116.0f;
        float row2  = ly + 28.0f;

        Gl.Color3((vFwd >= 0) ? 0.25f : 1.0f,
                   (vFwd >= 0) ? 1.00f : 0.55f,
                   (vFwd >= 0) ? 0.45f : 0.10f);
        DrawString(lx, ly, cw, ch, "FWD");

        Gl.Color3(0.35f, 0.70f, 1.0f);
        DrawString(col2, ly,   cw, ch, "RGT");
        DrawString(lx,   row2, cw, ch, "UP");
        DrawString(col2, row2, cw, ch, "SPD");

        Gl.Color3(0.0f, 1.0f, 0.65f);
        DrawFloatInt(lx   + 28, ly,   cw, ch, vFwd, 5);
        DrawFloatInt(col2 + 28, ly,   cw, ch, vRgt, 5);
        DrawFloatInt(lx   + 20, row2, cw, ch, vUp,  5);

        Gl.Color3(1.0f, 0.85f, 0.1f);
        DrawFloatInt(col2 + 28, row2, cw, ch, speed, 4);

        // ===== Center block =====
        float mx   = 238.0f;
        float barW = 162.0f, barH = 12.0f;

        Gl.Color3(0.5f, 0.7f, 1.0f);
        DrawString(mx, panelY + 4, cw, ch, $"ST{gs.Stage}");

        if (gs.IsBonusStage)
        {
            Gl.Color3(1.0f, 0.85f, 0.0f);
            DrawString(mx + 32, panelY + 4, cw, ch, "BONUS");
        }
        else
        {
            Gl.Color3(0.7f, 1.0f, 0.5f);
            DrawString(mx + 32, panelY + 4, cw, ch, $"{gs.RingsDone}/5");
        }

        Gl.Color3(0.45f, 0.45f, 0.65f);
        DrawString(mx + 70, panelY + 4, cw, ch, "SCORE");
        Gl.Color3(1.0f, 0.88f, 0.15f);
        DrawString(mx + 116, panelY + 4, cw, ch, gs.Score.ToString());

        // Timer bar
        float ty    = panelY + 20.0f;
        float tfrac = gs.RingTimer / C.RING_TIME_LIMIT;
        if (tfrac < 0) tfrac = 0;
        if (tfrac > 1) tfrac = 1;
        int  tsec   = (int)gs.RingTimer + 1;
        bool urgent = gs.RingTimer < 5.0f;

        Gl.Color3(0.15f, 0.15f, 0.18f);
        Gl.Begin(PrimitiveType.Quads);
        Gl.Vertex2(mx, ty); Gl.Vertex2(mx + barW, ty);
        Gl.Vertex2(mx + barW, ty + barH); Gl.Vertex2(mx, ty + barH);
        Gl.End();

        float tr = urgent ? 1.0f : (tfrac < 0.4f ? 1.0f : 0.1f);
        float tg = urgent ? 0.1f : (tfrac < 0.4f ? 0.6f : 0.9f);
        Gl.Color3(tr, tg, 0.05f);
        Gl.Begin(PrimitiveType.Quads);
        Gl.Vertex2(mx, ty); Gl.Vertex2(mx + barW * tfrac, ty);
        Gl.Vertex2(mx + barW * tfrac, ty + barH); Gl.Vertex2(mx, ty + barH);
        Gl.End();

        Gl.Color3(0.25f, 0.35f, 0.6f);
        Gl.Begin(PrimitiveType.LineLoop);
        Gl.Vertex2(mx, ty); Gl.Vertex2(mx + barW, ty);
        Gl.Vertex2(mx + barW, ty + barH); Gl.Vertex2(mx, ty + barH);
        Gl.End();

        Gl.Color3(urgent ? 1.0f : 0.6f, urgent ? 0.2f : 0.8f, 0.2f);
        DrawString(mx + barW + 4, ty, cw, ch, tsec.ToString());

        // Fuel bar
        float fy2  = panelY + 38.0f;
        float frac = gs.Fuel / gs.InitialFuel;
        if (frac < 0) frac = 0;

        Gl.Color3(0.15f, 0.15f, 0.18f);
        Gl.Begin(PrimitiveType.Quads);
        Gl.Vertex2(mx, fy2); Gl.Vertex2(mx + barW, fy2);
        Gl.Vertex2(mx + barW, fy2 + barH); Gl.Vertex2(mx, fy2 + barH);
        Gl.End();

        float cr  = (frac < 0.3f) ? 1.0f : 0.15f;
        float cg2 = (frac > 0.3f) ? 0.75f : 0.25f;
        Gl.Color3(cr, cg2, 0.1f);
        Gl.Begin(PrimitiveType.Quads);
        Gl.Vertex2(mx, fy2); Gl.Vertex2(mx + barW * frac, fy2);
        Gl.Vertex2(mx + barW * frac, fy2 + barH); Gl.Vertex2(mx, fy2 + barH);
        Gl.End();

        Gl.Color3(0.25f, 0.35f, 0.6f);
        Gl.Begin(PrimitiveType.LineLoop);
        Gl.Vertex2(mx, fy2); Gl.Vertex2(mx + barW, fy2);
        Gl.Vertex2(mx + barW, fy2 + barH); Gl.Vertex2(mx, fy2 + barH);
        Gl.End();

        Gl.Color3(0.55f, 0.55f, 0.80f);
        DrawString(mx + barW + 4, fy2, cw, ch, ((int)gs.Fuel).ToString());

        Gl.Color3(0.38f, 0.38f, 0.55f);
        DrawString(mx - 28, ty,  cw * 0.85f, ch * 0.85f, "TM");
        DrawString(mx - 28, fy2, cw * 0.85f, ch * 0.85f, "FL");

        // Mode + ship labels
        {
            string[] mlabels = { "EASY", "NRM", "HARD" };
            string[] slabels = { "STD", "AGILE", "BOOST" };
            float mr  = (gs.Mode == GameMode.Easy) ? 0.3f : (gs.Mode == GameMode.Normal) ? 0.4f : 1.0f;
            float mgv = (gs.Mode == GameMode.Easy) ? 1.0f : (gs.Mode == GameMode.Normal) ? 0.8f : 0.35f;
            Gl.Color3(mr, mgv, 0.15f);
            DrawString(mx - 28, fy2 + 16, cw * 0.75f, ch * 0.75f, mlabels[(int)gs.Mode]);

            float sr = (gs.Ship == ShipType.Standard) ? 0.8f : (gs.Ship == ShipType.Agile) ? 0.3f : 1.0f;
            float sg = (gs.Ship == ShipType.Standard) ? 0.8f : (gs.Ship == ShipType.Agile) ? 0.9f : 0.6f;
            float sb = (gs.Ship == ShipType.Standard) ? 0.8f : (gs.Ship == ShipType.Agile) ? 1.0f : 0.2f;
            Gl.Color3(sr, sg, sb);
            DrawString(mx + 14, fy2 + 16, cw * 0.75f, ch * 0.75f, slabels[(int)gs.Ship]);
        }

        // ===== 質量弾インジケータ (左ブロック下部) =====
        {
            bool rdy  = !gs.Bullet.Active;
            float bix = 6.0f, biy = panelY + 52.0f;
            Gl.Color3(rdy ? 0.05f : 0.40f, rdy ? 0.85f : 0.12f, rdy ? 0.28f : 0.12f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(bix,     biy); Gl.Vertex2(bix + 8, biy);
            Gl.Vertex2(bix + 8, biy + 8); Gl.Vertex2(bix, biy + 8);
            Gl.End();
            Gl.Color3(rdy ? 0.40f : 0.60f, rdy ? 1.0f : 0.35f, rdy ? 0.55f : 0.35f);
            DrawString(bix + 11, biy - 1, cw * 0.8f, ch * 0.8f, rdy ? "SHOT RDY" : "SHOT OUT");
        }

        // ===== アイテム保持インジケータ (タイマーバー右端あたり) =====
        {
            float ix = mx + barW + 52.0f;

            // 時間アイテム (白)
            if (gs.HasTimeItem) Gl.Color3(1.0f, 1.0f, 1.0f);
            else                Gl.Color3(0.25f, 0.25f, 0.28f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(ix,       ty); Gl.Vertex2(ix + 10, ty);
            Gl.Vertex2(ix + 10,  ty + 10); Gl.Vertex2(ix, ty + 10);
            Gl.End();
            if (gs.HasTimeItem) Gl.Color3(0.8f, 0.8f, 1.0f);
            else                Gl.Color3(0.35f, 0.35f, 0.40f);
            DrawString(ix + 12, ty, cw * 0.8f, ch * 0.8f, "TM");

            // 燃料アイテム (緑)
            if (gs.HasFuelItem) Gl.Color3(0.3f, 1.0f, 0.3f);
            else                Gl.Color3(0.10f, 0.25f, 0.10f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(ix,       fy2); Gl.Vertex2(ix + 10, fy2);
            Gl.Vertex2(ix + 10,  fy2 + 10); Gl.Vertex2(ix, fy2 + 10);
            Gl.End();
            if (gs.HasFuelItem) Gl.Color3(0.5f, 1.0f, 0.5f);
            else                Gl.Color3(0.20f, 0.40f, 0.20f);
            DrawString(ix + 12, fy2, cw * 0.8f, ch * 0.8f, "FL");
        }

        // ===== Right block: ring direction indicator =====
        Vec3  toRing   = Vec3.TorusDelta(gs.Pos, gs.Ring.Pos);
        float distRing = Vec3.Len(toRing);
        Vec3  right3d  = Vec3.Norm(Vec3.Cross(gs.Fwd, gs.Up));

        float localX = Vec3.Dot(toRing, right3d);
        float localY = Vec3.Dot(toRing, gs.Up);
        float localZ = Vec3.Dot(toRing, gs.Fwd);

        float indCx = fw - 42.0f;
        float indCy = panelY + 35.0f;
        float indR  = 28.0f;

        Gl.Color4(0.04f, 0.04f, 0.16f, 0.95f);
        Gl.Begin(PrimitiveType.TriangleFan);
        Gl.Vertex2(indCx, indCy);
        for (int i = 0; i <= 32; i++)
        {
            float a = (float)i / 32.0f * 2.0f * MathF.PI;
            Gl.Vertex2(indCx + MathF.Cos(a) * indR, indCy + MathF.Sin(a) * indR);
        }
        Gl.End();

        Gl.Color3(0.18f, 0.35f, 0.70f);
        Gl.Begin(PrimitiveType.LineLoop);
        for (int i = 0; i < 32; i++)
        {
            float a = (float)i / 32.0f * 2.0f * MathF.PI;
            Gl.Vertex2(indCx + MathF.Cos(a) * indR, indCy + MathF.Sin(a) * indR);
        }
        Gl.End();

        Gl.Color3(0.12f, 0.16f, 0.28f);
        Gl.Begin(PrimitiveType.Lines);
        Gl.Vertex2(indCx - indR, indCy); Gl.Vertex2(indCx + indR, indCy);
        Gl.Vertex2(indCx, indCy - indR); Gl.Vertex2(indCx, indCy + indR);
        Gl.End();

        if (distRing > 0.1f)
        {
            float nx   = localX / distRing;
            float ny   = localY / distRing;
            float dotX = indCx + nx * (indR - 5.0f);
            float dotY = indCy - ny * (indR - 5.0f);
            float ddx  = dotX - indCx, ddy = dotY - indCy;
            float ddl  = MathF.Sqrt(ddx * ddx + ddy * ddy);
            if (ddl > indR - 5.0f)
            {
                dotX = indCx + ddx / ddl * (indR - 5.0f);
                dotY = indCy + ddy / ddl * (indR - 5.0f);
            }
            Gl.Color3((localZ < 0) ? 1.0f : 0.0f, (localZ < 0) ? 0.4f : 1.0f, 0.2f);
            Gl.Begin(PrimitiveType.TriangleFan);
            Gl.Vertex2(dotX, dotY);
            for (int i = 0; i <= 16; i++)
            {
                float a = (float)i / 16.0f * 2.0f * MathF.PI;
                Gl.Vertex2(dotX + MathF.Cos(a) * 4, dotY + MathF.Sin(a) * 4);
            }
            Gl.End();
        }

        Gl.Color3(0.45f, 0.45f, 0.70f);
        DrawString(indCx - indR - 58, panelY + 6, cw, ch, "NEXT");
        Gl.Color3(0.70f, 0.70f, 1.0f);
        DrawString(indCx - indR - 58, panelY + 22, cw, ch, ((int)distRing).ToString());

        // No-fuel warning
        if (gs.Fuel <= 0.0f && gs.State == GameStateEnum.Playing)
        {
            Gl.Color3(1.0f, 0.15f, 0.15f);
            DrawString(fw * 0.5f - 56, panelY - 22, cw * 1.3f, ch * 1.3f, "NO FUEL");
        }

        // スコアポップアップ (弾命中・ライバルリング通過)
        if (gs.FloatScoreTimer > 0f)
        {
            float alpha = Math.Min(1.0f, gs.FloatScoreTimer);
            bool pos = gs.FloatScore >= 0;
            Gl.Color4(1.0f, pos ? 0.85f : 0.30f, pos ? 0.10f : 0.10f, alpha);
            string fs = (gs.FloatScore >= 0 ? "+" : "") + gs.FloatScore.ToString();
            float fscw = 12.0f, fsch = 18.0f;
            DrawString(fw * 0.5f - (float)fs.Length * (fscw + 1) * 0.5f,
                       fh * 0.35f, fscw, fsch, fs);
        }

        // ---- Stage clear ----
        if (gs.State == GameStateEnum.StageClear)
        {
            // 失敗時は青紫、クリア時は緑のオーバーレイ
            bool bonusFail = gs.IsBonusStage && gs.BonusFailed;
            Gl.Color4(bonusFail ? 0.02f : 0.0f,
                      bonusFail ? 0.0f  : 0.05f,
                      bonusFail ? 0.08f : 0.0f,
                      0.80f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();
            float scw = 11.0f, sch = 17.0f, scw2 = 8.5f, sch2 = 13.0f;
            float cy2 = fh * 0.5f - 55.0f;

            if (gs.IsBonusStage && gs.BonusFailed)
            {
                // 失敗
                ulong blink2 = GetTicks() / 600;
                float nb = ((blink2 & 1) == 0) ? 1.0f : 0.55f;
                string nob = "NO BONUS";
                Gl.Color3(nb * 0.55f, nb * 0.55f, nb * 0.80f);
                DrawString(fw * 0.5f - (float)nob.Length * (scw + 1) * 0.5f, cy2, scw, sch, nob);

                string buf3 = $"TOTAL SCORE  {gs.Score}";
                Gl.Color3(0.75f, 0.75f, 0.88f);
                DrawString(fw * 0.5f - (float)buf3.Length * (scw2 + 1) * 0.5f, cy2 + 40, scw2, sch2, buf3);

                int secLeft = (int)MathF.Ceiling(gs.StageClearTimer);
                string cntd = $"NEXT STAGE IN  {secLeft}";
                Gl.Color3(0.42f, 0.55f, 0.80f);
                DrawString(fw * 0.5f - (float)cntd.Length * (scw2 + 1) * 0.5f, cy2 + 72, scw2, sch2, cntd);
            }
            else if (gs.IsBonusStage)
            {
                // クリア
                string buf = "DOCKING COMPLETE!";
                Gl.Color3(1.0f, 0.85f, 0.0f);
                DrawString(fw * 0.5f - (float)buf.Length * (scw + 1) * 0.5f, cy2, scw, sch, buf);

                string spdb = $"DOCK BONUS   {gs.SpeedBonusScore}";
                Gl.Color3(1.0f, 0.7f, 0.2f);
                DrawString(fw * 0.5f - (float)spdb.Length * (scw2 + 1) * 0.5f, cy2 + 34, scw2, sch2, spdb);

                string buf2 = $"FUEL BONUS   {gs.StageFuelBonus}";
                Gl.Color3(0.5f, 1.0f, 0.6f);
                DrawString(fw * 0.5f - (float)buf2.Length * (scw2 + 1) * 0.5f, cy2 + 54, scw2, sch2, buf2);

                string buf3 = $"TOTAL SCORE  {gs.Score}";
                Gl.Color3(1.0f, 0.88f, 0.15f);
                DrawString(fw * 0.5f - (float)buf3.Length * (scw2 + 1) * 0.5f, cy2 + 74, scw2, sch2, buf3);

                int secLeft = (int)MathF.Ceiling(gs.StageClearTimer);
                string cntd = $"NEXT STAGE IN  {secLeft}";
                Gl.Color3(0.5f, 0.75f, 1.0f);
                DrawString(fw * 0.5f - (float)cntd.Length * (scw2 + 1) * 0.5f, cy2 + 105, scw2, sch2, cntd);
            }
            else
            {
                string buf = $"STAGE {gs.Stage - 1} CLEAR";
                Gl.Color3(0.3f, 1.0f, 0.4f);
                DrawString(fw * 0.5f - (float)buf.Length * (scw + 1) * 0.5f, cy2, scw, sch, buf);

                string buf2 = $"FUEL BONUS  {gs.StageFuelBonus}";
                Gl.Color3(0.5f, 1.0f, 0.6f);
                DrawString(fw * 0.5f - (float)buf2.Length * (scw2 + 1) * 0.5f, cy2 + 34, scw2, sch2, buf2);

                string buf3 = $"TOTAL SCORE {gs.Score}";
                Gl.Color3(1.0f, 0.88f, 0.15f);
                DrawString(fw * 0.5f - (float)buf3.Length * (scw2 + 1) * 0.5f, cy2 + 54, scw2, sch2, buf3);

                Gl.Color3(0.5f, 0.75f, 1.0f);
                DrawString(fw * 0.5f - 85, cy2 + 85, scw2, sch2, "SPACE TO CONTINUE");
            }
        }

        // ---- Bonus stage intro ----
        if (gs.State == GameStateEnum.BonusIntro)
        {
            Gl.Color4(0.0f, 0.02f, 0.06f, 0.84f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();

            float bcw = 13.0f, bch = 20.0f;
            float mcw = 8.5f,  mch = 13.0f;
            float scw3 = 7.0f, sch3 = 11.0f;
            float bcy = fh * 0.5f - 100.0f;

            // ヘッダー
            string hdr = "** DOCKING SEQUENCE **";
            Gl.Color3(1.0f, 0.85f, 0.0f);
            DrawString(fw * 0.5f - (float)hdr.Length * (bcw + 1) * 0.5f, bcy, bcw, bch, hdr);

            // 母艦告知
            string ringDesc = "NAVIGATE TO THE MOTHERSHIP";
            Gl.Color3(0.7f, 0.9f, 1.0f);
            DrawString(fw * 0.5f - (float)ringDesc.Length * (mcw + 1) * 0.5f, bcy + 30, mcw, mch, ringDesc);

            string hint = "FLY THROUGH THE DOCKING PORT";
            Gl.Color3(0.85f, 0.85f, 0.95f);
            DrawString(fw * 0.5f - (float)hint.Length * (scw3 + 1) * 0.5f, bcy + 50, scw3, sch3, hint);

            // 回数別の条件
            int   bn        = gs.BonusStageNum;
            float portR     = bn == 1 ? C.DOCK_RING_RADIUS : bn == 2 ? C.DOCK_RING_RADIUS * 0.75f : C.DOCK_RING_RADIUS * 0.56f;
            float msSpeed   = bn == 1 ? 0f : bn == 2 ? 35f : 75f;
            string portInfo = msSpeed > 0f
                ? $"PORT RADIUS {(int)portR}  |  MOTHERSHIP SPEED {(int)msSpeed} PX/S"
                : $"PORT RADIUS {(int)portR}  |  MOTHERSHIP STATIONARY";
            Gl.Color3(0.6f, 0.75f, 1.0f);
            DrawString(fw * 0.5f - (float)portInfo.Length * (scw3 + 1) * 0.5f, bcy + 65, scw3, sch3, portInfo);

            // 母艦までの距離
            float introDistVal = Vec3.Len(Vec3.TorusDelta(gs.Pos, gs.Ring.Pos));
            string introDist = $"MOTHERSHIP DISTANCE  {(int)introDistVal} PX";
            Gl.Color3(0.85f, 0.85f, 0.55f);
            DrawString(fw * 0.5f - (float)introDist.Length * (scw3 + 1) * 0.5f, bcy + 79, scw3, sch3, introDist);

            string limit = $"APPROACH SPEED MUST BE BELOW  {(int)C.DOCK_MAX_SPEED} PX/S";
            Gl.Color3(1.0f, 0.45f, 0.15f);
            DrawString(fw * 0.5f - (float)limit.Length * (scw3 + 1) * 0.5f, bcy + 93, scw3, sch3, limit);

            // ドッキングボーナス表
            float tx  = fw * 0.5f - 90.0f;
            float ty2 = bcy + 110.0f;
            float tdy = 17.0f;
            (string spd, string pts, float r, float g, float b)[] rows =
            {
                (" <10 px/s", "1000 pts", 0.3f, 1.0f,  0.5f),
                (" <20 px/s",  "800 pts", 0.6f, 1.0f,  0.3f),
                (" <30 px/s",  "600 pts", 1.0f, 0.88f, 0.1f),
                (" <40 px/s",  "400 pts", 1.0f, 0.68f, 0.0f),
                ("≥40 px/s",    "CRASH!", 1.0f, 0.20f, 0.0f),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                Gl.Color3(row.r, row.g, row.b);
                DrawString(tx,      ty2 + i * tdy, scw3, sch3, row.spd);
                DrawString(tx + 96, ty2 + i * tdy, scw3, sch3, row.pts);
            }

            // PRESS ANY KEY (点滅)
            ulong blink = GetTicks() / 500;
            if ((blink & 1) == 0)
            {
                Gl.Color3(0.5f, 0.88f, 1.0f);
                DrawString(fw * 0.5f - (float)"PRESS ANY KEY".Length * (mcw + 1) * 0.5f,
                           bcy + 200, mcw, mch, "PRESS ANY KEY");
            }
        }

        // ---- Explosion flash ----
        if (gs.State == GameStateEnum.Exploding)
        {
            float t = gs.ExplodeTimer / C.EXPLODE_DURATION;
            float rr = 1.0f, gg = (t > 0.5f) ? 1.0f : t * 2.0f;
            float bb = (t > 0.7f) ? (t - 0.7f) / 0.3f : 0.0f;
            Gl.Color4(rr, gg, bb, t * 0.85f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();
        }

        // ---- Initials entry ----
        if (gs.State == GameStateEnum.Entry)
        {
            Gl.Color4(0.0f, 0.0f, 0.0f, 0.75f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();

            float cy2 = fh * 0.5f - 75.0f;
            Gl.Color3(1.0f, 0.85f, 0.1f);
            DrawString(fw * 0.5f - 72, cy2, 10.0f, 16.0f, "NEW RECORD");

            string sbuf3 = $"SCORE {gs.Score}";
            Gl.Color3(0.9f, 0.9f, 0.9f);
            DrawString(fw * 0.5f - (float)sbuf3.Length * 9 * 0.5f, cy2 + 25, 9.0f, 13.0f, sbuf3);

            float bcw2 = 36.0f, bch2 = 56.0f, bsp = 10.0f;
            float bx0  = fw * 0.5f - (3 * bcw2 + 2 * bsp) * 0.5f;
            float byy  = cy2 + 50.0f;
            ulong blink = GetTicks() / 400;

            for (int i = 0; i < 3; i++)
            {
                float bx   = bx0 + i * (bcw2 + bsp);
                bool isCur = (i == gs.EntryCur);

                Gl.Color4(0.05f, 0.05f, isCur ? 0.25f : 0.10f, 1.0f);
                Gl.Begin(PrimitiveType.Quads);
                Gl.Vertex2(bx - 2, byy - 2); Gl.Vertex2(bx + bcw2 + 2, byy - 2);
                Gl.Vertex2(bx + bcw2 + 2, byy + bch2 + 2); Gl.Vertex2(bx - 2, byy + bch2 + 2);
                Gl.End();

                if (isCur && (blink & 1) != 0)
                    Gl.Color3(1.0f, 0.9f, 0.2f);
                else
                    Gl.Color3(0.3f, 0.4f, isCur ? 0.8f : 0.5f);
                Gl.LineWidth(isCur ? 2.5f : 1.5f);
                Gl.Begin(PrimitiveType.LineLoop);
                Gl.Vertex2(bx - 2, byy - 2); Gl.Vertex2(bx + bcw2 + 2, byy - 2);
                Gl.Vertex2(bx + bcw2 + 2, byy + bch2 + 2); Gl.Vertex2(bx - 2, byy + bch2 + 2);
                Gl.End();
                Gl.LineWidth(1.0f);

                string ch_str = gs.EntryCh[i].ToString();
                Gl.Color3(isCur ? 1.0f : 0.7f, isCur ? 1.0f : 0.7f, isCur ? 0.3f : 0.7f);
                DrawString(bx, byy + 4, bcw2, bch2 - 8, ch_str);
            }

            Gl.Color3(0.5f, 0.65f, 0.85f);
            DrawString(fw * 0.5f - 126, byy + bch2 + 16, 7.5f, 11.0f, "UP/DN:CHANGE  LR:MOVE");
            DrawString(fw * 0.5f - 68,  byy + bch2 + 34, 7.5f, 11.0f, "ENTER:DECIDE");
        }

        // ---- Stage select (コナミコマンド後) ----
        if (gs.State == GameStateEnum.StageSelect)
        {
            Gl.Color4(0.0f, 0.0f, 0.0f, 0.82f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();

            float cy  = fh * 0.5f - 80.0f;
            Gl.Color3(0.3f, 1.0f, 0.5f);
            DrawString(fw * 0.5f - 78, cy, 10.0f, 16.0f, "STAGE SELECT");

            int stageNum = gs.StageSel0 * 10 + gs.StageSel1;
            string hint = stageNum < 1 ? "(invalid)" : $"= STAGE {stageNum:D2}";
            Gl.Color3(0.55f, 0.75f, 0.55f);
            DrawString(fw * 0.5f - (float)hint.Length * 7 * 0.5f, cy + 26, 7.0f, 11.0f, hint);

            float bcw  = 40.0f, bch = 60.0f, bsp = 14.0f;
            float bx0  = fw * 0.5f - (2 * bcw + bsp) * 0.5f;
            float byy  = cy + 52.0f;
            ulong blink = GetTicks() / 400;

            for (int i = 0; i < 2; i++)
            {
                float bx   = bx0 + i * (bcw + bsp);
                bool  isCur = (i == gs.StageSelCur);
                int   digit = (i == 0) ? gs.StageSel0 : gs.StageSel1;

                Gl.Color4(0.02f, isCur ? 0.12f : 0.04f, 0.04f, 1.0f);
                Gl.Begin(PrimitiveType.Quads);
                Gl.Vertex2(bx - 2, byy - 2); Gl.Vertex2(bx + bcw + 2, byy - 2);
                Gl.Vertex2(bx + bcw + 2, byy + bch + 2); Gl.Vertex2(bx - 2, byy + bch + 2);
                Gl.End();

                if (isCur && (blink & 1) != 0)
                    Gl.Color3(0.3f, 1.0f, 0.4f);
                else
                    Gl.Color3(0.15f, isCur ? 0.8f : 0.4f, 0.2f);
                Gl.LineWidth(isCur ? 2.5f : 1.5f);
                Gl.Begin(PrimitiveType.LineLoop);
                Gl.Vertex2(bx - 2, byy - 2); Gl.Vertex2(bx + bcw + 2, byy - 2);
                Gl.Vertex2(bx + bcw + 2, byy + bch + 2); Gl.Vertex2(bx - 2, byy + bch + 2);
                Gl.End();
                Gl.LineWidth(1.0f);

                Gl.Color3(isCur ? 0.4f : 0.25f, isCur ? 1.0f : 0.75f, isCur ? 0.4f : 0.3f);
                DrawString(bx + 1, byy + 5, bcw - 2, bch - 10, digit.ToString());
            }

            Gl.Color3(0.45f, 0.65f, 0.55f);
            DrawString(fw * 0.5f - 124, byy + bch + 16, 7.5f, 11.0f, "UP/DN:CHANGE  LR:MOVE");
            DrawString(fw * 0.5f - 68,  byy + bch + 34, 7.5f, 11.0f, "ENTER:DECIDE");
        }

        // ---- Game over ----
        if (gs.State == GameStateEnum.GameOver)
        {
            Gl.Color4(0.0f, 0.0f, 0.0f, 0.70f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();
            float cy2 = fh * 0.5f - 55.0f;
            Gl.Color3(1.0f, 0.2f, 0.1f);
            DrawString(fw * 0.5f - 65, cy2, 14.0f, 22.0f, "GAME OVER");
            string buf = $"SCORE {gs.Score}  STAGE {gs.Stage}";
            Gl.Color3(1.0f, 0.85f, 0.2f);
            DrawString(fw * 0.5f - (float)buf.Length * 9 * 0.5f, cy2 + 38, 9.0f, 13.0f, buf);
            Gl.Color3(0.5f, 0.75f, 1.0f);
            DrawString(fw * 0.5f - 72, cy2 + 70, 8.5f, 13.0f, "HIT ANY KEY");
        }

        // ---- Title ----
        if (gs.State == GameStateEnum.Title)
        {
            Gl.Color4(0.0f, 0.0f, 0.05f, 0.82f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();
            Gl.Color3(0.5f, 0.8f, 1.0f);
            DrawString(fw * 0.5f - 90, fh * 0.5f - 40, 17.0f, 27.0f, "SPACE SHIP");
            Gl.Color3(0.7f, 0.7f, 0.9f);
            DrawString(fw * 0.5f - 55, fh * 0.5f + 20, 9.0f, 14.0f, "HIT ANY KEY");
        }

        // ---- Mode select ----
        if (gs.State == GameStateEnum.ModeSelect)
        {
            Gl.Color4(0.0f, 0.0f, 0.05f, 0.88f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();

            float my0 = fh * 0.5f - 95.0f;
            Gl.Color3(0.5f, 0.8f, 1.0f);
            DrawString(fw * 0.5f - 72, my0, 11.0f, 17.0f, "MODE SELECT");

            string[] modeLabels = { "EASY",   "NORMAL", "HARD" };
            string[] modeDescs  =
            {
                "STEERING - VELOCITY FOLLOWS HEADING",
                "INERTIA  - FULL THRUST AND BRAKE",
                "NO BRAKE - DECELERATION DISABLED"
            };
            float[] lr = { 0.4f, 0.3f, 0.8f };
            float[] lg = { 0.9f, 0.8f, 0.3f };
            float[] lb = { 0.4f, 0.4f, 0.3f };

            for (int i = 0; i < 3; i++)
            {
                float ry = my0 + 35.0f + i * 52.0f;
                bool sel = (i == gs.ModeSel);

                if (sel)
                {
                    Gl.Color4(0.05f, 0.1f, 0.3f, 1.0f);
                    Gl.Begin(PrimitiveType.Quads);
                    Gl.Vertex2(fw * 0.5f - 160, ry - 4); Gl.Vertex2(fw * 0.5f + 160, ry - 4);
                    Gl.Vertex2(fw * 0.5f + 160, ry + 36); Gl.Vertex2(fw * 0.5f - 160, ry + 36);
                    Gl.End();
                    Gl.Color3(0.9f, 0.85f, 0.2f);
                    Gl.LineWidth(1.5f);
                    Gl.Begin(PrimitiveType.LineLoop);
                    Gl.Vertex2(fw * 0.5f - 160, ry - 4); Gl.Vertex2(fw * 0.5f + 160, ry - 4);
                    Gl.Vertex2(fw * 0.5f + 160, ry + 36); Gl.Vertex2(fw * 0.5f - 160, ry + 36);
                    Gl.End();
                    Gl.LineWidth(1.0f);
                }

                Gl.Color3(sel ? 1.0f : 0.3f, sel ? 0.9f : 0.3f, sel ? 0.2f : 0.3f);
                DrawString(fw * 0.5f - 152, ry, 9.0f, 14.0f, sel ? ">" : " ");

                float sc2 = sel ? 1.3f : 0.7f;
                Gl.Color3(lr[i] * sc2, lg[i] * sc2, lb[i] * sc2);
                DrawString(fw * 0.5f - 135, ry, 11.0f, 17.0f, modeLabels[i]);

                Gl.Color3(sel ? 0.75f : 0.4f, sel ? 0.75f : 0.4f, sel ? 0.75f : 0.4f);
                DrawString(fw * 0.5f - 145, ry + 20, 7.0f, 11.0f, modeDescs[i]);
            }

            Gl.Color3(0.5f, 0.65f, 0.85f);
            DrawString(fw * 0.5f - 84, my0 + 35.0f + 3 * 52.0f + 8, 7.5f, 11.0f, "UP/DN:SELECT  ENTER:NEXT");
        }

        // ---- Ship select ----
        if (gs.State == GameStateEnum.ShipSelect)
        {
            Gl.Color4(0.0f, 0.0f, 0.05f, 0.88f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();

            float sy0 = fh * 0.5f - 95.0f;
            Gl.Color3(0.5f, 0.8f, 1.0f);
            DrawString(fw * 0.5f - 77, sy0, 11.0f, 17.0f, "SELECT SHIP");

            string[] shipLabels = { "STANDARD", "AGILE",    "BOOST" };
            string[] shipDescs  =
            {
                "BALANCED - TURN 1.0X  ACCEL 1.0X",
                "HIGH TURN - TURN 2.0X  ACCEL 1.0X",
                "HIGH THRUST - TURN 1.0X  ACCEL 2.0X"
            };
            float[,] shipCol = { { 0.8f, 0.8f, 0.8f }, { 0.3f, 0.9f, 1.0f }, { 1.0f, 0.6f, 0.2f } };

            for (int i = 0; i < 3; i++)
            {
                float ry2 = sy0 + 35.0f + i * 52.0f;
                bool sel  = (i == gs.ShipSel);

                if (sel)
                {
                    Gl.Color4(0.05f, 0.1f, 0.3f, 1.0f);
                    Gl.Begin(PrimitiveType.Quads);
                    Gl.Vertex2(fw * 0.5f - 160, ry2 - 4); Gl.Vertex2(fw * 0.5f + 160, ry2 - 4);
                    Gl.Vertex2(fw * 0.5f + 160, ry2 + 36); Gl.Vertex2(fw * 0.5f - 160, ry2 + 36);
                    Gl.End();
                    Gl.Color3(shipCol[i, 0], shipCol[i, 1], shipCol[i, 2]);
                    Gl.LineWidth(1.5f);
                    Gl.Begin(PrimitiveType.LineLoop);
                    Gl.Vertex2(fw * 0.5f - 160, ry2 - 4); Gl.Vertex2(fw * 0.5f + 160, ry2 - 4);
                    Gl.Vertex2(fw * 0.5f + 160, ry2 + 36); Gl.Vertex2(fw * 0.5f - 160, ry2 + 36);
                    Gl.End();
                    Gl.LineWidth(1.0f);
                }

                Gl.Color3(sel ? 1.0f : 0.3f, sel ? 0.9f : 0.3f, sel ? 0.2f : 0.3f);
                DrawString(fw * 0.5f - 152, ry2, 9.0f, 14.0f, sel ? ">" : " ");

                float sc3 = sel ? 1.0f : 0.6f;
                Gl.Color3(shipCol[i, 0] * sc3, shipCol[i, 1] * sc3, shipCol[i, 2] * sc3);
                DrawString(fw * 0.5f - 135, ry2, 11.0f, 17.0f, shipLabels[i]);

                Gl.Color3(sel ? 0.75f : 0.4f, sel ? 0.75f : 0.4f, sel ? 0.75f : 0.4f);
                DrawString(fw * 0.5f - 145, ry2 + 20, 7.0f, 11.0f, shipDescs[i]);
            }

            Gl.Color3(0.5f, 0.65f, 0.85f);
            DrawString(fw * 0.5f - 84, sy0 + 35.0f + 3 * 52.0f + 8, 7.5f, 11.0f, "UP/DN:SELECT  ENTER:START");
        }

        // ---- Ranking ----
        if (gs.State == GameStateEnum.Ranking)
        {
            string[]  rmodeNames = { "EASY", "NORMAL", "HARD" };
            float[,]  rmodeCol   = { { 0.3f, 1.0f, 0.4f }, { 1.0f, 0.85f, 0.2f }, { 1.0f, 0.35f, 0.2f } };
            int m = gs.RankingModeIdx % 3;

            Gl.Color4(0.0f, 0.0f, 0.05f, 0.82f);
            Gl.Begin(PrimitiveType.Quads);
            Gl.Vertex2(0, 0); Gl.Vertex2(fw, 0); Gl.Vertex2(fw, fh); Gl.Vertex2(0, fh);
            Gl.End();

            float rx   = fw * 0.5f - 110.0f, ry = fh * 0.5f - 80.0f;
            float rscw = 9.0f, rsch = 13.0f;

            Gl.Color3(rmodeCol[m, 0], rmodeCol[m, 1], rmodeCol[m, 2]);
            string hdr = $"HIGH SCORE [{rmodeNames[m]}]";
            DrawString(fw * 0.5f - (float)hdr.Length * 10 * 0.5f, ry, 10.0f, 15.0f, hdr);

            int cnt = HiScoreManager.Counts[m];
            if (cnt == 0)
            {
                Gl.Color3(0.5f, 0.5f, 0.6f);
                DrawString(rx + 20, ry + 35, rscw, rsch, "NO RECORD YET");
            }
            for (int i = 0; i < cnt; i++)
            {
                ref HiScore hs = ref HiScoreManager.Scores[m, i];
                string rbuf = $"{i + 1}  {hs.Initials,3}  {hs.Score,6}  ST{hs.Stage}";
                float ri_r = (i == 0) ? rmodeCol[m, 0] : 0.7f;
                float ri_g = (i == 0) ? rmodeCol[m, 1] : 0.7f;
                float ri_b = (i == 0) ? rmodeCol[m, 2] : 0.7f;
                Gl.Color3(ri_r, ri_g, ri_b);
                DrawString(rx, ry + 28 + i * 22, rscw, rsch, rbuf);
            }
            Gl.Color3(0.5f, 0.65f, 0.85f);
            DrawString(fw * 0.5f - 55, ry + 150, rscw, rsch, "HIT ANY KEY");
        }

        // ---- 合成速度 大きめ表示 (ゲームビュー内 右上) ----
        if (gs.State == GameStateEnum.Playing   ||
            gs.State == GameStateEnum.BonusIntro ||
            gs.State == GameStateEnum.Countdown)
        {
            float bigCw = cw * 2.2f, bigCh = ch * 2.2f;  // ~15×24
            float sx = fw - 150.0f, sy = 44.0f;
            Gl.Color3(0.28f, 0.35f, 0.52f);
            DrawString(sx, sy + 5, cw, ch, "SPD");
            if (gs.IsBonusStage) Gl.Color3(1.0f, 0.85f, 0.0f);
            else                 Gl.Color3(0.95f, 0.88f, 0.12f);
            DrawFloatInt(sx + 28, sy, bigCw, bigCh, speed, 4);

            // ボーナスステージ: 母艦距離を速度の直下に大きく表示
            if (gs.IsBonusStage)
            {
                float dockDist = Vec3.Len(Vec3.TorusDelta(gs.Pos, gs.Ring.Pos));
                float sy2 = sy + bigCh + 12f;

                // 残距離に応じて色を変える
                float dr, dg, db;
                if      (dockDist > 400f) { dr = 0.3f; dg = 0.9f; db = 1.0f; }   // 遠い: シアン
                else if (dockDist > 200f) { dr = 0.3f; dg = 1.0f; db = 0.35f; }  // 中間: 緑
                else if (dockDist > 100f) { dr = 1.0f; dg = 0.65f; db = 0.1f; }  // 近い: オレンジ
                else                      { dr = 1.0f; dg = 0.15f; db = 0.05f; } // 至近: 赤

                Gl.Color3(0.28f, 0.35f, 0.52f);
                DrawString(sx, sy2 + 5, cw, ch, "DST");
                Gl.Color3(dr, dg, db);
                DrawFloatInt(sx + 28, sy2, bigCw, bigCh, dockDist, 4);
            }
        }

        // ---- Playing warnings ----
        if (gs.State == GameStateEnum.Playing)
        {
            ulong tick = GetTicks();

            if (gs.CollisionWarning != 0 && (tick / 200) % 2 == 0)
            {
                float p = 0.7f + 0.3f * MathF.Sin((float)tick * 0.01f);
                Gl.Color3(1.0f, p * 0.1f, 0.0f);
                DrawString(fw * 0.5f - 54, 18.0f, 14.0f, 22.0f, "DANGER");
            }

            if (gs.ExcellentTimer > 0.0f)
            {
                float alpha = (gs.ExcellentTimer < 0.5f) ? gs.ExcellentTimer * 2.0f : 1.0f;
                float pulse = 0.85f + 0.15f * MathF.Sin((float)tick * 0.025f);
                Gl.Color3(1.0f * pulse * alpha, 1.0f * pulse * alpha, 0.2f * alpha);
                DrawString(fw * 0.5f - 81, fh * 0.38f, 12.0f, 19.0f, "EXCELLENT");
                Gl.Color3(1.0f * alpha, 0.7f * alpha, 0.1f * alpha);
                DrawString(fw * 0.5f - 22, fh * 0.38f + 24, 8.0f, 12.0f, "+50");
            }

            // ボーナスステージ: 速度超過＋至近距離で中央に BRAKE! 警告
            if (gs.IsBonusStage)
            {
                float dockDist = Vec3.Len(Vec3.TorusDelta(gs.Pos, gs.Ring.Pos));
                if (speed > C.DOCK_MAX_SPEED && dockDist < 250f && (tick / 150) % 2 == 0)
                {
                    float p = 0.8f + 0.2f * MathF.Sin((float)tick * 0.03f);
                    Gl.Color3(1.0f, p * 0.1f, 0.0f);
                    DrawString(fw * 0.5f - 60, fh * 0.42f, 14.0f, 22.0f, "BRAKE!");
                }
            }

            if (gs.FuelWarning != 0 && (tick / 500) % 2 == 0)
            {
                float p = 0.6f + 0.4f * MathF.Sin((float)tick * 0.004f);
                Gl.Color3(1.0f, p * 0.45f, 0.0f);
                DrawString(fw - 86.0f, 12.0f, 9.0f, 14.0f, "FUEL LOW");
            }

            if (gs.TimeWarning != 0 && (tick / 167) % 2 == 0)
            {
                float p = 0.7f + 0.3f * MathF.Sin((float)tick * 0.019f);
                Gl.Color3(1.0f, p * 0.15f, 0.0f);
                DrawString(6.0f, 12.0f, 9.0f, 14.0f, "TIME LOW");
            }
        }

        // ---- Countdown ----
        if (gs.State == GameStateEnum.Countdown)
        {
            int n = (int)MathF.Ceiling(gs.CountdownVal);
            float cy2 = fh * 0.5f - 50.0f;
            if (gs.CountdownVal > 0.0f && n >= 1)
            {
                float pulse = 0.7f + 0.3f * MathF.Sin(gs.CountdownVal * MathF.PI);
                Gl.Color3(pulse, pulse * 0.8f, 0.1f);
                DrawString(fw * 0.5f - 30, cy2, 52.0f, 80.0f, n.ToString());
            }
            else
            {
                float pulse = 0.7f + 0.3f * MathF.Sin(gs.CountdownVal * MathF.PI * 4);
                Gl.Color3(0.2f, pulse, 0.2f);
                DrawString(fw * 0.5f - 65, cy2, 52.0f, 80.0f, "GO");
            }
        }

        Gl.Enable(EnableCap.DepthTest);
        Gl.MatrixMode(MatrixMode.Projection);
        Gl.PopMatrix();
        Gl.MatrixMode(MatrixMode.Modelview);
        Gl.PopMatrix();
    }
}
