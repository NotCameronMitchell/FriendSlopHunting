using FriendSlop.Profile;
using FriendSlop.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    /// <summary>
    /// What everyone sees of you: name tag, hat, accessory and body color, taken from your saved
    /// profile when you join. Owner-written network variables; every peer builds the look locally.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerAppearance : NetworkBehaviour
    {
        [SerializeField] Material nameTagMaterial;

        public readonly NetworkVariable<FixedString32Bytes> DisplayName = new NetworkVariable<FixedString32Bytes>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> OutfitIndex = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> HeadIndex = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> HatIndex = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> AccessoryIndex = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> ColorIndex = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        NetworkPlayer _player;
        HunterBody _body;
        TextMesh _nameTag;

        void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _body = GetComponentInChildren<HunterBody>(true);
        }

        public override void OnNetworkSpawn()
        {
            DisplayName.OnValueChanged += OnNameChanged;
            OutfitIndex.OnValueChanged += OnLookChanged;
            HeadIndex.OnValueChanged += OnLookChanged;
            HatIndex.OnValueChanged += OnLookChanged;
            AccessoryIndex.OnValueChanged += OnLookChanged;
            ColorIndex.OnValueChanged += OnLookChanged;

            if (IsOwner) PushFromProfile();
            RebuildLook();
            BuildNameTag();
        }

        public override void OnNetworkDespawn()
        {
            DisplayName.OnValueChanged -= OnNameChanged;
            OutfitIndex.OnValueChanged -= OnLookChanged;
            HeadIndex.OnValueChanged -= OnLookChanged;
            HatIndex.OnValueChanged -= OnLookChanged;
            AccessoryIndex.OnValueChanged -= OnLookChanged;
            ColorIndex.OnValueChanged -= OnLookChanged;
        }

        /// <summary>Owner: publish name and outfit from the saved profile (only owned items).</summary>
        public void PushFromProfile()
        {
            if (!IsOwner) return;
            var p = PlayerProfile.Current;
            string name = p.EffectiveName;
            if (name.Length > 24) name = name.Substring(0, 24);
            DisplayName.Value = new FixedString32Bytes(name);
            OutfitIndex.Value = IndexIfOwned(p.outfit, p);
            HeadIndex.Value = IndexIfOwned(p.head, p);
            HatIndex.Value = IndexIfOwned(p.hat, p);
            AccessoryIndex.Value = IndexIfOwned(p.accessory, p);
            ColorIndex.Value = p.colorIndex >= 0 && IsColorOwned(p.colorIndex, p) ? p.colorIndex : -1;
        }

        static int IndexIfOwned(string id, PlayerProfile p)
        {
            var item = Cosmetics.Find(id);
            return item != null && Cosmetics.IsUnlocked(item, p) ? Cosmetics.Items.IndexOf(item) : -1;
        }

        static bool IsColorOwned(int index, PlayerProfile p)
        {
            var item = Cosmetics.Find($"color_{index}");
            return item != null && Cosmetics.IsUnlocked(item, p);
        }

        void OnNameChanged(FixedString32Bytes previous, FixedString32Bytes current)
        {
            if (_nameTag) _nameTag.text = current.ToString();
        }

        void OnLookChanged(int previous, int current) => RebuildLook();

        void RebuildLook()
        {
            var color = Cosmetics.BodyColor(ColorIndex.Value, OwnerClientId);
            if (_body != null)
            {
                _body.Apply(ItemId(OutfitIndex.Value), ItemId(HeadIndex.Value), ItemId(HatIndex.Value), ItemId(AccessoryIndex.Value), color);
                // Like your body: invisible to your own camera, but you still cast a shadow.
                _body.SetShadowOnly(IsOwner);
            }
            _player.SetBodyColor(color);
            if (IsOwner && TryGetComponent(out FriendSlop.Weapons.PlayerWeapons weapons)) weapons.RefreshModel(); // re-color your hands
        }

        static string ItemId(int index) =>
            index >= 0 && index < Cosmetics.Items.Count ? Cosmetics.Items[index].Id : null;

        void BuildNameTag()
        {
            if (IsOwner) return; // you don't need your own name floating in your face
            var go = new GameObject("NameTag");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.25f, 0f);
            _nameTag = go.AddComponent<TextMesh>();
            _nameTag.text = DisplayName.Value.ToString();
            _nameTag.anchor = TextAnchor.MiddleCenter;
            _nameTag.alignment = TextAlignment.Center;
            _nameTag.characterSize = 0.035f;
            _nameTag.fontSize = 64;
            _nameTag.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                _nameTag.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                if (nameTagMaterial != null) go.AddComponent<WorldText>().SetBaseMaterial(nameTagMaterial);
            }
        }

        void LateUpdate()
        {
            if (_nameTag == null) return;
            var cam = NetworkPlayer.LocalCamera;
            if (cam == null) return;
            // Billboard toward the local player's camera (text faces along its forward).
            Vector3 toTag = _nameTag.transform.position - cam.position;
            if (toTag.sqrMagnitude > 0.01f) _nameTag.transform.rotation = Quaternion.LookRotation(toTag);
        }

        /// <summary>Display name for any connected client (falls back to Host / Player N).</summary>
        public static string NameOf(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            var obj = nm != null && nm.SpawnManager != null ? nm.SpawnManager.GetPlayerNetworkObject(clientId) : null;
            if (obj != null && obj.TryGetComponent(out PlayerAppearance a))
            {
                string n = a.DisplayName.Value.ToString();
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            return clientId == NetworkManager.ServerClientId ? "Host" : $"Player {clientId}";
        }
    }
}
