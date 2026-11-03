using System.Collections.Generic;
using Saltline.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Saltline.FX
{
    /// <summary>
    /// Spent cases thrown from the ejection port: a pooled ballistic simulation (gravity, tumble,
    /// bounces off the world with raycasts, then lying on their side) with lathe-built meshes per
    /// calibre. Purely cosmetic; first-person cases start on the viewmodel layer and move to the
    /// world once they are clear of the gun.
    /// </summary>
    public sealed class ShellCasings
    {
        struct Shell
        {
            public Transform T;
            public MeshFilter F;
            public MeshRenderer R;
            public Vector3 Pos, Vel, SpinAxis;
            public Quaternion Rot;
            public float Spin, Age, Radius, VmUntil;
            public bool Active, Resting, OnViewmodel;
        }

        const float Gravity = 9.81f;
        readonly Shell[] m_S;
        int m_Next;
        readonly Mesh[] m_Mesh = new Mesh[5];
        readonly Material[][] m_Mats = new Material[5][];
        readonly float[] m_Radius = new float[5];
        public float RestSeconds = 8f;
        public int Active { get; private set; }

        /// <summary>Where the cases that have come to rest lie (their average position), for reviews and tests.</summary>
        public bool RestingCentroid(out Vector3 centre, out int count)
        {
            centre = Vector3.zero;
            count = 0;
            foreach (var s in m_S)
                if (s.Active && s.Resting) { centre += s.Pos; count++; }
            if (count > 0) centre /= count;
            return count > 0;
        }

        public ShellCasings(Transform parent, int capacity, Material brass, Material hull)
        {
            m_S = new Shell[capacity];
            // Profiles: (distance from the base, radius) in millimetres; original approximations of common calibres.
            Build(WeaponClass.AssaultRifle, new[] { (0f, 0f), (0f, 4.7f), (1.1f, 4.7f), (1.4f, 4.1f), (2.6f, 4.1f), (3.2f, 4.75f), (36f, 4.5f), (39f, 3.1f), (45f, 3.1f), (45f, 2.7f), (43f, 2.7f), (43f, 0f) }, -1f, brass, hull);
            Build(WeaponClass.SMG, new[] { (0f, 0f), (0f, 4.9f), (1.0f, 4.9f), (1.3f, 4.2f), (2.0f, 4.2f), (2.4f, 4.9f), (19f, 4.8f), (19f, 4.3f), (17f, 4.3f), (17f, 0f) }, -1f, brass, hull);
            m_Mesh[(int)WeaponClass.Pistol] = m_Mesh[(int)WeaponClass.SMG];
            m_Mats[(int)WeaponClass.Pistol] = m_Mats[(int)WeaponClass.SMG];
            m_Radius[(int)WeaponClass.Pistol] = m_Radius[(int)WeaponClass.SMG];
            Build(WeaponClass.Sniper, new[] { (0f, 0f), (0f, 6.0f), (1.3f, 6.0f), (1.6f, 5.2f), (2.8f, 5.2f), (3.4f, 6.0f), (39f, 5.7f), (43f, 4.4f), (51f, 4.3f), (51f, 3.9f), (49f, 3.9f), (49f, 0f) }, -1f, brass, hull);
            // Shotgun shell: brass head to 14 mm, plastic hull after it, folded crimp at the end.
            Build(WeaponClass.Shotgun, new[] { (0f, 0f), (0f, 11.0f), (1.4f, 11.0f), (1.6f, 10.3f), (14f, 10.3f), (14f, 10.2f), (68f, 10.2f), (70f, 9.0f), (70.5f, 3f), (70.5f, 0f) }, 14.05f, brass, hull);

            for (int i = 0; i < m_S.Length; i++)
            {
                var go = new GameObject("Shell");
                go.transform.SetParent(parent, false);
                go.layer = Layers.Debris;
                ref var s = ref m_S[i];
                s.T = go.transform;
                s.F = go.AddComponent<MeshFilter>();
                s.R = go.AddComponent<MeshRenderer>();
                s.R.shadowCastingMode = ShadowCastingMode.On;
                s.R.lightProbeUsage = LightProbeUsage.BlendProbes;
                s.R.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                go.SetActive(false);
            }
        }

        void Build(WeaponClass cls, (float z, float r)[] mm, float hullFrom, Material brass, Material hull)
        {
            int segs = cls == WeaponClass.Shotgun ? 20 : 14;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>() };
            float maxR = 0f;
            for (int i = 0; i + 1 < mm.Length; i++)
            {
                Vector2 a = new Vector2(mm[i].z, mm[i].r) * 0.001f, b = new Vector2(mm[i + 1].z, mm[i + 1].r) * 0.001f;
                Vector2 d = b - a;
                if (d.sqrMagnitude < 1e-12f) continue;
                d.Normalize();
                // Profile normal in (radial, axial): the outline runs base -> rim -> body -> mouth -> inner
                // wall, so this faces out on the body, back on the base, forward on the mouth and in on the inner wall.
                float nr = d.x, nz = -d.y;
                int sub = hullFrom > 0f && (mm[i].z + mm[i + 1].z) * 0.5f > hullFrom ? 1 : 0;
                int baseIndex = verts.Count;
                for (int j = 0; j <= segs; j++)
                {
                    float th = j / (float)segs * Mathf.PI * 2f;
                    float c = Mathf.Cos(th), s = Mathf.Sin(th);
                    verts.Add(new Vector3(a.y * c, a.y * s, a.x));
                    verts.Add(new Vector3(b.y * c, b.y * s, b.x));
                    norms.Add(new Vector3(c * nr, s * nr, nz));
                    norms.Add(new Vector3(c * nr, s * nr, nz));
                }
                maxR = Mathf.Max(maxR, a.y, b.y);
                for (int j = 0; j < segs; j++)
                {
                    int v0 = baseIndex + j * 2, v1 = v0 + 1, v2 = v0 + 2, v3 = v0 + 3;
                    AddTri(tris[sub], verts, norms, v0, v2, v1);
                    AddTri(tris[sub], verts, norms, v1, v2, v3);
                }
            }
            var m = new Mesh { name = $"Case_{cls}" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            bool two = tris[1].Count > 0;
            m.subMeshCount = two ? 2 : 1;
            m.SetTriangles(tris[0], 0);
            if (two) m.SetTriangles(tris[1], 1);
            m.RecalculateBounds();
            m_Mesh[(int)cls] = m;
            m_Mats[(int)cls] = two ? new[] { brass, hull } : new[] { brass };
            m_Radius[(int)cls] = maxR;
        }

        /// <summary>Adds a triangle wound so that it faces along the vertices' normal (Unity: clockwise from the front).</summary>
        static void AddTri(List<int> t, List<Vector3> v, List<Vector3> n, int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (face.sqrMagnitude < 1e-16f) return;
            if (Vector3.Dot(face, n[a] + n[b] + n[c]) < 0f) { int k = b; b = c; c = k; }
            t.Add(a); t.Add(b); t.Add(c);
        }

        /// <summary>Throws one case. <paramref name="gun"/> is the gun's frame (bore along +z).</summary>
        public void Eject(Vector3 port, Quaternion gun, WeaponClass cls, Vector3 inherit, bool viewmodel)
        {
            int k = (int)cls;
            if (k < 0 || k >= m_Mesh.Length || m_Mesh[k] == null) return;
            ref var s = ref m_S[m_Next];
            m_Next = (m_Next + 1) % m_S.Length;
            if (!s.Active) Active++;

            Vector3 right = gun * Vector3.right, up = gun * Vector3.up, fwd = gun * Vector3.forward;
            Vector3 v = cls switch
            {
                WeaponClass.Pistol => right * Random.Range(1.3f, 1.9f) + up * Random.Range(2.0f, 2.8f) - fwd * Random.Range(0.2f, 0.5f),
                WeaponClass.Shotgun => right * Random.Range(1.6f, 2.2f) + up * Random.Range(0.4f, 0.9f) + fwd * Random.Range(-0.1f, 0.3f),
                WeaponClass.Sniper => right * Random.Range(1.2f, 1.7f) + up * Random.Range(0.8f, 1.3f) - fwd * Random.Range(0.0f, 0.3f),
                WeaponClass.SMG => right * Random.Range(1.9f, 2.6f) + up * Random.Range(0.9f, 1.5f) - fwd * Random.Range(0.1f, 0.4f),
                _ => right * Random.Range(2.3f, 3.0f) + up * Random.Range(1.2f, 1.9f) - fwd * Random.Range(0.2f, 0.6f),
            };
            s.Pos = port;
            s.Vel = v + inherit;
            s.Rot = gun;
            s.SpinAxis = (up + Random.insideUnitSphere * 0.4f).normalized;
            s.Spin = Random.Range(1800f, 3600f) * (Random.value < 0.5f ? -1f : 1f);
            s.Age = 0f;
            s.Radius = m_Radius[k];
            s.Active = true;
            s.Resting = false;
            s.OnViewmodel = viewmodel;
            s.VmUntil = 0.16f;
            s.F.sharedMesh = m_Mesh[k];
            s.R.sharedMaterials = m_Mats[k];
            s.R.shadowCastingMode = viewmodel ? ShadowCastingMode.Off : ShadowCastingMode.On;
            s.T.gameObject.layer = viewmodel ? Layers.Viewmodel : Layers.Debris;
            s.T.localScale = Vector3.one;
            s.T.SetPositionAndRotation(s.Pos, s.Rot);
            s.T.gameObject.SetActive(true);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < m_S.Length; i++)
            {
                ref var s = ref m_S[i];
                if (!s.Active) continue;
                s.Age += dt;
                if (s.OnViewmodel && s.Age > s.VmUntil)
                {
                    s.OnViewmodel = false;
                    s.T.gameObject.layer = Layers.Debris;
                    s.R.shadowCastingMode = ShadowCastingMode.On;
                }
                if (s.Age > RestSeconds)
                {
                    float k = 1f - (s.Age - RestSeconds) / 0.4f;
                    if (k <= 0f) { s.Active = false; Active--; s.T.gameObject.SetActive(false); continue; }
                    s.T.localScale = Vector3.one * k;
                }
                if (s.Resting) continue;

                s.Vel += Vector3.down * (Gravity * dt);
                Vector3 step = s.Vel * dt;
                float len = step.magnitude;
                if (len > 1e-6f && Physics.Raycast(s.Pos, step / len, out var hit, len + s.Radius, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 n = hit.normal;
                    Vector3 vn = Vector3.Dot(s.Vel, n) * n;
                    Vector3 vt = s.Vel - vn;
                    s.Vel = -vn * 0.32f + vt * 0.55f;
                    s.Spin *= 0.45f;
                    s.SpinAxis = Vector3.Slerp(s.SpinAxis, n, 0.5f).normalized;
                    s.Pos = hit.point + n * s.Radius;
                    if (s.Vel.magnitude < 0.45f && n.y > 0.55f)
                    {
                        // Lie on its side along whatever direction the case was pointing.
                        Vector3 axis = Vector3.ProjectOnPlane(s.Rot * Vector3.forward, n);
                        if (axis.sqrMagnitude < 1e-4f) axis = Vector3.ProjectOnPlane(Vector3.forward, n);
                        s.Rot = Quaternion.LookRotation(axis.normalized, n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                        s.Resting = true;
                    }
                }
                else s.Pos += step;
                if (!s.Resting) s.Rot = Quaternion.AngleAxis(s.Spin * dt, s.SpinAxis) * s.Rot;
                s.T.SetPositionAndRotation(s.Pos, s.Rot);
                if (s.Pos.y < -50f) { s.Active = false; Active--; s.T.gameObject.SetActive(false); }
            }
        }
    }
}
