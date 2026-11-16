using FriendSlop.Audio;
using FriendSlop.Game;
using FriendSlop.Settings;
using FriendSlop.World;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Fishing
{
    /// <summary>
    /// Fishing with the Fishing Rod (lives on the player; PlayerWeapons hands control here while the rod is out).
    ///
    /// Click at water to cast → the bobber floats, nibbles a few times → a real bite yanks it under ("!"):
    /// click within ~1 s → reel: HOLD to pull the fish in, but the line's tension climbs (faster when the fish
    /// thrashes); LET GO to ease off. Snap the line or let the fish swim back out and it's gone.
    /// The host rolls what's on the hook (where and when you fish changes the odds) and spawns the catch at
    /// your feet, so it can be carried to the trader like a carcass. Right-click reels in early.
    /// </summary>
    public class FishingRod : NetworkBehaviour
    {
        enum State { Idle, Flying, Waiting, Bite, Reeling }

        public static FishingRod Local { get; private set; }

        State _state;
        GameObject _bobber;
        LineRenderer _line;
        Transform _tip;
        Vector3 _castFrom, _castTo, _bobberPos;
        float _surfaceY, _stateTime, _biteAt, _nextNibble, _dip, _reelTick;
        FishWater _water;
        int _kind = -1;
        float _kg, _progress, _tension, _seed;
        string _message;
        bool _aimAtWater;
        float _messageUntil;

        // host: what's on each client's hook right now
        readonly System.Collections.Generic.Dictionary<ulong, (int kind, float kg, float time, Vector3 at)> _pending = new();

        public bool Active => _state != State.Idle;
        static Material _lineMat, _bobberRed, _bobberWhite;

        public override void OnNetworkSpawn() { if (IsOwner) Local = this; }
        public override void OnNetworkDespawn() { if (Local == this) Local = null; Cleanup(); }

        void Say(string text, float seconds = 2.2f) { _message = text; _messageUntil = Time.time + seconds; }

        /// <summary>Owner, every frame while the rod is equipped.</summary>
        public void Tick(bool canUse, Transform tip, Camera cam)
        {
            _tip = tip;
            float dt = Time.deltaTime;
            _stateTime += dt;
            bool pressed = canUse && Controls.Pressed(GameAction.Fire);
            bool held = canUse && Controls.Held(GameAction.Fire);
            if (canUse && Controls.Pressed(GameAction.Aim) && _state != State.Idle) { Cancel(); Say("Reeled in."); return; }

            switch (_state)
            {
                case State.Idle:
                    if (pressed) TryCast(cam);
                    if (cam != null && Time.frameCount % 6 == 0)
                    {
                        _aimAtWater = Physics.Raycast(cam.transform.position, cam.transform.forward, out var h, 28f, ~0, QueryTriggerInteraction.Ignore)
                                      && WaterVolume.At(h.point, out float sy) != null && sy - h.point.y >= 0.25f;
                    }
                    break;

                case State.Flying:
                {
                    float t = Mathf.Clamp01(_stateTime / 0.55f);
                    _bobberPos = Vector3.Lerp(_castFrom, _castTo, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 3f;
                    if (t >= 1f)
                    {
                        _bobberPos = _castTo;
                        Splash(_bobberPos, 0.5f);
                        SfxPlayer.PlayAt(Sfx.Thud, _bobberPos, 0.35f, 2.2f, 2f, 30f);
                        bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
                        float wait = Random.Range(3.5f, 9f) * (Twilight() ? 0.7f : 1f) * (night ? 0.85f : 1f) * (_water == FishWater.FishermansLake ? 0.85f : 1f);
                        _biteAt = wait;
                        _nextNibble = Random.Range(1f, 2.5f);
                        Enter(State.Waiting);
                    }
                    break;
                }

                case State.Waiting:
                    _bobberPos = new Vector3(_castTo.x, _surfaceY + Mathf.Sin(Time.time * 2.4f) * 0.02f - _dip, _castTo.z);
                    _dip = Mathf.MoveTowards(_dip, 0f, dt * 0.6f);
                    if (_stateTime > _nextNibble && _stateTime < _biteAt - 0.8f)
                    {
                        _nextNibble = _stateTime + Random.Range(0.8f, 2.2f);
                        _dip = 0.06f; // fake-out nibble
                        SfxPlayer.PlayAt(Sfx.HitTick, _bobberPos, 0.25f, 0.6f, 2f, 20f);
                    }
                    if (pressed) { Cancel(); Say("Too early! Wait for the bobber to go under."); break; }
                    if (_stateTime >= _biteAt)
                    {
                        _kind = -1;
                        RollRpc(_bobberPos);
                        _dip = 0.22f;
                        Splash(_bobberPos, 0.35f);
                        SfxPlayer.Play2D(Sfx.HeadshotDing, 0.6f, 0.7f);
                        Enter(State.Bite);
                    }
                    break;

                case State.Bite:
                    _bobberPos = new Vector3(_castTo.x + Mathf.Sin(Time.time * 30f) * 0.04f, _surfaceY - 0.18f, _castTo.z);
                    if (pressed)
                    {
                        _progress = 0.3f; _tension = 0.15f; _seed = Random.value * 100f;
                        Enter(State.Reeling);
                    }
                    else if (_stateTime > 1.05f) { Cancel(); Say("It got away... be quicker!"); }
                    break;

                case State.Reeling:
                {
                    float fight = _kind >= 0 ? FishSpecies.Get(_kind).Fight : 0.4f;
                    float thrash = Mathf.Clamp01((Mathf.PerlinNoise(Time.time * 1.3f, _seed) - 0.35f) * 2.2f) * fight;
                    if (held)
                    {
                        _progress += (0.3f - fight * 0.12f) * dt;
                        _tension += (0.32f + fight * 0.35f + thrash * 0.9f) * dt;
                        _reelTick -= dt;
                        if (_reelTick <= 0f) { _reelTick = 0.09f; SfxPlayer.Play2D(Sfx.HitTick, 0.18f, 0.5f + _tension * 0.6f); }
                    }
                    else
                    {
                        _progress -= (0.05f + fight * 0.1f + thrash * 0.18f) * dt;
                        _tension -= 0.75f * dt;
                    }
                    _tension = Mathf.Max(0f, _tension);
                    if (thrash > 0.35f) FriendSlop.Player.NetworkPlayer.Shake(0.02f);
                    // The bobber gets dragged toward you as you win.
                    Vector3 feet = transform.position; feet.y = _surfaceY;
                    _bobberPos = Vector3.Lerp(new Vector3(_castTo.x, _surfaceY, _castTo.z), feet, Mathf.Clamp01(_progress) * 0.8f)
                                 + new Vector3(Mathf.Sin(Time.time * 9f) * 0.12f * thrash, -0.1f, Mathf.Cos(Time.time * 7f) * 0.12f * thrash);
                    if (_tension >= 1f)
                    {
                        SfxPlayer.Play2D(Sfx.Boing, 0.7f, 0.5f);
                        FriendSlop.Player.NetworkPlayer.Shake(0.25f);
                        Cancel(); Say("SNAP! The line broke. Ease off when it thrashes.", 3f);
                    }
                    else if (_progress <= 0f) { Cancel(); Say("It swam off. Keep reeling!", 2.5f); }
                    else if (_progress >= 1f)
                    {
                        LandRpc(_bobberPos);
                        Splash(_bobberPos, 0.8f);
                        Cancel(silent: true);
                    }
                    break;
                }
            }
            UpdateVisuals();
        }

        bool Twilight()
        {
            var c = DayNightCycle.Instance;
            if (c == null) return false;
            float t = c.TimeOfDay;
            return (t > 0.2f && t < 0.32f) || (t > 0.7f && t < 0.82f);
        }

        void TryCast(Camera cam)
        {
            if (cam == null) return;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, 28f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(transform))
            { Say("Cast at a lake or stream (within 28 m)."); return; }
            var vol = WaterVolume.At(hit.point, out float surface);
            if (vol == null || surface - hit.point.y < 0.25f) { Say("That's not water! Aim at a lake or stream."); return; }
            _surfaceY = surface;
            _water = !vol.isLake ? FishWater.Stream : vol.gameObject.name == "FishermansLake" ? FishWater.FishermansLake : FishWater.Lake;
            _castFrom = _tip != null ? _tip.position : cam.transform.position;
            _castTo = new Vector3(hit.point.x, surface, hit.point.z);
            SfxPlayer.Play2D(Sfx.Whoosh, 0.5f, 1.3f);
            EnsureVisuals();
            Enter(State.Flying);
        }

        void Enter(State s) { _state = s; _stateTime = 0f; }

        /// <summary>Dev bridge: cast where the camera looks (as if clicked).</summary>
        public void DevCast(Camera cam) { if (_state == State.Idle) TryCast(cam); }
        /// <summary>Dev bridge: skip the wait and the minigame: hook and land a fish at the bobber (host only).</summary>
        public void DevCatch()
        {
            var at = _bobberPos;
            if (_state == State.Idle)
            {
                // Nearest lake centre, just under its surface.
                float best = float.MaxValue;
                foreach (var w in WaterVolume.All)
                    if (w.isLake && w.points.Length > 0 && (w.points[0] - transform.position).sqrMagnitude < best)
                    { best = (w.points[0] - transform.position).sqrMagnitude; at = w.points[0] + Vector3.down * 0.1f; }
                // Pull it within casting range so the host accepts it.
                if ((at - transform.position).sqrMagnitude > 25f * 25f) { float y = at.y; at = transform.position + (at - transform.position).normalized * 25f; at.y = y; }
            }
            RollRpc(at);
            LandRpc(at);
            Cancel(silent: true);
        }
        public string DevState => $"{_state} t={_stateTime:0.0} kind={_kind} water={_water}";

        /// <summary>Stop fishing (reel the bobber in).</summary>
        public void Cancel(bool silent = false)
        {
            if (_state == State.Idle) return;
            _state = State.Idle;
            _stateTime = 0f;
            if (!silent) SfxPlayer.Play2D(Sfx.Whoosh, 0.25f, 1.8f);
            Cleanup();
        }

        void Cleanup()
        {
            if (_bobber) Destroy(_bobber);
            if (_line) Destroy(_line.gameObject);
            _bobber = null; _line = null;
        }

        // ------------------------------------------------------------------ visuals

        void EnsureVisuals()
        {
            _lineMat ??= new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.9f, 0.9f, 0.85f) };
            _bobberRed ??= new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.9f, 0.15f, 0.1f) };
            _bobberWhite ??= new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.white };
            if (_bobber == null)
            {
                _bobber = new GameObject("Bobber");
                var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(top.GetComponent<Collider>());
                top.transform.SetParent(_bobber.transform, false);
                top.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                top.transform.localScale = new Vector3(0.11f, 0.08f, 0.11f);
                top.GetComponent<Renderer>().sharedMaterial = _bobberRed;
                var bottom = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(bottom.GetComponent<Collider>());
                bottom.transform.SetParent(_bobber.transform, false);
                bottom.transform.localPosition = new Vector3(0f, -0.02f, 0f);
                bottom.transform.localScale = new Vector3(0.1f, 0.08f, 0.1f);
                bottom.GetComponent<Renderer>().sharedMaterial = _bobberWhite;
            }
            if (_line == null)
            {
                var go = new GameObject("FishingLine");
                _line = go.AddComponent<LineRenderer>();
                _line.sharedMaterial = _lineMat;
                _line.positionCount = 14;
                _line.widthMultiplier = 0.01f;
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _line.receiveShadows = false;
            }
        }

        void UpdateVisuals()
        {
            if (_state == State.Idle || _bobber == null || _line == null) return;
            _bobber.transform.position = _bobberPos;
            Vector3 a = _tip != null ? _tip.position : transform.position + Vector3.up * 1.5f;
            float slack = _state == State.Reeling ? 0.05f + (1f - _tension) * 0.2f : _state == State.Flying ? 0.1f : 0.6f;
            for (int i = 0; i < _line.positionCount; i++)
            {
                float t = i / (float)(_line.positionCount - 1);
                var p = Vector3.Lerp(a, _bobberPos, t);
                p.y -= Mathf.Sin(t * Mathf.PI) * slack * Vector3.Distance(a, _bobberPos) * 0.15f;
                _line.SetPosition(i, p);
            }
            _line.startColor = _line.endColor = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.2f), _state == State.Reeling ? _tension : 0f);
        }

        static void Splash(Vector3 at, float size)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "Splash";
            ring.transform.position = at + Vector3.up * 0.02f;
            _bobberWhite ??= new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.white };
            ring.GetComponent<Renderer>().sharedMaterial = _bobberWhite;
            ring.AddComponent<SplashRing>().size = size;
        }

        // ------------------------------------------------------------------ UI

        GUIStyle _hintStyle;

        void OnGUI()
        {
            if (!IsOwner) return;
            _hintStyle ??= new GUIStyle(UiTheme.HudLabel) { fontSize = 17, alignment = TextAnchor.MiddleCenter, richText = true };
            float cx = Screen.width / 2f, y = Screen.height / 2f + 70f;
            string hint = _state switch
            {
                State.Flying => "",
                State.Waiting => "Wait for a bite...  (right-click to reel in)",
                State.Bite => "<color=#FFD34F><b>!! BITE !!  CLICK!</b></color>",
                State.Reeling => "HOLD to reel  ·  LET GO when the line goes red",
                _ => Time.time < _messageUntil ? _message : "",
            };
            if (_state == State.Idle && Time.time >= _messageUntil && FriendSlop.Weapons.PlayerWeapons.Local != null
                && FriendSlop.Weapons.PlayerWeapons.Local.Current != null && FriendSlop.Weapons.PlayerWeapons.Local.Current.kind == FriendSlop.Weapons.WeaponKind.Rod)
                hint = _aimAtWater ? "Click to cast" : "Aim at a lake or stream to cast (the west lake has rare fish)";
            if (!string.IsNullOrEmpty(hint))
            {
                float w = _hintStyle.CalcSize(new GUIContent(hint)).x + 40f;
                var r = new Rect(cx - w / 2f, y, w, 32f);
                UiTheme.Pill(r);
                UiTheme.Shadow(r, hint, _hintStyle);
            }
            if (_state == State.Reeling)
            {
                var p = new Rect(cx - 160f, y + 40f, 320f, 18f);
                UiTheme.Bar(p, _progress, UiTheme.Good, _kind >= 0 ? $"{FishSpecies.Get(_kind).Name}?" : "Reel!", UiTheme.Small);
                var t = new Rect(cx - 160f, y + 62f, 320f, 14f);
                var col = Color.Lerp(new Color(0.5f, 0.8f, 1f), UiTheme.Bad, _tension);
                if (_tension > 0.8f) col = Color.Lerp(col, Color.white, 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * 25f)));
                UiTheme.Bar(t, _tension, col, "line tension", UiTheme.Small);
            }
        }

        // ------------------------------------------------------------------ host

        bool OwnsRod(out FriendSlop.Weapons.PlayerWeapons pw)
        {
            pw = GetComponent<FriendSlop.Weapons.PlayerWeapons>();
            return pw != null && pw.Current != null && pw.Current.kind == FriendSlop.Weapons.WeaponKind.Rod;
        }

        [Rpc(SendTo.Server)]
        void RollRpc(Vector3 bobber, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !OwnsRod(out _)) return;
            if ((bobber - transform.position).sqrMagnitude > 32f * 32f) return;
            var vol = WaterVolume.At(bobber + Vector3.down * 0.4f, out _);
            if (vol == null) return;
            var water = !vol.isLake ? FishWater.Stream : vol.gameObject.name == "FishermansLake" ? FishWater.FishermansLake : FishWater.Lake;
            var cycle = DayNightCycle.Instance;
            bool night = cycle != null && cycle.IsNight;
            float tod = cycle != null ? cycle.TimeOfDay : 0.5f;
            bool twilight = (tod > 0.2f && tod < 0.32f) || (tod > 0.7f && tod < 0.82f);
            var (kind, kg) = FishSpecies.Roll(water, night, twilight);
            _pending[OwnerClientId] = (kind, kg, Time.time, bobber);
            HookedRpc(kind, kg);
        }

        [Rpc(SendTo.Owner)]
        void HookedRpc(int kind, float kg) { _kind = kind; _kg = kg; }

        [Rpc(SendTo.Server)]
        void LandRpc(Vector3 bobber, RpcParams rpcParams = default)
        {
            ulong who = rpcParams.Receive.SenderClientId;
            if (who != OwnerClientId || !_pending.TryGetValue(who, out var hook)) return;
            _pending.Remove(who);
            if (Time.time - hook.time > 60f || !OwnsRod(out _)) return;
            var state = GameState.Instance;
            if (state == null || state.FishPrefab == null) return;
            var sp = FishSpecies.Get(hook.kind);
            // Flops onto the bank right in front of you.
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 at = transform.position + fwd * 1.2f + Vector3.up * 1.2f;
            var go = Instantiate(state.FishPrefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.GetComponent<NetworkObject>().Spawn(true);
            var fish = go.GetComponent<FishCatch>();
            fish.ServerInit(hook.kind, hook.kg);
            if (go.TryGetComponent(out Rigidbody rb)) rb.linearVelocity = -fwd * 1.5f + Vector3.up * 2f;
            string name = GameState.PlayerName(who);
            string line = sp.Junk ? $"{name} reeled in... an old boot." : hook.kind == 6 ? $"{name} caught a GOLDEN CARP ({hook.kg:0.0} kg)! Rare!" : $"{name} caught a {hook.kg:0.0} kg {sp.Name}!";
            state.ServerAnnounce(line);
            state.ServerKillFeed(who, name, "Fishing Rod", GameState.NoPlayer, $"{sp.Name} ({hook.kg:0.0} kg)", hook.kind == 6 ? GameState.Feed.Boss : GameState.Feed.None);
            FriendSlop.Player.PlayerProgress.Of(who)?.ServerCreditCatch(hook.kind, hook.kg);
        }
    }

}
