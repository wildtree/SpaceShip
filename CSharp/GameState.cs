using System.Text.Json;
using System.Text.Json.Serialization;

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
    public float         InitialFuel     = C.INITIAL_FUEL;    // モード別初期燃料
    public float         TimeLimit       = C.RING_TIME_LIMIT; // モード別制限時間
    public Vec3          NeutronStarPos  = default;           // 中性子星位置 (ステージ16以降)
    public bool          HasNeutronStar  = false;
    public int           KonamiStep    = 0;   // コナミコマンド入力ステップ (0-9)
    public int           StageStart    = 1;   // 開始ステージ (チートで変更)
    public int           StageSel0     = 0;   // ステージセレクト 十の位
    public int           StageSel1     = 1;   // ステージセレクト 一の位
    public int           StageSelCur   = 0;   // ステージセレクト カーソル位置
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
        string baseDir = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        string dir = Path.Combine(baseDir, "WildTreeJP", "spaceship");
        Directory.CreateDirectory(dir); // 既に存在する場合は何もしない
        return dir;
    }

    private static string GetPath() => Path.Combine(GetDataDir(), HiScoreFile);

    public static void Save()
    {
        try
        {
            char[] modeChars = { 'E', 'N', 'H' };
            var entries = new List<ScoreEntry>();
            for (int m = 0; m < 3; m++)
                for (int i = 0; i < Counts[m]; i++)
                    entries.Add(new ScoreEntry(
                        modeChars[m].ToString(),
                        Scores[m, i].Initials[..3],
                        Scores[m, i].Score,
                        Scores[m, i].Stage));

            string json = JsonSerializer.Serialize(
                new ScoresFile(entries),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetPath(), json);
        }
        catch { }
    }

    public static void Load()
    {
        string path = GetPath();
        if (!File.Exists(path)) return;
        try
        {
            string json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<ScoresFile>(json);
            if (file?.Scores == null) return;
            foreach (var e in file.Scores)
            {
                if (string.IsNullOrEmpty(e.Mode) || string.IsNullOrEmpty(e.Initials)) continue;
                int m = e.Mode[0] switch { 'E' => 0, 'N' => 1, 'H' => 2, _ => -1 };
                if (m >= 0 && Counts[m] < C.HISCORE_COUNT)
                    Add((e.Initials + "   ")[..3], e.Score, e.Stage, m);
            }
        }
        catch { }
    }

    // ---- JSON DTOs ----
    private record ScoreEntry(
        [property: JsonPropertyName("mode")]     string Mode,
        [property: JsonPropertyName("initials")] string Initials,
        [property: JsonPropertyName("score")]    int    Score,
        [property: JsonPropertyName("stage")]    int    Stage);

    private record ScoresFile(
        [property: JsonPropertyName("scores")] List<ScoreEntry> Scores);
}
