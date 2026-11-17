using FriendSlop.Profile;
using UnityEngine;

namespace FriendSlop.Player
{
    /// <summary>Dresses a camp NPC (trader, gunsmith) in a fixed hunter outfit and gives them a wave now and then.</summary>
    public class NpcLook : MonoBehaviour
    {
        public string outfit = Cosmetics.DefaultOutfit;
        public string head = Cosmetics.DefaultHead;
        public string hat = "none";
        public string accessory = "none";
        public int colorIndex = 0;

        HunterBody _body;
        float _nextWave;

        void Start()
        {
            _body = GetComponentInChildren<HunterBody>();
            if (_body) _body.Apply(outfit, head, hat, accessory, Cosmetics.BodyColor(colorIndex, 0));
            _nextWave = Time.time + Random.Range(3f, 9f);
        }

        void Update()
        {
            if (_body == null || Time.time < _nextWave) return;
            _nextWave = Time.time + Random.Range(8f, 16f);
            var cam = NetworkPlayer.LocalCamera;
            // Only bother waving when someone is close enough to see it.
            if (cam != null && (cam.position - transform.position).sqrMagnitude < 18f * 18f) _body.Wave();
        }
    }
}
