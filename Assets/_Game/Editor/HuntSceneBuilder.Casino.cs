using FriendSlop.World;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// The Lucky Lodge machines: a slot cabinet with a pull lever, three real reel drums, chasing bulbs and a
    /// face; a roulette table with a 37-pocket wheel, a ball and RED/GREEN/BLACK bet pads; a scratch-card
    /// booth with six peelable foil panels. Each keeps its carcass pad (toss an animal to bet it) and gets a
    /// <see cref="GambleBetSpot"/> for betting team money.
    /// </summary>
    public static partial class HuntSceneBuilder
    {
        static GameObject BetSpot(GameObject go, GambleStation station, int option, string label, string odds)
        {
            var spot = go.AddComponent<GambleBetSpot>();
            spot.station = station; spot.option = option; spot.label = label; spot.odds = odds;
            return go;
        }

        static void LodgeJackpot(Transform lodge)
        {
            var sign = Text3D(lodge, "JACKPOT  $500", new Vector3(0f, 2.62f, -2.72f), 0.05f, new Color(1f, 0.82f, 0.3f));
            sign.gameObject.AddComponent<LodgeJackpotSign>().text = sign;
            Visual(PrimitiveType.Cube, "JackpotBoard", lodge, new Vector3(0f, 2.62f, -2.86f), new Vector3(3.2f, 0.5f, 0.06f), "CasinoBlack");
            for (int i = 0; i < 14; i++)
                Visual(PrimitiveType.Sphere, "JackpotBulb", lodge, new Vector3(-1.5f + i * (3f / 13f), 2.92f, -2.8f), Vector3.one * 0.07f, "Bulb");
        }

        // =========================================================== Slots

        static void BuildSlots(Transform lodge, Vector3 at, int id)
        {
            var m = new GameObject("SlotMachine").transform;
            m.SetParent(lodge, false);
            m.localPosition = at;

            // Cabinet: base, body behind the reels, a chrome-framed reel window, a gold marquee with a face on top.
            Solid(PrimitiveType.Cube, "Base", m, new Vector3(0f, 0.45f, 0f), new Vector3(1.5f, 0.9f, 0.9f), "CasinoRed");
            Solid(PrimitiveType.Cube, "Body", m, new Vector3(0f, 1.5f, -0.17f), new Vector3(1.5f, 1.2f, 0.56f), "CasinoRed");
            Solid(PrimitiveType.Cube, "FrameTop", m, new Vector3(0f, 1.955f, 0.29f), new Vector3(1.5f, 0.29f, 0.4f), "CasinoRed");
            Solid(PrimitiveType.Cube, "FrameBottom", m, new Vector3(0f, 1.045f, 0.29f), new Vector3(1.5f, 0.29f, 0.4f), "CasinoRed");
            for (int s = -1; s <= 1; s += 2)
            {
                Solid(PrimitiveType.Cube, "FrameSide", m, new Vector3(s * 0.69f, 1.5f, 0.29f), new Vector3(0.12f, 0.62f, 0.4f), "CasinoRed");
                Visual(PrimitiveType.Cube, "ChromeEdge", m, new Vector3(s * 0.76f, 1.5f, 0.1f), new Vector3(0.03f, 1.25f, 0.82f), "Chrome");
                Visual(PrimitiveType.Cube, "Divider", m, new Vector3(s * 0.2f, 1.5f, 0.3f), new Vector3(0.035f, 0.62f, 0.38f), "Chrome");
            }
            Visual(PrimitiveType.Cube, "WindowTrimTop", m, new Vector3(0f, 1.82f, 0.495f), new Vector3(1.3f, 0.025f, 0.02f), "Chrome");
            Visual(PrimitiveType.Cube, "WindowTrimBottom", m, new Vector3(0f, 1.18f, 0.495f), new Vector3(1.3f, 0.025f, 0.02f), "Chrome");
            Visual(PrimitiveType.Cube, "Payline", m, new Vector3(0f, 1.5f, 0.49f), new Vector3(1.26f, 0.012f, 0.01f), "Payline");
            Visual(PrimitiveType.Cube, "Marquee", m, new Vector3(0f, 2.36f, -0.1f), new Vector3(1.62f, 0.52f, 0.72f), "Gold");
            Visual(PrimitiveType.Sphere, "Head", m, new Vector3(0f, 2.66f, -0.1f), new Vector3(1.1f, 0.5f, 0.62f), "Gold");

            // Reels: drums turning on their X axis; eight symbol faces round each (face k at k × 45°).
            var reels = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var pivot = new GameObject($"Reel{i}").transform;
                pivot.SetParent(m, false);
                pivot.localPosition = new Vector3(-0.4f + i * 0.4f, 1.5f, 0.18f);
                Visual(PrimitiveType.Cylinder, "Drum", pivot, Vector3.zero, new Vector3(0.56f, 0.17f, 0.56f), "ReelFace").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                for (int k = 0; k < GambleStation.ReelFaces; k++)
                {
                    float phi = k * 45f * Mathf.Deg2Rad;
                    int sym = GambleStation.ReelStrip[k];
                    var face = Text3D(pivot, GambleStation.SlotSymbols[sym], new Vector3(0f, Mathf.Sin(phi) * 0.29f, Mathf.Cos(phi) * 0.29f), 0.0105f, GambleStation.SlotColors[sym] * 0.85f + Color.black * 0.15f);
                    face.transform.localRotation = Quaternion.AngleAxis(-k * 45f, Vector3.right) * Quaternion.Euler(0f, 180f, 0f);
                    face.fontStyle = FontStyle.Bold;
                }
                reels[i] = pivot;
            }

            // Lever on the right: pulled toward you on every bet.
            var lever = new GameObject("Lever").transform;
            lever.SetParent(m, false);
            lever.localPosition = new Vector3(0.84f, 1.35f, 0f);
            Visual(PrimitiveType.Cylinder, "Arm", lever, new Vector3(0f, 0.3f, 0f), new Vector3(0.05f, 0.3f, 0.05f), "Chrome");
            Visual(PrimitiveType.Sphere, "Knob", lever, new Vector3(0f, 0.62f, 0f), Vector3.one * 0.15f, "CasinoRed");
            Visual(PrimitiveType.Cylinder, "Hub", m, new Vector3(0.8f, 1.35f, 0f), new Vector3(0.16f, 0.05f, 0.16f), "Chrome").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Bulbs chasing round the marquee.
            var bulbs = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 9; i++) bulbs.Add(Visual(PrimitiveType.Sphere, "Bulb", m, new Vector3(-0.72f + i * 0.18f, 2.6f, 0.27f), Vector3.one * 0.07f, "Bulb").GetComponent<Renderer>());
            for (int i = 8; i >= 0; i--) bulbs.Add(Visual(PrimitiveType.Sphere, "Bulb", m, new Vector3(-0.72f + i * 0.18f, 2.12f, 0.27f), Vector3.one * 0.07f, "Bulb").GetComponent<Renderer>());

            // A face: it watches the reels, gasps at near misses, grins at wins.
            var eyes = new Transform[2]; var pupils = new Transform[2];
            for (int s = 0; s < 2; s++)
            {
                var eye = new GameObject("Eye").transform;
                eye.SetParent(m, false);
                eye.localPosition = new Vector3(s == 0 ? -0.24f : 0.24f, 2.76f, 0.13f);
                eye.localScale = Vector3.one * 0.2f;
                Visual(PrimitiveType.Sphere, "White", eye, Vector3.zero, Vector3.one, "ReelFace");
                var pupil = Visual(PrimitiveType.Sphere, "Pupil", eye, new Vector3(0f, 0f, 0.42f), new Vector3(0.5f, 0.55f, 0.3f), "CasinoBlack").transform;
                eyes[s] = eye; pupils[s] = pupil;
            }
            var mouth = Visual(PrimitiveType.Cube, "Mouth", m, new Vector3(0f, 2.59f, 0.2f), new Vector3(0.22f, 0.045f, 0.04f), "CasinoBlack").transform;

            // Coin tray and the paytable on the front.
            Visual(PrimitiveType.Cube, "Tray", m, new Vector3(0f, 0.78f, 0.47f), new Vector3(0.62f, 0.07f, 0.18f), "Chrome");
            var spout = new GameObject("CoinSpout").transform;
            spout.SetParent(m, false);
            spout.localPosition = new Vector3(0f, 1.0f, 0.5f);
            var pay = new Color(1f, 0.92f, 0.7f);
            Text3D(m, "3x ANTLERS = 50x + JACKPOT", new Vector3(0f, 0.6f, 0.455f), 0.014f, new Color(1f, 0.82f, 0.25f));
            Text3D(m, "MOOSE 25x  DEER 12x  GOOSE 7x", new Vector3(0f, 0.48f, 0.455f), 0.012f, pay);
            Text3D(m, "DUCK 5x  RABBIT 4x  PAIR 1.3x", new Vector3(0f, 0.37f, 0.455f), 0.012f, pay);
            Text3D(m, "2x ANTLERS 4x", new Vector3(0f, 0.26f, 0.455f), 0.012f, pay);

            var station = Pad(lodge, at, id, GambleGame.Slots, "Gold", out _);
            station.reels = reels; station.lever = lever; station.bulbs = bulbs.ToArray();
            station.eyes = eyes; station.pupils = pupils; station.mouth = mouth;
            station.coinSpout = spout; station.coinMaterial = Mats["Gold"];
            station.status = Text3D(m, "SLOTS", new Vector3(0f, 2.36f, 0.265f), 0.038f, new Color(1f, 0.85f, 0.3f));
            station.readout = Text3D(m, "", new Vector3(0f, 1.955f, 0.495f), 0.03f, Color.white);

            var hit = m.gameObject.AddComponent<BoxCollider>();
            hit.size = new Vector3(1.6f, 2.95f, 1.0f); hit.center = new Vector3(0f, 1.47f, 0f);
            BetSpot(m.gameObject, station, 0, "SLOTS", "");
        }

        // =========================================================== Roulette

        static void BuildRoulette(Transform lodge, Vector3 at, int id)
        {
            var m = new GameObject("Roulette").transform;
            m.SetParent(lodge, false);
            m.localPosition = at;
            Solid(PrimitiveType.Cube, "Table", m, new Vector3(0f, 0.42f, 0f), new Vector3(2.6f, 0.84f, 1.7f), "Plank");
            Visual(PrimitiveType.Cube, "Felt", m, new Vector3(0f, 0.855f, 0f), new Vector3(2.5f, 0.02f, 1.6f), "Felt");
            Visual(PrimitiveType.Cube, "Rail", m, new Vector3(0f, 0.87f, 0.83f), new Vector3(2.6f, 0.06f, 0.05f), "Gold");

            // The wheel: a dark wooden bowl and a spinning head with 37 coloured pockets.
            Visual(PrimitiveType.Cylinder, "Bowl", m, new Vector3(0f, 0.9f, -0.3f), new Vector3(1.5f, 0.05f, 1.5f), "Bark");
            Visual(PrimitiveType.Cylinder, "Track", m, new Vector3(0f, 0.95f, -0.3f), new Vector3(1.38f, 0.01f, 1.38f), "Plank");
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(m, false);
            wheel.localPosition = new Vector3(0f, 0.96f, -0.3f);
            Visual(PrimitiveType.Cylinder, "Head", wheel, Vector3.zero, new Vector3(1.05f, 0.02f, 1.05f), "Gold");
            for (int i = 0; i < GambleStation.WheelOrder.Length; i++)
            {
                int n = GambleStation.WheelOrder[i];
                float a = i * 360f / 37f;
                string mat = n == 0 ? "CasinoGreen" : GambleStation.Reds.Contains(n) ? "CasinoRed" : "CasinoBlack";
                var pocket = Visual(PrimitiveType.Cube, "Pocket", wheel, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.025f, 0.43f), new Vector3(0.068f, 0.02f, 0.17f), mat);
                pocket.transform.localRotation = Quaternion.Euler(0f, a, 0f);
                var label = Text3D(wheel, n.ToString(), Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.04f, 0.47f), 0.0045f, Color.white);
                label.transform.localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(90f, 180f, 0f);
            }
            Visual(PrimitiveType.Sphere, "Cone", wheel, new Vector3(0f, 0.05f, 0f), new Vector3(0.36f, 0.14f, 0.36f), "Gold");
            for (int s = 0; s < 4; s++)
                Visual(PrimitiveType.Cube, "Spoke", wheel, new Vector3(0f, 0.08f, 0f), new Vector3(0.04f, 0.03f, 0.42f), "Chrome").transform.localRotation = Quaternion.Euler(0f, s * 45f, 0f);
            var ball = Visual(PrimitiveType.Sphere, "Ball", m, new Vector3(0f, 1.03f, 0.3f), Vector3.one * 0.06f, "ReelFace").transform;

            // Bet pads on the felt (look at one and press E): RED, GREEN, BLACK.
            var station = Pad(lodge, at + new Vector3(0f, 0f, 0.4f), id, GambleGame.Roulette, null, out var pad);
            (string label, string mat, float x, int option, string odds)[] bets =
                { ("RED", "CasinoRed", -0.85f, 0, "pays 2x"), ("GREEN", "CasinoGreen", 0f, 2, "pays 33x"), ("BLACK", "CasinoBlack", 0.85f, 1, "pays 2x") };
            foreach (var b in bets)
            {
                var spot = Solid(PrimitiveType.Cube, $"Bet_{b.label}", m, new Vector3(b.x, 0.88f, 0.52f), new Vector3(b.option == 2 ? 0.5f : 0.75f, 0.03f, 0.42f), b.mat);
                BetSpot(spot, station, b.option, b.label, b.odds);
                var t = Text3D(m, b.label, new Vector3(b.x, 0.9f, 0.52f), 0.016f, Color.white);
                t.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
            }
            // The carcass pad in front: land on the RED half, BLACK half or green bullseye.
            Visual(PrimitiveType.Cube, "PadRed", pad, new Vector3(0.45f, 0.08f, 0f), new Vector3(0.9f, 0.06f, 1.8f), "CasinoRed");
            Visual(PrimitiveType.Cube, "PadBlack", pad, new Vector3(-0.45f, 0.08f, 0f), new Vector3(0.9f, 0.06f, 1.8f), "CasinoBlack");
            Visual(PrimitiveType.Cylinder, "PadGreen", pad, new Vector3(0f, 0.1f, 0f), new Vector3(0.9f, 0.03f, 0.9f), "CasinoGreen");
            Text3D(pad, "RED", new Vector3(0.5f, 0.2f, -0.75f), 0.05f, Color.white);
            Text3D(pad, "BLACK", new Vector3(-0.5f, 0.2f, -0.75f), 0.05f, Color.white);

            var spout = new GameObject("CoinSpout").transform;
            spout.SetParent(m, false);
            spout.localPosition = new Vector3(0f, 1.1f, -0.3f);
            station.wheel = wheel; station.ball = ball;
            station.coinSpout = spout; station.coinMaterial = Mats["Gold"];
            station.readout = Text3D(m, "PLACE YOUR BETS", new Vector3(0f, 1.5f, -0.3f), 0.03f, Color.white);
            station.status = Text3D(m, "ROULETTE", new Vector3(0f, 1.85f, -0.3f), 0.045f, new Color(1f, 0.85f, 0.3f));
            for (int i = 0; i < 12; i++)
                Visual(PrimitiveType.Sphere, "Bulb", m, new Vector3(-1.1f + i * 0.2f, 2.12f, -0.3f), Vector3.one * 0.06f, "Bulb");
        }

        // =========================================================== Scratchers

        static void BuildScratchers(Transform lodge, Vector3 at, int id)
        {
            var m = new GameObject("Scratchers").transform;
            m.SetParent(lodge, false);
            m.localPosition = at;
            Solid(PrimitiveType.Cube, "Booth", m, new Vector3(0f, 0.5f, 0f), new Vector3(1.8f, 1.0f, 0.8f), "CasinoGreen");
            Visual(PrimitiveType.Cube, "Counter", m, new Vector3(0f, 1.02f, 0.05f), new Vector3(1.9f, 0.05f, 0.9f), "Gold");
            Solid(PrimitiveType.Cube, "Easel", m, new Vector3(0f, 1.35f, -0.25f), new Vector3(0.12f, 0.7f, 0.06f), "Plank");

            // The big card: white with a gold border, six foil panels (3 × 2) with prizes underneath.
            Visual(PrimitiveType.Cube, "Card", m, new Vector3(0f, 1.78f, -0.2f), new Vector3(1.5f, 0.95f, 0.03f), "ReelFace");
            Visual(PrimitiveType.Cube, "BorderTop", m, new Vector3(0f, 2.26f, -0.19f), new Vector3(1.56f, 0.05f, 0.04f), "Gold");
            Visual(PrimitiveType.Cube, "BorderBottom", m, new Vector3(0f, 1.3f, -0.19f), new Vector3(1.56f, 0.05f, 0.04f), "Gold");
            for (int s = -1; s <= 1; s += 2)
                Visual(PrimitiveType.Cube, "BorderSide", m, new Vector3(s * 0.77f, 1.78f, -0.19f), new Vector3(0.05f, 1.0f, 0.04f), "Gold");
            var foils = new Transform[GambleStation.ScratchPanels];
            var symbols = new TextMesh[GambleStation.ScratchPanels];
            for (int i = 0; i < GambleStation.ScratchPanels; i++)
            {
                float x = -0.46f + (i % 3) * 0.46f, y = i < 3 ? 1.92f : 1.6f;
                Visual(PrimitiveType.Cube, "Well", m, new Vector3(x, y, -0.18f), new Vector3(0.4f, 0.26f, 0.01f), "Felt");
                symbols[i] = Text3D(m, "?", new Vector3(x, y, -0.172f), 0.012f, Color.white);
                symbols[i].fontStyle = FontStyle.Bold;
                var pivot = new GameObject($"Foil{i}").transform;
                pivot.SetParent(m, false);
                pivot.localPosition = new Vector3(x, y + 0.135f, -0.166f);
                Visual(PrimitiveType.Cube, "FoilSheet", pivot, new Vector3(0f, -0.135f, 0f), new Vector3(0.42f, 0.27f, 0.012f), "Foil");
                Text3D(pivot, "SCRATCH", new Vector3(0f, -0.135f, 0.008f), 0.007f, new Color(0.45f, 0.45f, 0.5f));
                foils[i] = pivot;
            }
            Text3D(m, "BUCKS 1x  GOOSE 2x  DEER 4x  BEAR 10x  ANTLERS 40x", new Vector3(0f, 1.36f, -0.175f), 0.0085f, new Color(0.25f, 0.2f, 0.15f));

            var station = Pad(lodge, at, id, GambleGame.Scratchers, "CasinoGreen", out _);
            var spout = new GameObject("CoinSpout").transform;
            spout.SetParent(m, false);
            spout.localPosition = new Vector3(0f, 1.2f, 0.4f);
            station.foils = foils; station.scratchSymbols = symbols;
            station.coinSpout = spout; station.coinMaterial = Mats["Gold"];
            station.status = Text3D(m, "LUCKY SCRATCH", new Vector3(0f, 2.45f, -0.2f), 0.036f, new Color(1f, 0.85f, 0.3f));
            station.readout = Text3D(m, "match 3 to win", new Vector3(0f, 1.12f, 0.46f), 0.014f, new Color(1f, 0.95f, 0.85f));

            var hit = m.gameObject.AddComponent<BoxCollider>();
            hit.size = new Vector3(1.9f, 2.6f, 0.95f); hit.center = new Vector3(0f, 1.3f, 0f);
            BetSpot(m.gameObject, station, 0, "a SCRATCH CARD", "match 3 to win");
        }
    }
}
