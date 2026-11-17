using System.Collections.Generic;
using FriendSlop.Settings;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Proximity voice chat (Lethal-Company style): your voice comes out of your hunter's head and fades with distance.
    ///
    /// Owner: records the default mic at 16 kHz, packs 20 ms frames as 8-bit µ-law (~16 KB/s while talking) and sends
    /// batches of three frames unreliably through the host to everyone else. Push-to-talk (V) by default; open mic
    /// uses a simple loudness gate. Muting (N) stops sending entirely.
    /// Everyone else: a small jitter buffer feeds a streaming, fully 3D AudioSource on that player.
    /// </summary>
    public class VoiceChat : NetworkBehaviour
    {
        const int Rate = 16000;
        const int Frame = 320;               // 20 ms
        const int FramesPerPacket = 3;       // 60 ms per message
        const float OpenMicGate = 0.012f;    // RMS needed to open the mic in open-mic mode
        const float GateHold = 0.35f;        // keep sending this long after you stop talking

        static readonly Dictionary<ulong, float> _lastHeard = new Dictionary<ulong, float>();
        /// <summary>True if this player's voice was heard in the last moment (for the HUD's speaker list).</summary>
        /// <summary>Players this client has muted (local only).</summary>
        public static readonly HashSet<ulong> MutedPlayers = new HashSet<ulong>();
        static readonly Dictionary<ulong, float> _level = new Dictionary<ulong, float>();
        static float _localLevel;
        /// <summary>0..1 loudness of a player's voice right now (drives their head-bob). Works for the local player too.</summary>
        public static float Level(ulong clientId)
        {
            if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId) return LocalTransmitting ? _localLevel : 0f;
            return IsSpeaking(clientId) && _level.TryGetValue(clientId, out float l) ? l : 0f;
        }
        public static bool IsSpeaking(ulong clientId) => _lastHeard.TryGetValue(clientId, out float t) && Time.unscaledTime - t < 0.3f;
        public static bool LocalTransmitting { get; private set; }
        public static bool HasMic => Microphone.devices.Length > 0;

        // owner
        AudioClip _mic;
        string _device;
        int _readPos;
        readonly List<float> _pending = new List<float>(Frame * 4);
        readonly List<byte> _packet = new List<byte>(Frame * FramesPerPacket);
        float _gateUntil;

        // listeners
        AudioSource _source;
        AudioLowPassFilter _lowPass;
        AudioHighPassFilter _highPass;
        AudioDistortionFilter _distortion;
        /// <summary>True while we're hearing this player over walkie-talkie radio.</summary>
        public bool InRadio { get; private set; }
        readonly float[] _ring = new float[Rate * 2];
        int _ringWrite, _ringRead, _ringCount;
        readonly object _lock = new object();
        bool _primed;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) StartMic();
            else BuildSpeaker();
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && _device != null && Microphone.IsRecording(_device)) Microphone.End(_device);
            LocalTransmitting = false;
        }

        // ------------------------------------------------------------------ owner: capture + send

        void StartMic()
        {
            if (!HasMic) { Debug.Log("[Voice] no microphone found; voice chat is receive-only."); return; }
            _device = Microphone.devices[0];
            Microphone.GetDeviceCaps(_device, out int minF, out int maxF);
            int freq = (minF == 0 && maxF == 0) ? Rate : Mathf.Clamp(Rate, minF, maxF);
            _mic = Microphone.Start(_device, true, 1, freq);
            _readPos = 0;
            Debug.Log($"[Voice] mic '{_device}' at {freq} Hz");
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (!IsOwner)
            {
                if (_source)
                {
                    _source.volume = GameSettings.VoiceVolume;
                    _source.mute = MutedPlayers.Contains(OwnerClientId);
                    // Glub glub: voices sound muffled when the speaker's (or your) head is underwater.
                    var listener = FriendSlop.Player.NetworkPlayer.Local;
                    bool wet = FriendSlop.World.WaterVolume.DepthAt(transform.position + Vector3.up * 1.5f) > 0.05f
                               || (listener != null && FriendSlop.World.WaterVolume.DepthAt(listener.transform.position + Vector3.up * 1.5f) > 0.05f);
                    // Walkie-talkies: if we both have one and they're far away, hear them over crackly radio instead of not at all.
                    bool radio = false;
                    if (listener != null && TryGetComponent(out FriendSlop.Player.PlayerUpgrades theirs) && theirs.HasWalkie
                        && listener.TryGetComponent(out FriendSlop.Player.PlayerUpgrades mine) && mine.HasWalkie)
                        radio = (listener.transform.position - transform.position).sqrMagnitude > 30f * 30f;
                    InRadio = radio;
                    _source.spatialBlend = radio ? 0f : 1f;
                    if (_highPass) _highPass.enabled = radio;
                    if (_distortion) _distortion.enabled = radio;
                    float cutoff = wet ? 550f : radio ? 3400f : 22000f;
                    if (_lowPass) _lowPass.cutoffFrequency = Mathf.Lerp(_lowPass.cutoffFrequency, cutoff, Time.deltaTime * 8f);
                }
                return;
            }
            if (_mic == null) { LocalTransmitting = false; return; }

            int pos = Microphone.GetPosition(_device);
            if (pos < 0 || pos == _readPos) return;
            int len = _mic.samples;
            int count = (pos - _readPos + len) % len;
            if (count <= 0) return;
            var buf = new float[count];
            // GetData wraps around the looping clip for us when we read from _readPos.
            _mic.GetData(buf, _readPos);
            _readPos = pos;
            // Resample if the device didn't give us 16 kHz.
            if (_mic.frequency != Rate) buf = Resample(buf, _mic.frequency, Rate);

            bool ptt = GameSettings.PushToTalk;
            bool want = !GameSettings.MicMuted && (!ptt || Controls.Held(GameAction.PushToTalk)) && !FriendSlop.Game.GameHud.ChatOpen;
            if (want && !ptt)
            {
                // open mic: only send while it's loud enough (plus a short hold so words don't get clipped)
                float rms = 0f;
                foreach (var s in buf) rms += s * s;
                rms = Mathf.Sqrt(rms / buf.Length);
                if (rms > OpenMicGate) _gateUntil = Time.unscaledTime + GateHold;
                want = Time.unscaledTime < _gateUntil;
            }
            LocalTransmitting = want;
            if (want)
            {
                float r = 0f; foreach (var s in buf) r += s * s;
                _localLevel = Mathf.Clamp01(Mathf.Sqrt(r / Mathf.Max(1, buf.Length)) * 12f);
            }
            if (!want) { _pending.Clear(); _packet.Clear(); return; }

            _pending.AddRange(buf);
            while (_pending.Count >= Frame)
            {
                for (int i = 0; i < Frame; i++) _packet.Add(MuLawEncode(_pending[i]));
                _pending.RemoveRange(0, Frame);
                if (_packet.Count >= Frame * FramesPerPacket)
                {
                    VoiceRpc(_packet.ToArray());
                    _packet.Clear();
                }
            }
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        void VoiceRpc(byte[] data)
        {
            _lastHeard[OwnerClientId] = Time.unscaledTime;
            float rms = 0f;
            foreach (byte b in data) { float v = MuLawDecode(b); rms += v * v; }
            _level[OwnerClientId] = Mathf.Clamp01(Mathf.Sqrt(rms / Mathf.Max(1, data.Length)) * 12f);
            if (_source == null) BuildSpeaker();
            lock (_lock)
            {
                foreach (byte b in data)
                {
                    if (_ringCount >= _ring.Length) { _ringRead = (_ringRead + 1) % _ring.Length; _ringCount--; } // overflow: drop oldest
                    _ring[_ringWrite] = MuLawDecode(b);
                    _ringWrite = (_ringWrite + 1) % _ring.Length;
                    _ringCount++;
                }
                if (_ringCount > Rate / 10) _primed = true; // ~100 ms of jitter buffer before we start playing
            }
        }

        // ------------------------------------------------------------------ listeners: playback

        void BuildSpeaker()
        {
            if (_source != null) return;
            var head = new GameObject("Voice").transform;
            head.SetParent(transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            _source = head.gameObject.AddComponent<AudioSource>();
            _source.clip = AudioClip.Create($"Voice_{OwnerClientId}", Rate, 1, Rate, true, OnAudioRead);
            _source.loop = true;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 4f;
            _source.maxDistance = 45f;
            _source.dopplerLevel = 0f;
            _source.volume = GameSettings.VoiceVolume;
            _lowPass = head.gameObject.AddComponent<AudioLowPassFilter>();
            _lowPass.cutoffFrequency = 22000f;
            _highPass = head.gameObject.AddComponent<AudioHighPassFilter>();
            _highPass.cutoffFrequency = 700f;
            _highPass.enabled = false;
            _distortion = head.gameObject.AddComponent<AudioDistortionFilter>();
            _distortion.distortionLevel = 0.55f;
            _distortion.enabled = false;
            _source.Play();
        }

        void OnAudioRead(float[] data)
        {
            lock (_lock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (_primed && _ringCount > 0)
                    {
                        data[i] = _ring[_ringRead];
                        _ringRead = (_ringRead + 1) % _ring.Length;
                        _ringCount--;
                    }
                    else { data[i] = 0f; _primed = false; }
                }
            }
        }

        // ------------------------------------------------------------------ codec

        static float[] Resample(float[] src, int from, int to)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(src.Length * (float)to / from));
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                float x = i * (float)from / to;
                int a = Mathf.Min((int)x, src.Length - 1), b = Mathf.Min(a + 1, src.Length - 1);
                dst[i] = Mathf.Lerp(src[a], src[b], x - a);
            }
            return dst;
        }

        const int Bias = 0x84, Clip = 32635;

        static byte MuLawEncode(float f)
        {
            int s = Mathf.Clamp((int)(f * 32767f), -32767, 32767);
            int sign = (s >> 8) & 0x80;
            if (sign != 0) s = -s;
            if (s > Clip) s = Clip;
            s += Bias;
            int exp = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exp > 0; mask >>= 1) exp--;
            int mant = (s >> (exp + 3)) & 0x0F;
            return (byte)~(sign | (exp << 4) | mant);
        }

        static float MuLawDecode(byte b)
        {
            int u = ~b & 0xFF;
            int sign = u & 0x80, exp = (u >> 4) & 0x07, mant = u & 0x0F;
            int s = ((mant << 3) + Bias) << exp;
            s -= Bias;
            return (sign != 0 ? -s : s) / 32768f;
        }
    }
}
