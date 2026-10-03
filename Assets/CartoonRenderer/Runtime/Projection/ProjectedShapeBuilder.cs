using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CartoonProjection
{
    // Pure CPU geometry stage. Input is the visible, unlit material projection, not
    // camera lighting. Shared boundaries are simplified once for both adjacent fills.
    public static class ProjectedShapeBuilder
    {
        public sealed class Result
        {
            public Vector3[] vertices;
            public Color32[] colors;
            public int[] triangles;
            public Vector3[] lines;
            public Color32[] regionPixels;
            public int regions, sourceEdges, simplifiedEdges;
            public double milliseconds;
        }

        struct Edge { public int a, b; public Edge(int a, int b) { this.a = a; this.b = b; } }
        struct Segment
        {
            public Vector2 a, b;
            public Segment(Vector2 a, Vector2 b) { this.a = a; this.b = b; }
            public float X(float y) => a.x + (b.x - a.x) * ((y - a.y) / (b.y - a.y));
        }

        public static Result Build(Color32[] pixels, Color32[] identities, int width, int height,
            int colorStep, int minimumArea, float epsilon)
        {
            var watch = Stopwatch.StartNew();
            int count = width * height;
            if (pixels.Length != count || identities.Length != count)
                throw new ArgumentException("Projection buffer dimensions do not match.");
            colorStep = Math.Max(1, colorStep);
            var parent = new int[count];
            var sizes = new int[count];
            var sums = new Vector3[count];
            var objectIds = new int[count];
            var keys = new long[count];
            int Find(int n)
            {
                while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; }
                return n;
            }
            void Join(int a, int b)
            {
                a = Find(a); b = Find(b);
                if (a == b) return;
                if (sizes[a] < sizes[b]) (a, b) = (b, a);
                parent[b] = a; sizes[a] += sizes[b]; sums[a] += sums[b];
            }
            for (int i = 0; i < count; i++)
            {
                var c = pixels[i]; var id = identities[i];
                objectIds[i] = id.r | (id.g << 8) | (id.b << 16) | (id.a << 24);
                parent[i] = i; sizes[i] = 1; sums[i] = new Vector3(c.r, c.g, c.b);
                keys[i] = ((long)(uint)objectIds[i] << 24) | (uint)((c.r / colorStep << 16) | (c.g / colorStep << 8) | c.b / colorStep);
                if (i % width > 0 && keys[i] == keys[i - 1]) Join(i, i - 1);
                if (i >= width && keys[i] == keys[i - width]) Join(i, i - width);
            }

            // Merge small islands only with the same source draw / paint layer.
            // Never delete a region to transparency, or merge foreground into sky.
            for (int iteration = 0; iteration < 3 && minimumArea > 1; iteration++)
            {
                var targets = new Dictionary<int, (int root, float distance)>();
                void Consider(int a, int b)
                {
                    if (objectIds[a] != objectIds[b]) return;
                    a = Find(a); b = Find(b);
                    if (a == b) return;
                    float d = (sums[a] / sizes[a] - sums[b] / sizes[b]).sqrMagnitude;
                    if (sizes[a] < minimumArea && (!targets.TryGetValue(a, out var t) || d < t.distance)) targets[a] = (b, d);
                    if (sizes[b] < minimumArea && (!targets.TryGetValue(b, out t) || d < t.distance)) targets[b] = (a, d);
                }
                for (int i = 0; i < count; i++)
                {
                    if (i % width > 0) Consider(i, i - 1);
                    if (i >= width) Consider(i, i - width);
                }
                if (targets.Count == 0) break;
                foreach (var t in targets) if (sizes[Find(t.Key)] < minimumArea) Join(t.Key, t.Value.root);
            }

            var labels = new int[count];
            var rootLabels = new Dictionary<int, int>();
            var palette = new List<Color32>();
            var regionPixels = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                int root = Find(i);
                if (!rootLabels.TryGetValue(root, out int label))
                {
                    label = palette.Count; rootLabels[root] = label;
                    var c = sums[root] / sizes[root];
                    palette.Add(new Color32((byte)c.x, (byte)c.y, (byte)c.z, 255));
                }
                labels[i] = label; regionPixels[i] = palette[label];
            }

            int stride = width + 1;
            var groups = new Dictionary<long, List<Edge>>();
            var degree = new int[(width + 1) * (height + 1)];
            int sourceEdges = 0;
            void Add(int a, int b, int left, int right)
            {
                if (left == right) return;
                int lo = Math.Min(left, right) + 1, hi = Math.Max(left, right) + 1;
                long key = ((long)lo << 32) | (uint)hi;
                if (!groups.TryGetValue(key, out var edges)) groups[key] = edges = new List<Edge>();
                edges.Add(new Edge(a, b)); degree[a]++; degree[b]++; sourceEdges++;
            }
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int i = y * width + x, p = y * stride + x;
                if (x == 0) Add(p, p + stride, labels[i], -1);
                if (y == 0) Add(p, p + 1, labels[i], -1);
                Add(p + 1, p + stride + 1, labels[i], x + 1 < width ? labels[i + 1] : -1);
                Add(p + stride, p + stride + 1, labels[i], y + 1 < height ? labels[i + width] : -1);
            }

            var segments = new List<Segment>[palette.Count];
            for (int i = 0; i < segments.Length; i++) segments[i] = new List<Segment>();
            var lines = new List<Vector3>();
            int simplifiedEdges = 0;
            foreach (var group in groups)
            {
                int labelA = (int)(group.Key >> 32) - 1, labelB = (int)(group.Key & uint.MaxValue) - 1;
                var edges = group.Value;
                var adjacent = new Dictionary<int, List<int>>();
                for (int i = 0; i < edges.Count; i++)
                {
                    void Link(int p) { if (!adjacent.TryGetValue(p, out var list)) adjacent[p] = list = new List<int>(); list.Add(i); }
                    Link(edges[i].a); Link(edges[i].b);
                }
                var used = new bool[edges.Count];
                void Trace(int start, int first)
                {
                    var points = new List<Vector2>();
                    int p = start, current = first;
                    points.Add(new Vector2(p % stride, p / stride));
                    while (current >= 0 && !used[current])
                    {
                        used[current] = true;
                        var e = edges[current]; p = e.a == p ? e.b : e.a;
                        points.Add(new Vector2(p % stride, p / stride));
                        if (p == start || degree[p] != 2 || adjacent[p].Count != 2) break;
                        current = -1;
                        foreach (int next in adjacent[p]) if (!used[next]) { current = next; break; }
                    }
                    var simple = Simplify(points, epsilon);
                    for (int j = 1; j < simple.Count; j++)
                    {
                        var s = new Segment(simple[j - 1], simple[j]);
                        if (labelA >= 0) segments[labelA].Add(s);
                        if (labelB >= 0) segments[labelB].Add(s);
                        lines.Add(new Vector3(s.a.x / width, s.a.y / height, 0));
                        lines.Add(new Vector3(s.b.x / width, s.b.y / height, 0));
                        simplifiedEdges++;
                    }
                }
                // Junction endpoints are shared anchors. Process cycles afterwards.
                foreach (var entry in adjacent)
                    if (degree[entry.Key] != 2 || entry.Value.Count != 2)
                        foreach (int edge in entry.Value) if (!used[edge]) Trace(entry.Key, edge);
                for (int i = 0; i < edges.Count; i++) if (!used[i]) Trace(edges[i].a, i);
            }

            // Even-odd trapezoid tessellation of the actual simplified boundaries.
            // Holes require no bridges; every pair of crossings is an occupied span.
            var vertices = new List<Vector3>(); var colors = new List<Color32>(); var indices = new List<int>();
            for (int label = 0; label < segments.Length; label++)
            {
                var list = segments[label]; var ys = new SortedSet<float>();
                foreach (var s in list) { ys.Add(s.a.y); ys.Add(s.b.y); }
                var levels = new List<float>(ys); var crossings = new List<Segment>();
                for (int row = 1; row < levels.Count; row++)
                {
                    float y0 = levels[row - 1], y1 = levels[row], mid = (y0 + y1) * 0.5f;
                    crossings.Clear();
                    foreach (var s in list)
                        if (Math.Min(s.a.y, s.b.y) < mid && Math.Max(s.a.y, s.b.y) > mid) crossings.Add(s);
                    crossings.Sort((a, b) => a.X(mid).CompareTo(b.X(mid)));
                    for (int j = 0; j + 1 < crossings.Count; j += 2)
                    {
                        var l = crossings[j]; var r = crossings[j + 1];
                        if (r.X(mid) - l.X(mid) < 0.0001f) continue;
                        int v = vertices.Count;
                        vertices.Add(new Vector3(l.X(y0) / width, y0 / height, 0));
                        vertices.Add(new Vector3(r.X(y0) / width, y0 / height, 0));
                        vertices.Add(new Vector3(r.X(y1) / width, y1 / height, 0));
                        vertices.Add(new Vector3(l.X(y1) / width, y1 / height, 0));
                        for (int k = 0; k < 4; k++) colors.Add(palette[label]);
                        indices.Add(v); indices.Add(v + 1); indices.Add(v + 2);
                        indices.Add(v); indices.Add(v + 2); indices.Add(v + 3);
                    }
                }
            }
            watch.Stop();
            return new Result { vertices = vertices.ToArray(), colors = colors.ToArray(), triangles = indices.ToArray(),
                lines = lines.ToArray(), regionPixels = regionPixels, regions = palette.Count,
                sourceEdges = sourceEdges, simplifiedEdges = simplifiedEdges, milliseconds = watch.Elapsed.TotalMilliseconds };
        }

        static List<Vector2> Simplify(List<Vector2> points, float epsilon)
        {
            if (points.Count < 3 || epsilon <= 0) return points;
            // Split a closed loop at its farthest point before RDP, keeping >=3 sides.
            if (points[0] == points[points.Count - 1])
            {
                int far = 1; float distance = 0;
                for (int i = 1; i < points.Count - 1; i++)
                    if ((points[i] - points[0]).sqrMagnitude > distance) { far = i; distance = (points[i] - points[0]).sqrMagnitude; }
                var a = Simplify(points.GetRange(0, far + 1), epsilon);
                var b = Simplify(points.GetRange(far, points.Count - far), epsilon);
                a.RemoveAt(a.Count - 1); a.AddRange(b);
                return a.Count >= 4 ? a : Simplify(points, epsilon * 0.5f < 0.1f ? 0 : epsilon * 0.5f);
            }
            var keep = new bool[points.Count]; keep[0] = keep[points.Count - 1] = true;
            var stack = new Stack<(int a, int b)>(); stack.Push((0, points.Count - 1));
            while (stack.Count > 0)
            {
                var range = stack.Pop(); var a = points[range.a]; var delta = points[range.b] - a;
                float max = epsilon * epsilon; int far = -1;
                for (int i = range.a + 1; i < range.b; i++)
                {
                    float t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(points[i] - a, delta) / delta.sqrMagnitude) : 0;
                    float d = (points[i] - a - t * delta).sqrMagnitude;
                    if (d > max) { max = d; far = i; }
                }
                if (far < 0) continue;
                keep[far] = true; stack.Push((range.a, far)); stack.Push((far, range.b));
            }
            var result = new List<Vector2>();
            for (int i = 0; i < points.Count; i++) if (keep[i]) result.Add(points[i]);
            return result;
        }
    }
}
