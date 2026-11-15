using System.Collections;
using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Game;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.World
{
    public enum GambleGame { Slots, Roulette, Scratchers }

    /// <summary>
    /// A station in the Lucky Lodge. Toss a carcass onto its pad to bet the animal's sale value:
    ///  • Slots: three reels; any pair pays 1.2×, three of a kind 5×, three golden antlers 25×.
    ///  • Roulette: land the carcass on the RED or BLACK half of the pad (2×) or the green bullseye (0, 14×).
    ///  • Scratchers: three panels; three of a kind pays (Antlers 10×, Bucks 4×, Goose 3×), Skulls lose.
    /// The host decides results; everyone watches the same reels/wheel/panels (sent via <see cref="GameState"/>).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GambleStation : MonoBehaviour
    {
        public static readonly List<GambleStation> All = new();

        public GambleGame game;
        [Tooltip("Unique per station (set by the scene builder); identifies it in network messages.")]
        public int id;
        [Tooltip("Displays: slots = 3 reels; roulette = [result]; scratchers = 3 panels.")]
        public TextMesh[] displays = System.Array.Empty<TextMesh>();
        public TextMesh status;
        [Tooltip("Roulette wheel that spins (optional).")]
        public Transform wheel;

        public static readonly string[] SlotSymbols = { "RABBIT", "DUCK", "GOOSE", "DEER", "MOOSE", "ANTLERS" };
        static readonly int[] SlotWeights = { 30, 25, 20, 13, 8, 4 };
        public static readonly string[] ScratchSymbols = { "BUCKS", "SKULL", "GOOSE", "ANTLERS" };
        static readonly int[] ScratchWeights = { 30, 30, 25, 15 };
        static readonly HashSet<int> Reds = new() { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        public int Index => id;
        bool _busy;
        readonly HashSet<Animal> _taken = new();
        Coroutine _anim;

        void OnEnable() { All.Add(this); GameState.GambleResult += OnResult; }
        void OnDisable() { All.Remove(this); GameState.GambleResult -= OnResult; }
        void Reset() => GetComponent<Collider>().isTrigger = true;

        // ------------------------------------------------------------------ host

        void OnTriggerStay(Collider other)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || _busy || GameState.Instance == null) return;
            var animal = other.GetComponentInParent<Animal>();
            if (animal == null || !animal.IsSpawned || !animal.IsDead.Value || animal.Carry.IsHeld) return;
            if (!_taken.Add(animal)) return;

            int stake = Mathf.Max(1, animal.SaleValue);
            string name = animal.DisplayName;
            Vector3 local = transform.InverseTransformPoint(animal.transform.position);
            animal.NetworkObject.Despawn(true);
            _busy = true;

            int a = 0, b = 0, c = 0;
            float mult = 0f;
            string bet = "";
            switch (game)
            {
                case GambleGame.Slots:
                    a = Pick(SlotWeights); b = Pick(SlotWeights); c = Pick(SlotWeights);
                    if (a == b && b == c) mult = a == 5 ? 25f : 5f;
                    else if (a == b || b == c || a == c) mult = 1.2f;
                    break;
                case GambleGame.Roulette:
                {
                    bool green = new Vector2(local.x, local.z).magnitude < 0.45f;
                    bool red = local.x > 0f; // the left half as you face the station
                    bet = green ? "GREEN" : red ? "RED" : "BLACK";
                    a = Random.Range(0, 37);
                    b = green ? 2 : red ? 0 : 1;
                    if (green ? a == 0 : (a != 0 && Reds.Contains(a) == red)) mult = green ? 14f : 2f;
                    break;
                }
                case GambleGame.Scratchers:
                    a = Pick(ScratchWeights); b = Pick(ScratchWeights); c = Pick(ScratchWeights);
                    if (a == b && b == c) mult = a switch { 3 => 10f, 0 => 4f, 2 => 3f, _ => 0f };
                    break;
            }

            int payout = Mathf.RoundToInt(stake * mult);
            GameState.Instance.ServerGambleResult(Index, a, b, c, payout);
            StartCoroutine(PayAfter(payout, stake, name, bet));
        }

        IEnumerator PayAfter(int payout, int stake, string animalName, string bet)
        {
            yield return new WaitForSeconds(game == GambleGame.Roulette ? 4.2f : 3.2f);
            var state = GameState.Instance;
            string what = game switch { GambleGame.Slots => "SLOTS", GambleGame.Roulette => $"ROULETTE ({bet})", _ => "SCRATCHER" };
            if (state != null)
            {
                if (payout > 0) state.ServerAddMoney(payout, $"{what}: bet a {animalName} (${stake:N0}) and WON ${payout:N0}!");
                else state.ServerAnnounce($"{what}: the {animalName} (${stake:N0}) is gone. The house always wins.");
            }
            _busy = false;
        }

        static int Pick(int[] weights)
        {
            int total = 0; foreach (var w in weights) total += w;
            int r = Random.Range(0, total);
            for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r < 0) return i; }
            return weights.Length - 1;
        }

        void LateUpdate() => _taken.RemoveWhere(x => x == null);

        // ------------------------------------------------------------------ everyone: show it

        void OnResult(int station, int a, int b, int c, int payout)
        {
            if (station != Index) return;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(game switch
            {
                GambleGame.Slots => SpinReels(a, b, c, payout),
                GambleGame.Roulette => SpinWheel(a, payout),
                _ => Scratch(a, b, c, payout),
            });
        }

        IEnumerator SpinReels(int a, int b, int c, int payout)
        {
            int[] final = { a, b, c };
            SetStatus("SPINNING...", Color.white);
            float t = 0f;
            while (t < 3f)
            {
                for (int i = 0; i < displays.Length && i < 3; i++)
                {
                    bool stopped = t > 1.2f + i * 0.7f;
                    displays[i].text = SlotSymbols[stopped ? final[i] : Random.Range(0, SlotSymbols.Length)];
                    displays[i].color = stopped ? new Color(1f, 0.85f, 0.3f) : Color.white;
                }
                if (Mathf.Repeat(t, 0.25f) < Time.deltaTime) FriendSlop.Audio.SfxPlayer.PlayAt(FriendSlop.Audio.Sfx.HitTick, transform.position, 0.35f, 1.6f, 2f, 20f);
                t += Time.deltaTime;
                yield return null;
            }
            Finish(payout);
        }

        IEnumerator SpinWheel(int number, int payout)
        {
            SetStatus("NO MORE BETS", Color.white);
            float t = 0f, speed = 900f;
            while (t < 4f)
            {
                if (wheel) wheel.Rotate(0f, speed * Time.deltaTime, 0f, Space.Self);
                speed = Mathf.Lerp(900f, 30f, t / 4f);
                if (displays.Length > 0) displays[0].text = Random.Range(0, 37).ToString();
                t += Time.deltaTime;
                yield return null;
            }
            if (displays.Length > 0)
            {
                bool red = Reds.Contains(number);
                displays[0].text = number == 0 ? "0 GREEN" : $"{number} {(red ? "RED" : "BLACK")}";
                displays[0].color = number == 0 ? new Color(0.3f, 1f, 0.4f) : red ? new Color(1f, 0.3f, 0.25f) : new Color(0.75f, 0.75f, 0.8f);
            }
            Finish(payout);
        }

        IEnumerator Scratch(int a, int b, int c, int payout)
        {
            int[] final = { a, b, c };
            SetStatus("SCRATCHING...", Color.white);
            for (int i = 0; i < displays.Length && i < 3; i++) { displays[i].text = "?"; displays[i].color = Color.gray; }
            for (int i = 0; i < displays.Length && i < 3; i++)
            {
                yield return new WaitForSeconds(0.9f);
                displays[i].text = ScratchSymbols[final[i]];
                displays[i].color = final[i] == 1 ? new Color(0.8f, 0.3f, 0.3f) : new Color(1f, 0.85f, 0.3f);
                FriendSlop.Audio.SfxPlayer.PlayAt(FriendSlop.Audio.Sfx.Whoosh, transform.position, 0.4f, 1.8f, 2f, 20f);
            }
            yield return new WaitForSeconds(0.4f);
            Finish(payout);
        }

        void Finish(int payout)
        {
            if (payout > 0)
            {
                SetStatus($"WIN ${payout:N0}!", new Color(0.4f, 1f, 0.4f));
                FriendSlop.Audio.SfxPlayer.PlayAt(FriendSlop.Audio.Sfx.HeadshotDing, transform.position, 0.9f, 1.1f, 3f, 40f);
            }
            else
            {
                SetStatus("HOUSE WINS", new Color(1f, 0.4f, 0.35f));
                FriendSlop.Audio.SfxPlayer.PlayAt(FriendSlop.Audio.Sfx.Thud, transform.position, 0.7f, 0.7f, 3f, 30f);
            }
        }

        void SetStatus(string text, Color color)
        {
            if (!status) return;
            status.text = text;
            status.color = color;
        }
    }
}
