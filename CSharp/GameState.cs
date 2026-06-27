namespace SpaceShip;

// ==================== GameState ====================
internal sealed class GameState
{
    public Vec3          Pos             = new(512, 512, 512);
    public Vec3          Vel             = new(0, 0, 0);
    public Vec3          Fwd             = new(0, 0, 1);
    public Vec3          Up              = new(0, 1, 0);
    public float         Fuel            = C.INITIAL_FUEL;
    public int           Score           = 0;
    public Ring          Ring            = default;
    public Vec3          PrevPos         = new(512, 512, 512);
    public GameStateEnum State           = GameStateEnum.Title;
    public float         ExplodeTimer    = 0f;
    public float         RingTimer       = C.RING_TIME_LIMIT;
    public int           RingsDone       = 0;
    public int           Stage           = 1;
    public int           StageFuelBonus  = 0;
    public float         StageClearTimer = 0f;
    public float         TitleTimer      = C.TITLE_FLIP_SEC;
    public float         CountdownVal    = C.COUNTDOWN_START;
    public char[]        EntryCh         = new char[3] { 'A', 'A', 'A' };
    public int           EntryCur        = 0;
    public GameMode      Mode            = GameMode.Normal;
    public int           ModeSel         = (int)GameMode.Normal;
    public ShipType      Ship            = ShipType.Standard;
    public int           ShipSel         = (int)ShipType.Standard;
    public int           RankingModeIdx  = 0;
    public int           CollisionWarning = 0;
    public int           FuelWarning     = 0;
    public int           TimeWarning     = 0;
    public int           CountdownLastN  = (int)C.COUNTDOWN_START + 1;
    public float         ExcellentTimer  = 0f;

    public void ReOrthogonalize()
    {
        Fwd = Vec3.Norm(Fwd);
        Vec3 right = Vec3.Norm(Vec3.Cross(Fwd, Up));
        Up = Vec3.Norm(Vec3.Cross(right, Fwd));
    }
}

// ==================== HiScoreManager ====================
internal static class HiScoreManager
{
    private const string HiScoreFile = "scores.json";

    public static HiScore[,] Scores { get; } = new HiScore[3, C.HISCORE_COUNT];
    public static int[] Counts { get; } = new int[3];

    public static bool Qualifies(int score, int mode)
    {
        if (Counts[mode] < C.HISCORE_COUNT) return true;
        return score > Scores[mode, Counts[mode] - 1].Score;
    }

    public static void Add(string initials, int score, int stage, int mode)
    {
        // Ensure exactly 3 chars, pad with space
        string init3 = (initials + "   ").Substring(0, 3);
        int ins = Counts[mode];
        for (int i = 0; i < Counts[mode]; i++)
        {
            if (score > Scores[mode, i].Score) { ins = i; break; }
        }
        int nc = (Counts[mode] < C.HISCORE_COUNT) ? Counts[mode] + 1 : C.HISCORE_COUNT;
        for (int i = nc - 1; i > ins; i--)
            Scores[mode, i] = Scores[mode, i - 1];
        Scores[mode, ins] = new HiScore { Initials = init3, Score = score, Stage = stage };
        Counts[mode] = nc;
    }

    private static string GetDataDir()
    {
        string home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(home, ".local", "share", "WildTreeJP", "spaceship");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private static string GetPath() => Path.Combine(GetDataDir(), HiScoreFile);

    public static void Save()
    {
        string path = GetPath();
        try
        {
            char[] modeChars = { 'E', 'N', 'H' };
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n  \"scores\": [");
            bool first = true;
            for (int m = 0; m < 3; m++)
            {
                for (int i = 0; i < Counts[m]; i++)
                {
                    if (!first) sb.Append(',');
                    sb.Append("\n    {");
                    sb.Append($"\"mode\": \"{modeChars[m]}\", ");
                    sb.Append($"\"initials\": \"{Scores[m, i].Initials.Substring(0, 3)}\", ");
                    sb.Append($"\"score\": {Scores[m, i].Score}, ");
                    sb.Append($"\"stage\": {Scores[m, i].Stage}");
                    sb.Append('}');
                    first = false;
                }
            }
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(path, sb.ToString());
        }
        catch { }
    }

    public static void Load()
    {
        string path = GetPath();
        if (!File.Exists(path)) return;
        try
        {
            string buf = File.ReadAllText(path);
            // Find "scores" array
            int arrStart = buf.IndexOf("\"scores\"", StringComparison.Ordinal);
            if (arrStart < 0) return;
            arrStart = buf.IndexOf('[', arrStart);
            if (arrStart < 0) return;
            int arrEnd = buf.IndexOf(']', arrStart);
            if (arrEnd < 0) arrEnd = buf.Length;

            // Parse each { ... } object
            int p = arrStart;
            while (p < arrEnd)
            {
                int objStart = buf.IndexOf('{', p);
                if (objStart < 0 || objStart >= arrEnd) break;
                int objEnd = buf.IndexOf('}', objStart);
                if (objEnd < 0 || objEnd > arrEnd) break;
                string obj = buf.Substring(objStart, objEnd - objStart + 1);

                string modeS   = JsonGetStr(obj, "mode")     ?? "";
                string initials = JsonGetStr(obj, "initials") ?? "";
                int    score   = JsonGetInt(obj, "score");
                int    stage   = JsonGetInt(obj, "stage");

                if (modeS.Length > 0 && initials.Length > 0)
                {
                    int m = modeS[0] == 'E' ? 0 : modeS[0] == 'N' ? 1 : modeS[0] == 'H' ? 2 : -1;
                    if (m >= 0 && Counts[m] < C.HISCORE_COUNT)
                    {
                        string init3 = (initials + "   ").Substring(0, 3);
                        Add(init3, score, stage, m);
                    }
                }
                p = objEnd + 1;
            }
        }
        catch { }
    }

    private static string? JsonGetStr(string obj, string key)
    {
        string pat = $"\"{key}\":\"";
        int idx = obj.IndexOf(pat, StringComparison.Ordinal);
        if (idx < 0) return null;
        int start = idx + pat.Length;
        int end = obj.IndexOf('"', start);
        if (end < 0) return null;
        return obj.Substring(start, end - start);
    }

    private static int JsonGetInt(string obj, string key)
    {
        string pat = $"\"{key}\":";
        int idx = obj.IndexOf(pat, StringComparison.Ordinal);
        if (idx < 0) return 0;
        int start = idx + pat.Length;
        while (start < obj.Length && obj[start] == ' ') start++;
        int end = start;
        while (end < obj.Length && (char.IsDigit(obj[end]) || obj[end] == '-')) end++;
        return int.TryParse(obj.Substring(start, end - start), out int v) ? v : 0;
    }
}
