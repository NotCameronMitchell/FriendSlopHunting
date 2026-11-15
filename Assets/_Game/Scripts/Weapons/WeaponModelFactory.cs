using UnityEngine;

namespace FriendSlop.Weapons
{
    /// <summary>
    /// Builds chunky, toy-like cartoon weapon models from primitives at runtime (matching the
    /// KayKit/PEAK look): rounded barrels, colored bands, big sights. The model's +Z is forward.
    /// Returns the muzzle; the model also gets "GripR"/"GripL" points where first-person hands go.
    /// </summary>
    public static class WeaponModelFactory
    {
        static Material _metal, _gunmetal, _wood, _darkWood, _rubber, _brass, _string, _red, _glass, _skin;
        static readonly System.Collections.Generic.Dictionary<Color, Material> _accents = new();

        public static Transform Build(WeaponDefinition def, Transform parent)
        {
            EnsureMaterials();
            var accent = Accent(def.tint);
            var root = new GameObject($"Model_{def.displayName}").transform;
            root.SetParent(parent, false);

            Transform muzzle;
            switch (def.model)
            {
                case WeaponModel.Revolver:
                    Tube(root, new Vector3(0f, 0.04f, 0.14f), 0.034f, 0.22f, _metal);
                    Tube(root, new Vector3(0f, 0.04f, 0.25f), 0.042f, 0.018f, _gunmetal);                 // muzzle ring
                    Tube(root, new Vector3(0f, 0.022f, 0.0f), 0.09f, 0.075f, _gunmetal);                  // drum
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Tube(root, new Vector3(Mathf.Cos(a) * 0.026f, 0.022f + Mathf.Sin(a) * 0.026f, 0.04f), 0.016f, 0.004f, _brass);
                    }
                    Box(root, new Vector3(0f, 0.03f, -0.035f), new Vector3(0.036f, 0.06f, 0.07f), _metal);
                    Box(root, new Vector3(0f, 0.075f, -0.06f), new Vector3(0.014f, 0.03f, 0.025f), _gunmetal, Quaternion.Euler(-30f, 0f, 0f)); // hammer
                    Box(root, new Vector3(0f, 0.064f, 0.24f), new Vector3(0.008f, 0.018f, 0.014f), _brass); // front sight
                    Grip(root, new Vector3(0f, -0.05f, -0.07f), new Vector3(0.042f, 0.12f, 0.055f), _wood, 18f);
                    Guard(root, new Vector3(0f, -0.02f, 0.0f), _gunmetal);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.04f, 0.27f));
                    Point(root, "GripR", new Vector3(0f, -0.05f, -0.075f));
                    Point(root, "GripL", new Vector3(-0.025f, -0.075f, -0.05f));
                    break;

