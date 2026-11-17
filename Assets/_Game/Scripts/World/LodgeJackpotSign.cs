using FriendSlop.Game;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>The big "JACKPOT $X" sign over the Lucky Lodge, pulsing as the pot grows.</summary>
    public class LodgeJackpotSign : MonoBehaviour
    {
        public TextMesh text;
        int _shown = -1;
        float _punch;

        void Update()
        {
            var state = GameState.Instance;
            if (text == null) return;
            int value = state != null && state.IsSpawned ? state.LodgeJackpot.Value : GameState.JackpotSeed;
            if (value != _shown) { if (_shown >= 0) _punch = 1f; _shown = value; text.text = $"JACKPOT  ${value:N0}"; }
            _punch = Mathf.MoveTowards(_punch, 0f, Time.deltaTime * 2.5f);
            float glow = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
            text.color = Color.Lerp(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.95f, 0.6f), glow);
            transform.localScale = Vector3.one * (1f + _punch * 0.25f);
        }
    }
}
