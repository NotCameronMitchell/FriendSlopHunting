using System.Collections.Generic;
using FriendSlop.Animals;
using FriendSlop.Player;
using FriendSlop.Settings;
using FriendSlop.World;
using UnityEngine;

namespace FriendSlop.Game
{
    /// <summary>
    /// Toggleable map (M by default). The map image is painted once from the real terrain:
    /// ground types, water, hill shading, contour lines and tree dots, on a paper-ish border.
    /// On top: camp buildings, landmarks, boss totems, the tank, every hunter (you get a facing
    /// arrow) and any boss on the loose.
    /// </summary>
    public class MapOverlay : MonoBehaviour
    {
        const int Res = 512;

        public static bool IsOpen { get; set; }

        Texture2D _map;
        Rect _worldRect; // x, z min and size of the terrain
        readonly List<(Vector3 pos, string label, Color color, string icon)> _landmarks = new List<(Vector3, string, Color, string)>();
        GUIStyle _label, _icon, _title;

        void Update()
        {
            var state = GameState.Instance;
            if (state == null || !state.IsSpawned) { IsOpen = false; return; }
            if (GameMenus.Open != GameMenus.Menu.None) { IsOpen = false; return; }
            if (Controls.Pressed(GameAction.Map)) IsOpen = !IsOpen;
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            if (_map == null && !Build()) { IsOpen = false; return; }
            GUI.skin = UiTheme.Skin;
            _label ??= new GUIStyle(UiTheme.Small) { fontSize = 12, alignment = TextAnchor.UpperCenter };
            _icon ??= new GUIStyle(UiTheme.Center) { fontSize = 18 };
            _title ??= new GUIStyle(UiTheme.Title) { fontSize = 22, alignment = TextAnchor.MiddleCenter };

            float size = Mathf.Min(Screen.height - 120f, Screen.width - 120f, 820f);
            var frame = new Rect((Screen.width - size) / 2f - 14f, (Screen.height - size) / 2f - 34f, size + 28f, size + 62f);
            UiTheme.Panel(frame);
            GUI.Label(new Rect(frame.x, frame.y + 4, frame.width, 30), "MAP", _title);
            var map = new Rect((Screen.width - size) / 2f, frame.y + 36f, size, size);
            GUI.DrawTexture(map, _map);

            var prev = GUI.color;
            foreach (var lm in _landmarks) Marker(map, lm.pos, lm.icon, lm.label, lm.color);

            foreach (var t in FindObjectsByType<BossTotem>(FindObjectsSortMode.None))
            {
                if (t.BossSpecies == null) continue;
                string title = t.BossSpecies.bossTitle;
                int cut = title.IndexOf(" the ", System.StringComparison.Ordinal);
                string who = cut > 0 ? title.Substring(0, cut) : title;
                bool byLandmark = false; // a landmark already labels this spot: just say whose totem it is, a line lower
                foreach (var lm in _landmarks) if ((lm.pos - t.transform.position).sqrMagnitude < 15f * 15f && lm.icon == "✦") byLandmark = true;
                Marker(map, t.transform.position, "♜", byLandmark ? null : who + " totem", new Color(1f, 0.45f, 0.35f));
                if (byLandmark) UiTheme.Shadow(new Rect(ToMap(map, t.transform.position).x - 70, ToMap(map, t.transform.position).y + 24, 140, 18), $"({who} totem)", _label);
            }
            foreach (var tank in Tank.All) if (tank) Marker(map, tank.transform.position, "▣", "Tank", new Color(0.65f, 0.8f, 0.4f));
            foreach (var a in Animal.All)
                if (a != null && a.IsSpawned && a.Species != null && a.Species.isBoss && !a.IsDead.Value)
                    Marker(map, a.transform.position, "☠", a.Species.bossTitle, new Color(1f, 0.25f, 0.2f));

            foreach (var p in NetworkPlayer.All)
            {
                if (p == null || !p.IsSpawned) continue;
                bool me = p == NetworkPlayer.Local;
                var at = ToMap(map, p.transform.position);
                if (me)
                {
                    // Facing arrow for you.
                    float yaw = p.transform.eulerAngles.y;
                    var m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(yaw, at);
                    GUI.color = new Color(0f, 0f, 0f, 0.6f);
                    GUI.Label(new Rect(at.x - 14 + 1.5f, at.y - 16 + 1.5f, 28, 28), "▲", _icon);
                    GUI.color = UiTheme.Gold;
                    GUI.Label(new Rect(at.x - 14, at.y - 16, 28, 28), "▲", _icon);
                    GUI.matrix = m;
                    GUI.color = prev;
                    UiTheme.Shadow(new Rect(at.x - 60, at.y + 10, 120, 18), "You", _label);
                }
                else
                {
                    GUI.color = new Color(0.45f, 0.85f, 1f);
                    GUI.Label(new Rect(at.x - 10, at.y - 12, 20, 22), "●", _icon);
                    GUI.color = prev;
                    UiTheme.Shadow(new Rect(at.x - 60, at.y + 8, 120, 18), GameState.PlayerName(p.OwnerClientId), _label);
                }
            }
            GUI.color = prev;

            var help = new GUIStyle(UiTheme.Small) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            help.normal.textColor = UiTheme.Muted;
            GUI.Label(new Rect(frame.x, frame.yMax - 24, frame.width, 20), $"{Controls.Label(GameAction.Map)} to close  ·  north is up", help);
        }

