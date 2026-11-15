using FriendSlop.Weapons;
using UnityEngine;

namespace FriendSlop.World
{
    /// <summary>
    /// Display wall in the gun shop: every weapon model hung in a grid with a name/price tag.
    /// Purely cosmetic and built locally on each machine (no networking).
    /// Local +Z points out of the wall toward customers; weapons are laid out along local +X.
    /// </summary>
    public class WeaponRack : MonoBehaviour
    {
        [SerializeField] WeaponCatalog catalog;
        [SerializeField] int columns = 4;
        [SerializeField] Vector2 spacing = new Vector2(1.5f, 0.85f);
        [SerializeField] float modelScale = 2.2f;
        [Tooltip("Depth-tested text material so tags don't show through walls.")]
        [SerializeField] Material textMaterial;

        void Start()
        {
            if (catalog == null) return;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            int rows = Mathf.CeilToInt(catalog.Count / (float)columns);

            for (int i = 0; i < catalog.Count; i++)
            {
                var def = catalog.Get(i);
                if (def == null) continue;
                int col = i % columns, row = i / columns;
                // Customers face the rack (looking along -Z), so +X is on THEIR left: lay out
                // along -X so the list reads left-to-right in catalog order (Pistol first).
                float x = -(col - (columns - 1) * 0.5f) * spacing.x;
                float y = ((rows - 1) - row) * spacing.y;

                var slot = new GameObject($"Rack_{def.displayName}").transform;
                slot.SetParent(transform, false);
                slot.localPosition = new Vector3(x, y, 0f);

                // Hang the weapon sideways along the wall.
                var hook = new GameObject("Hook").transform;
                hook.SetParent(slot, false);
                hook.localRotation = Quaternion.Euler(0f, 90f, 0f);
                var model = WeaponModelFactory.Build(def, hook).parent;
                model.localScale = Vector3.one * modelScale;
                model.localPosition = new Vector3(0f, 0.12f, 0f);

                var tag = new GameObject("Tag");
                tag.transform.SetParent(slot, false);
                tag.transform.localPosition = new Vector3(0f, -0.14f, 0.05f);
                tag.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // readable from the shop floor
                var text = tag.AddComponent<TextMesh>();
                text.text = def.price == 0 ? $"{def.displayName}\nfree" : $"{def.displayName}\n${def.price:N0}";
                text.anchor = TextAnchor.UpperCenter;
                text.alignment = TextAlignment.Center;
                text.characterSize = 0.022f;
                text.fontSize = 48;
                text.color = def.price == 0 ? new Color(0.7f, 1f, 0.7f) : new Color(1f, 0.92f, 0.6f);
                if (font != null)
                {
                    text.font = font;
                    tag.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                    if (textMaterial != null) tag.AddComponent<WorldText>().SetBaseMaterial(textMaterial);
                }
            }
        }
    }
}
