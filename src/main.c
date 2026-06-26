#include <SDL3/SDL.h>
#include <SDL3/SDL_opengl.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <libgen.h>
#include <limits.h>
#include <string.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <errno.h>

// ==================== パラメータ ====================
#define SPACE_SIZE       1024.0f   // トーラス空間の一辺 [px]
#define SHIP_RADIUS      8.0f      // 宇宙船の半径 [px]
#define RING_RADIUS      32.0f     // リングの半径 [px] (直径64)
#define RING_TUBE_RADIUS 2.0f      // リングのチューブ半径 [px]
#define MAIN_ACCEL       40.0f     // メインスラスター加速度 [px/s^2]
#define BRAKE_ACCEL      40.0f     // ブレーキ加速度 [px/s^2]
#define ROTATION_SPEED   1.8f      // 方向転換速度 [rad/s]
#define DRAG_K           0.0f      // 速度比例減速係数 k (0で無効)
#define FUEL_MAIN        8.0f      // メインスラスター燃料消費 [/s]
#define FUEL_BRAKE       8.0f      // ブレーキ燃料消費 [/s]
#define FUEL_ROTATE      2.0f      // 方向転換燃料消費 [/s]
#define INITIAL_FUEL     1000.0f   // 初期燃料
#define STAR_COUNT       800       // 星の数
#define RING_SEGMENTS    64        // リング描画分割数
#define TUBE_SEGMENTS    10        // チューブ断面分割数

#define RING_TIME_LIMIT  30.0f    // リングごとの制限時間 [s]
#define RINGS_PER_STAGE  5        // ステージクリアに必要なリング数
#define RING_BASE_SCORE  100      // リング通過の基本点
#define RING_TIME_BONUS  5        // 残り秒数×この値が追加ボーナス
#define EXPLODE_DURATION 2.0f     // 爆発エフェクト秒数
#define STAGE_CLEAR_WAIT 3.0f     // ステージクリア表示秒数
#define TITLE_FLIP_SEC   5.0f     // タイトル/ランキング切替間隔 [s]
#define HISCORE_COUNT    5        // ハイスコア保持数
#define COUNTDOWN_START  5.0f     // カウントダウン開始値

#define WINDOW_WIDTH  640
#define WINDOW_HEIGHT 400
#define FOV_DEG       75.0f
#define NEAR_PLANE    0.5f
#define FAR_PLANE     2000.0f

// ==================== 数学 ====================
typedef struct { float x, y, z; } Vec3;

static inline Vec3 v3(float x, float y, float z) { return (Vec3){x, y, z}; }
static inline Vec3 vadd(Vec3 a, Vec3 b) { return v3(a.x+b.x, a.y+b.y, a.z+b.z); }
static inline Vec3 vsub(Vec3 a, Vec3 b) { return v3(a.x-b.x, a.y-b.y, a.z-b.z); }
static inline Vec3 vscale(Vec3 a, float s) { return v3(a.x*s, a.y*s, a.z*s); }
static inline float vdot(Vec3 a, Vec3 b) { return a.x*b.x + a.y*b.y + a.z*b.z; }
static inline Vec3 vcross(Vec3 a, Vec3 b) {
    return v3(a.y*b.z - a.z*b.y, a.z*b.x - a.x*b.z, a.x*b.y - a.y*b.x);
}
static inline float vlen(Vec3 a) { return sqrtf(vdot(a, a)); }
static inline Vec3 vnorm(Vec3 a) {
    float l = vlen(a);
    return (l < 1e-6f) ? v3(0,0,1) : vscale(a, 1.0f/l);
}

// トーラス空間の最短差分ベクトル
static Vec3 torus_delta(Vec3 from, Vec3 to) {
    Vec3 d = vsub(to, from);
    if (d.x >  SPACE_SIZE*0.5f) d.x -= SPACE_SIZE;
    if (d.x < -SPACE_SIZE*0.5f) d.x += SPACE_SIZE;
    if (d.y >  SPACE_SIZE*0.5f) d.y -= SPACE_SIZE;
    if (d.y < -SPACE_SIZE*0.5f) d.y += SPACE_SIZE;
    if (d.z >  SPACE_SIZE*0.5f) d.z -= SPACE_SIZE;
    if (d.z < -SPACE_SIZE*0.5f) d.z += SPACE_SIZE;
    return d;
}

// 座標ラップ
static Vec3 vwrap(Vec3 p) {
    p.x = fmodf(p.x, SPACE_SIZE); if (p.x < 0) p.x += SPACE_SIZE;
    p.y = fmodf(p.y, SPACE_SIZE); if (p.y < 0) p.y += SPACE_SIZE;
    p.z = fmodf(p.z, SPACE_SIZE); if (p.z < 0) p.z += SPACE_SIZE;
    return p;
}

// Rodriguesの回転式: vをaxisの周りにangle[rad]回転
static Vec3 vrotate(Vec3 v, Vec3 axis, float angle) {
    Vec3 n = vnorm(axis);
    float c = cosf(angle), s = sinf(angle);
    return vadd(vadd(vscale(v, c), vscale(vcross(n, v), s)),
                vscale(n, vdot(n, v) * (1.0f - c)));
}

// ==================== ゲーム状態 ====================
typedef struct {
    Vec3  pos;        // リング中心位置
    Vec3  normal;     // リング法線 (向き)
    Vec3  up;         // リング上方向
    float rot_speed;  // 回転角速度 [rad/s] (0=静止)
    Vec3  rot_axis;   // 世界座標系固定の回転軸
    float move_speed; // 移動速度 [px/s] (0=静止)
    Vec3  move_dir;   // 移動方向 (正規化済み)
    int   color_type; // 0=金, 1=シアン(回転), 2=マゼンタ(移動)
} Ring;

typedef enum {
    MODE_EASY = 0,
    MODE_NORMAL,
    MODE_HARD
} GameMode;

typedef enum {
    SHIP_STANDARD = 0, // 標準型
    SHIP_AGILE,        // 高機動型: 旋回×2
    SHIP_BOOST         // 高推力型: 加速×2
} ShipType;

typedef enum {
    STATE_TITLE,
    STATE_RANKING,
    STATE_MODE_SELECT,
    STATE_SHIP_SELECT,
    STATE_COUNTDOWN,
    STATE_PLAYING,
    STATE_EXPLODING,
    STATE_ENTRY,
    STATE_GAMEOVER,
    STATE_STAGE_CLEAR
} GameStateEnum;

// ハイスコア (モード別)
#define HISCORE_FILE "scores.json"
typedef struct {
    char initials[4]; // イニシャル3文字 + '\0'
    int  score;
    int  stage;
} HiScore;
static HiScore g_hiscores[3][HISCORE_COUNT]; // [mode][rank]
static int     g_hiscore_count[3];           // モードごとの登録数

static int hiscore_qualifies(int score, int mode) {
    if (g_hiscore_count[mode] < HISCORE_COUNT) return 1;
    return score > g_hiscores[mode][g_hiscore_count[mode] - 1].score;
}

static void hiscore_add(const char *init, int score, int stage, int mode) {
    int *cnt = &g_hiscore_count[mode];
    HiScore *hs = g_hiscores[mode];
    int ins = *cnt;
    for (int i = 0; i < *cnt; i++) {
        if (score > hs[i].score) { ins = i; break; }
    }
    int nc = (*cnt < HISCORE_COUNT) ? *cnt + 1 : HISCORE_COUNT;
    for (int i = nc - 1; i > ins; i--) hs[i] = hs[i-1];
    hs[ins].initials[0] = init[0];
    hs[ins].initials[1] = init[1];
    hs[ins].initials[2] = init[2];
    hs[ins].initials[3] = '\0';
    hs[ins].score = score;
    hs[ins].stage = stage;
    *cnt = nc;
}

static int mkdir_p(char *path, mode_t mode)
{
    char tmp[PATH_MAX];
    char *pdir;
    struct stat sbuf;
    strncpy(tmp, path, PATH_MAX);
    if (stat(tmp, &sbuf) == 0 && S_ISDIR(sbuf.st_mode))
    {
	return 0;
    }
    pdir = dirname(tmp);
    if (stat(pdir, &sbuf) != 0 || !S_ISDIR(sbuf.st_mode))
    {
	mkdir_p(pdir, mode);
    }
    return mkdir(path, mode);
}

static char *get_datadir(void)
{
    static char datadir[PATH_MAX];
    char *home_dir = getenv("HOME");
    if (home_dir == NULL)
	    return NULL;
    strncpy(datadir, home_dir, PATH_MAX);
    strncat(datadir, "/.local/share/WildTreeJP/spaceship", PATH_MAX);
    if (mkdir_p(datadir, 0755) != 0)
    {
	perror("Failed to mkdir");
    }
    return datadir;
}

// ---- 最小JSONヘルパー (固定スキーマ専用) ----

// obj[0..obj_len) の中で "key":"..." を探してvalueを返す
static int json_get_str(const char *obj, const char *key, char *out, int maxout) {
    char pat[64];
    snprintf(pat, sizeof(pat), "\"%s\":\"", key);
    const char *p = strstr(obj, pat);
    if (!p) return 0;
    p += strlen(pat);
    int i = 0;
    while (i < maxout - 1 && *p && *p != '"') out[i++] = *p++;
    out[i] = '\0';
    return 1;
}

// obj[0..obj_len) の中で "key":number を探してvalueを返す
static int json_get_int(const char *obj, const char *key, int *out) {
    char pat[64];
    snprintf(pat, sizeof(pat), "\"%s\":", key);
    const char *p = strstr(obj, pat);
    if (!p) return 0;
    p += strlen(pat);
    while (*p == ' ') p++;
    *out = atoi(p);
    return 1;
}

// パスを構築する共通処理
static void build_hiscore_path(char *buf, int bufsz) {
    const char *dir = get_datadir();
    snprintf(buf, bufsz, "%s/%s", dir ? dir : ".", HISCORE_FILE);
}

