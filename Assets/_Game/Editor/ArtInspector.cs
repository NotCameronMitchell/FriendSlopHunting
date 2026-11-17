using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>Dev aid: logs bounds, materials, textures and rigs of the imported third-party models.</summary>
    public static class ArtInspector
    {
        public static void Dump(string folder)
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var rends = go.GetComponentsInChildren<Renderer>(true);
                var b = new Bounds();
                bool first = true;
                foreach (var r in rends)
                {
                    var mb = r is SkinnedMeshRenderer s ? s.sharedMesh != null ? s.sharedMesh.bounds : new Bounds() : r.GetComponent<MeshFilter>()?.sharedMesh?.bounds ?? new Bounds();
                    var wb = TransformBounds(r.transform, mb);
                    if (first) { b = wb; first = false; } else b.Encapsulate(wb);
                }
                var mats = rends.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct()
                    .Select(m => $"{m.name}<{m.shader.name.Replace("Universal Render Pipeline/", "")}|{(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : (m.mainTexture ? m.mainTexture.name : "-"))}>");
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).Select(c => $"{c.name}({c.length:0.0}s)").ToArray();
                var anim = go.GetComponentInChildren<Animator>();
                var parts = rends.Select(r => r.name + (r is SkinnedMeshRenderer ? "*" : "")).ToArray();
                sb.AppendLine($"[Art] {System.IO.Path.GetFileNameWithoutExtension(path)} size={b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} min=({b.min.x:0.00},{b.min.y:0.00},{b.min.z:0.00}) max=({b.max.x:0.00},{b.max.y:0.00},{b.max.z:0.00}) rends={rends.Length} parts=[{string.Join(",", parts.Take(14))}] mats=[{string.Join(", ", mats)}]" +
                              (clips.Length > 0 ? $" clips({clips.Length})=[{string.Join(",", clips.Take(80))}]" : "") +
                              (anim ? $" avatar={(anim.avatar ? anim.avatar.name : "none")}" : ""));
            }
            Debug.Log(sb.ToString());
        }

        static Bounds TransformBounds(Transform t, Bounds local)
        {
            var c = t.TransformPoint(local.center);
            var e = local.extents;
            var ax = t.TransformVector(e.x, 0, 0); var ay = t.TransformVector(0, e.y, 0); var az = t.TransformVector(0, 0, e.z);
            var ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2f);
        }

        /// <summary>Logs the bone hierarchy (names only) of a rigged model.</summary>
        public static void Bones(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sb = new StringBuilder("[Art] bones of " + path + "\n");
            void Walk(Transform t, int d) { sb.Append(new string(' ', d * 2)).Append(t.name).Append(' ').Append(t.localPosition.ToString("0.000")).Append('\n'); foreach (Transform c in t) Walk(c, d + 1); }
            Walk(go.transform, 0);
            Debug.Log(sb.ToString());
        }
    }
}