        void Marker(Rect map, Vector3 world, string icon, string label, Color color)
        {
            var at = ToMap(map, world);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Label(new Rect(at.x - 12 + 1.5f, at.y - 13 + 1.5f, 24, 24), icon, _icon);
            GUI.color = color;
            GUI.Label(new Rect(at.x - 12, at.y - 13, 24, 24), icon, _icon);
            GUI.color = prev;
            if (!string.IsNullOrEmpty(label)) UiTheme.Shadow(new Rect(at.x - 70, at.y + 9, 140, 18), label, _label);
        }

        Vector2 ToMap(Rect map, Vector3 world)
        {
            float u = Mathf.Clamp01((world.x - _worldRect.x) / _worldRect.width);
            float v = Mathf.Clamp01((world.z - _worldRect.y) / _worldRect.height);
            return new Vector2(map.x + u * map.width, map.y + (1f - v) * map.height);
        }

        // ------------------------------------------------------------------ painting

        bool Build()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return false;
            var td = terrain.terrainData;
            var origin = terrain.transform.position;
            _worldRect = new Rect(origin.x, origin.z, td.size.x, td.size.z);

            int hr = td.heightmapResolution;
            var heights = td.GetHeights(0, 0, hr, hr);
            var splat = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
            int layers = splat.GetLength(2);
            // grass, forest floor, dirt, rock, sand, snow (TerrainBuilder's layer order)
            Color[] layerCol =
            {
                new Color(0.55f, 0.7f, 0.36f), new Color(0.4f, 0.55f, 0.3f), new Color(0.72f, 0.6f, 0.42f),
                new Color(0.6f, 0.58f, 0.56f), new Color(0.88f, 0.8f, 0.6f), new Color(0.96f, 0.97f, 1f),
            };
            Color water = new Color(0.36f, 0.6f, 0.82f), waterDeep = new Color(0.25f, 0.45f, 0.72f);
            Color paper = new Color(0.95f, 0.9f, 0.78f);

            float H(int x, int y) => heights[Mathf.Clamp(y, 0, hr - 1), Mathf.Clamp(x, 0, hr - 1)] * td.size.y + origin.y;

            var px = new Color[Res * Res];
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float u = i / (float)(Res - 1), v = j / (float)(Res - 1);
                    int hx = Mathf.RoundToInt(u * (hr - 1)), hy = Mathf.RoundToInt(v * (hr - 1));
                    float h = H(hx, hy);

