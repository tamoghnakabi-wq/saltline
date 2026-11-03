using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.EditorTools
{
    /// <summary>
    /// Where a map's rendering cost sits: renderers, triangles, shadow casters, static flags and
    /// Mesh LOD levels, grouped by mesh. Writes a text report:
    ///
    ///   Unity -batchmode -quit -executeMethod Saltline.EditorTools.PerfAudit.Run [-auditscene Kappa] [-auditout file]
    /// </summary>
    public static class PerfAudit
    {
        public static void Run()
        {
            string scene = Arg("-auditscene", "Kappa");
            string outPath = Arg("-auditout", "PerfAudit_" + scene + ".txt");
            EditorSceneManager.OpenScene(BootSceneFactory.ScenesDir + "/" + scene + ".unity", OpenSceneMode.Single);
            File.WriteAllText(outPath, Report(scene));
            Debug.Log($"[SALTLINE] PerfAudit written to {outPath}");
        }

        static string Arg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static long Tris(Mesh m, int lod = 0)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (m.lodCount > 1)
                {
                    var r = m.GetLod(s, Mathf.Min(lod, m.lodCount - 1));
                    n += r.indexCount / 3;
                }
                else n += m.GetIndexCount(s) / 3;
            }
            return n;
        }

        static long RendererTris(Renderer r)
        {
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>() is MeshFilter f ? f.sharedMesh : null;
            return m != null ? Tris(m) : 0;
        }

        public static string Report(string scene)
        {
            var sb = new StringBuilder();
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var skinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            long tris = 0, shadowTris = 0;
            int casters = 0, batchingStatic = 0, occluders = 0, occludees = 0, withLods = 0, lodGroups = 0;
            var byMesh = new Dictionary<Mesh, (int count, long tris, bool shadows, string example)>();
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                if (m == null || !r.enabled) continue;
                long t = Tris(m);
                tris += t;
                bool cast = r.shadowCastingMode != ShadowCastingMode.Off;
                if (cast) { casters++; shadowTris += t; }
                var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((flags & StaticEditorFlags.BatchingStatic) != 0) batchingStatic++;
                if ((flags & StaticEditorFlags.OccluderStatic) != 0) occluders++;
                if ((flags & StaticEditorFlags.OccludeeStatic) != 0) occludees++;
                if (m.lodCount > 1) withLods++;
                if (r.GetComponentInParent<LODGroup>() != null) lodGroups++;
                byMesh.TryGetValue(m, out var e);
                byMesh[m] = (e.count + 1, e.tris + t, cast || e.shadows, e.example ?? r.name);
            }
            sb.AppendLine($"Scene {scene}: {renderers.Length} mesh renderers, {skinned.Length} skinned");
            sb.AppendLine($"Triangles (LOD0, all renderers): {tris:N0}; shadow casters {casters} with {shadowTris:N0} triangles");
            sb.AppendLine($"Static: batching {batchingStatic}, occluder {occluders}, occludee {occludees}; meshes with Mesh LOD {withLods}; in LOD Groups {lodGroups}");
            sb.AppendLine($"Distinct meshes {byMesh.Count}");
            sb.AppendLine();
            sb.AppendLine("Top meshes by total triangles (count x triangles = total; LOD levels; shadows; example renderer):");
            foreach (var kv in byMesh.OrderByDescending(k => k.Value.tris).Take(60))
            {
                var m = kv.Key;
                string lods = m.lodCount > 1 ? string.Join("/", Enumerable.Range(0, m.lodCount).Select(l => Tris(m, l).ToString())) : "-";
                var b = m.bounds.size;
                sb.AppendLine($"  {kv.Value.count,4} x {Tris(m),8:N0} = {kv.Value.tris,10:N0}  lods {lods}  size {b.x:F1}x{b.y:F1}x{b.z:F1}  {(kv.Value.shadows ? "shadows" : "no shadows")}  {m.name} ({kv.Value.example})");
            }
            // Triangle share by size class: small props are where LODs pay off.
            long small = 0, mid = 0, large = 0;
            foreach (var kv in byMesh)
            {
                float d = kv.Key.bounds.size.magnitude;
                if (d < 2f) small += kv.Value.tris; else if (d < 8f) mid += kv.Value.tris; else large += kv.Value.tris;
            }
            sb.AppendLine();
            sb.AppendLine($"Triangles by mesh size (bounds diagonal): under 2 m {small:N0}, 2-8 m {mid:N0}, over 8 m {large:N0}");
            sb.AppendLine($"Occlusion culling data: {(StaticOcclusionCulling.isRunning ? "baking" : StaticOcclusionCulling.umbraDataSize > 0 ? $"{StaticOcclusionCulling.umbraDataSize / 1024} KB" : "none")}");

            // Characters and weapons, which the map scene does not contain.
            var lib = AssetDatabase.LoadAssetAtPath<Saltline.FX.PresentationLibrary>("Assets/_Saltline/Resources/PresentationLibrary.asset");
            if (lib == null) lib = Resources.Load<Saltline.FX.PresentationLibrary>("PresentationLibrary");
            sb.AppendLine();
            if (lib == null) { sb.AppendLine("No PresentationLibrary found."); return sb.ToString(); }
            void Model(string label, GameObject go)
            {
                if (go == null) { sb.AppendLine($"{label}: none"); return; }
                var lod = go.GetComponentInChildren<LODGroup>();
                if (lod != null)
                {
                    var parts = lod.GetLODs().Select((l, i) => $"LOD{i} {l.renderers.Where(r => r != null).Sum(r => RendererTris(r)):N0} tris (to {l.screenRelativeTransitionHeight:0.###})");
                    sb.AppendLine($"{label}: {string.Join(", ", parts)}");
                }
                else sb.AppendLine($"{label}: {go.GetComponentsInChildren<Renderer>(true).Sum(r => RendererTris(r)):N0} tris, no LOD Group");
            }
            Model("Operator", lib.operatorPrefab);
            Model("First-person arms", lib.armsPrefab);
            foreach (var w in lib.weaponModels)
            {
                if (w == null) continue;
                var rs = w.GetComponentsInChildren<Renderer>(true);
                long tp = rs.Where(r => Saltline.FX.WeaponModel.IsThirdPersonMesh(r.name)).Sum(r => RendererTris(r));
                long fp = rs.Where(r => !Saltline.FX.WeaponModel.IsThirdPersonMesh(r.name)).Sum(r => RendererTris(r));
                string lods = string.Join(", ", rs.Where(r => Saltline.FX.WeaponModel.IsThirdPersonMesh(r.name)).Select(r => r.GetComponent<MeshFilter>() is MeshFilter mf ? mf.sharedMesh : null).Where(m => m != null && m.lodCount > 1)
                    .Select(m => m.name + " " + string.Join("/", Enumerable.Range(0, m.lodCount).Select(l => Tris(m, l)))));
                sb.AppendLine($"Weapon {w.name}: first person {fp:N0} tris, third person {tp:N0} tris{(lods.Length > 0 ? "; third-person Mesh LODs " + lods : "")}");
            }
            return sb.ToString();
        }
    }
}
