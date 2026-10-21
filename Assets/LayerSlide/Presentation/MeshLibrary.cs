using System.Collections.Generic;
using LayerSlide.Core;
using UnityEngine;

namespace LayerSlide.Presentation
{
    /// <summary>
    /// Procedural meshes for blocks, board pieces, symbols and markers. Each distinct mesh is built once per
    /// process and shared, so loading a level or dragging never creates new geometry (GDD 04 §2, 06 §11).
    /// All flat meshes lie in the X-Z plane facing +Y with their pivot at the bottom center.
    /// </summary>
    public static class MeshLibrary
    {
        public const float CornerRadius = 0.06f;
        public const int CornerSegments = 3;

        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>Rectangular slab with rounded vertical corners (block layers).</summary>
        public static Mesh RoundedSlab(float sizeX, float height, float sizeZ) =>
            GetOrCreate($"slab{sizeX:F3}x{height:F3}x{sizeZ:F3}", () => BuildRoundedSlab(sizeX, height, sizeZ));

        public static Mesh Box(float sizeX, float height, float sizeZ) =>
            GetOrCreate($"box{sizeX:F3}x{height:F3}x{sizeZ:F3}", () => BuildBox(sizeX, height, sizeZ));

        public static Mesh Frame(float sizeX, float sizeZ, float thickness) =>
            GetOrCreate($"frame{sizeX:F3}x{sizeZ:F3}x{thickness:F3}", () => BuildFrame(sizeX, sizeZ, thickness));

        public static Mesh Arrow(float length) =>
            GetOrCreate($"arrow{length:F3}", () => BuildArrow(length, 0.08f, 0.22f));

        public static Mesh Cross(float size, float thickness) =>
            GetOrCreate($"cross{size:F3}x{thickness:F3}", () => BuildCross(size, thickness));

        /// <summary>Circle (Red), square (Blue) or triangle (Yellow) with the given outer diameter.</summary>
        public static Mesh Symbol(LayerColor color, float diameter) =>
            GetOrCreate($"symbol{color}{diameter:F3}", () => BuildSymbol(color, diameter * 0.5f));

        static Mesh GetOrCreate(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            mesh = build();
            mesh.name = key;
            Cache[key] = mesh;
            return mesh;
        }

        static Mesh BuildRoundedSlab(float sizeX, float height, float sizeZ)
        {
            var outline = new List<Vector3>();
            var normals = new List<Vector3>();
            float r = Mathf.Min(CornerRadius, Mathf.Min(sizeX, sizeZ) * 0.5f);
            float cx = sizeX * 0.5f - r, cz = sizeZ * 0.5f - r;
            Vector2[] corners = { new Vector2(cx, cz), new Vector2(-cx, cz), new Vector2(-cx, -cz), new Vector2(cx, -cz) };

            for (int c = 0; c < 4; c++)
            for (int s = 0; s <= CornerSegments; s++)
            {
                float angle = (c * 90f + s * 90f / CornerSegments) * Mathf.Deg2Rad;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                outline.Add(new Vector3(corners[c].x, 0f, corners[c].y) + radial * r);
                normals.Add(radial);
            }

            var builder = new MeshBuilder();
            var up = Vector3.up * height;
            var center = up;
            for (int i = 0; i < outline.Count; i++)
            {
                int next = (i + 1) % outline.Count;
                builder.AddTriangle(center, outline[i] + up, outline[next] + up, Vector3.up);
                builder.AddQuad(outline[i], outline[i] + up, outline[next] + up, outline[next],
                    normals[i], normals[i], normals[next], normals[next]);
            }
            return builder.ToMesh();
        }

        static Mesh BuildBox(float sizeX, float height, float sizeZ)
        {
            float x = sizeX * 0.5f, z = sizeZ * 0.5f;
            var builder = new MeshBuilder();
            builder.AddQuad(new Vector3(-x, height, -z), new Vector3(-x, height, z), new Vector3(x, height, z), new Vector3(x, height, -z), Vector3.up);
            builder.AddQuad(new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, height, -z), new Vector3(-x, height, -z), Vector3.back);
            builder.AddQuad(new Vector3(-x, 0, z), new Vector3(-x, height, z), new Vector3(x, height, z), new Vector3(x, 0, z), Vector3.forward);
            builder.AddQuad(new Vector3(-x, 0, -z), new Vector3(-x, height, -z), new Vector3(-x, height, z), new Vector3(-x, 0, z), Vector3.left);
            builder.AddQuad(new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(x, height, z), new Vector3(x, height, -z), Vector3.right);
            return builder.ToMesh();
        }

