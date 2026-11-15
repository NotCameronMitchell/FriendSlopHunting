using UnityEngine;

namespace FriendSlop.Weapons
{
    /// <summary>
    /// Builds a chunky low-poly weapon model from primitives at runtime. Placeholder until the
    /// art pass. The model's +Z is "forward"; returns the muzzle transform.
    /// </summary>
    public static class WeaponModelFactory
    {
        static Material _metal, _wood, _dark, _accent, _string;

        public static Transform Build(WeaponDefinition def, Transform parent)
        {
            EnsureMaterials(def);
            var root = new GameObject($"Model_{def.displayName}").transform;
            root.SetParent(parent, false);

            Transform muzzle;
            switch (def.model)
            {
                case WeaponModel.Revolver:
                    Box(root, new Vector3(0f, 0.02f, 0.12f), new Vector3(0.035f, 0.035f, 0.24f), _metal);
                    Cyl(root, new Vector3(0f, 0.015f, 0.0f), new Vector3(0.07f, 0.03f, 0.07f), _metal, Quaternion.Euler(90f, 0f, 0f));
                    Box(root, new Vector3(0f, -0.06f, -0.04f), new Vector3(0.04f, 0.11f, 0.05f), _wood, Quaternion.Euler(-15f, 0f, 0f));
                    muzzle = Point(root, new Vector3(0f, 0.02f, 0.25f));
                    break;
                case WeaponModel.Uzi:
                    Box(root, new Vector3(0f, 0.02f, 0.08f), new Vector3(0.05f, 0.07f, 0.24f), _dark);
                    Box(root, new Vector3(0f, -0.07f, 0.04f), new Vector3(0.035f, 0.13f, 0.04f), _dark);
                    Box(root, new Vector3(0f, 0.025f, 0.22f), new Vector3(0.02f, 0.02f, 0.06f), _metal);
                    muzzle = Point(root, new Vector3(0f, 0.025f, 0.26f));
                    break;
                case WeaponModel.MachineGun:
                    Box(root, new Vector3(0f, 0.01f, 0.1f), new Vector3(0.07f, 0.09f, 0.45f), _dark);
                    Box(root, new Vector3(0f, 0.02f, 0.45f), new Vector3(0.03f, 0.03f, 0.35f), _metal);
                    Box(root, new Vector3(0f, -0.08f, 0.12f), new Vector3(0.1f, 0.08f, 0.12f), _accent);
                    Box(root, new Vector3(0f, -0.02f, -0.22f), new Vector3(0.05f, 0.09f, 0.2f), _wood);
                    muzzle = Point(root, new Vector3(0f, 0.02f, 0.63f));
                    break;
                case WeaponModel.Minigun:
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Cyl(root, new Vector3(Mathf.Cos(a) * 0.045f, Mathf.Sin(a) * 0.045f, 0.3f), new Vector3(0.025f, 0.28f, 0.025f), _metal, Quaternion.Euler(90f, 0f, 0f));
                    }
                    Cyl(root, new Vector3(0f, 0f, 0.02f), new Vector3(0.16f, 0.1f, 0.16f), _dark, Quaternion.Euler(90f, 0f, 0f));
                    Box(root, new Vector3(0f, -0.12f, -0.05f), new Vector3(0.12f, 0.1f, 0.18f), _accent);
                    muzzle = Point(root, new Vector3(0f, 0f, 0.6f));
                    break;
                case WeaponModel.Shotgun:
                    Box(root, new Vector3(0f, 0.025f, 0.2f), new Vector3(0.04f, 0.04f, 0.6f), _metal);
                    Box(root, new Vector3(0f, -0.01f, 0.18f), new Vector3(0.045f, 0.035f, 0.18f), _wood);
                    Box(root, new Vector3(0f, -0.01f, -0.2f), new Vector3(0.06f, 0.1f, 0.3f), _wood);
                    muzzle = Point(root, new Vector3(0f, 0.025f, 0.5f));
                    break;
                case WeaponModel.Rifle:
                    Box(root, new Vector3(0f, 0.02f, 0.18f), new Vector3(0.035f, 0.035f, 0.62f), _metal);
                    Box(root, new Vector3(0f, -0.02f, -0.2f), new Vector3(0.06f, 0.1f, 0.3f), _wood);
                    Box(root, new Vector3(0f, 0.075f, 0.05f), new Vector3(0.04f, 0.04f, 0.18f), _dark);
                    muzzle = Point(root, new Vector3(0f, 0.02f, 0.5f));
                    break;
                case WeaponModel.Bow:
                    Box(root, new Vector3(0f, 0f, 0.1f), new Vector3(0.03f, 0.12f, 0.03f), _wood);
                    Box(root, new Vector3(0f, 0.17f, 0.06f), new Vector3(0.025f, 0.25f, 0.025f), _wood, Quaternion.Euler(-20f, 0f, 0f));
                    Box(root, new Vector3(0f, -0.17f, 0.06f), new Vector3(0.025f, 0.25f, 0.025f), _wood, Quaternion.Euler(20f, 0f, 0f));
                    Box(root, new Vector3(0f, 0f, -0.01f), new Vector3(0.005f, 0.56f, 0.005f), _string);
                    Box(root, new Vector3(0f, 0f, 0.12f), new Vector3(0.01f, 0.01f, 0.45f), _accent); // nocked arrow
                    muzzle = Point(root, new Vector3(0f, 0f, 0.35f));
                    break;
                case WeaponModel.Slingshot:
                    Box(root, new Vector3(0f, -0.06f, 0.08f), new Vector3(0.03f, 0.12f, 0.03f), _wood);
                    Box(root, new Vector3(-0.035f, 0.03f, 0.08f), new Vector3(0.022f, 0.08f, 0.022f), _wood, Quaternion.Euler(0f, 0f, 20f));
                    Box(root, new Vector3(0.035f, 0.03f, 0.08f), new Vector3(0.022f, 0.08f, 0.022f), _wood, Quaternion.Euler(0f, 0f, -20f));
                    Box(root, new Vector3(0f, 0.06f, 0.02f), new Vector3(0.09f, 0.008f, 0.008f), _accent);
                    muzzle = Point(root, new Vector3(0f, 0.06f, 0.12f));
                    break;
                case WeaponModel.Boomerang:
                    Box(root, new Vector3(-0.06f, 0f, 0.08f), new Vector3(0.16f, 0.015f, 0.04f), _accent, Quaternion.Euler(0f, 35f, 0f));
                    Box(root, new Vector3(0.06f, 0f, 0.08f), new Vector3(0.16f, 0.015f, 0.04f), _accent, Quaternion.Euler(0f, -35f, 0f));
                    muzzle = Point(root, new Vector3(0f, 0f, 0.2f));
                    break;
                case WeaponModel.Bazooka:
                    Cyl(root, new Vector3(0f, 0.06f, 0.08f), new Vector3(0.075f, 0.32f, 0.075f), _accent, Quaternion.Euler(90f, 0f, 0f));
                    Box(root, new Vector3(0f, -0.03f, 0.04f), new Vector3(0.03f, 0.08f, 0.035f), _dark);
                    Box(root, new Vector3(0.05f, 0.1f, 0.12f), new Vector3(0.025f, 0.03f, 0.08f), _dark);
                    muzzle = Point(root, new Vector3(0f, 0.06f, 0.42f));
                    break;
                default: // Pistol
                    Box(root, new Vector3(0f, 0.02f, 0.08f), new Vector3(0.035f, 0.05f, 0.18f), _metal);
                    Box(root, new Vector3(0f, -0.05f, 0.0f), new Vector3(0.03f, 0.1f, 0.045f), _dark, Quaternion.Euler(-12f, 0f, 0f));
                    muzzle = Point(root, new Vector3(0f, 0.025f, 0.18f));
                    break;
            }
            return muzzle;
        }

        static void EnsureMaterials(WeaponDefinition def)
        {
            if (_metal == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _metal = Make(shader, new Color(0.32f, 0.33f, 0.36f), 0.7f);
                _wood = Make(shader, new Color(0.45f, 0.29f, 0.16f), 0.1f);
                _dark = Make(shader, new Color(0.12f, 0.12f, 0.13f), 0.4f);
                _string = Make(shader, new Color(0.9f, 0.88f, 0.8f), 0f);
                _accent = Make(shader, Color.white, 0.2f);
            }
            _accent = new Material(_accent) { color = def.tint };
            _accent.SetColor("_BaseColor", def.tint);
        }

        static Material Make(Shader shader, Color c, float metallic)
        {
            var m = new Material(shader) { color = c };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        static void Box(Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null) =>
            Part(PrimitiveType.Cube, parent, pos, scale, mat, rot);

        static void Cyl(Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null) =>
            Part(PrimitiveType.Cylinder, parent, pos, scale, mat, rot);

        static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot)
        {
            var go = GameObject.CreatePrimitive(type);
            // Immediate: a leftover collider for even one frame would join a pickup's physics body.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform Point(Transform parent, Vector3 pos)
        {
            var t = new GameObject("Muzzle").transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }
    }
}
