using System.Collections;
using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Audio;
using FriendSlop.Game;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.World
{
    public enum GambleGame { Slots, Roulette, Scratchers }

    /// <summary>
    /// A Lucky Lodge game. Bet team money (look at the machine, scroll for the amount, press E) or toss a
    /// carcass onto its pad to bet the animal's value. The host decides; everyone watches the same show.
    ///
    /// Built around "juice" (Jonasson &amp; Purho) and Balatro-style theatrical reveals: outcomes are hidden until
    /// you commit, then revealed one piece at a time with escalating sound, eased overshoots, flashing bulbs,
    /// a machine with a face that reacts, a ticking win counter, coin showers and a little camera kick.
    ///  • Slots: 3 reels stop one by one; if the first two match the last one teases with a long, ticking spin.
    ///    Pairs 1.3× (antler pair 4×); triples 4×–25×; three golden antlers = 50× + the progressive JACKPOT.
    ///  • Roulette: a 37-pocket wheel and a ball that spirals into the result. RED/BLACK 2×, GREEN 33×.
    ///  • Scratchers: six foil panels peel off one at a time (press E to scratch faster); match three to win.
    /// Every bet feeds the lodge's progressive jackpot.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GambleStation : MonoBehaviour
    {
        public static readonly List<GambleStation> All = new();

        public GambleGame game;
        [Tooltip("Unique per station (set by the scene builder); identifies it in network messages.")]
        public int id;
        public TextMesh status;
        public TextMesh readout;

        [Header("Slots")]
        public Transform[] reels = System.Array.Empty<Transform>();
        public Transform lever;
        public Renderer[] bulbs = System.Array.Empty<Renderer>();
        public Transform[] eyes = System.Array.Empty<Transform>();
        public Transform[] pupils = System.Array.Empty<Transform>();
        public Transform mouth;

        [Header("Roulette")]
        public Transform wheel;
        public Transform ball;

        [Header("Scratchers")]
        public Transform[] foils = System.Array.Empty<Transform>();
        public TextMesh[] scratchSymbols = System.Array.Empty<TextMesh>();

        [Header("Fx")]
        public Transform coinSpout;
        public Material coinMaterial;

        // ------------------------------------------------------------------ tables

        public static readonly string[] SlotSymbols = { "RABBIT", "DUCK", "GOOSE", "DEER", "MOOSE", "ANTLERS" };
        public static readonly Color[] SlotColors =
        {
            new Color(0.95f, 0.95f, 0.95f), new Color(0.35f, 0.75f, 0.35f), new Color(0.6f, 0.85f, 1f),
            new Color(0.85f, 0.55f, 0.3f), new Color(0.55f, 0.35f, 0.25f), new Color(1f, 0.8f, 0.2f),
        };
        static readonly int[] SlotWeights = { 28, 24, 19, 13, 9, 7 };
        static readonly float[] TripleMult = { 4f, 5f, 7f, 12f, 25f, 50f };
        const float PairMult = 1.3f, AntlerPairMult = 4f;
        /// <summary>Symbol printed on each of a reel drum's faces (face k sits at k × 45° around the drum).</summary>
        public static readonly int[] ReelStrip = { 0, 1, 2, 3, 4, 5, 0, 1 };
        public const int ReelFaces = 8;

        public static readonly int[] WheelOrder = { 0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26 };
        public static readonly HashSet<int> Reds = new() { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
        static readonly float[] RouletteMult = { 2f, 2f, 33f };
        static readonly string[] RouletteNames = { "RED", "BLACK", "GREEN" };

        public static readonly string[] ScratchNames = { "BUCKS", "GOOSE", "DEER", "BEAR", "ANTLERS" };
        public static readonly Color[] ScratchColors = { new Color(0.45f, 0.9f, 0.45f), new Color(0.75f, 0.9f, 1f), new Color(0.9f, 0.6f, 0.35f), new Color(0.6f, 0.4f, 0.3f), new Color(1f, 0.82f, 0.2f) };
        static readonly float[] ScratchMult = { 1f, 2f, 4f, 10f, 40f };
        static readonly float[] ScratchChance = { 0.14f, 0.10f, 0.05f, 0.02f, 0.005f }; // otherwise a losing card (~69%)
        public const int ScratchPanels = 6;

        // ------------------------------------------------------------------ state

        /// <summary>Editor tests: force the next slot result (reels a, b, c).</summary>
        public static int[] DebugNextSlots;
        public bool Busy { get; private set; }
        int _pendingPayout, _pendingStake;
        string _pendingWhat, _pendingWho;
        bool _pendingJackpot, _pendingFromAnimal;
        readonly HashSet<Animal> _taken = new();
        Coroutine _show, _safety;
        bool _advance;
        MaterialPropertyBlock _mpb;
        Vector3[] _eyeRest = System.Array.Empty<Vector3>();
        Vector3 _mouthRest;
        Quaternion _leverRest;
        enum Mood { Idle, Spinning, Tense, Happy, Sad }
        Mood _mood;
        float _bulbFlash; // 0 idle chase, 1 fast chase, 2 strobe

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _eyeRest = new Vector3[eyes.Length];
            for (int i = 0; i < eyes.Length; i++) if (eyes[i]) _eyeRest[i] = eyes[i].localScale;
            if (mouth) _mouthRest = mouth.localScale;
            if (lever) _leverRest = lever.localRotation;
        }

        void OnEnable() { All.Add(this); GameState.GambleResult += OnResult; GameState.GambleAction += OnAction; }
        void OnDisable() { All.Remove(this); GameState.GambleResult -= OnResult; GameState.GambleAction -= OnAction; }
        void Reset() => GetComponent<Collider>().isTrigger = true;

        // ================================================================== host

        static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>Host: a player bet team money here.</summary>
        public void ServerBetMoney(ulong sender, int option, int amount)
        {
            var state = GameState.Instance;
            if (!IsHost || state == null) return;
            string who = GameState.PlayerName(sender);
            amount = Mathf.Clamp(amount, GambleBetSpot.BetSizes[0], GambleBetSpot.BetSizes[^1]);
            if (Busy) return;
            if (LevelOf(sender) < LodgeLevel) { state.ServerAnnounce($"The Lucky Lodge won't take {who}'s money until hunter level {LodgeLevel}."); return; }
            if (!state.ServerTrySpend(amount)) { state.ServerAnnounce($"{who} can't cover a ${amount:N0} bet. Go hunt!"); return; }
            ServerPlay(amount, option, who, fromAnimal: false, animalName: null);
        }

        void OnTriggerStay(Collider other)
        {
            if (!IsHost || Busy || GameState.Instance == null) return;
            var fish = other.GetComponentInParent<FriendSlop.Fishing.FishCatch>();
            if (fish != null && fish.IsSpawned && !fish.Carry.IsHeld && !fish.Species.Junk)
            {
                ulong angler = fish.Carry.LastHolder;
                if (angler != FriendSlop.Interaction.Carryable.NoHolder && LevelOf(angler) < LodgeLevel) return;
                int fstake = Mathf.Max(1, fish.SaleValue);
                string fname = fish.DisplayName;
                Vector3 fl = transform.InverseTransformPoint(fish.transform.position);
                int fopt = game == GambleGame.Roulette ? (new Vector2(fl.x, fl.z).magnitude < 0.45f ? 2 : fl.x > 0f ? 0 : 1) : 0;
                fish.NetworkObject.Despawn(true);
                ServerPlay(fstake, fopt, "", fromAnimal: true, animalName: fname);
                return;
            }
            var animal = other.GetComponentInParent<Animal>();
            if (animal == null || !animal.IsSpawned || !animal.IsDead.Value || animal.Carry.IsHeld) return;
            ulong thrower = animal.Carry.LastHolder;
            if (thrower != FriendSlop.Interaction.Carryable.NoHolder && LevelOf(thrower) < LodgeLevel)
            {
                // Too green to gamble: bounce the carcass back out (and only complain every few seconds).
                if (animal.TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
                    rb.linearVelocity = (animal.transform.position - transform.position).normalized * 4f + Vector3.up * 4f;
                if (Time.time > _nextRefusal)
                {
                    _nextRefusal = Time.time + 4f;
                    GameState.Instance.ServerAnnounce($"The Lucky Lodge opens at hunter level {LodgeLevel}. Come back later, {GameState.PlayerName(thrower)}!");
                }
                return;
            }
            if (!_taken.Add(animal)) return;
            int stake = Mathf.Max(1, animal.SaleValue);
            string name = animal.DisplayName;
            Vector3 local = transform.InverseTransformPoint(animal.transform.position);
            int option = 0;
            if (game == GambleGame.Roulette)
                option = new Vector2(local.x, local.z).magnitude < 0.45f ? 2 : local.x > 0f ? 0 : 1; // green bullseye, red half, black half
            animal.NetworkObject.Despawn(true);
            ServerPlay(stake, option, "", fromAnimal: true, animalName: name);
        }

        /// <summary>Hunter level needed to gamble at the Lucky Lodge.</summary>
        public const int LodgeLevel = 5;
        float _nextRefusal;

        static int LevelOf(ulong clientId) => FriendSlop.Player.PlayerProgress.Of(clientId) is { } p ? p.Level.Value : 1;

        void ServerPlay(int stake, int option, string who, bool fromAnimal, string animalName)
        {
            var state = GameState.Instance;
            Busy = true;
            state.LodgeJackpot.Value += Mathf.Max(1, Mathf.RoundToInt(stake * 0.08f));

            int a = 0, b = 0, c = 0, info = 0;
            float mult = 0f;
            bool jackpot = false;
            switch (game)
            {
                case GambleGame.Slots:
                    a = Pick(SlotWeights); b = Pick(SlotWeights); c = Pick(SlotWeights);
                    if (DebugNextSlots != null) { a = DebugNextSlots[0]; b = DebugNextSlots[1]; c = DebugNextSlots[2]; DebugNextSlots = null; }
                    if (a == b && b == c) { mult = TripleMult[a]; jackpot = a == 5; }
                    else if (a == b || b == c || a == c)
                    {
                        int pairSym = a == b ? a : c;
                        mult = pairSym == 5 ? AntlerPairMult : PairMult;
                    }
                    break;
                case GambleGame.Roulette:
                    option = Mathf.Clamp(option, 0, 2);
                    a = Random.Range(0, 37);
                    b = option;
                    bool hit = option == 2 ? a == 0 : a != 0 && Reds.Contains(a) == (option == 0);
                    if (hit) mult = RouletteMult[option];
                    break;
                case GambleGame.Scratchers:
                {
                    int winSym = -1;
                    float r = Random.value;
                    for (int s = ScratchChance.Length - 1; s >= 0; s--) { if (r < ScratchChance[s]) { winSym = s; break; } r -= ScratchChance[s]; }
                    var panels = ScratchCard(winSym);
                    for (int i = 0; i < ScratchPanels; i++) a |= panels[i] << (i * 3);
                    b = winSym;
                    if (winSym >= 0) mult = ScratchMult[winSym];
                    break;
                }
            }

            int payout = Mathf.RoundToInt(stake * mult);
            if (jackpot) { payout += state.LodgeJackpot.Value; state.LodgeJackpot.Value = GameState.JackpotSeed; info |= 1; }
            info |= option << 4;

            _pendingPayout = payout; _pendingStake = stake; _pendingJackpot = jackpot;
            _pendingFromAnimal = fromAnimal; _pendingWho = who;
            _pendingWhat = fromAnimal ? $"a {animalName} (${stake:N0})" : $"${stake:N0}";
            state.ServerGambleResult(id, a, b, c, payout, info, stake);
            if (_safety != null) StopCoroutine(_safety);
            _safety = StartCoroutine(SafetyFinish());
        }

        /// <summary>A losing or winning six-panel card: the winner (if any) shows exactly three times, nothing else does.</summary>
        static int[] ScratchCard(int winSym)
        {
            var panels = new int[ScratchPanels];
            var counts = new int[ScratchNames.Length];
            int fill = 0;
            if (winSym >= 0) { for (int i = 0; i < 3; i++) panels[fill++] = winSym; counts[winSym] = 3; }
            // Teasers: losing cards often carry two of something big.
            if (winSym < 0 && Random.value < 0.45f)
            {
                int tease = Random.value < 0.5f ? 4 : 3;
                panels[fill++] = tease; panels[fill++] = tease; counts[tease] = 2;
            }
            while (fill < ScratchPanels)
            {
                int s = Random.Range(0, ScratchNames.Length);
                if (s == winSym || counts[s] >= 2) continue;
                counts[s]++; panels[fill++] = s;
            }
            for (int i = ScratchPanels - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (panels[i], panels[j]) = (panels[j], panels[i]); }
            return panels;
        }

        IEnumerator SafetyFinish()
        {
            yield return new WaitForSeconds(16f);
            ServerFinish();
        }

        /// <summary>Host: the show finished (or timed out). Pay out and free the station.</summary>
        void ServerFinish()
        {
            if (!IsHost || !Busy) return;
            Busy = false;
            if (_safety != null) { StopCoroutine(_safety); _safety = null; }
            var state = GameState.Instance;
            if (state == null) return;
            string game = this.game switch { GambleGame.Slots => "SLOTS", GambleGame.Roulette => "ROULETTE", _ => "SCRATCHER" };
            string who = string.IsNullOrEmpty(_pendingWho) ? "" : _pendingWho + " ";
            if (_pendingJackpot) state.ServerPayGamble(_pendingPayout, $"JACKPOT!!! {who}hit THREE GOLDEN ANTLERS for ${_pendingPayout:N0}!");
            else if (_pendingPayout > 0) state.ServerPayGamble(_pendingPayout, $"{game}: {who}bet {_pendingWhat} and won ${_pendingPayout:N0}!");
            else state.ServerAnnounce($"{game}: {who}lost {_pendingWhat}. The house always wins.");
        }

        static int Pick(int[] weights)
        {
            int total = 0; foreach (var w in weights) total += w;
            int r = Random.Range(0, total);
            for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r < 0) return i; }
            return weights.Length - 1;
        }

        void LateUpdate() => _taken.RemoveWhere(x => x == null);

        // ================================================================== everyone: the show

        void OnResult(int station, int a, int b, int c, int payout, int info, int stake)
        {
            if (station != id) return;
            Busy = true; // clients too, so the hint and E behave
            if (_show != null) StopCoroutine(_show);
            _advance = false;
            _show = StartCoroutine(game switch
            {
                GambleGame.Slots => SlotShow(a, b, c, payout, stake, (info & 1) != 0),
                GambleGame.Roulette => RouletteShow(a, b, payout, stake),
                _ => ScratchShow(a, b, payout, stake),
            });
        }

        void OnAction(int station, int action)
        {
            if (station == id && action == 1) _advance = true;
        }

        void EndShow()
        {
            if (IsHost) ServerFinish();
            else Busy = false;
        }

        // ------------------------------------------------------------------ slots

        IEnumerator SlotShow(int a, int b, int c, int payout, int stake, bool jackpot)
        {
            int[] final = { a, b, c };
            bool tease = a == b; // the last reel will keep you waiting
            SetReadout("", Color.white);
            SetStatus("SPINNING", Color.white);
            _mood = Mood.Spinning; _bulbFlash = 1f;
            StartCoroutine(PullLever());
            Play(Sfx.Thud, 0.5f, 0.6f);

            var angle = new float[reels.Length];
            var spinning = new bool[reels.Length];
            for (int i = 0; i < reels.Length; i++) { spinning[i] = true; angle[i] = reels[i] ? reels[i].localEulerAngles.x : 0f; }
            float speed = 820f, t = 0f, tick = 0f;
            float[] stopAt = { 1.0f, 1.55f, tease ? 3.9f : 2.1f };

            for (int i = 0; i < reels.Length; i++)
            {
                while (t < stopAt[i])
                {
                    float dt = Time.deltaTime;
                    t += dt;
                    bool teasing = tease && i == 2 && t > 2.1f;
                    if (teasing) { _mood = Mood.Tense; _bulbFlash = 2f; SetStatus("COME ON...", new Color(1f, 0.85f, 0.3f)); }
                    float s = teasing ? Mathf.Lerp(speed, 260f, Mathf.InverseLerp(2.1f, stopAt[2], t)) : speed;
                    for (int r = 0; r < reels.Length; r++) if (spinning[r]) { angle[r] += s * dt; SetReel(r, angle[r]); }
                    tick -= dt;
                    if (tick <= 0f)
                    {
                        float pitch = teasing ? Mathf.Lerp(1.2f, 2.4f, Mathf.InverseLerp(2.1f, stopAt[2], t)) : 1.5f;
                        Play(Sfx.HitTick, 0.25f, pitch);
                        tick = teasing ? Mathf.Lerp(0.09f, 0.22f, Mathf.InverseLerp(2.1f, stopAt[2], t)) : 0.08f;
                    }
                    yield return null;
                }
                // Stop reel i on its symbol with an eased overshoot ("clunk").
                spinning[i] = false;
                int face = FaceOf(final[i]);
                float target = NextAngle(angle[i] + 40f, face * 45f);
                StartCoroutine(StopReel(i, angle[i], target, final[i] == 5));
                angle[i] = target;
            }
            yield return new WaitForSeconds(0.45f);

            // Near miss: the third reel creeps past the match and the machine gasps.
            if (tease && c != a)
            {
                SetStatus("SO CLOSE!", new Color(1f, 0.55f, 0.3f));
                Play(Sfx.Boing, 0.55f, 0.6f);
                _mood = Mood.Tense;
                yield return Wobble(2, angle[2]);
            }

            if (payout > 0)
            {
                float m = payout / (float)Mathf.Max(1, stake);
                _mood = Mood.Happy; _bulbFlash = 2f;
                SetStatus(jackpot ? "JACKPOT!!!" : m >= 10f ? "BIG WIN!" : "WIN!", jackpot ? new Color(1f, 0.85f, 0.2f) : new Color(0.5f, 1f, 0.5f));
                yield return CountUp(payout, jackpot ? 3.2f : Mathf.Lerp(0.6f, 2.2f, Mathf.InverseLerp(1f, 25f, m)));
                Celebrate(m, jackpot);
            }
            else
            {
                _mood = Mood.Sad; _bulbFlash = 0f;
                SetStatus("NO WIN", new Color(1f, 0.45f, 0.4f));
                SetReadout("", Color.white);
                Play(Sfx.Thud, 0.6f, 0.55f);
            }
            yield return new WaitForSeconds(payout > 0 ? 1.2f : 0.8f);
            EndShow();
            yield return new WaitForSeconds(2f);
            if (!Busy) { _mood = Mood.Idle; _bulbFlash = 0f; SetStatus("SLOTS", new Color(1f, 0.85f, 0.3f)); SetReadout("", Color.white); }
        }

        static int FaceOf(int symbol) { for (int k = 0; k < ReelStrip.Length; k++) if (ReelStrip[k] == symbol) return k; return 0; }
        static float NextAngle(float from, float targetMod)
        {
            float t = targetMod + Mathf.Ceil((from - targetMod) / 360f) * 360f;
            return t;
        }

        void SetReel(int i, float angle) { if (reels[i]) reels[i].localRotation = Quaternion.AngleAxis(angle, Vector3.right); }

        IEnumerator StopReel(int i, float from, float to, bool golden)
        {
            float t = 0f;
            const float dur = 0.42f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = EaseOutBack(Mathf.Clamp01(t / dur));
                SetReel(i, Mathf.LerpUnclamped(from, to, k));
                if (reels[i]) reels[i].localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI));
                yield return null;
            }
            SetReel(i, to);
            if (reels[i]) reels[i].localScale = Vector3.one;
            Play(Sfx.Thud, 0.55f, golden ? 1.4f : 0.9f + i * 0.1f);
            if (golden) Play(Sfx.HeadshotDing, 0.4f, 1.6f);
        }

        IEnumerator Wobble(int i, float rest)
        {
            float t = 0f;
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                SetReel(i, rest - Mathf.Sin(t * 22f) * 16f * (1f - t / 0.6f));
                yield return null;
            }
            SetReel(i, rest);
        }

        IEnumerator PullLever()
        {
            if (!lever) yield break;
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                float k = t < 0.18f ? t / 0.18f : 1f - EaseOutBack((t - 0.18f) / 0.32f);
                lever.localRotation = _leverRest * Quaternion.Euler(55f * Mathf.Clamp(k, -0.2f, 1f), 0f, 0f);
                yield return null;
            }
            lever.localRotation = _leverRest;
        }

        // ------------------------------------------------------------------ roulette

        IEnumerator RouletteShow(int number, int option, int payout, int stake)
        {
            SetStatus("NO MORE BETS", Color.white);
            SetReadout($"ON {RouletteNames[option]}", OptionColor(option));
            Play(Sfx.Whoosh, 0.5f, 0.7f);
            int pocket = System.Array.IndexOf(WheelOrder, number);
            float pocketAngle = pocket * 360f / 37f;
            float wheelAngle = wheel ? wheel.localEulerAngles.y : 0f;
            float ballAngle = Random.Range(0f, 360f); // ball angle in WHEEL space
            float wheelSpeed = 300f, ballSpeed = -720f; // degrees per second (ball runs the other way)
            const float total = 6.4f, drop = 4.3f, land = 5.4f;
            float t = 0f, tick = 0f;
            float landFrom = 0f, landTarget = 0f;
            bool landing = false;
            while (t < total)
            {
                float dt = Time.deltaTime;
                t += dt;
                wheelSpeed = Mathf.Lerp(300f, 0f, Mathf.Pow(t / total, 0.8f));
                wheelAngle += wheelSpeed * dt;
                if (wheel) wheel.localRotation = Quaternion.Euler(0f, wheelAngle, 0f);

                float radius = 0.6f, height = 0.07f;
                if (t < drop)
                {
                    ballSpeed = Mathf.Lerp(-720f, -260f, t / drop);
                    ballAngle += (ballSpeed - wheelSpeed) * dt; // relative to the wheel
                }
                else
                {
                    if (!landing)
                    {
                        landing = true;
                        landFrom = ballAngle;
                        landTarget = pocketAngle + Mathf.Floor((ballAngle - pocketAngle) / 360f) * 360f - 360f; // keep going the same way, at least a lap
                    }
                    float k = Mathf.Clamp01((t - drop) / (land - drop));
                    ballAngle = Mathf.Lerp(landFrom, landTarget, 1f - Mathf.Pow(1f - k, 3f));
                    radius = Mathf.Lerp(0.6f, 0.43f, k);
                    height = 0.07f + Mathf.Abs(Mathf.Sin(k * Mathf.PI * 5f)) * 0.06f * (1f - k);
                    tick -= dt;
                    if (tick <= 0f && k < 1f) { Play(Sfx.HitTick, 0.4f, Random.Range(1.6f, 2.1f)); tick = Mathf.Lerp(0.06f, 0.28f, k); }
                }
                if (ball && wheel)
                {
                    var local = Quaternion.Euler(0f, ballAngle, 0f) * new Vector3(0f, height, radius);
                    ball.position = wheel.TransformPoint(local);
                }
                yield return null;
            }

            bool red = Reds.Contains(number);
            int resultOption = number == 0 ? 2 : red ? 0 : 1;
            SetReadout(number == 0 ? "0  GREEN" : $"{number}  {(red ? "RED" : "BLACK")}", OptionColor(resultOption));
            if (payout > 0)
            {
                SetStatus(option == 2 ? "GREEN!!!" : "WINNER!", new Color(0.5f, 1f, 0.5f));
                yield return new WaitForSeconds(0.4f);
                yield return CountUp(payout, option == 2 ? 2.5f : 1f);
                Celebrate(payout / (float)Mathf.Max(1, stake), option == 2);
            }
            else
            {
                SetStatus("HOUSE WINS", new Color(1f, 0.45f, 0.4f));
                Play(Sfx.Thud, 0.6f, 0.6f);
            }
            yield return new WaitForSeconds(1.2f);
            EndShow();
            yield return new WaitForSeconds(2.5f);
            if (!Busy) { SetStatus("ROULETTE", new Color(1f, 0.85f, 0.3f)); SetReadout("PLACE YOUR BETS", Color.white); }
        }

        static Color OptionColor(int option) => option == 0 ? new Color(1f, 0.35f, 0.3f) : option == 1 ? new Color(0.8f, 0.8f, 0.85f) : new Color(0.35f, 1f, 0.45f);

        // ------------------------------------------------------------------ scratchers

        IEnumerator ScratchShow(int packed, int winSym, int payout, int stake)
        {
            var panels = new int[ScratchPanels];
            for (int i = 0; i < ScratchPanels; i++) panels[i] = (packed >> (i * 3)) & 7;
            for (int i = 0; i < foils.Length && i < ScratchPanels; i++)
            {
                if (foils[i]) { foils[i].gameObject.SetActive(true); foils[i].localScale = Vector3.one; foils[i].localRotation = Quaternion.identity; }
                if (i < scratchSymbols.Length && scratchSymbols[i])
                {
                    scratchSymbols[i].text = ScratchNames[panels[i]];
                    scratchSymbols[i].color = ScratchColors[panels[i]];
                    scratchSymbols[i].transform.localScale = Vector3.one;
                }
            }
            SetStatus("SCRATCH!", Color.white);
            SetReadout("E to scratch faster", new Color(1f, 0.95f, 0.85f));
            Play(Sfx.Whoosh, 0.4f, 1.2f);
            var seen = new int[ScratchNames.Length];
            yield return new WaitForSeconds(0.35f);

            for (int i = 0; i < ScratchPanels && i < foils.Length; i++)
            {
                float wait = 1.15f;
                while (wait > 0f && !_advance) { wait -= Time.deltaTime; yield return null; }
                _advance = false;
                yield return PeelFoil(i);
                int s = panels[i];
                seen[s]++;
                if (seen[s] == 3) { Play(Sfx.HeadshotDing, 0.8f, 1f + s * 0.1f); SetStatus($"3x {ScratchNames[s]}!", ScratchColors[s]); }
                else if (seen[s] == 2 && s >= 3) { SetStatus($"TWO {ScratchNames[s]}... ONE MORE!", ScratchColors[s]); Play(Sfx.Squeak, 0.4f, 1.6f); }
            }
            yield return new WaitForSeconds(0.3f);

            if (payout > 0 && winSym >= 0)
            {
                for (int i = 0; i < ScratchPanels && i < scratchSymbols.Length; i++)
                    if (panels[i] == winSym && scratchSymbols[i]) StartCoroutine(Pulse(scratchSymbols[i].transform));
                yield return CountUp(payout, Mathf.Lerp(0.6f, 2.5f, winSym / 4f));
                Celebrate(payout / (float)Mathf.Max(1, stake), winSym == 4);
            }
            else
            {
                SetStatus("NO MATCH", new Color(1f, 0.45f, 0.4f));
                SetReadout("", Color.white);
                Play(Sfx.Thud, 0.6f, 0.6f);
            }
            yield return new WaitForSeconds(1.2f);
            EndShow();
            yield return new WaitForSeconds(2.5f);
            if (!Busy) { SetStatus("LUCKY SCRATCH", new Color(1f, 0.85f, 0.3f)); SetReadout("match 3 to win", new Color(1f, 0.95f, 0.85f)); }
        }

        IEnumerator PeelFoil(int i)
        {
            var f = foils[i];
            Play(Sfx.Whoosh, 0.45f, Random.Range(1.5f, 2.1f));
            if (f)
            {
                float t = 0f;
                while (t < 0.28f)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / 0.28f);
                    f.localRotation = Quaternion.Euler(-120f * k, 0f, Random.Range(-6f, 6f) * (1f - k));
                    f.localScale = new Vector3(1f, 1f - k * 0.9f, 1f);
                    yield return null;
                }
                f.gameObject.SetActive(false);
                SpawnFlecks(f.position, 8, new Color(0.78f, 0.8f, 0.84f));
            }
            if (i < scratchSymbols.Length && scratchSymbols[i]) yield return Punch(scratchSymbols[i].transform, 0.35f);
        }

        // ------------------------------------------------------------------ shared juice

        IEnumerator CountUp(int payout, float duration)
        {
            float t = 0f, tick = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                int shown = Mathf.RoundToInt(payout * (1f - Mathf.Pow(1f - k, 2f)));
                SetReadout($"+${shown:N0}", Color.Lerp(new Color(0.6f, 1f, 0.6f), new Color(1f, 0.9f, 0.3f), k));
                if (readout) readout.transform.localScale = Vector3.one * (1f + 0.15f * k + 0.05f * Mathf.Sin(t * 30f));
                tick -= Time.deltaTime;
                if (tick <= 0f) { Play(Sfx.HitTick, 0.35f, 1f + k * 1.4f); tick = 0.06f; }
                yield return null;
            }
            SetReadout($"+${payout:N0}", new Color(1f, 0.9f, 0.3f));
            if (readout) yield return Punch(readout.transform, 0.5f);
            Play(Sfx.ChaChing, 0.9f, 1f);
        }

        void Celebrate(float multiple, bool huge)
        {
            int coins = huge ? 90 : Mathf.Clamp(Mathf.RoundToInt(multiple * 4f), 6, 60);
            StartCoroutine(CoinShower(coins));
            SpawnFlecks((coinSpout ? coinSpout.position : transform.position) + Vector3.up * 1.2f, huge ? 60 : 18, new Color(1f, 0.85f, 0.3f), confetti: true);
            if (huge) { Play(Sfx.HeadshotDing, 1f, 0.8f); StartCoroutine(DelayedDing()); }
            // A little camera kick for whoever's watching up close.
            var me = FriendSlop.Player.NetworkPlayer.Local;
            if (me && (me.transform.position - transform.position).sqrMagnitude < 12f * 12f)
                me.AddRecoil(huge ? 7f : Mathf.Clamp(multiple * 0.5f, 1f, 4f));
        }

        IEnumerator DelayedDing()
        {
            for (int i = 0; i < 4; i++) { yield return new WaitForSeconds(0.25f); Play(Sfx.HeadshotDing, 0.8f, 1f + i * 0.15f); }
        }

        IEnumerator CoinShower(int count)
        {
            var spout = coinSpout ? coinSpout : transform;
            var list = new List<GameObject>();
            for (int i = 0; i < count; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                c.name = "Coin";
                c.transform.position = spout.position + Random.insideUnitSphere * 0.08f;
                c.transform.localScale = new Vector3(0.12f, 0.012f, 0.12f);
                c.transform.rotation = Random.rotation;
                var r = c.GetComponent<Renderer>();
                if (coinMaterial) r.sharedMaterial = coinMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var rb = c.AddComponent<Rigidbody>();
                rb.mass = 0.02f;
                rb.linearVelocity = spout.forward * Random.Range(1.2f, 3f) + Vector3.up * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 0.8f;
                rb.angularVelocity = Random.insideUnitSphere * 20f;
                list.Add(c);
                if (i % 3 == 0) Play(Sfx.HitTick, 0.18f, Random.Range(2.2f, 2.8f));
                yield return new WaitForSeconds(0.025f);
            }
            yield return new WaitForSeconds(3.5f);
            foreach (var c in list) if (c) Destroy(c);
        }

        void SpawnFlecks(Vector3 at, int count, Color color, bool confetti = false)
        {
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(f.GetComponent<Collider>());
                f.transform.position = at + Random.insideUnitSphere * 0.1f;
                f.transform.localScale = Vector3.one * (confetti ? 0.05f : 0.025f);
                f.transform.rotation = Random.rotation;
                var r = f.GetComponent<Renderer>();
                if (coinMaterial) r.sharedMaterial = coinMaterial;
                Color c = confetti ? Color.HSVToRGB(Random.value, 0.75f, 1f) : color;
                block.SetColor("_BaseColor", c); block.SetColor("_Color", c);
                r.SetPropertyBlock(block);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                StartCoroutine(Fleck(f.transform, Random.insideUnitSphere * (confetti ? 3f : 1.2f) + Vector3.up * (confetti ? 3f : 1f)));
            }
        }

        IEnumerator Fleck(Transform f, Vector3 v)
        {
            float t = 0f, life = Random.Range(0.8f, 1.6f);
            var spin = Random.insideUnitSphere * 600f;
            while (t < life && f)
            {
                t += Time.deltaTime;
                v += Vector3.down * 6f * Time.deltaTime;
                v *= 1f - 1.5f * Time.deltaTime;
                f.position += v * Time.deltaTime;
                f.Rotate(spin * Time.deltaTime);
                yield return null;
            }
            if (f) Destroy(f.gameObject);
        }

        IEnumerator Punch(Transform tr, float amount)
        {
            float t = 0f;
            while (t < 0.35f && tr)
            {
                t += Time.deltaTime;
                tr.localScale = Vector3.one * (1f + amount * Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI) * (1f - t / 0.35f) * 2f);
                yield return null;
            }
            if (tr) tr.localScale = Vector3.one;
        }

        IEnumerator Pulse(Transform tr)
        {
            for (float t = 0f; t < 1.6f && tr; t += Time.deltaTime)
            {
                tr.localScale = Vector3.one * (1f + 0.25f * Mathf.Abs(Mathf.Sin(t * 8f)));
                yield return null;
            }
            if (tr) tr.localScale = Vector3.one;
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        void Play(AudioClip clip, float volume, float pitch) => SfxPlayer.PlayAt(clip, transform.position + Vector3.up, volume, pitch, 3f, 35f);

        void SetStatus(string text, Color color) { if (status) { status.text = text; status.color = color; } }
        void SetReadout(string text, Color color) { if (readout) { readout.text = text; readout.color = color; } }

        // ------------------------------------------------------------------ idle life: bulbs and the face

        void Update()
        {
            _mpb ??= new MaterialPropertyBlock(); // (Awake normally makes it; be safe if Update ever runs first)
            // Marquee bulbs: lazy chase when idle, racing while spinning, strobing on a win or a tease.
            if (bulbs.Length > 0)
            {
                float t = Time.time;
                for (int i = 0; i < bulbs.Length; i++)
                {
                    if (!bulbs[i]) continue;
                    float on;
                    if (_bulbFlash >= 2f) on = Mathf.Repeat(t * 8f, 1f) < 0.5f ? 1f : 0.15f;
                    else if (_bulbFlash >= 1f) on = Mathf.Repeat(t * 12f - i * 0.5f, bulbs.Length * 0.5f) < 1.5f ? 1f : 0.25f;
                    else on = Mathf.Repeat(t * 3f - i * 0.35f, bulbs.Length * 0.35f) < 1.2f ? 1f : 0.35f;
                    var c = Color.Lerp(new Color(0.35f, 0.25f, 0.1f), i % 2 == 0 ? new Color(1f, 0.9f, 0.55f) : new Color(1f, 0.5f, 0.3f), on);
                    _mpb.SetColor("_BaseColor", c);
                    bulbs[i].SetPropertyBlock(_mpb);
                }
            }

            // The machine's face: eyes widen while spinning, bulge on a tease, squint happily on a win, droop on a loss.
            float dt = Time.deltaTime;
            for (int i = 0; i < eyes.Length; i++)
            {
                if (!eyes[i]) continue;
                Vector3 want = _eyeRest[i] * _mood switch
                {
                    Mood.Spinning => 1.15f,
                    Mood.Tense => 1.45f + Mathf.Sin(Time.time * 30f) * 0.05f,
                    _ => 1f,
                };
                if (_mood == Mood.Happy) want = Vector3.Scale(_eyeRest[i], new Vector3(1.1f, 0.35f, 1f));
                if (_mood == Mood.Sad) want = Vector3.Scale(_eyeRest[i], new Vector3(1f, 0.7f, 1f));
                if (_mood == Mood.Idle && Mathf.Repeat(Time.time + i * 0.05f, 4f) < 0.12f) want = Vector3.Scale(_eyeRest[i], new Vector3(1f, 0.1f, 1f)); // blink
                eyes[i].localScale = Vector3.Lerp(eyes[i].localScale, want, 1f - Mathf.Exp(-14f * dt));
            }
            for (int i = 0; i < pupils.Length; i++)
            {
                if (!pupils[i]) continue;
                // Look down at the reels while playing, wander when idle.
                Vector3 look = _mood == Mood.Idle ? new Vector3(Mathf.Sin(Time.time * 0.7f) * 0.25f, Mathf.Sin(Time.time * 0.45f) * 0.15f, 0f)
                             : _mood == Mood.Sad ? new Vector3(0f, -0.3f, 0f) : new Vector3(0f, -0.2f, 0f);
                pupils[i].localPosition = Vector3.Lerp(pupils[i].localPosition, new Vector3(look.x, look.y, 0.42f), 1f - Mathf.Exp(-10f * dt));
            }
            if (mouth)
            {
                Vector3 want = _mood switch
                {
                    Mood.Happy => Vector3.Scale(_mouthRest, new Vector3(1.6f, 2.2f, 1f)),
                    Mood.Tense => Vector3.Scale(_mouthRest, new Vector3(0.6f, 2.6f, 1f)),
                    Mood.Sad => Vector3.Scale(_mouthRest, new Vector3(0.8f, 0.5f, 1f)),
                    _ => _mouthRest,
                };
                mouth.localScale = Vector3.Lerp(mouth.localScale, want, 1f - Mathf.Exp(-12f * dt));
            }
        }
    }
}