static void hiscore_save(void) {
    static const char mode_ch[3] = {'E', 'N', 'H'};
    char path[PATH_MAX];
    build_hiscore_path(path, sizeof(path));
    FILE *f = fopen(path, "w");
    if (!f) return;

    fprintf(f, "{\n  \"scores\": [");
    int first = 1;
    for (int m = 0; m < 3; m++) {
        for (int i = 0; i < g_hiscore_count[m]; i++) {
            fprintf(f, "%s\n    {"
                       "\"mode\": \"%c\", "
                       "\"initials\": \"%.3s\", "
                       "\"score\": %d, "
                       "\"stage\": %d"
                       "}",
                    first ? "" : ",",
                    mode_ch[m],
                    g_hiscores[m][i].initials,
                    g_hiscores[m][i].score,
                    g_hiscores[m][i].stage);
            first = 0;
        }
    }
    fprintf(f, "\n  ]\n}\n");
    fclose(f);
}

static void hiscore_load(void) {
    char path[PATH_MAX];
    build_hiscore_path(path, sizeof(path));
    FILE *f = fopen(path, "r");
    if (!f) return;

    // ファイル全体をバッファに読み込む
    fseek(f, 0, SEEK_END);
    long fsz = ftell(f);
    fseek(f, 0, SEEK_SET);
    if (fsz <= 0 || fsz > 65536) { fclose(f); return; }
    char *buf = malloc((size_t)fsz + 1);
    if (!buf) { fclose(f); return; }
    size_t nread = fread(buf, 1, (size_t)fsz, f);
    buf[nread] = '\0';
    buf[fsz] = '\0';
    fclose(f);

    // "scores" 配列の [ ... ] 内だけを対象にする
    const char *arr_start = strstr(buf, "\"scores\"");
    if (arr_start) arr_start = strchr(arr_start, '[');
    if (!arr_start) { free(buf); return; }
    const char *arr_end = strchr(arr_start, ']');
    if (!arr_end) arr_end = buf + fsz;

    // 配列内の各オブジェクト { ... } を走査
    const char *p = arr_start;
    while (p < arr_end && (p = strchr(p, '{')) != NULL && p < arr_end) {
        const char *obj_end = strchr(p, '}');
        if (!obj_end || obj_end > arr_end) break;

        // オブジェクトをコピーしてフィールド抽出
        int obj_len = (int)(obj_end - p + 1);
        char obj[256];
        if (obj_len >= (int)sizeof(obj)) { p = obj_end + 1; continue; }
        memcpy(obj, p, (size_t)obj_len);
        obj[obj_len] = '\0';

        char mode_s[4] = "", init[8] = "";
        int  score = 0, stage = 0;
        if (json_get_str(obj, "mode",     mode_s, sizeof(mode_s)) &&
            json_get_str(obj, "initials", init,   sizeof(init))   &&
            json_get_int(obj, "score",    &score)                  &&
            json_get_int(obj, "stage",    &stage)) {
            int m = (mode_s[0] == 'E') ? 0
                  : (mode_s[0] == 'N') ? 1
                  : (mode_s[0] == 'H') ? 2 : -1;
            if (m >= 0 && g_hiscore_count[m] < HISCORE_COUNT) {
                init[3] = '\0';
                hiscore_add(init, score, stage, m);
            }
        }
        p = obj_end + 1;
    }
    free(buf);
}

typedef struct {
    Vec3  pos;
    Vec3  vel;
    Vec3  fwd;
    Vec3  up;
    float fuel;
    int   score;
    Ring  ring;
    Vec3  prev_pos;
    GameStateEnum state;
    float explode_timer;
    float ring_timer;
    int   rings_done;
    int   stage;
    int   stage_fuel_bonus;
    float stage_clear_timer;
    float title_timer;      // タイトル↔ランキング切替タイマー
    float countdown_val;    // カウントダウン値 (COUNTDOWN_START→-1)
    char     entry_ch[3];   // イニシャル入力中の文字
    int      entry_cur;     // カーソル位置 0-2
    GameMode mode;          // 選択されたゲームモード
    int      mode_sel;      // モード選択カーソル
    ShipType ship;          // 選択された機体
    int      ship_sel;      // 機体選択カーソル
    int      ranking_mode_idx;  // ランキング表示モードのインデックス (0-2)
    int      collision_warning; // 衝突予測警告フラグ
} GameState;

// ==================== オーディオ ====================
static SDL_AudioStream *g_thruster_stream = NULL;
static float            g_noise_lpf       = 0.0f;

static SDL_AudioStream *g_warning_stream  = NULL;
static float            g_warn_sine_phase = 0.0f; // サイン波の位相
static float            g_warn_mod_phase  = 0.0f; // AM変調の位相

static void audio_init(void) {
    if (!SDL_InitSubSystem(SDL_INIT_AUDIO)) return;
    SDL_AudioSpec spec = { SDL_AUDIO_F32, 1, 22050 };
    g_thruster_stream = SDL_OpenAudioDeviceStream(
        SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, NULL, NULL);
    if (g_thruster_stream) SDL_ResumeAudioStreamDevice(g_thruster_stream);
    g_warning_stream = SDL_OpenAudioDeviceStream(
        SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, NULL, NULL);
    if (g_warning_stream) SDL_ResumeAudioStreamDevice(g_warning_stream);
}

static void audio_thruster(int on) {
    if (!g_thruster_stream) return;
    if (!on) {
        SDL_ClearAudioStream(g_thruster_stream);
        g_noise_lpf = 0.0f;
        return;
    }
    // バッファが80ms未満なら補充する
    const int SAMPLE_RATE  = 22050;
    const int TARGET_BYTES = (int)(SAMPLE_RATE * 0.08f) * (int)sizeof(float);
    int queued = SDL_GetAudioStreamQueued(g_thruster_stream);
    if (queued >= TARGET_BYTES) return;
    int n = (TARGET_BYTES - queued) / (int)sizeof(float);

    float buf[1024];
    while (n > 0) {
        int batch = n < 1024 ? n : 1024;
        for (int i = 0; i < batch; i++) {
            // ホワイトノイズ → 一極LPF (α=0.65, fc≈5kHz@22050Hz) → シュー音
            float r = ((float)rand() / (float)RAND_MAX) * 2.0f - 1.0f;
            g_noise_lpf = 0.65f * r + 0.35f * g_noise_lpf;
            buf[i] = g_noise_lpf * 0.45f;
        }
        SDL_PutAudioStreamData(g_thruster_stream, buf, batch * (int)sizeof(float));
        n -= batch;
    }
}

// 880Hz サイン波を 4Hz でパルス変調した警告音
static void audio_warning_tone(int on) {
    if (!g_warning_stream) return;
    if (!on) {
        SDL_ClearAudioStream(g_warning_stream);
        g_warn_sine_phase = 0.0f;
        g_warn_mod_phase  = 0.0f;
        return;
    }
    const int   SR         = 22050;
    const float FREQ       = 880.0f;
    const float MOD_FREQ   = 4.0f;   // 4回/秒でパルス
    const int   TARGET     = (int)(SR * 0.08f) * (int)sizeof(float);
    int queued = SDL_GetAudioStreamQueued(g_warning_stream);
    if (queued >= TARGET) return;
    int n = (TARGET - queued) / (int)sizeof(float);
    float buf[1024];
    while (n > 0) {
        int batch = n < 1024 ? n : 1024;
        for (int i = 0; i < batch; i++) {
            float s   = sinf(g_warn_sine_phase * 2.0f * (float)M_PI);
            g_warn_sine_phase += FREQ / SR;
            if (g_warn_sine_phase >= 1.0f) g_warn_sine_phase -= 1.0f;
            float mod = (g_warn_mod_phase < 0.5f) ? 1.0f : 0.0f;
            g_warn_mod_phase += MOD_FREQ / SR;
            if (g_warn_mod_phase >= 1.0f) g_warn_mod_phase -= 1.0f;
            buf[i] = s * mod * 0.35f;
        }
        SDL_PutAudioStreamData(g_warning_stream, buf, batch * (int)sizeof(float));
        n -= batch;
    }
}

static void audio_cleanup(void) {
    if (g_thruster_stream) {
        SDL_DestroyAudioStream(g_thruster_stream);
        g_thruster_stream = NULL;
    }
    if (g_warning_stream) {
        SDL_DestroyAudioStream(g_warning_stream);
        g_warning_stream = NULL;
    }
}

// ==================== ゲーム状態 ====================
static void gs_reorthogonalize(GameState *gs) {
    gs->fwd = vnorm(gs->fwd);
    Vec3 right = vnorm(vcross(gs->fwd, gs->up));
    gs->up = vnorm(vcross(right, gs->fwd));
}

// ring_num: ステージ内の何個目か (1-5)
// stage: 現在のステージ番号
static void spawn_ring(Ring *ring, int ring_num, int stage) {
    // ランダム位置
    ring->pos = v3(
        (float)(rand() % (int)SPACE_SIZE),
        (float)(rand() % (int)SPACE_SIZE),
        (float)(rand() % (int)SPACE_SIZE)
    );
    // ランダム向き
    float theta = ((float)rand() / RAND_MAX) * 2.0f * (float)M_PI;
    float phi   = acosf(2.0f * ((float)rand() / RAND_MAX) - 1.0f);
    ring->normal = vnorm(v3(sinf(phi)*cosf(theta), sinf(phi)*sinf(theta), cosf(phi)));

    // ring->up を法線と直交するように設定
    Vec3 arb = (fabsf(ring->normal.y) < 0.9f) ? v3(0,1,0) : v3(1,0,0);
    ring->up = vnorm(vcross(vnorm(vcross(ring->normal, arb)), ring->normal));

    // 回転: ring_num >= 7-stage かつ stage >= 2
    // (ring5: st2〜, ring4: st3〜, ring3: st4〜, ring2: st5〜, ring1: st6〜)
    int should_rotate = (stage >= 2) && (ring_num >= 7 - stage);
    ring->rot_speed = should_rotate ? ((float)M_PI / 30.0f) : 0.0f;
    // 回転軸: リング平面内のright方向 (スポーン時に固定)
    ring->rot_axis = should_rotate
        ? vnorm(vcross(ring->normal, ring->up))
        : v3(0.0f, 1.0f, 0.0f);

    // 移動: ring_num >= 12-stage かつ stage >= 7
    // (ring5: st7〜, ring4: st8〜, ring3: st9〜, ...)
    int should_move = (stage >= 7) && (ring_num >= 12 - stage);
    ring->move_speed = should_move ? 50.0f : 0.0f;
    if (should_move) {
        float mt = ((float)rand() / RAND_MAX) * 2.0f * (float)M_PI;
        float mp = acosf(2.0f * ((float)rand() / RAND_MAX) - 1.0f);
        ring->move_dir = vnorm(v3(sinf(mp)*cosf(mt), sinf(mp)*sinf(mt), cosf(mp)));
    } else {
        ring->move_dir = v3(0.0f, 0.0f, 0.0f);
    }

    // 色: 移動=マゼンタ > 回転=シアン > 通常=金
    ring->color_type = should_move ? 2 : (should_rotate ? 1 : 0);
}