                case WeaponModel.Uzi:
                    Box(root, new Vector3(0f, 0.025f, 0.07f), new Vector3(0.058f, 0.075f, 0.22f), _gunmetal);
                    Box(root, new Vector3(0f, 0.065f, 0.07f), new Vector3(0.04f, 0.012f, 0.2f), accent);      // top stripe
                    Tube(root, new Vector3(0f, 0.025f, 0.21f), 0.03f, 0.07f, _metal);
                    Box(root, new Vector3(0f, -0.07f, 0.04f), new Vector3(0.04f, 0.14f, 0.045f), _rubber, Quaternion.Euler(-8f, 0f, 0f));
                    Box(root, new Vector3(0f, -0.15f, 0.05f), new Vector3(0.034f, 0.05f, 0.036f), accent, Quaternion.Euler(-8f, 0f, 0f)); // mag base
                    Box(root, new Vector3(0f, 0.025f, -0.08f), new Vector3(0.012f, 0.012f, 0.1f), _metal);  // wire stock
                    Box(root, new Vector3(0f, -0.01f, -0.13f), new Vector3(0.05f, 0.07f, 0.012f), _metal);
                    Guard(root, new Vector3(0f, -0.018f, 0.09f), _gunmetal);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.025f, 0.25f));
                    Point(root, "GripR", new Vector3(0f, -0.055f, 0.035f));
                    Point(root, "GripL", new Vector3(0f, -0.005f, 0.15f));
                    break;

                case WeaponModel.MachineGun:
                    Box(root, new Vector3(0f, 0.02f, 0.06f), new Vector3(0.075f, 0.095f, 0.36f), _gunmetal);
                    Tube(root, new Vector3(0f, 0.03f, 0.38f), 0.07f, 0.3f, accent);                         // cooling shroud
                    for (int i = 0; i < 4; i++) Tube(root, new Vector3(0f, 0.03f, 0.27f + i * 0.07f), 0.076f, 0.012f, _gunmetal);
                    Tube(root, new Vector3(0f, 0.03f, 0.56f), 0.03f, 0.08f, _metal);
                    Box(root, new Vector3(0f, 0.095f, 0.06f), new Vector3(0.02f, 0.035f, 0.14f), _metal);   // carry handle
                    Tube(root, new Vector3(0.07f, -0.02f, 0.08f), 0.13f, 0.06f, accent, Quaternion.Euler(0f, 90f, 0f)); // drum mag
                    Grip(root, new Vector3(0f, -0.07f, -0.04f), new Vector3(0.045f, 0.12f, 0.055f), _rubber, 12f);
                    Box(root, new Vector3(0f, -0.005f, -0.22f), new Vector3(0.055f, 0.09f, 0.2f), _wood);
                    Box(root, new Vector3(0f, -0.02f, -0.33f), new Vector3(0.06f, 0.12f, 0.04f), _rubber);
                    Guard(root, new Vector3(0f, -0.035f, 0.0f), _gunmetal);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.03f, 0.61f));
                    Point(root, "GripR", new Vector3(0f, -0.07f, -0.045f));
                    Point(root, "GripL", new Vector3(0f, -0.02f, 0.3f));
                    break;

                case WeaponModel.Minigun:
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Tube(root, new Vector3(Mathf.Cos(a) * 0.05f, Mathf.Sin(a) * 0.05f, 0.33f), 0.028f, 0.5f, _metal);
                    }
                    Tube(root, new Vector3(0f, 0f, 0.55f), 0.15f, 0.025f, _gunmetal);
                    Tube(root, new Vector3(0f, 0f, 0.3f), 0.15f, 0.025f, _gunmetal);
                    Tube(root, new Vector3(0f, 0f, 0.04f), 0.19f, 0.16f, accent);                          // motor housing
                    Tube(root, new Vector3(0f, 0f, -0.05f), 0.14f, 0.04f, _gunmetal);
                    Box(root, new Vector3(0.11f, -0.07f, -0.02f), new Vector3(0.09f, 0.11f, 0.16f), _gunmetal); // ammo box
                    Box(root, new Vector3(0.11f, -0.01f, -0.02f), new Vector3(0.095f, 0.012f, 0.165f), accent);
                    Box(root, new Vector3(-0.12f, 0.0f, 0.06f), new Vector3(0.06f, 0.02f, 0.02f), _gunmetal);   // side handle arm
                    Box(root, new Vector3(-0.16f, 0.0f, 0.06f), new Vector3(0.03f, 0.1f, 0.035f), _rubber);     // side handle
                    Grip(root, new Vector3(0f, -0.14f, -0.02f), new Vector3(0.045f, 0.11f, 0.05f), _rubber, 10f);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0f, 0.6f));
                    Point(root, "GripR", new Vector3(0f, -0.14f, -0.025f));
                    Point(root, "GripL", new Vector3(-0.16f, 0f, 0.06f));
                    break;

                case WeaponModel.Shotgun:
                    Tube(root, new Vector3(0f, 0.035f, 0.22f), 0.042f, 0.56f, _metal);
                    Tube(root, new Vector3(0f, 0.035f, 0.5f), 0.05f, 0.02f, _gunmetal);
                    Tube(root, new Vector3(0f, -0.005f, 0.18f), 0.036f, 0.42f, _gunmetal);                  // mag tube
                    Box(root, new Vector3(0f, -0.005f, 0.22f), new Vector3(0.056f, 0.05f, 0.16f), accent);   // pump
                    for (int i = 0; i < 4; i++) Box(root, new Vector3(0f, -0.005f, 0.16f + i * 0.04f), new Vector3(0.058f, 0.052f, 0.008f), _darkWood);
                    Box(root, new Vector3(0f, 0.02f, -0.04f), new Vector3(0.055f, 0.075f, 0.16f), _gunmetal); // receiver
                    Box(root, new Vector3(0f, 0.065f, 0.47f), new Vector3(0.01f, 0.014f, 0.014f), _red);     // bead sight
                    Grip(root, new Vector3(0f, -0.05f, -0.11f), new Vector3(0.042f, 0.1f, 0.05f), _wood, 25f);
                    Box(root, new Vector3(0f, -0.02f, -0.26f), new Vector3(0.05f, 0.1f, 0.2f), _wood, Quaternion.Euler(-6f, 0f, 0f));
                    Box(root, new Vector3(0f, -0.03f, -0.37f), new Vector3(0.055f, 0.12f, 0.03f), _rubber);
                    Guard(root, new Vector3(0f, -0.02f, -0.06f), _gunmetal);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.035f, 0.52f));
                    Point(root, "GripR", new Vector3(0f, -0.05f, -0.11f));
                    Point(root, "GripL", new Vector3(0f, -0.01f, 0.22f));
                    break;

                case WeaponModel.Rifle:
                    Tube(root, new Vector3(0f, 0.035f, 0.25f), 0.032f, 0.56f, _metal);
                    Tube(root, new Vector3(0f, 0.035f, 0.53f), 0.042f, 0.03f, _gunmetal);
                    Box(root, new Vector3(0f, 0.005f, 0.14f), new Vector3(0.05f, 0.05f, 0.36f), _wood);       // forestock
                    Box(root, new Vector3(0f, 0.025f, -0.05f), new Vector3(0.05f, 0.06f, 0.14f), _gunmetal);  // action
                    Box(root, new Vector3(0.045f, 0.035f, -0.06f), new Vector3(0.05f, 0.012f, 0.012f), _metal); // bolt
                    Sphere(root, new Vector3(0.075f, 0.035f, -0.06f), 0.026f, _metal);
                    Tube(root, new Vector3(0f, 0.105f, 0.0f), 0.05f, 0.22f, _gunmetal);                     // scope
                    Tube(root, new Vector3(0f, 0.105f, 0.12f), 0.065f, 0.05f, accent);
                    Tube(root, new Vector3(0f, 0.105f, -0.11f), 0.06f, 0.04f, accent);
                    Tube(root, new Vector3(0f, 0.105f, 0.146f), 0.05f, 0.004f, _glass);
                    Box(root, new Vector3(0f, 0.07f, 0.04f), new Vector3(0.02f, 0.04f, 0.02f), _gunmetal);
                    Box(root, new Vector3(0f, 0.07f, -0.05f), new Vector3(0.02f, 0.04f, 0.02f), _gunmetal);
                    Grip(root, new Vector3(0f, -0.04f, -0.12f), new Vector3(0.042f, 0.09f, 0.05f), _wood, 30f);
                    Box(root, new Vector3(0f, -0.01f, -0.27f), new Vector3(0.05f, 0.1f, 0.22f), _wood, Quaternion.Euler(-5f, 0f, 0f));
                    Box(root, new Vector3(0f, -0.02f, -0.385f), new Vector3(0.055f, 0.115f, 0.03f), _rubber);
                    Guard(root, new Vector3(0f, -0.015f, -0.07f), _gunmetal);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.035f, 0.55f));
                    Point(root, "GripR", new Vector3(0f, -0.04f, -0.125f));
                    Point(root, "GripL", new Vector3(0f, -0.01f, 0.2f));
                    break;

                case WeaponModel.Bow:
                    Box(root, new Vector3(0f, 0f, 0.1f), new Vector3(0.035f, 0.13f, 0.04f), _rubber);         // wrapped grip
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Capsule(root, new Vector3(0f, s * 0.15f, 0.09f), new Vector3(0.03f, 0.1f, 0.03f), _wood, Quaternion.Euler(s * 15f, 0f, 0f));
                        Capsule(root, new Vector3(0f, s * 0.29f, 0.06f), new Vector3(0.026f, 0.08f, 0.026f), _wood, Quaternion.Euler(s * 35f, 0f, 0f));
                        Sphere(root, new Vector3(0f, s * 0.36f, 0.025f), 0.03f, accent);                      // limb tips
                    }
                    Box(root, new Vector3(0f, 0f, 0.01f), new Vector3(0.006f, 0.7f, 0.006f), _string);
                    Box(root, new Vector3(0f, 0f, 0.14f), new Vector3(0.012f, 0.012f, 0.42f), _wood);         // nocked arrow
                    Sphere(root, new Vector3(0f, 0f, 0.36f), 0.025f, _metal);
                    Box(root, new Vector3(0f, 0.015f, -0.04f), new Vector3(0.004f, 0.03f, 0.06f), accent);   // fletching
                    Box(root, new Vector3(0f, -0.015f, -0.04f), new Vector3(0.004f, 0.03f, 0.06f), accent);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0f, 0.38f));
                    Point(root, "GripL", new Vector3(0f, 0f, 0.1f));
                    Point(root, "GripR", new Vector3(0f, 0f, -0.02f));
                    root.localScale = Vector3.one * 0.72f;            // a full-size bow fills the screen
                    root.localRotation = Quaternion.Euler(0f, 0f, -28f); // canted, archer style
                    break;

                case WeaponModel.Slingshot:
                    Capsule(root, new Vector3(0f, -0.07f, 0.1f), new Vector3(0.035f, 0.07f, 0.035f), _wood, null);
                    Capsule(root, new Vector3(-0.04f, 0.025f, 0.1f), new Vector3(0.028f, 0.05f, 0.028f), _wood, Quaternion.Euler(0f, 0f, 25f));
                    Capsule(root, new Vector3(0.04f, 0.025f, 0.1f), new Vector3(0.028f, 0.05f, 0.028f), _wood, Quaternion.Euler(0f, 0f, -25f));
                    Box(root, new Vector3(-0.05f, 0.06f, 0.05f), new Vector3(0.008f, 0.008f, 0.1f), accent, Quaternion.Euler(0f, 20f, 0f));
                    Box(root, new Vector3(0.05f, 0.06f, 0.05f), new Vector3(0.008f, 0.008f, 0.1f), accent, Quaternion.Euler(0f, -20f, 0f));
                    Box(root, new Vector3(0f, 0.06f, 0.0f), new Vector3(0.04f, 0.02f, 0.02f), _darkWood);    // pouch
                    Sphere(root, new Vector3(0f, 0.07f, 0.005f), 0.022f, _metal);                           // pebble
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.06f, 0.14f));
                    Point(root, "GripL", new Vector3(0f, -0.08f, 0.1f));
                    Point(root, "GripR", new Vector3(0f, 0.06f, -0.01f));
                    break;

                case WeaponModel.Boomerang:
                    Capsule(root, new Vector3(-0.07f, 0f, 0.1f), new Vector3(0.05f, 0.1f, 0.02f), accent, Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 55f));
                    Capsule(root, new Vector3(0.07f, 0f, 0.1f), new Vector3(0.05f, 0.1f, 0.02f), accent, Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -55f));
                    Box(root, new Vector3(-0.07f, 0.011f, 0.1f), new Vector3(0.025f, 0.004f, 0.06f), _string, Quaternion.Euler(0f, 35f, 0f));
                    Box(root, new Vector3(0.07f, 0.011f, 0.1f), new Vector3(0.025f, 0.004f, 0.06f), _string, Quaternion.Euler(0f, -35f, 0f));
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0f, 0.22f));
                    Point(root, "GripR", new Vector3(0.13f, 0f, 0.04f));
                    break;

                case WeaponModel.Bazooka:
                    Tube(root, new Vector3(0f, 0.07f, 0.1f), 0.11f, 0.7f, accent);
                    Tube(root, new Vector3(0f, 0.07f, 0.45f), 0.13f, 0.06f, _gunmetal);
                    Tube(root, new Vector3(0f, 0.07f, -0.25f), 0.13f, 0.06f, _gunmetal);
                    Tube(root, new Vector3(0f, 0.07f, 0.1f), 0.115f, 0.03f, _gunmetal);
                    Sphere(root, new Vector3(0f, 0.07f, 0.47f), 0.07f, _red);                               // rocket nose
                    Box(root, new Vector3(-0.07f, 0.13f, 0.12f), new Vector3(0.025f, 0.05f, 0.1f), _gunmetal); // sight
                    Tube(root, new Vector3(-0.07f, 0.16f, 0.12f), 0.03f, 0.08f, _glass);
                    Grip(root, new Vector3(0f, -0.04f, 0.0f), new Vector3(0.042f, 0.11f, 0.05f), _rubber, 10f);
                    Grip(root, new Vector3(0f, -0.03f, 0.22f), new Vector3(0.04f, 0.09f, 0.045f), _rubber, 0f);
                    Guard(root, new Vector3(0f, -0.01f, 0.04f), _gunmetal);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.07f, 0.5f));
                    Point(root, "GripR", new Vector3(0f, -0.045f, 0.0f));
                    Point(root, "GripL", new Vector3(0f, -0.035f, 0.22f));
                    break;

                default: // Pistol: the trusty starter with an orange grip
                    Box(root, new Vector3(0f, 0.035f, 0.07f), new Vector3(0.045f, 0.055f, 0.2f), _gunmetal);  // slide
                    for (int i = 0; i < 4; i++) Box(root, new Vector3(0f, 0.035f, -0.015f + i * 0.012f), new Vector3(0.047f, 0.04f, 0.004f), _metal); // serrations
                    Tube(root, new Vector3(0f, 0.035f, 0.175f), 0.026f, 0.012f, _metal);
                    Box(root, new Vector3(0f, 0.0f, 0.06f), new Vector3(0.04f, 0.03f, 0.17f), _metal);         // frame
                    Box(root, new Vector3(0f, 0.069f, 0.16f), new Vector3(0.008f, 0.014f, 0.012f), _red);       // front sight
                    Box(root, new Vector3(0f, 0.069f, -0.02f), new Vector3(0.03f, 0.012f, 0.012f), _gunmetal);
                    Grip(root, new Vector3(0f, -0.05f, -0.005f), new Vector3(0.042f, 0.11f, 0.052f), Accent(new Color(0.95f, 0.5f, 0.12f)), 14f);
                    Guard(root, new Vector3(0f, -0.022f, 0.05f), _metal);
                    muzzle = Point(root, "Muzzle", new Vector3(0f, 0.035f, 0.19f));
                    Point(root, "GripR", new Vector3(0f, -0.05f, -0.01f));
                    Point(root, "GripL", new Vector3(-0.022f, -0.075f, 0.01f));
                    break;
            }
            return muzzle;
        }

        // ------------------------------------------------------------------ first-person hands

        /// <summary>
        /// Chunky mitten hands (KayKit-style) with sleeves, gripping the model's GripR/GripL points
        /// and reaching back toward the bottom of the screen. First person only.
        /// </summary>
        public static void AddHands(Transform model, Color sleeve)
        {
            EnsureMaterials();
            var sleeveMat = Accent(sleeve);
            var r = model.Find("GripR");
            var l = model.Find("GripL");
            if (r) Hand(model, r.localPosition, new Vector3(0.2f, -0.35f, -0.45f), sleeveMat, false);
            if (l) Hand(model, l.localPosition, new Vector3(-0.22f, -0.38f, -0.25f), sleeveMat, true);
        }

        static void Hand(Transform model, Vector3 grip, Vector3 elbow, Material sleeve, bool left)
        {
            var hand = new GameObject(left ? "HandL" : "HandR").transform;
            hand.SetParent(model, false);
            hand.localPosition = grip;
            float side = left ? -1f : 1f;
            Box(hand, new Vector3(side * 0.012f, 0f, 0f), new Vector3(0.06f, 0.075f, 0.075f), _skin, Quaternion.Euler(0f, 0f, side * 8f));      // mitt
            Box(hand, new Vector3(-side * 0.02f, 0.03f, 0.03f), new Vector3(0.025f, 0.025f, 0.05f), _skin, Quaternion.Euler(0f, side * 25f, 0f)); // thumb
            Vector3 d = elbow - grip;
            Capsule(model, grip + d * 0.5f, new Vector3(0.07f, d.magnitude * 0.5f, 0.07f), sleeve, Quaternion.FromToRotation(Vector3.up, d.normalized)); // forearm
            Tube(model, grip + d.normalized * 0.05f, 0.078f, 0.025f, _rubber, Quaternion.FromToRotation(Vector3.forward, d.normalized)); // cuff
        }

        // ------------------------------------------------------------------ materials & parts

        static void EnsureMaterials()
        {
            if (_metal != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _metal = Make(shader, new Color(0.62f, 0.64f, 0.68f), 0.35f, 0.45f);
            _gunmetal = Make(shader, new Color(0.24f, 0.25f, 0.28f), 0.25f, 0.35f);
            _wood = Make(shader, new Color(0.62f, 0.38f, 0.2f), 0f, 0.15f);
            _darkWood = Make(shader, new Color(0.38f, 0.22f, 0.12f), 0f, 0.1f);
            _rubber = Make(shader, new Color(0.13f, 0.12f, 0.13f), 0f, 0.1f);
            _brass = Make(shader, new Color(0.92f, 0.72f, 0.3f), 0.5f, 0.5f);
            _string = Make(shader, new Color(0.95f, 0.93f, 0.85f), 0f, 0f);
            _red = Make(shader, new Color(0.9f, 0.18f, 0.15f), 0f, 0.3f);
            _glass = Make(shader, new Color(0.35f, 0.7f, 0.95f), 0f, 0.9f);
            _skin = Make(shader, new Color(0.98f, 0.78f, 0.62f), 0f, 0.2f);
        }

        static Material Accent(Color c)
        {
            // Very dark tints read as black blobs at gun scale; brighten them into a colored stripe.
            if (c.maxColorComponent < 0.25f) c = new Color(0.3f, 0.32f, 0.36f);
            if (_accents.TryGetValue(c, out var m) && m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = Make(shader, c, 0f, 0.35f);
            _accents[c] = m;
            return m;
        }

        static Material Make(Shader shader, Color c, float metallic, float smooth)
        {
            var m = new Material(shader) { color = c };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        static void Box(Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null) =>
            Part(PrimitiveType.Cube, parent, pos, scale, mat, rot);

        static void Sphere(Transform parent, Vector3 pos, float diameter, Material mat) =>
            Part(PrimitiveType.Sphere, parent, pos, Vector3.one * diameter, mat, null);

        static void Capsule(Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot) =>
            Part(PrimitiveType.Capsule, parent, pos, scale, mat, rot);

        /// <summary>Cylinder along +Z (or along <paramref name="rot"/>'s forward).</summary>
        static void Tube(Transform parent, Vector3 pos, float diameter, float length, Material mat, Quaternion? rot = null) =>
            Part(PrimitiveType.Cylinder, parent, pos, new Vector3(diameter, length * 0.5f, diameter), mat, (rot ?? Quaternion.identity) * Quaternion.Euler(90f, 0f, 0f));

        /// <summary>A pistol grip: rounded block raked back.</summary>
        static void Grip(Transform parent, Vector3 pos, Vector3 size, Material mat, float rake)
        {
            Part(PrimitiveType.Cube, parent, pos, size, mat, Quaternion.Euler(-rake, 0f, 0f));
            Part(PrimitiveType.Capsule, parent, pos + new Vector3(0f, -size.y * 0.42f, -Mathf.Sin(rake * Mathf.Deg2Rad) * size.y * 0.42f), new Vector3(size.x, size.z * 0.5f, size.z), mat, Quaternion.Euler(90f - rake, 0f, 0f));
        }

        /// <summary>Trigger guard + trigger.</summary>
        static void Guard(Transform parent, Vector3 pos, Material mat)
        {
            Part(PrimitiveType.Cube, parent, pos + new Vector3(0f, -0.022f, 0f), new Vector3(0.012f, 0.008f, 0.06f), mat, null);
            Part(PrimitiveType.Cube, parent, pos + new Vector3(0f, -0.008f, 0.028f), new Vector3(0.012f, 0.03f, 0.008f), mat, null);
            Part(PrimitiveType.Cube, parent, pos + new Vector3(0f, -0.006f, 0.0f), new Vector3(0.008f, 0.022f, 0.008f), _rubber, Quaternion.Euler(-15f, 0f, 0f));
        }

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

        static Transform Point(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }
    }
}
