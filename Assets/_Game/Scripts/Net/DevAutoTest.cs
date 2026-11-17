using FriendSlop.Game;
using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Dev only (build flag -fsh-autotest): a scripted second player for multiplayer smoke tests. Once joined it
    /// follows the host around, says hi in chat, and revives any downed hunter it's standing next to. Everything it
    /// does is logged with "[AutoTest]" so the host's and client's logs can be compared.
    /// </summary>
    public class DevAutoTest : MonoBehaviour
    {
        float _next, _chatAt = -1f;
        bool _greeted;

        void Update()
        {
            var me = NetworkPlayer.Local;
            var state = GameState.Instance;
            if (me == null || state == null || !state.IsSpawned || Time.time < _next) return;
            _next = Time.time + 1f;

            if (_chatAt < 0f) _chatAt = Time.time + 3f;
            if (!_greeted && Time.time > _chatAt)
            {
                _greeted = true;
                state.SendChat("hello from the autotest client!");
                Debug.Log("[AutoTest] sent chat");
            }

            // Stand next to the first other hunter (the host).
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null || p == me || !p.IsSpawned) continue;
                var target = p.transform.position + p.transform.right * 1.6f;
                if ((me.transform.position - target).sqrMagnitude > 4f) { me.Teleport(target + Vector3.up * 0.3f); Debug.Log($"[AutoTest] moved next to {GameState.PlayerName(p.OwnerClientId)}"); }
                var hp = p.GetComponent<PlayerHealth>();
                if (hp != null && hp.IsDown.Value)
                {
                    hp.RequestRevive();
                    Debug.Log($"[AutoTest] reviving {GameState.PlayerName(p.OwnerClientId)}");
                }
                break;
            }
        }
    }
}