// リング通過判定
static int check_ring_pass(GameState *gs) {
    Vec3 d_curr = torus_delta(gs->pos,      gs->ring.pos);
    Vec3 d_prev = torus_delta(gs->prev_pos, gs->ring.pos);

    // リング平面をまたいだか (法線方向の符号が変わったか)
    float side_curr = vdot(d_curr, gs->ring.normal);
    float side_prev = vdot(d_prev, gs->ring.normal);
    if ((side_curr >= 0.0f) == (side_prev >= 0.0f)) return 0;

    // 補間して通過点を求める
    float t = side_prev / (side_prev - side_curr);
    Vec3 cross_pt = vadd(gs->prev_pos, vscale(vsub(gs->pos, gs->prev_pos), t));

    // リング中心からの平面内距離
    Vec3 to_ring = torus_delta(cross_pt, gs->ring.pos);
    float along  = vdot(to_ring, gs->ring.normal);
    Vec3  in_plane = vsub(to_ring, vscale(gs->ring.normal, along));
    return vlen(in_plane) <= RING_RADIUS;
}

// リング枠への衝突判定
// リングはトーラス形状: メジャー半径 RING_RADIUS、チューブ半径 RING_TUBE_RADIUS
// 船の半径 SHIP_RADIUS を合算して判定する
static int check_ring_hit(GameState *gs) {
    Vec3 raw = vsub(gs->ring.pos, gs->pos);
    float hit_r = RING_TUBE_RADIUS + SHIP_RADIUS;

    for (int dx = -1; dx <= 1; dx++) {
        for (int dy = -1; dy <= 1; dy++) {
            for (int dz = -1; dz <= 1; dz++) {
                // d = 船→リング中心 (torus複製)
                Vec3 d = v3(raw.x + dx*SPACE_SIZE,
                            raw.y + dy*SPACE_SIZE,
                            raw.z + dz*SPACE_SIZE);
                if (vlen(d) > SPACE_SIZE * 0.6f) continue;

                // 船をリング中心基準に変換: s = -d (リング中心→船)
                Vec3 s = vscale(d, -1.0f);

                // リング平面への法線成分と平面内成分に分解
                float along_n  = vdot(s, gs->ring.normal);
                Vec3  in_plane = vsub(s, vscale(gs->ring.normal, along_n));
                float r        = vlen(in_plane); // リング軸からの距離

                // トーラス面までの距離
                float dist = sqrtf((r - RING_RADIUS)*(r - RING_RADIUS)
                                   + along_n*along_n);
                if (dist < hit_r) return 1;
            }
        }
    }
    return 0;
}

// 衝突予測: 現在の速度・進路で WARN_HORIZON 秒以内にリングに衝突するか
#define WARN_HORIZON 5.0f
#define WARN_DT_STEP 0.05f  // サンプリング間隔 (5s / 0.05 = 100ステップ)

static int predict_collision(const GameState *gs) {
    float hit_r = RING_TUBE_RADIUS + SHIP_RADIUS;
    for (float t = WARN_DT_STEP; t <= WARN_HORIZON; t += WARN_DT_STEP) {
        // 船の予測位置
        Vec3 spos = vwrap(vadd(gs->pos, vscale(gs->vel, t)));
        // リングの予測位置・法線
        Vec3 rpos  = gs->ring.pos;
        Vec3 rnorm = gs->ring.normal;
        if (gs->ring.move_speed > 0.0f)
            rpos = vwrap(vadd(rpos, vscale(gs->ring.move_dir, gs->ring.move_speed * t)));
        if (gs->ring.rot_speed != 0.0f)
            rnorm = vnorm(vrotate(rnorm, gs->ring.rot_axis, gs->ring.rot_speed * t));
        // トーラス衝突判定 (27コピー)
        Vec3 raw = vsub(rpos, spos);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++) {
            Vec3 d = v3(raw.x+dx*SPACE_SIZE, raw.y+dy*SPACE_SIZE, raw.z+dz*SPACE_SIZE);
            if (vlen(d) > SPACE_SIZE * 0.6f) continue;
            Vec3  s      = vscale(d, -1.0f);
            float an     = vdot(s, rnorm);
            float r      = vlen(vsub(s, vscale(rnorm, an)));
            float dist   = sqrtf((r-RING_RADIUS)*(r-RING_RADIUS) + an*an);
            if (dist < hit_r) return 1;
        }
    }
    return 0;
}

// ==================== 描画ユーティリティ ====================
static Vec3 g_stars[STAR_COUNT];

static void init_stars(void) {
    for (int i = 0; i < STAR_COUNT; i++) {
        g_stars[i] = v3(
            (float)(rand() % (int)SPACE_SIZE),
            (float)(rand() % (int)SPACE_SIZE),
            (float)(rand() % (int)SPACE_SIZE)
        );
    }
}

static void render_stars(Vec3 ship_pos) {
    glPointSize(2.0f);
    glColor3f(1.0f, 1.0f, 1.0f);
    glBegin(GL_POINTS);
    for (int i = 0; i < STAR_COUNT; i++) {
        Vec3 r = torus_delta(ship_pos, g_stars[i]);
        glVertex3f(r.x, r.y, r.z);
    }
    glEnd();
    glPointSize(1.0f);
}

// 指定した相対位置にリングを1個描画
static void render_ring_at(Ring *ring, Vec3 rel) {
    Vec3 norm = ring->normal;
    Vec3 rr   = vnorm(vcross(norm, ring->up));
    Vec3 up   = ring->up;

    switch (ring->color_type) {
        case 1:  glColor3f(0.0f, 1.0f, 1.0f); break; // シアン (回転)
        case 2:  glColor3f(1.0f, 0.0f, 1.0f); break; // マゼンタ (移動)
        default: glColor3f(1.0f, 0.55f, 0.0f); break; // 金 (通常)
    }
    for (int i = 0; i < RING_SEGMENTS; i++) {
        float a0 = (float)i       / RING_SEGMENTS * 2.0f * (float)M_PI;
        float a1 = (float)(i + 1) / RING_SEGMENTS * 2.0f * (float)M_PI;

        Vec3 c0 = vadd(rel, vadd(vscale(rr, cosf(a0)*RING_RADIUS), vscale(up, sinf(a0)*RING_RADIUS)));
        Vec3 c1 = vadd(rel, vadd(vscale(rr, cosf(a1)*RING_RADIUS), vscale(up, sinf(a1)*RING_RADIUS)));

        glBegin(GL_TRIANGLE_STRIP);
        for (int j = 0; j <= TUBE_SEGMENTS; j++) {
            float b = (float)j / TUBE_SEGMENTS * 2.0f * (float)M_PI;
            Vec3 tn0 = vnorm(vadd(vscale(vnorm(vsub(c0, rel)), cosf(b)), vscale(norm, sinf(b))));
            Vec3 tn1 = vnorm(vadd(vscale(vnorm(vsub(c1, rel)), cosf(b)), vscale(norm, sinf(b))));
            Vec3 v0  = vadd(c0, vscale(tn0, RING_TUBE_RADIUS));
            Vec3 v1  = vadd(c1, vscale(tn1, RING_TUBE_RADIUS));
            glNormal3f(tn0.x, tn0.y, tn0.z); glVertex3f(v0.x, v0.y, v0.z);
            glNormal3f(tn1.x, tn1.y, tn1.z); glVertex3f(v1.x, v1.y, v1.z);
        }
        glEnd();
    }
}

// トーラス空間でリングを描画する
// 問題: torus_delta は「最短経路」しか返さないので、船がリングと512px離れた
//       軸で「最短経路」が反転した瞬間にリングが突然後方へ飛び、消えて見える。
// 解決: リングを27通りのトーラス複製位置すべてで試行し、一定距離内の複製を
//       描画する。OpenGL がフラスタム外の複製を自動カリングするのでコストは低い。
//       閾値 SPACE_SIZE*0.6 ≈ 614px — これにより「最近傍」の複製だけが通常
//       描画され、境界付近では前後2つが同時に存在しても視野内に入るのは1つ。
static void render_ring(Ring *ring, Vec3 ship_pos) {
    Vec3 raw = vsub(ring->pos, ship_pos);  // ラップなしの生の差分
    float draw_dist = SPACE_SIZE * 0.6f;

    for (int dx = -1; dx <= 1; dx++) {
        for (int dy = -1; dy <= 1; dy++) {
            for (int dz = -1; dz <= 1; dz++) {
                Vec3 rel = v3(raw.x + dx*SPACE_SIZE,
                              raw.y + dy*SPACE_SIZE,
                              raw.z + dz*SPACE_SIZE);
                if (vlen(rel) > draw_dist) continue;
                render_ring_at(ring, rel);
            }
        }
    }
}

// コックピット枠 (上隅 + パネル境界隅)
static void render_cockpit(int w, int h, float panel_y) {
    float fw = (float)w;
    float mg = 16.0f, arm = 55.0f;
    float bot = panel_y - 4.0f;

    glLineWidth(2.0f);
    glColor3f(0.25f, 0.38f, 0.50f);
    glBegin(GL_LINES);
    // 左上
    glVertex2f(mg, mg);         glVertex2f(mg + arm, mg);
    glVertex2f(mg, mg);         glVertex2f(mg, mg + arm);
    // 右上
    glVertex2f(fw-mg, mg);      glVertex2f(fw-mg-arm, mg);
    glVertex2f(fw-mg, mg);      glVertex2f(fw-mg, mg+arm);
    // 左下 (パネル上端)
    glVertex2f(mg, bot);        glVertex2f(mg + arm, bot);
    glVertex2f(mg, bot);        glVertex2f(mg, bot - arm);
    // 右下
    glVertex2f(fw-mg, bot);     glVertex2f(fw-mg-arm, bot);
    glVertex2f(fw-mg, bot);     glVertex2f(fw-mg, bot-arm);
    glEnd();
    glLineWidth(1.0f);
}

