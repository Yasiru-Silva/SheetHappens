using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// The pathfinding module: a 3D grid of the free air inside the house, built at runtime
    /// from the level colliders, searched with A*. Ghosts float, so stairwells and the attic
    /// hatch are just more free cells. Barricades block cells dynamically.
    /// </summary>
    public class GhostGrid : MonoBehaviour
    {
        [Tooltip("Upper limit on grid cells; the cell size grows if the level is bigger.")]
        public int maxCells = 150000;
        public float minCellSize = 0.55f;
        [Tooltip("A cell is 'grounded' (cheap to travel) if a floor is this close below it.")]
        public float groundProbe = 1.6f;
        [Tooltip("Ghosts never float higher than this above a floor.")]
        public float maxHeightAboveFloor = 4.5f;
        [Tooltip("A cell counts as indoors if a roof or ceiling is within this distance above it.")]
        public float roofProbe = 12f;
        public float airCostMultiplier = 2.5f;
        [Tooltip("Extra cost for cells right next to a wall, so ghosts keep their bodies out of the walls.")]
        public float nearWallCostMultiplier = 2f;
        [Tooltip("How far from a wall a ghost prefers to stay (metres).")]
        public float wallClearance = 0.3f;
        public bool drawGizmos;

        public bool Built { get; private set; }
        public float Cell { get; private set; }
        public readonly List<int> GroundCells = new List<int>();
        public readonly List<int> WalkCells = new List<int>();

        Vector3 origin;
        int nx, ny, nz;
        bool[] walk, ground, nearWall;
        // edge[a][i]: can a ghost move from cell i to its +a neighbour (a = x, y, z)? false if a wall or floor is between them.
        bool[][] edge;
        static readonly RaycastHit[] Hits = new RaycastHit[16];
        int[] blocked;
        float[] gScore;
        int[] parent, openStamp, closedStamp;
        int searchId;
        readonly List<HeapItem> heap = new List<HeapItem>();
        static readonly Collider[] Buffer = new Collider[64];
        static Vector3Int[] offsets;
        static float[] offsetCost;

        struct HeapItem { public float f; public int i; }

        // ------------------------------------------------------------------ build

        public void Build()
        {
            if (!TryGetLevelBounds(out Bounds b)) { Built = false; return; }
            b.Expand(1.5f);
            float vol = b.size.x * b.size.y * b.size.z;
            Cell = Mathf.Max(minCellSize, Mathf.Pow(vol / Mathf.Max(1000, maxCells), 1f / 3f));
            nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / Cell));
            ny = Mathf.Max(1, Mathf.CeilToInt(b.size.y / Cell));
            nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / Cell));
            origin = b.min;
            int n = nx * ny * nz;
            walk = new bool[n]; ground = new bool[n]; nearWall = new bool[n]; blocked = new int[n];
            edge = new[] { new bool[n], new bool[n], new bool[n] };
            gScore = new float[n]; parent = new int[n]; openStamp = new int[n]; closedStamp = new int[n];
            BuildOffsets();

            var free = new bool[n];
            var indoor = new bool[n];
            int indoorCount = 0;
            Vector3 half = Vector3.one * Cell * 0.4f;
            // Ladder shafts are the ghosts' only way up into the attic: the hatch is narrow, so check it with a finer
            // probe, and allow the tall climb from the floor below.
            var shafts = new List<Bounds>();
            foreach (var l in FindObjectsByType<Ladder>(FindObjectsSortMode.None))
            {
                var col = l.GetComponent<Collider>();
                if (col == null) continue;
                Bounds sb = col.bounds;
                sb.SetMinMax(sb.min, sb.max + Vector3.up * 1.2f);
                shafts.Add(sb);
            }
            Vector3 shaftHalf = Vector3.one * Mathf.Min(0.12f, Cell * 0.25f);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Center(i);
                bool inShaft = false;
                foreach (var sb in shafts) if (sb.Contains(p)) { inShaft = true; break; }
                if (IsSolid(p, inShaft ? shaftHalf : half)) continue;
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit floorHit, inShaft ? 12f : maxHeightAboveFloor, ~0, QueryTriggerInteraction.Ignore)) continue;
                free[i] = true;
                ground[i] = floorHit.distance <= groundProbe;
                if (Physics.Raycast(p, Vector3.up, roofProbe, ~0, QueryTriggerInteraction.Ignore)) { indoor[i] = true; indoorCount++; }
            }
            // Thin walls and floors can sit in the gap between two free cells: test the line between every pair of
            // neighbouring cells so ghosts can never cross a wall, floor or ceiling.
            int[] step = { 1, nx, nx * ny };
            int cut = 0;
            for (int i = 0; i < n; i++)
            {
                if (!free[i]) continue;
                Coords(i, out int x, out int y, out int z);
                int[] c3 = { x, y, z }, max = { nx, ny, nz };
                for (int a = 0; a < 3; a++)
                {
                    if (c3[a] + 1 >= max[a]) continue;
                    int nb = i + step[a];
                    if (!free[nb]) continue;
                    bool open = !SolidBetween(Center(i), Center(nb));
                    edge[a][i] = open;
                    if (!open) cut++;
                }
                Vector3 wallHalf = new Vector3(Cell * 0.5f + wallClearance, Cell * 0.3f, Cell * 0.5f + wallClearance);
                nearWall[i] = IsSolid(Center(i), wallHalf);
            }
            bool useIndoor = indoorCount > 150;
            for (int i = 0; i < n; i++) walk[i] = free[i] && (!useIndoor || indoor[i]);

            KeepLargestComponent();

            GroundCells.Clear(); WalkCells.Clear();
            for (int i = 0; i < n; i++)
            {
                if (!walk[i]) continue;
                WalkCells.Add(i);
                if (ground[i]) GroundCells.Add(i);
            }
            Built = WalkCells.Count > 0;
            Debug.Log($"[SheetHappens] Ghost grid: {nx}x{ny}x{nz} cells of {Cell:0.00} m, {WalkCells.Count} walkable, {GroundCells.Count} near a floor, {cut} wall/floor crossings blocked.");
        }

        bool TryGetLevelBounds(out Bounds result)
        {
            var cols = new List<Collider>();
            foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c.isTrigger || c is CharacterController || IsPassable(c)) continue;
                cols.Add(c);
            }
            result = default;
            if (cols.Count == 0) return false;
            // Trim stray far-away objects: keep colliders whose centre is inside the 3rd-97th percentile box (+ margin).
            Vector3 lo = Percentile(cols, 0.03f), hi = Percentile(cols, 0.97f);
            Vector3 margin = (hi - lo) * 0.25f + Vector3.one * 2f;
            var box = new Bounds((lo + hi) * 0.5f, hi - lo + margin * 2f);
            bool has = false;
            foreach (var c in cols)
            {
                if (!box.Contains(c.bounds.center)) continue;
                if (!has) { result = c.bounds; has = true; } else result.Encapsulate(c.bounds);
            }
            return has;
        }

        static Vector3 Percentile(List<Collider> cols, float q)
        {
            var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
            foreach (var c in cols) { var p = c.bounds.center; xs.Add(p.x); ys.Add(p.y); zs.Add(p.z); }
            xs.Sort(); ys.Sort(); zs.Sort();
            int k = Mathf.Clamp(Mathf.RoundToInt(q * (xs.Count - 1)), 0, xs.Count - 1);
            return new Vector3(xs[k], ys[k], zs[k]);
        }

        public static bool IsPassable(Collider c)
        {
            if (c.isTrigger) return true;
            if (c.attachedRigidbody != null) return true; // props, barricades (handled dynamically)
            return c.GetComponentInParent<GhostPassable>() != null || c.GetComponentInParent<GhostAgent>() != null || c.GetComponentInParent<PlayerController>() != null;
        }

        static bool IsSolid(Vector3 p, Vector3 half)
        {
            int count = Physics.OverlapBoxNonAlloc(p, half, Buffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
                if (!IsPassable(Buffer[k]) && !(Buffer[k] is CharacterController)) return true;
            return false;
        }

        /// <summary>Is there a wall, floor or ceiling on the line between two points? Checked both ways (mesh colliders are one-sided).</summary>
        public static bool SolidBetween(Vector3 a, Vector3 b)
        {
            return HitsSolid(a, b) || HitsSolid(b, a);
        }

        static bool HitsSolid(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            int count = Physics.RaycastNonAlloc(from, d.normalized, Hits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
                if (!IsPassable(Hits[k].collider) && !(Hits[k].collider is CharacterController)) return true;
            return false;
        }

        /// <summary>Is the step from cell c one cell along axis a (dir +1 or -1) open?</summary>
        bool Open(int c, int a, int dir)
        {
            if (dir > 0) return edge[a][c];
            int s = a == 0 ? 1 : a == 1 ? nx : nx * ny;
            return edge[a][c - s];
        }

        void KeepLargestComponent()
        {
            int n = walk.Length;
            var comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;
            int best = -1, bestSize = 0, id = 0;
            var queue = new Queue<int>();
            for (int s = 0; s < n; s++)
            {
                if (!walk[s] || comp[s] >= 0) continue;
                int size = 0;
                comp[s] = id; queue.Enqueue(s);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue(); size++;
                    ForEachNeighbour(c, (nb, cost) => { if (comp[nb] < 0 && walk[nb]) { comp[nb] = id; queue.Enqueue(nb); } });
                }
                if (size > bestSize) { bestSize = size; best = id; }
                id++;
            }
            for (int i = 0; i < n; i++) walk[i] = walk[i] && comp[i] == best;
        }

        static void BuildOffsets()
        {
            if (offsets != null) return;
            var list = new List<Vector3Int>(); var costs = new List<float>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        list.Add(new Vector3Int(dx, dy, dz));
                        costs.Add(new Vector3(dx, dy, dz).magnitude);
                    }
            offsets = list.ToArray(); offsetCost = costs.ToArray();
        }

        void ForEachNeighbour(int c, Action<int, float> visit)
        {
            Coords(c, out int x, out int y, out int z);
            for (int k = 0; k < offsets.Length; k++)
            {
                var o = offsets[k];
                int X = x + o.x, Y = y + o.y, Z = z + o.z;
                if (X < 0 || Y < 0 || Z < 0 || X >= nx || Y >= ny || Z >= nz) continue;
                int nb = Index(X, Y, Z);
                if (!walk[nb]) continue;
                // No corner cutting: every axis step of a diagonal must be free too.
                if (o.x != 0 && !walk[Index(x + o.x, y, z)]) continue;
                if (o.y != 0 && !walk[Index(x, y + o.y, z)]) continue;
                if (o.z != 0 && !walk[Index(x, y, z + o.z)]) continue;
                // ...and no wall or floor may be crossed on the way (checked from both ends for diagonals).
                if (o.x != 0 && (!Open(c, 0, o.x) || !Open(nb, 0, -o.x))) continue;
                if (o.y != 0 && (!Open(c, 1, o.y) || !Open(nb, 1, -o.y))) continue;
                if (o.z != 0 && (!Open(c, 2, o.z) || !Open(nb, 2, -o.z))) continue;
                visit(nb, offsetCost[k] * Cell);
            }
        }

        // ------------------------------------------------------------------ queries

        int Index(int x, int y, int z) => x + nx * (y + ny * z);
        void Coords(int i, out int x, out int y, out int z) { x = i % nx; int r = i / nx; y = r % ny; z = r / ny; }

        public Vector3 Center(int i) { Coords(i, out int x, out int y, out int z); return origin + new Vector3((x + 0.5f) * Cell, (y + 0.5f) * Cell, (z + 0.5f) * Cell); }
        /// <summary>Where a ghost's base sits when it is in this cell.</summary>
        public Vector3 PathPoint(int i) => Center(i) - Vector3.up * Cell * 0.3f;
        public bool IsGround(int i) => i >= 0 && ground[i];
        public bool IsWalkable(int i) => i >= 0 && i < walk.Length && walk[i];
        public bool IsBlocked(int i) => i >= 0 && blocked[i] > 0;

        public int NearestWalkable(Vector3 p, int maxRadius = 6)
        {
            if (!Built) return -1;
            int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / Cell), 0, nx - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - origin.y) / Cell), 0, ny - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((p.z - origin.z) / Cell), 0, nz - 1);
            int best = -1; float bestD = float.MaxValue;
            for (int r = 0; r <= maxRadius; r++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                    for (int y = cy - r; y <= cy + r; y++)
                        for (int z = cz - r; z <= cz + r; z++)
                        {
                            if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) continue;
                            if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Max(Mathf.Abs(y - cy), Mathf.Abs(z - cz))) != r) continue;
                            int i = Index(x, y, z);
                            if (!walk[i]) continue;
                            float d = (Center(i) - p).sqrMagnitude;
                            if (d < bestD) { bestD = d; best = i; }
                        }
                if (best >= 0) return best;
            }
            return -1;
        }

        public void AddBlock(Bounds b, int delta)
        {
            if (!Built) return;
            b.Expand(Cell * 0.4f);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - origin.x) / Cell), 0, nx - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((b.max.x - origin.x) / Cell), 0, nx - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((b.min.y - origin.y) / Cell), 0, ny - 1), y1 = Mathf.Clamp(Mathf.FloorToInt((b.max.y - origin.y) / Cell), 0, ny - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - origin.z) / Cell), 0, nz - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((b.max.z - origin.z) / Cell), 0, nz - 1);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        int i = Index(x, y, z);
                        blocked[i] = Mathf.Max(0, blocked[i] + delta);
                    }
        }

        public bool AnyBlocked(List<int> path, int from, int lookAhead)
        {
            for (int k = from; k < path.Count && k < from + lookAhead; k++)
                if (blocked[path[k]] > 0) return true;
            return false;
        }

        /// <summary>
        /// A* from 'from' to 'to'. extraCost adds a per-cell penalty (Green Sneak uses it to avoid the player).
        /// With allowBlocked, barricaded cells are very expensive instead of impossible.
        /// </summary>
        public bool FindPath(Vector3 from, Vector3 to, Func<int, float> extraCost, List<int> result, bool allowBlocked = false)
        {
            result.Clear();
            if (!Built) return false;
            int start = NearestWalkable(from), goal = NearestWalkable(to);
            if (start < 0 || goal < 0) return false;
            if (start == goal) { result.Add(goal); return true; }

            searchId++;
            heap.Clear();
            Vector3 goalPos = Center(goal);
            gScore[start] = 0; parent[start] = -1; openStamp[start] = searchId;
            Push(new HeapItem { f = (Center(start) - goalPos).magnitude, i = start });
            int expansions = 0;
            bool found = false;
            while (heap.Count > 0 && expansions < 60000)
            {
                var cur = Pop();
                int c = cur.i;
                if (closedStamp[c] == searchId) continue;
                closedStamp[c] = searchId;
                expansions++;
                if (c == goal) { found = true; break; }
                float gc = gScore[c];
                ForEachNeighbour(c, (nb, stepCost) =>
                {
                    if (closedStamp[nb] == searchId) return;
                    float cost = stepCost * (ground[nb] ? 1f : airCostMultiplier) * (nearWall[nb] ? nearWallCostMultiplier : 1f);
                    if (blocked[nb] > 0)
                    {
                        if (!allowBlocked) return;
                        cost *= 15f;
                    }
                    if (extraCost != null) cost += extraCost(nb);
                    float g = gc + cost;
                    if (openStamp[nb] == searchId && g >= gScore[nb]) return;
                    openStamp[nb] = searchId; gScore[nb] = g; parent[nb] = c;
                    Push(new HeapItem { f = g + (Center(nb) - goalPos).magnitude, i = nb });
                });
            }
            if (!found) return false;
            for (int c = goal; c >= 0; c = parent[c]) result.Add(c);
            result.Reverse();
            return true;
        }

        void Push(HeapItem item)
        {
            heap.Add(item);
            int k = heap.Count - 1;
            while (k > 0)
            {
                int p = (k - 1) / 2;
                if (heap[p].f <= heap[k].f) break;
                (heap[p], heap[k]) = (heap[k], heap[p]);
                k = p;
            }
        }

        HeapItem Pop()
        {
            var top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int k = 0;
            while (true)
            {
                int l = 2 * k + 1, r = l + 1, m = k;
                if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                if (m == k) break;
                (heap[m], heap[k]) = (heap[k], heap[m]);
                k = m;
            }
            return top;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Built) return;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            foreach (var i in GroundCells) Gizmos.DrawCube(Center(i), Vector3.one * Cell * 0.25f);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            for (int i = 0; i < blocked.Length; i++) if (blocked[i] > 0 && walk[i]) Gizmos.DrawCube(Center(i), Vector3.one * Cell * 0.3f);
        }
    }
}
