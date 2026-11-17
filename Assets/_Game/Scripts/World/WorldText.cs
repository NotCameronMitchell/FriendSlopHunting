using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// Makes a TextMesh render like a real object in the world (hidden behind walls, invisible
    /// from behind) by swapping Unity's draw-on-top font material for the FriendSlop/WorldText
    /// shader. The font's glyph texture is plugged in at runtime, since dynamic font textures
    /// only exist once the game is running.
    /// </summary>
    [RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
    public class WorldText : MonoBehaviour
    {
        [SerializeField] Material baseMaterial;

        static readonly Dictionary<Font, Material> Cache = new Dictionary<Font, Material>();

        public void SetBaseMaterial(Material material)
        {
            baseMaterial = material;
            Apply();
        }

        void Awake() => Apply();

        void Apply()
        {
            var text = GetComponent<TextMesh>();
            if (baseMaterial == null || text.font == null) return;
            if (!Cache.TryGetValue(text.font, out var mat) || mat == null)
            {
                mat = new Material(baseMaterial) { name = $"WorldText_{text.font.name}" };
                mat.mainTexture = text.font.material.mainTexture;
                Cache[text.font] = mat;
            }
            GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