// 照準
static void render_crosshair(int w, int h) {
    float cx = w * 0.5f, cy = h * 0.5f;
    glLineWidth(1.5f);
    glColor3f(0.2f, 1.0f, 0.3f);
    glBegin(GL_LINES);
    glVertex2f(cx - 14, cy); glVertex2f(cx -  4, cy);
    glVertex2f(cx +  4, cy); glVertex2f(cx + 14, cy);
    glVertex2f(cx, cy - 14); glVertex2f(cx, cy -  4);
    glVertex2f(cx, cy +  4); glVertex2f(cx, cy + 14);
    glEnd();
    glLineWidth(1.0f);
}

// 7セグメントフォント
// ビット: 0=top 1=tr 2=br 3=bot 4=bl 5=tl 6=mid
static const unsigned char SEG7[10] = {
    0x3F,0x06,0x5B,0x4F,0x66,0x6D,0x7D,0x07,0x7F,0x6F
};
// A-Z (近似。表現できない文字はそれらしい形に)
static const unsigned char SEG7_ALPHA[26] = {
    0x77, // A
    0x7C, // B (lowercase b)
    0x39, // C
    0x5E, // D (lowercase d)
    0x79, // E
    0x71, // F
    0x7D, // G
    0x76, // H
    0x06, // I (= 1)
    0x1E, // J
    0x72, // K (approx)
    0x38, // L
    0x37, // M (approx: top+tl+tr+bl+br)
    0x74, // N (lowercase n)
    0x3F, // O (= 0)
    0x73, // P
    0x67, // Q
    0x50, // R (lowercase r)
    0x6D, // S (= 5)
    0x78, // T (lowercase t)
    0x3E, // U
    0x1C, // V
    0x3E, // W (= U, approx)
    0x76, // X (= H)
    0x6E, // Y
    0x5B, // Z (= 2)
};

static void draw_seg7_char(float x, float y, float w, float h, char c) {
    if (c == ' ') return;
    if (c == '-') {
        glBegin(GL_LINES);
        glVertex2f(x+2, y+h*0.5f); glVertex2f(x+w-2, y+h*0.5f);
        glEnd();
        return;
    }
    if (c == '.') {
        glBegin(GL_POINTS);
        glVertex2f(x+w*0.5f, y+h-2);
        glEnd();
        return;
    }
    unsigned char s;
    if (c >= '0' && c <= '9') s = SEG7[c - '0'];
    else if (c >= 'A' && c <= 'Z') s = SEG7_ALPHA[c - 'A'];
    else if (c >= 'a' && c <= 'z') s = SEG7_ALPHA[c - 'a'];
    else if (c == '-') s = 0x40; // middle segment only
    else return;         // ' ' など: 何も描画しない
    float m = 2.0f; // margin
    // top
    if (s & (1<<0)) { glBegin(GL_LINES); glVertex2f(x+m, y+m); glVertex2f(x+w-m, y+m); glEnd(); }
    // top-right
    if (s & (1<<1)) { glBegin(GL_LINES); glVertex2f(x+w-m, y+m); glVertex2f(x+w-m, y+h*0.5f); glEnd(); }
    // bot-right
    if (s & (1<<2)) { glBegin(GL_LINES); glVertex2f(x+w-m, y+h*0.5f); glVertex2f(x+w-m, y+h-m); glEnd(); }
    // bot
    if (s & (1<<3)) { glBegin(GL_LINES); glVertex2f(x+m, y+h-m); glVertex2f(x+w-m, y+h-m); glEnd(); }
    // bot-left
    if (s & (1<<4)) { glBegin(GL_LINES); glVertex2f(x+m, y+h*0.5f); glVertex2f(x+m, y+h-m); glEnd(); }
    // top-left
    if (s & (1<<5)) { glBegin(GL_LINES); glVertex2f(x+m, y+m); glVertex2f(x+m, y+h*0.5f); glEnd(); }
    // mid
    if (s & (1<<6)) { glBegin(GL_LINES); glVertex2f(x+m, y+h*0.5f); glVertex2f(x+w-m, y+h*0.5f); glEnd(); }
}

// 文字列を描画 (各文字 cw x ch)
static void draw_string(float x, float y, float cw, float ch, const char *s) {
    glLineWidth(1.5f);
    glPointSize(3.0f);
    for (; *s; s++, x += cw + 1) {
        draw_seg7_char(x, y, cw, ch, *s);
    }
    glLineWidth(1.0f);
    glPointSize(1.0f);
}

// float を符号付き整数表示 (n桁)
static void draw_float_int(float x, float y, float cw, float ch, float val, int digits) {
    char buf[32];
    snprintf(buf, sizeof(buf), "%+*.0f", digits, val);
    draw_string(x, y, cw, ch, buf);
}