        static Mesh BuildFrame(float sizeX, float sizeZ, float thickness)
        {
            float ox = sizeX * 0.5f, oz = sizeZ * 0.5f;
            float ix = ox - thickness, iz = oz - thickness;
            var builder = new MeshBuilder();
            AddFlatRect(builder, -ox, ox, -oz, -iz);
            AddFlatRect(builder, -ox, ox, iz, oz);
            AddFlatRect(builder, -ox, -ix, -iz, iz);
            AddFlatRect(builder, ix, ox, -iz, iz);
            return builder.ToMesh();
        }

        /// <summary>Arrow from the origin along +Z.</summary>
        static Mesh BuildArrow(float length, float shaftWidth, float headLength)
        {
            float shaftEnd = Mathf.Max(0f, length - headLength);
            float headHalf = shaftWidth * 1.4f;
            var builder = new MeshBuilder();
            AddFlatRect(builder, -shaftWidth * 0.5f, shaftWidth * 0.5f, 0f, shaftEnd);
            builder.AddTriangle(new Vector3(0, 0, length), new Vector3(headHalf, 0, shaftEnd), new Vector3(-headHalf, 0, shaftEnd), Vector3.up);
            return builder.ToMesh();
        }

        static Mesh BuildCross(float size, float thickness)
        {
            var builder = new MeshBuilder();
            foreach (float angle in new[] { 45f, -45f })
            {
                var rotation = Quaternion.Euler(0f, angle, 0f);
                float l = size * 0.5f, t = thickness * 0.5f;
                builder.AddQuad(rotation * new Vector3(-t, 0, -l), rotation * new Vector3(-t, 0, l),
                    rotation * new Vector3(t, 0, l), rotation * new Vector3(t, 0, -l), Vector3.up);
            }
            return builder.ToMesh();
        }

        static Mesh BuildSymbol(LayerColor color, float radius)
        {
            var builder = new MeshBuilder();
            switch (color)
            {
                case LayerColor.Red:
                    const int segments = 24;
                    for (int i = 0; i < segments; i++)
                    {
                        float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                        builder.AddTriangle(Vector3.zero, new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius,
                            new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius, Vector3.up);
                    }
                    break;
                case LayerColor.Blue:
                    float half = radius * 0.82f;
                    AddFlatRect(builder, -half, half, -half, half);
                    break;
                default:
                    builder.AddTriangle(new Vector3(0, 0, radius), new Vector3(radius * 0.866f, 0, -radius * 0.5f),
                        new Vector3(-radius * 0.866f, 0, -radius * 0.5f), Vector3.up);
                    break;
            }
            return builder.ToMesh();
        }

        static void AddFlatRect(MeshBuilder builder, float minX, float maxX, float minZ, float maxZ) =>
            builder.AddQuad(new Vector3(minX, 0, minZ), new Vector3(minX, 0, maxZ), new Vector3(maxX, 0, maxZ), new Vector3(maxX, 0, minZ), Vector3.up);

        /// <summary>Collects triangles and fixes their winding so each faces its given normal.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> _vertices = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<int> _triangles = new List<int>();

            public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal) =>
                AddTriangle(a, b, c, normal, normal, normal);

            public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc) < 0f)
                {
                    (b, c) = (c, b);
                    (nb, nc) = (nc, nb);
                }
                int start = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _normals.Add(na); _normals.Add(nb); _normals.Add(nc);
                _triangles.Add(start); _triangles.Add(start + 1); _triangles.Add(start + 2);
            }

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal) =>
                AddQuad(a, b, c, d, normal, normal, normal, normal);

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
            {
                AddTriangle(a, b, c, na, nb, nc);
                AddTriangle(a, c, d, na, nc, nd);
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