                    int sx = Mathf.Clamp(Mathf.RoundToInt(u * (td.alphamapWidth - 1)), 0, td.alphamapWidth - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt(v * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
                    Color c = Color.black;
                    for (int l = 0; l < layers && l < layerCol.Length; l++) c += layerCol[l] * splat[sy, sx, l];
                    c.a = 1f;

                    // Hill shading from the slope (light from the north-west), and contour lines every 4 m.
                    float dx = H(hx + 1, hy) - H(hx - 1, hy), dz = H(hx, hy + 1) - H(hx, hy - 1);
                    float shade = Mathf.Clamp(1f + (-dx + dz) * 0.06f, 0.6f, 1.25f);
                    c *= shade;
                    if (h > Ground.WaterY + 0.2f)
                    {
                        float band = Mathf.Repeat(h, 4f);
                        if (band < 0.35f) c = Color.Lerp(c, new Color(0.35f, 0.28f, 0.2f), 0.28f);
                    }
                    if (h < Ground.WaterY - 0.05f) c = Color.Lerp(water, waterDeep, Mathf.Clamp01((Ground.WaterY - h) / 1.4f));
                    c.a = 1f;
                    px[j * Res + i] = c;
                }

            // Tree dots.
            var trees = GameObject.Find("Trees");
            if (trees != null)
                foreach (Transform t in trees.transform)
                {
                    int ci = Mathf.RoundToInt((t.position.x - _worldRect.x) / _worldRect.width * (Res - 1));
                    int cj = Mathf.RoundToInt((t.position.z - _worldRect.y) / _worldRect.height * (Res - 1));
                    for (int oy = -2; oy <= 2; oy++)
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            if (ox * ox + oy * oy > 5) continue;
                            int x = ci + ox, y = cj + oy;
                            if (x < 0 || y < 0 || x >= Res || y >= Res) continue;
                            px[y * Res + x] = Color.Lerp(px[y * Res + x], new Color(0.18f, 0.36f, 0.18f), 0.75f);
                        }
                }

            // Paper border with a soft vignette.
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float e = Mathf.Min(Mathf.Min(i, j), Mathf.Min(Res - 1 - i, Res - 1 - j));
                    if (e < 6) px[j * Res + i] = paper * 0.85f;
                    else if (e < 22) px[j * Res + i] = Color.Lerp(px[j * Res + i], paper, (22 - e) / 40f);
                }

            _map = new Texture2D(Res, Res, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _map.SetPixels(px);
            _map.Apply();

            // Fixed landmarks found in the scene.
            _landmarks.Clear();
            var camp = new Color(1f, 0.85f, 0.45f);
            foreach (var g in FindObjectsByType<GunShop>(FindObjectsSortMode.None)) { _landmarks.Add((g.transform.position, "Gun Shop", camp, "⌂")); break; }
            foreach (var s in FindObjectsByType<SellZone>(FindObjectsSortMode.None)) { _landmarks.Add((s.transform.position, "Trader", camp, "$")); break; }
            foreach (var b in FindObjectsByType<Bed>(FindObjectsSortMode.None)) { _landmarks.Add((b.transform.position, "Cabin", camp, "⌂")); break; }
            foreach (var gs in FindObjectsByType<GambleStation>(FindObjectsSortMode.None)) { _landmarks.Add((gs.transform.position, "Lucky Lodge", camp, "♣")); break; }
            var fire = GameObject.Find("Campfire");
            if (fire) _landmarks.Add((fire.transform.position, "", new Color(1f, 0.55f, 0.2f), "♨"));
            var garage = GameObject.Find("Garage");
            if (garage) _landmarks.Add((garage.transform.position, "Garage", camp, "⌂"));
            foreach (var name in new[] { ("HogWallow", "Hog Wallow"), ("RangerTower", "Ranger Tower"), ("AbandonedCamp", "Abandoned Camp") })
            {
                var go = GameObject.Find(name.Item1);
                if (go) _landmarks.Add((go.transform.position, name.Item2, new Color(0.95f, 0.95f, 0.85f), "✦"));
            }
            return true;
        }
    }
}