// ==================== HUD ====================
// レイアウト (640x400ウィンドウ基準):
//   [宇宙空間ビュー]  y=0..329
//   [パネル]          y=330..399  (70px)
//     左ブロック  x=6   : VX/VY/VZ 速度、SPD
//     中央ブロック x=240 : スコア / 燃料バー
//     右ブロック  x=530 : リング方向インジケータ (円)
static void render_hud(GameState *gs, int w, int h) {
    glMatrixMode(GL_PROJECTION);
    glPushMatrix();
    glLoadIdentity();
    glOrtho(0, w, h, 0, -1, 1);
    glMatrixMode(GL_MODELVIEW);
    glPushMatrix();
    glLoadIdentity();
    glDisable(GL_DEPTH_TEST);

    float fw = (float)w, fh = (float)h;
    float panel_y = fh - 70.0f;  // 70px パネル

    // ---- パネル背景 ----
    glColor4f(0.04f, 0.04f, 0.10f, 0.88f);
    glBegin(GL_QUADS);
    glVertex2f(0, panel_y); glVertex2f(fw, panel_y);
    glVertex2f(fw, fh);     glVertex2f(0, fh);
    glEnd();

    // パネル上端ライン (2色グラデーション風)
    glLineWidth(1.0f);
    glColor3f(0.15f, 0.30f, 0.55f);
    glBegin(GL_LINES);
    glVertex2f(0, panel_y);   glVertex2f(fw, panel_y);
    glEnd();
    glColor3f(0.08f, 0.18f, 0.35f);
    glBegin(GL_LINES);
    glVertex2f(0, panel_y+1); glVertex2f(fw, panel_y+1);
    glEnd();

    // パネル内の縦区切り線
    glColor3f(0.10f, 0.18f, 0.30f);
    glBegin(GL_LINES);
    glVertex2f(232, panel_y+4); glVertex2f(232, fh-4);
    glVertex2f(522, panel_y+4); glVertex2f(522, fh-4);
    glEnd();

    // ---- 照準・コックピット枠 ----
    render_crosshair(w, h);
    render_cockpit(w, h, panel_y);

    // ---- 文字スケール ----
    float cw = 7.0f, ch = 11.0f;  // 1文字セル

    // ======== 左ブロック: 速度計 (船体ローカル座標) ========
    // FWD = 推進軸方向の速度成分 (正=前進)
    // RGT = 右方向の速度成分
    // UP  = 上方向の速度成分
    Vec3 rgt3d = vnorm(vcross(gs->fwd, gs->up));
    float v_fwd = vdot(gs->vel, gs->fwd);
    float v_rgt = vdot(gs->vel, rgt3d);
    float v_up  = vdot(gs->vel, gs->up);
    float speed = vlen(gs->vel);

    float lx = 6.0f, ly = panel_y + 6.0f;
    float col2 = 116.0f;
    float row2 = ly + 28.0f;

    // ラベル: FWD を推進方向に合わせて色分け
    //   FWD > 0 (前進中) → 緑、FWD < 0 (後退中) → 橙
    glColor3f((v_fwd >= 0) ? 0.25f : 1.0f,
              (v_fwd >= 0) ? 1.00f : 0.55f,
              (v_fwd >= 0) ? 0.45f : 0.10f);
    draw_string(lx, ly, cw, ch, "FWD");

    glColor3f(0.35f, 0.70f, 1.0f);
    draw_string(col2, ly,   cw, ch, "RGT");
    draw_string(lx,   row2, cw, ch, "UP");
    draw_string(col2, row2, cw, ch, "SPD");

    // 値
    glColor3f(0.0f, 1.0f, 0.65f);
    draw_float_int(lx   + 28, ly,   cw, ch, v_fwd, 5);
    draw_float_int(col2 + 28, ly,   cw, ch, v_rgt, 5);
    draw_float_int(lx   + 20, row2, cw, ch, v_up,  5);

    glColor3f(1.0f, 0.85f, 0.1f);
    draw_float_int(col2 + 28, row2, cw, ch, speed, 4);

    // ======== 中央ブロック: ステージ / タイマー / 燃料 / スコア ========
    float mx = 238.0f;
    float bar_w = 162.0f, bar_h = 12.0f;

    // ---- 行1: STAGE N   N/5   SCORE NNNNN ----
    char st_buf[32];
    snprintf(st_buf, sizeof(st_buf), "ST%d", gs->stage);
    glColor3f(0.5f, 0.7f, 1.0f);
    draw_string(mx, panel_y + 4, cw, ch, st_buf);

    char rng_buf[8];
    snprintf(rng_buf, sizeof(rng_buf), "%d/5", gs->rings_done);
    glColor3f(0.7f, 1.0f, 0.5f);
    draw_string(mx + 32, panel_y + 4, cw, ch, rng_buf);

    glColor3f(0.45f, 0.45f, 0.65f);
    draw_string(mx + 70, panel_y + 4, cw, ch, "SCORE");
    char snum[16]; snprintf(snum, sizeof(snum), "%d", gs->score);
    glColor3f(1.0f, 0.88f, 0.15f);
    draw_string(mx + 116, panel_y + 4, cw, ch, snum);

    // ---- 行2: タイマーバー ----
    float ty = panel_y + 20.0f;
    float tfrac = gs->ring_timer / RING_TIME_LIMIT;
    if (tfrac < 0) tfrac = 0;
    if (tfrac > 1) tfrac = 1;
    int   tsec   = (int)gs->ring_timer + 1;
    int   urgent = gs->ring_timer < 5.0f;

    glColor3f(0.15f, 0.15f, 0.18f);
    glBegin(GL_QUADS);
    glVertex2f(mx, ty); glVertex2f(mx+bar_w, ty);
    glVertex2f(mx+bar_w, ty+bar_h); glVertex2f(mx, ty+bar_h);
    glEnd();

    float tr = urgent ? 1.0f : (tfrac < 0.4f ? 1.0f : 0.1f);
    float tg = urgent ? 0.1f : (tfrac < 0.4f ? 0.6f : 0.9f);
    glColor3f(tr, tg, 0.05f);
    glBegin(GL_QUADS);
    glVertex2f(mx, ty); glVertex2f(mx+bar_w*tfrac, ty);
    glVertex2f(mx+bar_w*tfrac, ty+bar_h); glVertex2f(mx, ty+bar_h);
    glEnd();

    glColor3f(0.25f, 0.35f, 0.6f);
    glBegin(GL_LINE_LOOP);
    glVertex2f(mx, ty); glVertex2f(mx+bar_w, ty);
    glVertex2f(mx+bar_w, ty+bar_h); glVertex2f(mx, ty+bar_h);
    glEnd();

    // タイマー秒数 (残り5秒以下で赤)
    char tbuf[16]; snprintf(tbuf, sizeof(tbuf), "%d", tsec);
    glColor3f(urgent ? 1.0f : 0.6f, urgent ? 0.2f : 0.8f, 0.2f);
    draw_string(mx + bar_w + 4, ty, cw, ch, tbuf);

    // ---- 行3: 燃料バー ----
    float fy2 = panel_y + 38.0f;
    float frac = gs->fuel / INITIAL_FUEL;
    if (frac < 0) frac = 0;

    glColor3f(0.15f, 0.15f, 0.18f);
    glBegin(GL_QUADS);
    glVertex2f(mx, fy2); glVertex2f(mx+bar_w, fy2);
    glVertex2f(mx+bar_w, fy2+bar_h); glVertex2f(mx, fy2+bar_h);
    glEnd();

    float cr = (frac < 0.3f) ? 1.0f : 0.15f;
    float cg = (frac > 0.3f) ? 0.75f : 0.25f;
    glColor3f(cr, cg, 0.1f);
    glBegin(GL_QUADS);
    glVertex2f(mx, fy2); glVertex2f(mx+bar_w*frac, fy2);
    glVertex2f(mx+bar_w*frac, fy2+bar_h); glVertex2f(mx, fy2+bar_h);
    glEnd();

    glColor3f(0.25f, 0.35f, 0.6f);
    glBegin(GL_LINE_LOOP);
    glVertex2f(mx, fy2); glVertex2f(mx+bar_w, fy2);
    glVertex2f(mx+bar_w, fy2+bar_h); glVertex2f(mx, fy2+bar_h);
    glEnd();

    char fbuf[16]; snprintf(fbuf, sizeof(fbuf), "%.0f", gs->fuel);
    glColor3f(0.55f, 0.55f, 0.80f);
    draw_string(mx + bar_w + 4, fy2, cw, ch, fbuf);

    // ---- 行ラベル ----
    glColor3f(0.38f, 0.38f, 0.55f);
    draw_string(mx - 28, ty,  cw * 0.85f, ch * 0.85f, "TM");
    draw_string(mx - 28, fy2, cw * 0.85f, ch * 0.85f, "FL");

    // ---- モード + 機体表示 ----
    {
        static const char *mlabels[3] = {"EASY", "NRM", "HARD"};
        static const char *slabels[3] = {"STD", "AGILE", "BOOST"};
        float mr = (gs->mode == MODE_EASY) ? 0.3f : (gs->mode == MODE_NORMAL) ? 0.4f : 1.0f;
        float mg = (gs->mode == MODE_EASY) ? 1.0f : (gs->mode == MODE_NORMAL) ? 0.8f : 0.35f;
        glColor3f(mr, mg, 0.15f);
        draw_string(mx - 28, fy2 + 16, cw * 0.75f, ch * 0.75f, mlabels[gs->mode]);

        float sr = (gs->ship == SHIP_STANDARD) ? 0.8f : (gs->ship == SHIP_AGILE) ? 0.3f : 1.0f;
        float sg = (gs->ship == SHIP_STANDARD) ? 0.8f : (gs->ship == SHIP_AGILE) ? 0.9f : 0.6f;
        float sb = (gs->ship == SHIP_STANDARD) ? 0.8f : (gs->ship == SHIP_AGILE) ? 1.0f : 0.2f;
        glColor3f(sr, sg, sb);
        draw_string(mx + 14, fy2 + 16, cw * 0.75f, ch * 0.75f, slabels[gs->ship]);
    }

    // ======== 右ブロック: リング方向インジケータ ========
    Vec3 to_ring = torus_delta(gs->pos, gs->ring.pos);
    float dist_ring = vlen(to_ring);
    Vec3 right3d = vnorm(vcross(gs->fwd, gs->up));

    float local_x = vdot(to_ring, right3d);
    float local_y = vdot(to_ring, gs->up);
    float local_z = vdot(to_ring, gs->fwd);

    float ind_cx = fw - 42.0f;
    float ind_cy = panel_y + 35.0f;
    float ind_r  = 28.0f;

    // 背景円
    glColor4f(0.04f, 0.04f, 0.16f, 0.95f);
    glBegin(GL_TRIANGLE_FAN);
    glVertex2f(ind_cx, ind_cy);
    for (int i = 0; i <= 32; i++) {
        float a = (float)i / 32.0f * 2.0f * (float)M_PI;
        glVertex2f(ind_cx + cosf(a)*ind_r, ind_cy + sinf(a)*ind_r);
    }
    glEnd();

    glColor3f(0.18f, 0.35f, 0.70f);
    glBegin(GL_LINE_LOOP);
    for (int i = 0; i < 32; i++) {
        float a = (float)i / 32.0f * 2.0f * (float)M_PI;
        glVertex2f(ind_cx + cosf(a)*ind_r, ind_cy + sinf(a)*ind_r);
    }
    glEnd();

    glColor3f(0.12f, 0.16f, 0.28f);
    glBegin(GL_LINES);
    glVertex2f(ind_cx - ind_r, ind_cy); glVertex2f(ind_cx + ind_r, ind_cy);
    glVertex2f(ind_cx, ind_cy - ind_r); glVertex2f(ind_cx, ind_cy + ind_r);
    glEnd();

    if (dist_ring > 0.1f) {
        float nx = local_x / dist_ring;
        float ny = local_y / dist_ring;
        float dot_x = ind_cx + nx * (ind_r - 5.0f);
        float dot_y = ind_cy - ny * (ind_r - 5.0f);
        // クランプ
        float ddx = dot_x - ind_cx, ddy = dot_y - ind_cy;
        float ddl = sqrtf(ddx*ddx + ddy*ddy);
        if (ddl > ind_r - 5.0f) {
            dot_x = ind_cx + ddx/ddl * (ind_r - 5.0f);
            dot_y = ind_cy + ddy/ddl * (ind_r - 5.0f);
        }

        glColor3f((local_z < 0) ? 1.0f : 0.0f,
                  (local_z < 0) ? 0.4f : 1.0f,
                  0.2f);
        glBegin(GL_TRIANGLE_FAN);
        glVertex2f(dot_x, dot_y);
        for (int i = 0; i <= 16; i++) {
            float a = (float)i / 16.0f * 2.0f * (float)M_PI;
            glVertex2f(dot_x + cosf(a)*4, dot_y + sinf(a)*4);
        }
        glEnd();
    }

    // "NEXT" ラベルと距離
    glColor3f(0.45f, 0.45f, 0.70f);
    draw_string(ind_cx - ind_r - 58, panel_y + 6, cw, ch, "NEXT");
    char dbuf[32]; snprintf(dbuf, sizeof(dbuf), "%.0f", dist_ring);
    glColor3f(0.70f, 0.70f, 1.0f);
    draw_string(ind_cx - ind_r - 58, panel_y + 22, cw, ch, dbuf);

    // ---- 燃料切れ警告 ----
    if (gs->fuel <= 0.0f && gs->state == STATE_PLAYING) {
        glColor3f(1.0f, 0.15f, 0.15f);
        draw_string(fw * 0.5f - 56, panel_y - 22, cw * 1.3f, ch * 1.3f, "NO FUEL");
    }

    // ---- ステージクリア画面 ----
    if (gs->state == STATE_STAGE_CLEAR) {
        glColor4f(0.0f, 0.05f, 0.0f, 0.75f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();
        char buf[40]; float scw=11.0f, sch=17.0f, scw2=8.5f, sch2=13.0f;
        float cy2 = fh*0.5f - 55.0f;
        snprintf(buf, sizeof(buf), "STAGE %d CLEAR", gs->stage - 1);
        glColor3f(0.3f, 1.0f, 0.4f);
        draw_string(fw*0.5f-(float)strlen(buf)*(scw+1)*0.5f, cy2, scw, sch, buf);
        snprintf(buf, sizeof(buf), "FUEL BONUS  %d", gs->stage_fuel_bonus);
        glColor3f(0.5f, 1.0f, 0.6f);
        draw_string(fw*0.5f-(float)strlen(buf)*(scw2+1)*0.5f, cy2+34, scw2, sch2, buf);
        snprintf(buf, sizeof(buf), "TOTAL SCORE %d", gs->score);
        glColor3f(1.0f, 0.88f, 0.15f);
        draw_string(fw*0.5f-(float)strlen(buf)*(scw2+1)*0.5f, cy2+54, scw2, sch2, buf);
        glColor3f(0.5f, 0.75f, 1.0f);
        draw_string(fw*0.5f-85, cy2+85, scw2, sch2, "SPACE TO CONTINUE");
    }

    // ---- 爆発フラッシュ ----
    if (gs->state == STATE_EXPLODING) {
        float t = gs->explode_timer / EXPLODE_DURATION;
        float rr = 1.0f, gg = (t > 0.5f) ? 1.0f : t*2.0f;
        float bb = (t > 0.7f) ? (t-0.7f)/0.3f : 0.0f;
        glColor4f(rr, gg, bb, t * 0.85f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();
    }

    // ---- イニシャル入力画面 ----
    if (gs->state == STATE_ENTRY) {
        glColor4f(0.0f, 0.0f, 0.0f, 0.75f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();

        float cy2 = fh*0.5f - 75.0f;
        glColor3f(1.0f, 0.85f, 0.1f);
        draw_string(fw*0.5f-72, cy2, 10.0f, 16.0f, "NEW RECORD");

        char sbuf3[32]; snprintf(sbuf3, sizeof(sbuf3), "SCORE %d", gs->score);
        glColor3f(0.9f, 0.9f, 0.9f);
        draw_string(fw*0.5f-(float)strlen(sbuf3)*9*0.5f, cy2+25, 9.0f, 13.0f, sbuf3);

        // 3文字ボックス
        float bcw = 36.0f, bch = 56.0f, bsp = 10.0f;
        float bx0 = fw*0.5f - (3*bcw + 2*bsp)*0.5f;
        float by  = cy2 + 50.0f;
        Uint64 blink = SDL_GetTicks() / 400;  // 0.4秒周期でブリンク

        for (int i = 0; i < 3; i++) {
            float bx = bx0 + i*(bcw + bsp);
            int is_cur = (i == gs->entry_cur);

            // ボックス背景
            glColor4f(0.05f, 0.05f, is_cur ? 0.25f : 0.10f, 1.0f);
            glBegin(GL_QUADS);
            glVertex2f(bx-2, by-2); glVertex2f(bx+bcw+2, by-2);
            glVertex2f(bx+bcw+2, by+bch+2); glVertex2f(bx-2, by+bch+2);
            glEnd();

            // ボックス枠 (カーソル位置は明るく)
            if (is_cur && (blink & 1))
                glColor3f(1.0f, 0.9f, 0.2f);
            else
                glColor3f(0.3f, 0.4f, is_cur ? 0.8f : 0.5f);
            glLineWidth(is_cur ? 2.5f : 1.5f);
            glBegin(GL_LINE_LOOP);
            glVertex2f(bx-2, by-2); glVertex2f(bx+bcw+2, by-2);
            glVertex2f(bx+bcw+2, by+bch+2); glVertex2f(bx-2, by+bch+2);
            glEnd();
            glLineWidth(1.0f);

            // 文字
            char ch_str[2] = {gs->entry_ch[i], '\0'};
            glColor3f(is_cur ? 1.0f : 0.7f, is_cur ? 1.0f : 0.7f, is_cur ? 0.3f : 0.7f);
            draw_string(bx, by + 4, bcw, bch - 8, ch_str);
        }

        // 操作説明
        glColor3f(0.5f, 0.65f, 0.85f);
        draw_string(fw*0.5f-126, by+bch+16, 7.5f, 11.0f, "UP/DN:CHANGE  LR:MOVE");
        draw_string(fw*0.5f-68,  by+bch+34, 7.5f, 11.0f, "ENTER:DECIDE");
    }

    // ---- ゲームオーバー画面 ----
    if (gs->state == STATE_GAMEOVER) {
        glColor4f(0.0f, 0.0f, 0.0f, 0.70f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();
        float cy2 = fh*0.5f - 55.0f;
        glColor3f(1.0f, 0.2f, 0.1f);
        draw_string(fw*0.5f-65, cy2, 14.0f, 22.0f, "GAME OVER");
        char buf[40];
        snprintf(buf, sizeof(buf), "SCORE %d  STAGE %d", gs->score, gs->stage);
        glColor3f(1.0f, 0.85f, 0.2f);
        draw_string(fw*0.5f-(float)strlen(buf)*9*0.5f, cy2+38, 9.0f, 13.0f, buf);
        glColor3f(0.5f, 0.75f, 1.0f);
        draw_string(fw*0.5f-72, cy2+70, 8.5f, 13.0f, "HIT ANY KEY");
    }

    // ---- タイトル画面 ----
    if (gs->state == STATE_TITLE) {
        glColor4f(0.0f, 0.0f, 0.05f, 0.82f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();
        // "SPACE SHIP" = 10文字、cw=17で10*(17+1)=180px → 中央: 320-90=230
        glColor3f(0.5f, 0.8f, 1.0f);
        draw_string(fw*0.5f-90, fh*0.5f-40, 17.0f, 27.0f, "SPACE SHIP");
        glColor3f(0.7f, 0.7f, 0.9f);
        draw_string(fw*0.5f-55, fh*0.5f+20, 9.0f, 14.0f, "HIT ANY KEY");
    }

    // ---- モード選択画面 ----
    if (gs->state == STATE_MODE_SELECT) {
        glColor4f(0.0f, 0.0f, 0.05f, 0.88f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();

        float my0 = fh*0.5f - 95.0f;
        glColor3f(0.5f, 0.8f, 1.0f);
        draw_string(fw*0.5f-72, my0, 11.0f, 17.0f, "MODE SELECT");

        // 3つの選択肢
        static const char *MODE_LABELS[3]  = {"EASY",   "NORMAL", "HARD"};
        static const char *MODE_DESCS[3]   = {
            "STEERING - VELOCITY FOLLOWS HEADING",
            "INERTIA  - FULL THRUST AND BRAKE",
            "NO BRAKE - DECELERATION DISABLED"
        };
        for (int i = 0; i < 3; i++) {
            float ry = my0 + 35.0f + i * 52.0f;
            int selected = (i == gs->mode_sel);

            // 選択枠
            if (selected) {
                glColor4f(0.05f, 0.1f, 0.3f, 1.0f);
                glBegin(GL_QUADS);
                glVertex2f(fw*0.5f-160, ry-4); glVertex2f(fw*0.5f+160, ry-4);
                glVertex2f(fw*0.5f+160, ry+36); glVertex2f(fw*0.5f-160, ry+36);
                glEnd();
                glColor3f(0.9f, 0.85f, 0.2f);
                glLineWidth(1.5f);
                glBegin(GL_LINE_LOOP);
                glVertex2f(fw*0.5f-160, ry-4); glVertex2f(fw*0.5f+160, ry-4);
                glVertex2f(fw*0.5f+160, ry+36); glVertex2f(fw*0.5f-160, ry+36);
                glEnd();
                glLineWidth(1.0f);
            }

            // カーソル矢印
            glColor3f(selected ? 1.0f : 0.3f, selected ? 0.9f : 0.3f, selected ? 0.2f : 0.3f);
            draw_string(fw*0.5f-152, ry, 9.0f, 14.0f, selected ? ">" : " ");

            // モード名
            float lr = (i == 0) ? 0.4f : (i == 1) ? 0.3f : 0.8f;
            float lg = (i == 0) ? 0.9f : (i == 1) ? 0.8f : 0.3f;
            float lb = (i == 0) ? 0.4f : (i == 1) ? 0.4f : 0.3f;
            glColor3f(selected ? lr*1.3f : lr*0.7f,
                      selected ? lg*1.3f : lg*0.7f,
                      selected ? lb*1.3f : lb*0.7f);
            draw_string(fw*0.5f-135, ry, 11.0f, 17.0f, MODE_LABELS[i]);

            // 説明文
            glColor3f(selected ? 0.75f : 0.4f, selected ? 0.75f : 0.4f, selected ? 0.75f : 0.4f);
            draw_string(fw*0.5f-145, ry+20, 7.0f, 11.0f, MODE_DESCS[i]);
        }

        glColor3f(0.5f, 0.65f, 0.85f);
        draw_string(fw*0.5f-84, my0 + 35.0f + 3*52.0f + 8, 7.5f, 11.0f, "UP/DN:SELECT  ENTER:NEXT");
    }

    // ---- 機体選択画面 ----
    if (gs->state == STATE_SHIP_SELECT) {
        glColor4f(0.0f, 0.0f, 0.05f, 0.88f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();

        float sy0 = fh*0.5f - 95.0f;
        glColor3f(0.5f, 0.8f, 1.0f);
        draw_string(fw*0.5f-77, sy0, 11.0f, 17.0f, "SELECT SHIP");

        static const char *SHIP_LABELS[3] = {"STANDARD", "AGILE",   "BOOST"};
        static const char *SHIP_DESCS[3]  = {
            "BALANCED - TURN 1.0X  ACCEL 1.0X",
            "HIGH TURN - TURN 2.0X  ACCEL 1.0X",
            "HIGH THRUST - TURN 1.0X  ACCEL 2.0X"
        };
        // 機体カラー: 標準=白, 高機動=シアン, 高推力=オレンジ
        static const float SHIP_COL[3][3] = {
            {0.8f, 0.8f, 0.8f},
            {0.3f, 0.9f, 1.0f},
            {1.0f, 0.6f, 0.2f}
        };

        for (int i = 0; i < 3; i++) {
            float ry2 = sy0 + 35.0f + i * 52.0f;
            int selected = (i == gs->ship_sel);

            if (selected) {
                glColor4f(0.05f, 0.1f, 0.3f, 1.0f);
                glBegin(GL_QUADS);
                glVertex2f(fw*0.5f-160, ry2-4); glVertex2f(fw*0.5f+160, ry2-4);
                glVertex2f(fw*0.5f+160, ry2+36); glVertex2f(fw*0.5f-160, ry2+36);
                glEnd();
                glColor3f(SHIP_COL[i][0], SHIP_COL[i][1], SHIP_COL[i][2]);
                glLineWidth(1.5f);
                glBegin(GL_LINE_LOOP);
                glVertex2f(fw*0.5f-160, ry2-4); glVertex2f(fw*0.5f+160, ry2-4);
                glVertex2f(fw*0.5f+160, ry2+36); glVertex2f(fw*0.5f-160, ry2+36);
                glEnd();
                glLineWidth(1.0f);
            }

            glColor3f(selected ? 1.0f : 0.3f, selected ? 0.9f : 0.3f, selected ? 0.2f : 0.3f);
            draw_string(fw*0.5f-152, ry2, 9.0f, 14.0f, selected ? ">" : " ");

            float sc = selected ? 1.0f : 0.6f;
            glColor3f(SHIP_COL[i][0]*sc, SHIP_COL[i][1]*sc, SHIP_COL[i][2]*sc);
            draw_string(fw*0.5f-135, ry2, 11.0f, 17.0f, SHIP_LABELS[i]);

            glColor3f(selected ? 0.75f : 0.4f, selected ? 0.75f : 0.4f, selected ? 0.75f : 0.4f);
            draw_string(fw*0.5f-145, ry2+20, 7.0f, 11.0f, SHIP_DESCS[i]);
        }

        glColor3f(0.5f, 0.65f, 0.85f);
        draw_string(fw*0.5f-84, sy0 + 35.0f + 3*52.0f + 8, 7.5f, 11.0f, "UP/DN:SELECT  ENTER:START");
    }

    // ---- ランキング画面 (モード別ローテーション) ----
    if (gs->state == STATE_RANKING) {
        static const char *rmode_names[3] = {"EASY", "NORMAL", "HARD"};
        // モード別ヘッダー色
        static const float rmode_col[3][3] = {
            {0.3f, 1.0f, 0.4f},
            {1.0f, 0.85f, 0.2f},
            {1.0f, 0.35f, 0.2f}
        };
        int m = gs->ranking_mode_idx % 3;

        glColor4f(0.0f, 0.0f, 0.05f, 0.82f);
        glBegin(GL_QUADS);
        glVertex2f(0,0); glVertex2f(fw,0); glVertex2f(fw,fh); glVertex2f(0,fh);
        glEnd();
        float rx = fw*0.5f - 110.0f, ry = fh*0.5f - 80.0f;
        float rscw = 9.0f, rsch = 13.0f;

        // "HIGH SCORE [MODE]"
        glColor3f(rmode_col[m][0], rmode_col[m][1], rmode_col[m][2]);
        char hdr[32]; snprintf(hdr, sizeof(hdr), "HIGH SCORE [%s]", rmode_names[m]);
        draw_string(fw*0.5f - (float)strlen(hdr)*10*0.5f, ry, 10.0f, 15.0f, hdr);

        int cnt = g_hiscore_count[m];
        if (cnt == 0) {
            glColor3f(0.5f, 0.5f, 0.6f);
            draw_string(rx + 20, ry + 35, rscw, rsch, "NO RECORD YET");
        }
        for (int i = 0; i < cnt; i++) {
            char rbuf[64];
            snprintf(rbuf, sizeof(rbuf), "%d  %.3s  %6d  ST%d",
                     i+1, g_hiscores[m][i].initials,
                     g_hiscores[m][i].score, g_hiscores[m][i].stage);
            float ri_r = (i == 0) ? rmode_col[m][0] : 0.7f;
            float ri_g = (i == 0) ? rmode_col[m][1] : 0.7f;
            float ri_b = (i == 0) ? rmode_col[m][2] : 0.7f;
            glColor3f(ri_r, ri_g, ri_b);
            draw_string(rx, ry + 28 + i * 22, rscw, rsch, rbuf);
        }
        glColor3f(0.5f, 0.65f, 0.85f);
        draw_string(fw*0.5f - 55, ry + 150, rscw, rsch, "HIT ANY KEY");
    }

    // ---- 衝突警告 (PLAYING中のみ) ----
    if (gs->state == STATE_PLAYING && gs->collision_warning) {
        // 5Hzで点滅 (200ms周期)
        if ((SDL_GetTicks() / 200) % 2 == 0) {
            float pulse = 0.7f + 0.3f * sinf((float)SDL_GetTicks() * 0.01f);
            glColor3f(1.0f, pulse * 0.1f, 0.0f);
            // "DANGER" を画面上部中央に大きく
            draw_string(fw*0.5f - 54, 18.0f, 14.0f, 22.0f, "DANGER");
        }
    }

    // ---- カウントダウン ----
    if (gs->state == STATE_COUNTDOWN) {
        int n = (int)ceilf(gs->countdown_val);
        float cy2 = fh*0.5f - 50.0f;
        if (gs->countdown_val > 0.0f && n >= 1) {
            // 数字
            char cbuf[16]; snprintf(cbuf, sizeof(cbuf), "%d", n);
            float pulse = 0.7f + 0.3f * sinf(gs->countdown_val * (float)M_PI);
            glColor3f(pulse, pulse * 0.8f, 0.1f);
            draw_string(fw*0.5f - 30, cy2, 52.0f, 80.0f, cbuf);
        } else {
            // GO
            float pulse = 0.7f + 0.3f * sinf(gs->countdown_val * (float)M_PI * 4);
            glColor3f(0.2f, pulse, 0.2f);
            draw_string(fw*0.5f - 65, cy2, 52.0f, 80.0f, "GO");
        }
    }

    glEnable(GL_DEPTH_TEST);
    glMatrixMode(GL_PROJECTION);
    glPopMatrix();
    glMatrixMode(GL_MODELVIEW);
    glPopMatrix();
}

// ==================== メイン ====================
int main(int argc, char *argv[]) {
    (void)argc; (void)argv;
    srand((unsigned)time(NULL));
    hiscore_load();
    audio_init();

    if (!SDL_Init(SDL_INIT_VIDEO)) {
        SDL_Log("SDL_Init: %s", SDL_GetError());
        return 1;
    }

    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 2);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 1);
    SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);
    SDL_GL_SetAttribute(SDL_GL_DEPTH_SIZE, 24);

    SDL_Window *window = SDL_CreateWindow("SpaceShip",
        WINDOW_WIDTH, WINDOW_HEIGHT,
        SDL_WINDOW_OPENGL | SDL_WINDOW_RESIZABLE);
    if (!window) {
        SDL_Log("SDL_CreateWindow: %s", SDL_GetError());
        SDL_Quit();
        return 1;
    }

    SDL_GLContext gl_ctx = SDL_GL_CreateContext(window);
    if (!gl_ctx) {
        SDL_Log("SDL_GL_CreateContext: %s", SDL_GetError());
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 1;
    }
    SDL_GL_SetSwapInterval(1);

    // OpenGL初期設定
    glEnable(GL_DEPTH_TEST);
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

    // ゲーム状態初期化 (タイトル画面から開始)
    GameState gs = {0};
    gs.state      = STATE_TITLE;
    gs.title_timer = TITLE_FLIP_SEC;

    init_stars();
    spawn_ring(&gs.ring, 1, 1); // タイトル画面用ダミー

    Uint64 prev_tick = SDL_GetTicks();
    int running = 1;

    while (running) {
        Uint64 now = SDL_GetTicks();
        float dt = (float)(now - prev_tick) * 0.001f;
        prev_tick = now;
        if (dt > 0.05f) dt = 0.05f;  // 最大20fps相当でキャップ

        // イベント処理
        SDL_Event ev;
        int any_key = 0;
        int key_up = 0, key_down = 0, key_left = 0, key_right = 0, key_enter = 0;
        while (SDL_PollEvent(&ev)) {
            if (ev.type == SDL_EVENT_QUIT) running = 0;
            if (ev.type == SDL_EVENT_KEY_DOWN) {
                switch (ev.key.key) {
                    case SDLK_ESCAPE: running = 0; break;
                    case SDLK_UP:     key_up    = 1; any_key = 1; break;
                    case SDLK_DOWN:   key_down  = 1; any_key = 1; break;
                    case SDLK_LEFT:   key_left  = 1; any_key = 1; break;
                    case SDLK_RIGHT:  key_right = 1; any_key = 1; break;
                    case SDLK_RETURN:
                    case SDLK_KP_ENTER: key_enter = 1; any_key = 1; break;
                    default:          any_key = 1; break;
                }
            }
        }

        // ---- タイトル / ランキング ----
        if (gs.state == STATE_TITLE || gs.state == STATE_RANKING) {
            gs.title_timer -= dt;
            if (gs.title_timer <= 0.0f) {
                gs.title_timer = TITLE_FLIP_SEC;
                if (gs.state == STATE_TITLE) {
                    gs.state = STATE_RANKING;
                    gs.ranking_mode_idx++;  // ランキング表示モードを進める
                } else {
                    gs.state = STATE_TITLE;
                }
            }
            if (any_key) {
                gs.state    = STATE_MODE_SELECT;
                gs.mode_sel = MODE_NORMAL; // デフォルトはNORMAL
            }
            goto render;
        }

        // ---- モード選択 ----
        if (gs.state == STATE_MODE_SELECT) {
            if (key_up)   gs.mode_sel = (gs.mode_sel + 2) % 3;
            if (key_down) gs.mode_sel = (gs.mode_sel + 1) % 3;
            if (key_enter) {
                gs.state    = STATE_SHIP_SELECT;
                gs.ship_sel = SHIP_STANDARD;
            }
            goto render;
        }

        // ---- 機体選択 ----
        if (gs.state == STATE_SHIP_SELECT) {
            if (key_up)   gs.ship_sel = (gs.ship_sel + 2) % 3;
            if (key_down) gs.ship_sel = (gs.ship_sel + 1) % 3;
            if (key_enter) {
                GameMode  chosen_mode = (GameMode) gs.mode_sel;
                ShipType  chosen_ship = (ShipType) gs.ship_sel;
                int       rmi         = gs.ranking_mode_idx;
                gs = (GameState){0};
                gs.pos              = v3(512, 512, 512);
                gs.fwd              = v3(0, 0, 1);
                gs.up               = v3(0, 1, 0);
                gs.fuel             = INITIAL_FUEL;
                gs.prev_pos         = gs.pos;
                gs.ring_timer       = RING_TIME_LIMIT;
                gs.stage            = 1;
                gs.countdown_val    = COUNTDOWN_START;
                gs.mode             = chosen_mode;
                gs.ship             = chosen_ship;
                gs.ranking_mode_idx = rmi;
                gs.state            = STATE_COUNTDOWN;
                spawn_ring(&gs.ring, 1, 1); // ステージ1の1個目
            }
            goto render;
        }

        // ---- カウントダウン ----
        if (gs.state == STATE_COUNTDOWN) {
            gs.countdown_val -= dt;
            if (gs.countdown_val <= -1.0f) gs.state = STATE_PLAYING;
            goto render;
        }

        // ---- 爆発 ----
        if (gs.state == STATE_EXPLODING) {
            gs.explode_timer -= dt;
            if (gs.explode_timer <= 0.0f) {
                if (hiscore_qualifies(gs.score, gs.mode)) {
                    // イニシャル入力へ
                    gs.entry_ch[0] = gs.entry_ch[1] = gs.entry_ch[2] = 'A';
                    gs.entry_cur = 0;
                    gs.state = STATE_ENTRY;
                } else {
                    gs.state = STATE_GAMEOVER;
                }
            }
            goto render;
        }

        // ---- イニシャル入力 ----
        static const char ENTRY_SET[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.- ";
        static const int  ENTRY_N = (int)(sizeof(ENTRY_SET) - 1); // 39
        if (gs.state == STATE_ENTRY) {
            if (key_up || key_down) {
                char cur = gs.entry_ch[gs.entry_cur];
                int idx = 0;
                for (int j = 0; j < ENTRY_N; j++)
                    if (ENTRY_SET[j] == cur) { idx = j; break; }
                if (key_up)   idx = (idx + 1) % ENTRY_N;
                if (key_down) idx = (idx + ENTRY_N - 1) % ENTRY_N;
                gs.entry_ch[gs.entry_cur] = ENTRY_SET[idx];
            }
            if (key_right && gs.entry_cur < 2) gs.entry_cur++;
            if (key_left  && gs.entry_cur > 0) gs.entry_cur--;
            if (key_enter) {
                hiscore_add(gs.entry_ch, gs.score, gs.stage, gs.mode);
                hiscore_save();
                gs.state = STATE_GAMEOVER;
            }
            goto render;
        }

        // ---- ゲームオーバー: 任意キーでタイトルへ ----
        if (gs.state == STATE_GAMEOVER) {
            if (any_key) {
                gs.state       = STATE_TITLE;
                gs.title_timer = TITLE_FLIP_SEC;
            }
            goto render;
        }

        // ---- ステージクリア: Spaceでカウントダウン→次ステージ ----
        if (gs.state == STATE_STAGE_CLEAR) {
            if (any_key) {
                gs.rings_done    = 0;
                gs.ring_timer    = RING_TIME_LIMIT;
                gs.fuel          = INITIAL_FUEL;
                gs.countdown_val = COUNTDOWN_START;
                gs.state         = STATE_COUNTDOWN;
                spawn_ring(&gs.ring, 1, gs.stage); // 次ステージの1個目
            }
            goto render;
        }

        // ---- 以下は STATE_PLAYING のみ ----

        // 入力
        const bool *keys = SDL_GetKeyboardState(NULL);

        // 機体パラメータ倍率
        float ship_rot_mul   = (gs.ship == SHIP_AGILE)  ? 2.0f : 1.0f;
        float ship_accel_mul = (gs.ship == SHIP_BOOST)  ? 2.0f : 1.0f;

        // 方向転換 (慣性なし・押している間だけ)
        Vec3 right = vnorm(vcross(gs.fwd, gs.up));
        float rot = ROTATION_SPEED * ship_rot_mul * dt;
        bool rotating = false;

        if (keys[SDL_SCANCODE_UP]) {
            gs.fwd = vrotate(gs.fwd, right, rot);
            gs.up  = vrotate(gs.up,  right, rot);
            rotating = true;
        }
        if (keys[SDL_SCANCODE_DOWN]) {
            gs.fwd = vrotate(gs.fwd, right, -rot);
            gs.up  = vrotate(gs.up,  right, -rot);
            rotating = true;
        }
        if (keys[SDL_SCANCODE_LEFT]) {
            gs.fwd = vrotate(gs.fwd, gs.up, -rot);
            rotating = true;
        }
        if (keys[SDL_SCANCODE_RIGHT]) {
            gs.fwd = vrotate(gs.fwd, gs.up, rot);
            rotating = true;
        }
        if (rotating) {
            gs_reorthogonalize(&gs);
            if (gs.fuel > 0.0f) gs.fuel -= FUEL_ROTATE * dt;
            // イージーモード: 操舵で速度方向を機首方向に追従させる
            if (gs.mode == MODE_EASY) {
                float spd = vlen(gs.vel);
                if (spd > 1e-4f) gs.vel = vscale(gs.fwd, spd);
            }
        }

        // メインスラスター (Z/A)
        int thrusting = (keys[SDL_SCANCODE_Z] || keys[SDL_SCANCODE_A]) && gs.fuel > 0.0f;
        if (thrusting) {
            gs.vel = vadd(gs.vel, vscale(gs.fwd, MAIN_ACCEL * ship_accel_mul * dt));
            gs.fuel -= FUEL_MAIN * dt;
        }
        audio_thruster(thrusting);

        // 衝突予測警告
        gs.collision_warning = predict_collision(&gs);
        audio_warning_tone(gs.collision_warning);

        // ブレーキ (X/B) - ハードモード以外のみ有効
        if (gs.mode != MODE_HARD &&
            (keys[SDL_SCANCODE_X] || keys[SDL_SCANCODE_B]) && gs.fuel > 0.0f) {
            float spd = vlen(gs.vel);
            if (spd > 1e-4f) {
                float dv = BRAKE_ACCEL * dt;
                if (dv >= spd) {
                    gs.vel = v3(0, 0, 0);
                } else {
                    gs.vel = vadd(gs.vel, vscale(gs.vel, -dv / spd));
                }
            }
            gs.fuel -= FUEL_BRAKE * dt;
        }

        if (gs.fuel < 0.0f) gs.fuel = 0.0f;

        // 速度減衰 (-kv)
        if (DRAG_K > 0.0f) {
            gs.vel = vadd(gs.vel, vscale(gs.vel, -DRAG_K * dt));
        }

        // リング更新 (回転・移動)
        if (gs.ring.rot_speed != 0.0f) {
            float angle = gs.ring.rot_speed * dt;
            gs.ring.normal = vnorm(vrotate(gs.ring.normal, gs.ring.rot_axis, angle));
            gs.ring.up     = vnorm(vrotate(gs.ring.up,     gs.ring.rot_axis, angle));
        }
        if (gs.ring.move_speed > 0.0f) {
            gs.ring.pos = vwrap(vadd(gs.ring.pos,
                                     vscale(gs.ring.move_dir, gs.ring.move_speed * dt)));
        }

        // 位置更新 (トーラス空間)
        gs.prev_pos = gs.pos;
        gs.pos = vwrap(vadd(gs.pos, vscale(gs.vel, dt)));

        // 制限時間カウントダウン
        gs.ring_timer -= dt;
        if (gs.ring_timer <= 0.0f) {
            // タイムアップ → ゲームオーバー
            gs.state         = STATE_EXPLODING;
            gs.explode_timer = EXPLODE_DURATION;
            goto render;
        }

        // リング枠衝突判定 (通過判定より先に行う)
        if (check_ring_hit(&gs)) {
            gs.state         = STATE_EXPLODING;
            gs.explode_timer = EXPLODE_DURATION;
            goto render;
        }

        // リング通過判定 (穴をくぐった)
        if (check_ring_pass(&gs)) {
            // スコア: 基本点 (リング種別で変化) + 残り秒数ボーナス
            int base = (gs.ring.color_type == 2) ? 400
                     : (gs.ring.color_type == 1) ? 200
                     : RING_BASE_SCORE;
            int ring_score = base + (int)(gs.ring_timer) * RING_TIME_BONUS;
            gs.score     += ring_score;
            gs.rings_done++;
            gs.ring_timer = RING_TIME_LIMIT;  // タイマーリセット

            if (gs.rings_done >= RINGS_PER_STAGE) {
                // ステージクリア
                int fuel_bonus = (int)gs.fuel;
                gs.score           += fuel_bonus;
                gs.stage_fuel_bonus = fuel_bonus;
                gs.stage++;
                gs.state = STATE_STAGE_CLEAR;
            } else {
                spawn_ring(&gs.ring, gs.rings_done + 1, gs.stage);
            }
        }

        // ==================== 描画 ====================
        render:;
        // PLAYING以外のフレームではスラスター音を止める
        if (gs.state != STATE_PLAYING) {
            audio_thruster(0);
            audio_warning_tone(0);
        }
        int ww, wh;
        SDL_GetWindowSize(window, &ww, &wh);
        if (wh == 0) wh = 1;

        glViewport(0, 0, ww, wh);
        glClearColor(0.0f, 0.0f, 0.015f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        // --- 透視投影 ---
        glMatrixMode(GL_PROJECTION);
        glLoadIdentity();
        float fov_rad = FOV_DEG * (float)M_PI / 180.0f;
        float f = 1.0f / tanf(fov_rad * 0.5f);
        float asp = (float)ww / (float)wh;
        float nr = NEAR_PLANE, fr = FAR_PLANE;
        float proj[16] = {
            f/asp, 0,  0,                      0,
            0,     f,  0,                      0,
            0,     0,  (fr+nr)/(nr-fr),       -1,
            0,     0,  (2.0f*fr*nr)/(nr-fr),   0
        };
        glLoadMatrixf(proj);

        // --- ビュー行列 (パイロット視点) ---
        glMatrixMode(GL_MODELVIEW);
        glLoadIdentity();
        Vec3 fv = gs.fwd;
        Vec3 rv = vnorm(vcross(fv, gs.up));
        Vec3 uv = vnorm(vcross(rv, fv));
        // gluLookAt 相当 (右手系: +Z が手前)
        float view[16] = {
             rv.x,  uv.x, -fv.x, 0,
             rv.y,  uv.y, -fv.y, 0,
             rv.z,  uv.z, -fv.z, 0,
             0,     0,     0,    1
        };
        glLoadMatrixf(view);

        render_stars(gs.pos);
        render_ring(&gs.ring, gs.pos);
        render_hud(&gs, ww, wh);

        SDL_GL_SwapWindow(window);
    }

    audio_cleanup();
    SDL_GL_DestroyContext(gl_ctx);
    SDL_DestroyWindow(window);
    SDL_Quit();
    return 0;
}
