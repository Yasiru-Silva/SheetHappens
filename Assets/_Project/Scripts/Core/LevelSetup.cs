using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SheetHappens
{
    /// <summary>
    /// Fills the existing house at runtime: finds the attic bell spot, the player start, ghost spawn points,
    /// and places hide-spot tables, pushable barricades, throwable objects and energy balls from the asset pack.
    /// Put a GameObject named "BellPoint", "PlayerStart", "GrandpaArmchair" or "GhostSpawn" in the scene to override a spot.
    /// </summary>
    public class LevelSetup : MonoBehaviour
    {
        [Header("Ghost prefabs (used as-is)")]
        public GameObject wispPrefab;
        public GameObject blueBooPrefab;
        public GameObject greenSneakPrefab;
        public GameObject grandpaPrefab;

        [Header("Props from the asset pack")]
        public GameObject hideSpotPrefab;
        public GameObject[] barricadePrefabs;
        public GameObject[] throwablePrefabs;
        [Header("Furniture from the asset pack")]
        [Tooltip("Furniture size. 0 = match the house automatically (the house is built at 2x the asset pack's size).")]
        public float furnitureScale = 0f;
        [Tooltip("Furnish the house randomly when there is no saved [FURNITURE] layout. Off: the house is used exactly as built.")]
        public bool randomFurniture = false;
        [Tooltip("Furniture that was built inside the house and is already the right size (not scaled up).")]
        public GameObject[] houseSizedPrefabs;
        [Tooltip("Cabinets that hang on the wall (e.g. the pantry) instead of standing on the floor.")]
        public GameObject[] wallHungPrefabs;
        [Tooltip("Height of the bottom of wall-hung cabinets above the floor (metres).")]
        public float wallHungHeight = 2f;
        /// <summary>Our own furniture placed this game (for the F6 debug jump).</summary>
        public readonly List<GameObject> OwnPieces = new List<GameObject>();
        [Tooltip("Cupboards with doors that open (Blue Boos hide in these).")]
        public GameObject[] hideCupboards;
        public int hideCupboardCount = 6;
        public GameObject[] wallFurniture;
        public GameObject[] diningTables;
        public GameObject[] chairs;
        public GameObject[] carpets;
        [Tooltip("Standing pieces placed along the walls: globes, cauldrons, baskets, trunks...")]
        public GameObject[] floorDecor;
        public int floorDecorCount = 10;
        [Tooltip("Hung on the walls: paintings, boards, shelves.")]
        public GameObject[] wallDecor;
        public int wallDecorCount = 12;
        [Tooltip("Small things put on tables, drawers and shelves: books, bottles, dishes, candles, skulls...")]
        public GameObject[] clutter;
        public int clutterPerSurface = 3;
        [Tooltip("True if the furniture models face their +Z axis (front away from the wall).")]
        public bool furnitureFacesPlusZ = true;
        public int wallPieces = 14;
        public int extraTables = 2;
        public int carpetCount = 4;

        [Tooltip("Any URP Lit material; used to build materials for the bell, energy balls and converted props.")]
        public Material urpTemplate;

        [Header("Counts")]
        public int hideSpots = 5;
        public int barricades = 5;
        public int throwables = 8;
        public int energyBallsPerWave = 6;
        public int seed = 7;

        /// <summary>The furniture scale in use (see furnitureScale).</summary>
        public float Scale { get; private set; } = 1f;
        GameManager gm;
        GhostGrid grid;
        readonly List<GameObject> surfaces = new List<GameObject>();
        readonly Dictionary<GameObject, Vector3> frontCache = new Dictionary<GameObject, Vector3>();
        System.Random rng;
        float lowestY;
        int bellCell = -1, playerCell = -1, grandpaCell = -1;
        readonly List<int> ground = new List<int>();
        readonly List<int> groundFloor = new List<int>();
        readonly List<int> flatCells = new List<int>();
        readonly List<Vector3> used = new List<Vector3>();
        readonly List<Transform> customSpawns = new List<Transform>();
        readonly List<Bounds> keepOut = new List<Bounds>();

        static readonly Regex DoorName = new Regex(@"^(Wood)?Door(01)?( \(\d+\))?$");
        public static bool IsDoorName(string n) => DoorName.IsMatch(n);

        /// <summary>Called before the grid is built so doors are already marked ghost-passable.</summary>
        public void PrepareDoors()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!IsDoorName(t.name) || t.GetComponentInChildren<Renderer>() == null) continue;
                if (t.GetComponentInParent<Door>() || t.GetComponentInChildren<Door>()) continue;
                t.gameObject.AddComponent<Door>();
            }
            foreach (var d in FindObjectsByType<Door>(FindObjectsSortMode.None))
                if (!d.GetComponent<GhostPassable>()) d.gameObject.AddComponent<GhostPassable>();
        }

        public void Setup(GameManager manager)
        {
            gm = manager;
            grid = manager.Grid;
            if (urpTemplate) Mats.Template = urpTemplate;
            rng = new System.Random(seed);
            Scale = furnitureScale > 0f ? furnitureScale : HouseScale();
            Debug.Log($"[SheetHappens] Furniture scale {Scale:0.00} (matches the house).");
            ground.AddRange(grid.GroundCells);
            if (ground.Count == 0) { Debug.LogError("[SheetHappens] No floor cells found for the ghosts."); return; }

            lowestY = float.MaxValue;
            foreach (int c in ground) lowestY = Mathf.Min(lowestY, grid.PathPoint(c).y);
            foreach (int c in ground)
            {
                if (grid.PathPoint(c).y < lowestY + 1.2f) groundFloor.Add(c);
                if (IsFlatOpen(c)) flatCells.Add(c);
            }
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("GhostSpawn")) customSpawns.Add(t);

            BuildKeepOuts();
            PlaceBell();
            PlacePlayer();
            grandpaCell = Marker("GrandpaArmchair", out var gp) ? grid.NearestWalkable(gp) : ClosestTo(groundFloor, Centroid(groundFloor), null);
            if (HasSavedLayout()) { ApplySavedLayout(); return; }
            if (!randomFurniture) { Debug.Log("[SheetHappens] No [FURNITURE] layout and random furniture is off: using the house exactly as built."); return; }
            Furnish();

            var tables = new List<GameObject>();
            for (int i = 0; i < hideSpots && hideSpotPrefab; i++)
            {
                var t = PlaceProp(hideSpotPrefab, 4.5f + Scale);
                if (!t) break;
                Tag(t, PropKind.HideTable, hideSpotPrefab);
                t.AddComponent<GhostPassable>();
                t.AddComponent<HideSpot>();
                EnsureCollider(t);
                AddChairs(t);
                tables.Add(t);
                surfaces.Add(t);
            }
            for (int i = 0; i < barricades && barricadePrefabs != null && barricadePrefabs.Length > 0; i++)
            {
                var bp = barricadePrefabs[rng.Next(barricadePrefabs.Length)];
                var b = PlaceProp(bp, 3f);
                if (!b) break;
                Tag(b, PropKind.Barricade, bp);
                EnsureCollider(b);
                var rb = b.GetComponent<Rigidbody>();
                if (rb == null) rb = b.AddComponent<Rigidbody>();
                rb.mass = 30f; rb.linearDamping = 4f; rb.angularDamping = 4f;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                b.AddComponent<Barricade>();
            }
            for (int i = 0; i < throwables && throwablePrefabs != null && throwablePrefabs.Length > 0; i++)
            {
                var prefab = throwablePrefabs[rng.Next(throwablePrefabs.Length)];
                GameObject o;
                if (tables.Count > 0 && i < tables.Count * 2)
                {
                    var table = tables[i % tables.Count];
                    Bounds tb = RendererBounds(table);
                    Vector3 top = new Vector3(tb.center.x + (float)(rng.NextDouble() - 0.5) * tb.size.x * 0.5f, tb.max.y + 0.05f, tb.center.z + (float)(rng.NextDouble() - 0.5) * tb.size.z * 0.5f);
                    o = Spawn(prefab, top, Quaternion.Euler(0f, rng.Next(360), 0f) * prefab.transform.rotation);
                    SitOn(o, top.y);
                }
                else o = PlaceProp(prefab, 1.5f);
                if (!o) break;
                Tag(o, PropKind.Throwable, prefab);
                EnsureCollider(o);
                if (!o.GetComponent<Rigidbody>()) o.AddComponent<Rigidbody>().mass = 0.5f;
                o.AddComponent<Throwable>();
            }
            AddClutter();
        }

        // ------------------------------------------------------------ placement

        void PlaceBell()
        {
            var bell = FindAnyObjectByType<Bell>();
            if (Marker("BellPoint", out var p)) bellCell = grid.NearestWalkable(p);
            else if (bell)
            {
                // The ghosts' target must be on the attic floor itself, never in the room below it.
                Vector3 bp = bell.transform.position;
                float floorY = Physics.Raycast(bp + Vector3.up * 1f, Vector3.down, out var fh, 3f, ~0, QueryTriggerInteraction.Ignore) ? fh.point.y : bp.y;
                bellCell = ClosestTo(ground, bp, c => Mathf.Abs(FloorPoint(c).y - floorY) < 0.3f);
                if (bellCell < 0 || Mathf.Abs(FloorPoint(bellCell).y - floorY) >= 0.3f) bellCell = grid.NearestWalkable(bp + Vector3.up * 0.8f);
            }
            else
            {
                // The attic: the highest floor the ghosts can reach.
                float maxY = float.MinValue;
                foreach (int c in ground) maxY = Mathf.Max(maxY, grid.PathPoint(c).y);
                var top = ground.FindAll(c => grid.PathPoint(c).y > maxY - 0.4f);
                bellCell = ClosestTo(top, Centroid(top), null);
            }
            Vector3 floor = FloorPoint(bellCell);
            if (!bell) bell = Bell.Create(floor);
            bell.RingPoint = grid.PathPoint(bellCell);
            gm.Bell = bell;
            used.Add(floor);
        }

        void PlacePlayer()
        {
            var p = gm.Player;
            if (p == null) return;
            if (Marker("PlayerStart", out var start)) { playerCell = grid.NearestWalkable(start); }
            else if (p.autoPlace)
            {
                var candidates = groundFloor.FindAll(IsFlatOpen);
                if (candidates.Count == 0) candidates = groundFloor;
                playerCell = ClosestTo(candidates, Centroid(candidates), null);
            }
            else { playerCell = grid.NearestWalkable(p.transform.position); return; }
            p.Teleport(FloorPoint(playerCell) + Vector3.up * 0.05f);
            used.Add(FloorPoint(playerCell));
        }

        // ------------------------------------------------------------ front door and the starting chair

        /// <summary>The house's front door (the lowest door that leads outside). Locked.</summary>
        public Door MainDoor { get; private set; }

        /// <summary>Locks every door that leads outside: there's no leaving the house tonight.</summary>
        public void LockOuterDoors()
        {
            int locked = 0;
            float lowest = float.MaxValue;
            foreach (var d in FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                Bounds b = RendererBounds(d.gameObject);
                if (b.size == Vector3.zero) continue;
                Vector3 n = b.size.x < b.size.z ? Vector3.right : Vector3.forward; // across the door
                Vector3 c = new Vector3(b.center.x, b.min.y + 1f, b.center.z);
                if (OutsideSide(c, n) == OutsideSide(c, -n)) continue; // both sides indoors (or both out)
                d.locked = true;
                locked++;
                if (b.min.y < lowest) { lowest = b.min.y; MainDoor = d; }
            }
            Debug.Log($"[SheetHappens] Locked {locked} outside door(s){(MainDoor ? ", front door: " + MainDoor.name : "")}.");
        }

        /// <summary>Is there open sky (no ceiling) a little way out on this side of a door, with no wall in between?</summary>
        static bool OutsideSide(Vector3 c, Vector3 dir)
        {
            foreach (float d in new[] { 1.5f, 3f, 4.5f })
            {
                Vector3 p = c + dir * d;
                if (Physics.Raycast(p, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore)) continue; // roof above: still indoors
                if (Physics.Linecast(c + dir * 0.6f, p, ~0, QueryTriggerInteraction.Ignore)) return false; // a wall in between
                return true;
            }
            return false;
        }

        static readonly Regex SeatName = new Regex("chair|sofa|couch|armchair|rocking|bench|stool", RegexOptions.IgnoreCase);
        static readonly Regex ComfyName = new Regex("sofa|couch|armchair|rocking", RegexOptions.IgnoreCase);

        /// <summary>
        /// The night starts with the player sitting in a chair in the living room by the front door.
        /// A scene object named "PlayerSeat" picks the chair yourself; otherwise the comfiest seat near the front door is used.
        /// </summary>
        public void SeatPlayerAtStart()
        {
            var p = gm.Player;
            if (p == null) return;
            var marker = GameObject.Find("PlayerSeat");
            Transform chair = marker ? marker.transform : FindStartChair();
            if (chair == null) { Debug.Log("[SheetHappens] No chair found near the front door: the player starts standing."); return; }
            if (!SeatOn(chair, out var seat, out var facing, out var stand)) return;
            p.SitAt(seat, facing, stand);
            Debug.Log($"[SheetHappens] The player starts sitting on '{chair.name}'.");
        }

        Transform FindStartChair()
        {
            Vector3 anchor;
            float floorY;
            if (MainDoor) { Bounds db = RendererBounds(MainDoor.gameObject); anchor = db.center; floorY = db.min.y; }
            else if (playerCell >= 0) { anchor = FloorPoint(playerCell); floorY = anchor.y; }
            else return null;
            Transform best = null;
            float bestScore = float.MaxValue;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!t.gameObject.activeInHierarchy || !SeatName.IsMatch(t.name)) continue;
                if (t.parent && SeatName.IsMatch(t.parent.name)) continue; // only the whole chair, not its parts
                var root = t.root.name;
                if (root.StartsWith("[FURNITURE OLD") || root == "[GAME]" || root == "[PLAYER]") continue;
                Bounds b = RendererBounds(t.gameObject);
                if (b.size == Vector3.zero || Mathf.Abs(b.min.y - floorY) > 1.2f) continue; // other floors
                float d = new Vector2(b.center.x - anchor.x, b.center.z - anchor.z).magnitude;
                if (d > 14f) continue;
                float score = d - (ComfyName.IsMatch(t.name) ? 3f : 0f);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        /// <summary>Works out the seat cushion, which way the chair faces (away from its backrest) and a clear spot to stand up onto.</summary>
        bool SeatOn(Transform chair, out Vector3 seat, out Vector3 facing, out Vector3 stand)
        {
            Bounds b = RendererBounds(chair.gameObject);
            seat = new Vector3(b.center.x, b.min.y + b.size.y * 0.45f, b.center.z);
            facing = Vector3.zero;
            stand = Vector3.zero;
            if (b.size == Vector3.zero) return false;

            // Height map of the chair from above (its own colliders only).
            var cols = new HashSet<Collider>(chair.GetComponentsInChildren<Collider>());
            var pts = new List<Vector3>();
            if (cols.Count > 0)
            {
                const int n = 9;
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        float x = Mathf.Lerp(b.min.x + b.size.x * 0.08f, b.max.x - b.size.x * 0.08f, i / (n - 1f));
                        float z = Mathf.Lerp(b.min.z + b.size.z * 0.08f, b.max.z - b.size.z * 0.08f, j / (n - 1f));
                        float top = float.MinValue;
                        foreach (var h in Physics.RaycastAll(new Vector3(x, b.max.y + 0.3f, z), Vector3.down, b.size.y + 0.6f, ~0, QueryTriggerInteraction.Ignore))
                            if (cols.Contains(h.collider) && h.point.y > top) top = h.point.y;
                        if (top > float.MinValue) pts.Add(new Vector3(x, top, z));
                    }
            }
            if (pts.Count >= 8)
            {
                float lo = b.min.y + b.size.y * 0.2f, hi = b.min.y + b.size.y * 0.75f;
                var cushion = pts.FindAll(q => q.y >= lo && q.y <= hi);
                if (cushion.Count >= 3)
                {
                    cushion.Sort((u, v) => u.y.CompareTo(v.y));
                    float seatH = cushion[cushion.Count / 2].y;
                    Vector3 sum = Vector3.zero; int k = 0;
                    foreach (var q in cushion) if (Mathf.Abs(q.y - seatH) < b.size.y * 0.08f) { sum += q; k++; }
                    if (k > 0) seat = new Vector3(sum.x / k, seatH, sum.z / k);
                    var back = pts.FindAll(q => q.y > seatH + b.size.y * 0.25f);
                    if (back.Count >= 2)
                    {
                        Vector3 bc = Vector3.zero; foreach (var q in back) bc += q; bc /= back.Count;
                        facing = seat - bc; facing.y = 0f;
                    }
                }
            }
            if (facing.sqrMagnitude < 1e-4f)
            {
                // No backrest found: face the most open direction (into the room).
                float bestOpen = -1f;
                foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    Vector3 from = new Vector3(seat.x, seat.y + 0.4f, seat.z) + dir * (Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z + 0.05f);
                    float open = Physics.Raycast(from, dir, out var h, 8f, ~0, QueryTriggerInteraction.Ignore) ? h.distance : 8f;
                    if (open > bestOpen) { bestOpen = open; facing = dir; }
                }
            }
            facing.Normalize();

            // A clear floor spot in front of the chair to stand up onto.
            float edge = Mathf.Abs(facing.x) * b.extents.x + Mathf.Abs(facing.z) * b.extents.z;
            var p = gm.Player;
            float r = 0.35f, hgt = p ? p.standHeight : 1.75f;
            foreach (float extra in new[] { 0.45f, 0.8f, 1.2f, 1.6f })
            {
                Vector3 q = new Vector3(b.center.x, 0f, b.center.z) + facing * (edge + extra);
                if (!Physics.Raycast(new Vector3(q.x, b.max.y + 0.5f, q.z), Vector3.down, out var h, b.size.y + 2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(h.point.y - b.min.y) > 0.45f) continue;
                if (Physics.CheckCapsule(h.point + Vector3.up * (r + 0.08f), h.point + Vector3.up * (hgt - r), r, ~0, QueryTriggerInteraction.Ignore)) continue;
                stand = h.point + Vector3.up * 0.05f;
                return true;
            }
            int cell = grid.NearestWalkable(seat);
            if (cell < 0) return false;
            stand = FloorPoint(cell) + Vector3.up * 0.05f;
            return true;
        }

        public void RespawnPlayer()
        {
            if (gm.Player && playerCell >= 0) gm.Player.Teleport(FloorPoint(playerCell) + Vector3.up * 0.05f);
        }

        GameObject PlaceProp(GameObject prefab, float spacing)
        {
            int tries = IsHouseSized(prefab) ? 600 : 60; // big custom pieces need more tries to find a room they fit in
            int blocked = 0;
            for (int attempt = 0; attempt < tries && flatCells.Count > 0; attempt++)
            {
                int c = flatCells[rng.Next(flatCells.Count)];
                Vector3 floor = FloorPoint(c);
                if (TooClose(floor, spacing)) continue;
                var go = Spawn(prefab, floor, Quaternion.Euler(0f, 90f * rng.Next(4), 0f) * prefab.transform.rotation); // keep the model upright
                // Centre the model itself on the spot (a prefab's pivot can be far from its geometry).
                Bounds gb0 = RendererBounds(go);
                go.transform.position += new Vector3(floor.x - gb0.center.x, 0f, floor.z - gb0.center.z);
                SitOn(go, floor.y);
                if (!Fits(go, out string hitName)) { blocked++; DestroyImmediate(go); continue; }
                used.Add(floor);
                return go;
            }
            if (IsHouseSized(prefab)) Debug.LogWarning($"[SheetHappens] Could not place {prefab.name}: no free spot big enough ({blocked} spots blocked).");
            return null;
        }

        /// <summary>Instantiates a prefab at the furniture scale with URP materials.</summary>
        GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            var go = Instantiate(prefab, pos, rot);
            go.name = prefab.name;
            go.transform.localScale = prefab.transform.localScale * (IsHouseSized(prefab) ? 1f : Scale);
            if (IsHouseSized(prefab)) OwnPieces.Add(go);
            Mats.FixForUrp(go);
            return go;
        }

        bool IsWallHung(GameObject prefab) => wallHungPrefabs != null && System.Array.IndexOf(wallHungPrefabs, prefab) >= 0;

        bool IsHouseSized(GameObject prefab) => houseSizedPrefabs != null && System.Array.IndexOf(houseSizedPrefabs, prefab) >= 0;

        /// <summary>The first picks go through the list in order (so every kind appears), then they are random.</summary>
        GameObject Pick(GameObject[] list, int i) => list[i < list.Length ? i : rng.Next(list.Length)];

        /// <summary>How much bigger the house is than the asset pack's own walls (2.5 m high).</summary>
        static float HouseScale()
        {
            var heights = new List<float>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("IntWall") && r.bounds.size.y > 1f && !r.GetComponentInParent<PlacedProp>()) heights.Add(r.bounds.size.y);
            if (heights.Count == 0) return 1f;
            heights.Sort();
            return Mathf.Clamp(heights[heights.Count / 2] / 2.5f, 0.5f, 4f);
        }

        static void SitOn(GameObject go, float floorY)
        {
            Bounds b = RendererBounds(go);
            go.transform.position += Vector3.up * (floorY - b.min.y + 0.01f);
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        bool TooClose(Vector3 p, float spacing)
        {
            foreach (var u in used) if ((u - p).sqrMagnitude < spacing * spacing) return true;
            foreach (var k in keepOut) if (k.Contains(p + Vector3.up * 0.5f)) return true;
            if (playerCell >= 0 && (FloorPoint(playerCell) - p).sqrMagnitude < 2.5f * 2.5f) return true;
            return false;
        }

        /// <summary>A flat floor cell with open, flat neighbours on all sides (not stairs, not against a wall).</summary>
        bool IsFlatOpen(int c)
        {
            Vector3 f = FloorPoint(c);
            float step = grid.Cell;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int n = grid.NearestWalkable(grid.Center(c) + new Vector3(dx * step, 0f, dz * step), 0);
                    if (n < 0 || !grid.IsGround(n)) return false;
                    if (Mathf.Abs(FloorPoint(n).y - f.y) > 0.12f) return false;
                }
            return true;
        }

        public Vector3 FloorPoint(int c)
        {
            Vector3 p = grid.Center(c);
            return Physics.Raycast(p, Vector3.down, out RaycastHit hit, grid.maxHeightAboveFloor + 0.5f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : grid.PathPoint(c);
        }

        static bool Marker(string name, out Vector3 pos)
        {
            var go = GameObject.Find(name);
            pos = go ? go.transform.position : Vector3.zero;
            return go != null;
        }

        Vector3 Centroid(List<int> cells)
        {
            Vector3 s = Vector3.zero;
            foreach (int c in cells) s += grid.PathPoint(c);
            return cells.Count > 0 ? s / cells.Count : Vector3.zero;
        }

        int ClosestTo(List<int> cells, Vector3 p, System.Predicate<int> filter)
        {
            int best = -1; float bd = float.MaxValue;
            foreach (int c in cells)
            {
                if (filter != null && !filter(c)) continue;
                float d = (grid.PathPoint(c) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best >= 0 ? best : (cells.Count > 0 ? cells[0] : -1);
        }

        // ------------------------------------------------------------ saved layout

        static void Tag(GameObject go, PropKind kind, GameObject prefab)
        {
            var p = go.GetComponent<PlacedProp>();
            if (p == null) p = go.AddComponent<PlacedProp>();
            p.kind = kind;
            p.prefab = prefab;
        }

        static System.Collections.Generic.IEnumerable<GameObject> SavedPieces()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.parent == null && t.name == "[FURNITURE]")
                    foreach (Transform child in t) yield return child.gameObject;
        }

        /// <summary>Layouts saved before the furniture was resized to the house are switched off.</summary>
        static void HideOldLayouts()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.parent == null && t.name.StartsWith("[FURNITURE OLD"))
                    t.gameObject.SetActive(false);
        }

        static bool HasSavedLayout()
        {
            foreach (var _ in SavedPieces()) return true;
            return false;
        }

        static PropKind KindOf(GameObject go)
        {
            var p = go.GetComponent<PlacedProp>();
            return p ? p.kind : PropKind.WallFurniture;
        }

        /// <summary>Called before the ghost grid is built: furniture never blocks ghosts, pushable/throwable props are handled dynamically.</summary>
        public void PrepareSavedLayout()
        {
            HideOldLayouts();
            PrepareHandPlacedObjects();
            foreach (var go in SavedPieces())
            {
                if (!go.GetComponent<GhostPassable>()) go.AddComponent<GhostPassable>();
                var k = KindOf(go);
                if ((k == PropKind.Barricade || k == PropKind.Throwable) && !go.GetComponent<Rigidbody>()) go.AddComponent<Rigidbody>();
            }
        }

        static readonly string[] GameRoots = { "[GAME]", "[PLAYER]", "[ROOF]", "[LADDERS]", "AtticBell" };

        /// <summary>
        /// Furniture and models placed straight into the scene by hand (sofas, bathtub, toilet...): give them URP materials
        /// so they never render pink, and a collider if they have none so the player cannot walk through them.
        /// Called before the ghost grid is built, so ghosts float around them too.
        /// </summary>
        void PrepareHandPlacedObjects()
        {
            int fixedCols = 0;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(GameRoots, root.name) >= 0 || root.name.StartsWith("[FURNITURE OLD") || !root.activeInHierarchy) continue;
                Mats.FixForUrp(root);
                if (root.name == "[FURNITURE]") continue; // handled by the saved layout
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null || mf.GetComponentInParent<Collider>() || mf.GetComponentInChildren<Collider>()) continue;
                    if (mf.GetComponentInParent<Rigidbody>() || mf.GetComponentInParent<Door>() || mf.GetComponent<Renderer>() == null) continue;
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    fixedCols++;
                }
            }
            if (fixedCols > 0) Debug.Log($"[SheetHappens] Added colliders to {fixedCols} hand-placed models.");
        }

        /// <summary>Uses the furniture saved in the scene instead of furnishing the house randomly.</summary>
        void ApplySavedLayout()
        {
            int n = 0;
            foreach (var go in new List<GameObject>(SavedPieces()))
            {
                n++;
                Mats.FixForUrp(go);
                switch (KindOf(go))
                {
                    case PropKind.Carpet:
                        foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                        break;
                    case PropKind.HideTable:
                        EnsureCollider(go);
                        if (!go.GetComponent<HideSpot>()) go.AddComponent<HideSpot>();
                        break;
                    case PropKind.HideCupboard:
                        if (!go.GetComponent<Cupboard>()) go.AddComponent<Cupboard>().Setup();
                        break;
                    case PropKind.Clutter:
                        {
                            EnsureCollider(go);
                            var rb = go.GetComponent<Rigidbody>();
                            if (rb) rb.isKinematic = true;
                            break;
                        }
                    case PropKind.WallDecor:
                        break;
                    case PropKind.Barricade:
                        {
                            EnsureCollider(go);
                            var rb = go.GetComponent<Rigidbody>();
                            if (rb == null) rb = go.AddComponent<Rigidbody>();
                            rb.mass = 30f; rb.linearDamping = 4f; rb.angularDamping = 4f;
                            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                            if (!go.GetComponent<Barricade>()) go.AddComponent<Barricade>();
                            break;
                        }
                    case PropKind.Throwable:
                        {
                            EnsureCollider(go);
                            var rb = go.GetComponent<Rigidbody>();
                            if (rb == null) rb = go.AddComponent<Rigidbody>();
                            rb.mass = 0.5f;
                            if (!go.GetComponent<Throwable>()) go.AddComponent<Throwable>();
                            break;
                        }
                    default:
                        EnsureCollider(go);
                        break;
                }
            }
            Debug.Log($"[SheetHappens] Using the saved furniture layout ({n} pieces).");
        }

        // ------------------------------------------------------------ furniture

        /// <summary>Doorways, stairs and the ladder stay clear of furniture.</summary>
        void BuildKeepOuts()
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.name.StartsWith("IntWall02door")) keepOut.Add(new Bounds(r.bounds.center, new Vector3(3.4f, 6f, 3.4f)));
                else if (r.name.StartsWith("WoodStairs")) { var b = r.bounds; b.Expand(new Vector3(2.6f, 1.5f, 2.6f)); keepOut.Add(b); }
            }
            foreach (var d in FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                var col = d.GetComponentInChildren<Collider>();
                if (col) { var b = col.bounds; b.Expand(new Vector3(2.6f, 1f, 2.6f)); keepOut.Add(b); }
            }
            foreach (var l in FindObjectsByType<Ladder>(FindObjectsSortMode.None))
            {
                var b = l.GetComponent<Collider>().bounds; b.Expand(new Vector3(2.8f, 1f, 2.8f)); keepOut.Add(b);
            }
        }

        void Furnish()
        {
            Physics.SyncTransforms();
            // Cupboards first (they need the most wall space), then the rest of the wall furniture.
            int cupboards = 0;
            for (int i = 0; i < hideCupboardCount && hideCupboards != null && hideCupboards.Length > 0; i++)
            {
                var prefab = Pick(hideCupboards, i);
                var go = PlaceAgainstWall(prefab, PropKind.HideCupboard, 2.6f * Scale);
                if (!go) break;
                var cb = go.AddComponent<Cupboard>();
                cb.Setup();
                cupboards++;
                // Check: the doors face the room (nothing solid between the front and the cupboard).
                Physics.SyncTransforms();
                Vector3 eye = cb.FrontPoint + Vector3.up * 1.2f;
                Vector3 toBody = RendererBounds(go).center - eye;
                bool facesRoom = Physics.Raycast(eye, toBody.normalized, out var fh, toBody.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore) && fh.collider.transform.IsChildOf(go.transform);
                Debug.Log($"[SheetHappens] Cupboard {cupboards} at {go.transform.position:F1}, doors face {cb.Forward}, {(facesRoom ? "facing the room" : "NOT facing the room")}.");
            }
            // Tables next: big ones need open floor before the rooms fill up.
            for (int i = 0; i < extraTables && diningTables != null && diningTables.Length > 0; i++)
            {
                var tp = Pick(diningTables, i);
                var t = PlaceProp(tp, 3.5f + Scale);
                if (!t) continue;
                Tag(t, PropKind.Table, tp);
                EnsureCollider(t);
                t.AddComponent<GhostPassable>();
                AddChairs(t);
                surfaces.Add(t);
            }
            int placed = 0;
            for (int i = 0; i < wallPieces && wallFurniture != null && wallFurniture.Length > 0; i++)
            {
                var prefab = Pick(wallFurniture, i);
                var go = PlaceAgainstWall(prefab, PropKind.WallFurniture, 1.4f * Scale);
                if (!go) continue;
                placed++;
                if (RendererBounds(go).size.y < 1.25f * Scale && RendererBounds(go).size.y > 0.5f) surfaces.Add(go); // drawers, workbenches, sinks: things go on top
            }
            int decor = 0;
            for (int i = 0; i < floorDecorCount && floorDecor != null && floorDecor.Length > 0; i++)
            {
                var prefab = floorDecor[rng.Next(floorDecor.Length)];
                if (PlaceAgainstWall(prefab, PropKind.FloorDecor, 1.0f * Scale)) decor++;
            }
            int hung = 0;
            for (int i = 0; i < wallDecorCount && wallDecor != null && wallDecor.Length > 0; i++)
            {
                var prefab = wallDecor[rng.Next(wallDecor.Length)];
                if (HangOnWall(prefab)) hung++;
            }
            for (int i = 0; i < carpetCount && carpets != null && carpets.Length > 0; i++)
            {
                var cpp = carpets[rng.Next(carpets.Length)];
                var cp = PlaceCarpet(cpp);
                if (!cp) break;
                Tag(cp, PropKind.Carpet, cpp);
            }
            Debug.Log($"[SheetHappens] Furnished the house: {cupboards} cupboards with doors, {placed} wall pieces, {decor} standing decorations, {hung} things on the walls.");
        }

        /// <summary>The direction the model's front faces when placed with the prefab's own rotation.</summary>
        Vector3 FrontOf(GameObject prefab)
        {
            if (frontCache.TryGetValue(prefab, out var f)) return f;
            f = furnitureFacesPlusZ ? Vector3.forward : Vector3.back;
            var go = Instantiate(prefab, Vector3.zero, prefab.transform.rotation);
            Vector3 doors = Vector3.zero; int nd = 0; Bounds body = default; bool hasBody = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                string n = r.name.ToLower();
                if (n.Contains("glass")) continue;
                if (n.Contains("door")) { doors += r.bounds.center; nd++; }
                else if (!hasBody) { body = r.bounds; hasBody = true; } else body.Encapsulate(r.bounds);
            }
            if (nd > 0 && hasBody)
            {
                Vector3 d = doors / nd - body.center; d.y = 0f;
                if (d.sqrMagnitude > 1e-6f) f = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
            }
            DestroyImmediate(go);
            frontCache[prefab] = f;
            return f;
        }

        /// <summary>Rotation that keeps the prefab upright and turns its front to face 'into'.</summary>
        Quaternion Facing(GameObject prefab, Vector3 into) =>
            Quaternion.LookRotation(into) * Quaternion.Inverse(Quaternion.LookRotation(FrontOf(prefab))) * prefab.transform.rotation;

        static readonly Vector3[] Dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        /// <summary>Stands a piece on the floor with its back against a wall, facing into the room.</summary>
        GameObject PlaceAgainstWall(GameObject prefab, PropKind kind, float spacing)
        {
            if (prefab == null) return null;
            int noWall = 0, near = 0, notFlat = 0, blocked = 0;
            for (int attempt = 0; attempt < 400; attempt++)
            {
                int c = ground[rng.Next(ground.Count)];
                Vector3 floor = FloorPoint(c);
                Vector3 d = Dirs[rng.Next(4)];
                bool hung = IsWallHung(prefab);
                if (!WallAt(floor, d, out float dist, hung ? new[] { wallHungHeight + 0.2f, wallHungHeight + 0.6f, wallHungHeight + 1f } : null)) { noWall++; continue; }
                if (TooClose(floor, spacing)) { near++; continue; }
                var go = Spawn(prefab, Vector3.zero, Facing(prefab, -d));
                Bounds b = RendererBounds(go);
                Vector3 off = go.transform.position - b.center;
                float depth = Mathf.Abs(d.x) > 0.5f ? b.size.x : b.size.z;
                Vector3 center = floor + d * (dist - depth * 0.5f - 0.04f);
                if (Physics.Raycast(center + Vector3.up * 0.6f, Vector3.down, out var fh, 1.2f, ~0, QueryTriggerInteraction.Ignore)) center.y = fh.point.y;
                if (Mathf.Abs(center.y - floor.y) > 0.1f) { DestroyImmediate(go); notFlat++; continue; } // something under it: not a clean floor spot
                go.transform.position = new Vector3(center.x + off.x, center.y, center.z + off.z);
                SitOn(go, center.y + (hung ? wallHungHeight : 0f));
                if (!Fits(go, out string hitName) || TooClose(center, spacing)) { DestroyImmediate(go); blocked++; if (blocked < 4 && IsHouseSized(prefab)) Debug.Log($"[SheetHappens] {prefab.name} blocked by {hitName}"); continue; }
                EnsureCollider(go);
                go.AddComponent<GhostPassable>();
                var rb = go.GetComponent<Rigidbody>();
                if (rb) rb.isKinematic = true;
                Tag(go, kind, prefab);
                used.Add(center);
                return go;
            }
            string why = $"[SheetHappens] Could not place {prefab.name}: no wall {noWall}, too close {near}, uneven floor {notFlat}, blocked {blocked}.";
            if (IsHouseSized(prefab)) Debug.LogWarning(why); else Debug.Log(why + " (the house is full)");
            return null;
        }

        readonly List<Bounds> hungAt = new List<Bounds>();

        /// <summary>Hangs a painting, board or shelf flat on a wall at about eye level.</summary>
        bool HangOnWall(GameObject prefab)
        {
            if (prefab == null) return false;
            for (int attempt = 0; attempt < 300; attempt++)
            {
                int c = ground[rng.Next(ground.Count)];
                Vector3 floor = FloorPoint(c);
                Vector3 d = Dirs[rng.Next(4)];
                float hangY = floor.y + 1.15f * Scale;
                if (!WallAt(floor, d, out float dist, new[] { 1.15f * Scale - 0.5f, 1.15f * Scale, 1.15f * Scale + 0.5f })) continue;

                bool shelf = prefab.name.ToLower().Contains("shelf");
                var go = Spawn(prefab, Vector3.zero, Facing(prefab, -d));
                Bounds b = RendererBounds(go);
                // Flat pieces may be modelled facing sideways: turn them so their thin side is against the wall.
                float along = Mathf.Abs(d.x) > 0.5f ? b.size.x : b.size.z, across = Mathf.Abs(d.x) > 0.5f ? b.size.z : b.size.x;
                if (!shelf && along > across) { go.transform.rotation = Quaternion.AngleAxis(90f, Vector3.up) * go.transform.rotation; b = RendererBounds(go); }
                Vector3 off = go.transform.position - b.center;
                float depth = Mathf.Abs(d.x) > 0.5f ? b.size.x : b.size.z;
                Vector3 center = floor + d * (dist - depth * 0.5f - 0.02f);
                center.y = shelf ? hangY + 0.2f * Scale : hangY;
                go.transform.position = center + off;
                Bounds placedB = RendererBounds(go);
                placedB.Expand(0.6f);
                bool overlaps = false;
                foreach (var h in hungAt) if (h.Intersects(placedB)) { overlaps = true; break; }
                if (overlaps || !Fits(go)) { DestroyImmediate(go); continue; }
                foreach (var col in go.GetComponentsInChildren<Collider>()) if (!shelf) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                go.AddComponent<GhostPassable>();
                Tag(go, PropKind.WallDecor, prefab);
                hungAt.Add(RendererBounds(go));
                if (shelf) { EnsureCollider(go); surfaces.Add(go); }
                return true;
            }
            return false;
        }

        GameObject PlaceCarpet(GameObject prefab)
        {
            for (int attempt = 0; attempt < 60 && flatCells.Count > 0; attempt++)
            {
                int c = flatCells[rng.Next(flatCells.Count)];
                Vector3 floor = FloorPoint(c);
                bool blocked = false;
                foreach (var k in keepOut) if (k.Contains(floor + Vector3.up * 0.5f)) { blocked = true; break; }
                if (blocked) continue;
                var go = Spawn(prefab, floor, Quaternion.Euler(0f, 90f * rng.Next(4), 0f) * prefab.transform.rotation);
                foreach (var col in go.GetComponentsInChildren<Collider>()) DestroyImmediate(col); // flat, nothing to bump into
                SitOn(go, floor.y);
                Bounds b = RendererBounds(go);
                // Keep carpets on one floor (not hanging over stairs or into walls).
                bool ok = true;
                foreach (var corner in new[] { new Vector3(b.min.x, 0, b.min.z), new Vector3(b.max.x, 0, b.min.z), new Vector3(b.min.x, 0, b.max.z), new Vector3(b.max.x, 0, b.max.z) })
                {
                    Vector3 p = new Vector3(corner.x, floor.y + 0.4f, corner.z);
                    if (!Physics.Raycast(p, Vector3.down, out var h, 0.7f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(h.point.y - floor.y) > 0.08f || Physics.CheckSphere(p, 0.05f, ~0, QueryTriggerInteraction.Ignore)) { ok = false; break; }
                }
                if (!ok) { DestroyImmediate(go); continue; }
                return go;
            }
            return null;
        }

        /// <summary>Books, bottles, dishes, candles and curiosities on tables, drawers and shelves.</summary>
        void AddClutter()
        {
            if (clutter == null || clutter.Length == 0) return;
            Physics.SyncTransforms();
            int n = 0;
            foreach (var surface in surfaces)
            {
                if (surface == null) continue;
                Bounds sb = RendererBounds(surface);
                int count = clutterPerSurface + rng.Next(-1, 2);
                for (int i = 0, tries = 0; i < count && tries < count * 6; tries++)
                {
                    float u = (float)rng.NextDouble() - 0.5f, v = (float)rng.NextDouble() - 0.5f;
                    Vector3 above = new Vector3(sb.center.x + u * sb.size.x * 0.7f, sb.max.y + 0.6f, sb.center.z + v * sb.size.z * 0.7f);
                    if (!Physics.Raycast(above, Vector3.down, out var hit, sb.size.y + 1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!hit.collider.transform.IsChildOf(surface.transform) || hit.normal.y < 0.8f) continue;
                    var prefab = clutter[rng.Next(clutter.Length)];
                    if (prefab == null) continue;
                    var go = Spawn(prefab, hit.point, Quaternion.Euler(0f, rng.Next(360), 0f) * prefab.transform.rotation);
                    SitOn(go, hit.point.y);
                    Bounds gb = RendererBounds(go);
                    if (gb.min.x < sb.min.x || gb.max.x > sb.max.x || gb.min.z < sb.min.z || gb.max.z > sb.max.z || !Fits(go)) { DestroyImmediate(go); continue; }
                    EnsureCollider(go);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                    go.AddComponent<GhostPassable>();
                    Tag(go, PropKind.Clutter, prefab);
                    Physics.SyncTransforms();
                    i++; n++;
                }
            }
            Debug.Log($"[SheetHappens] {n} small items on {surfaces.Count} tables, drawers and shelves.");
        }

        /// <summary>A solid, flat wall (not a pillar, beam, railing or open side) within reach in direction d.</summary>
        static bool WallAt(Vector3 floor, Vector3 d, out float dist, float[] heights = null)
        {
            dist = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, d) * 0.3f;
            float first = -1f;
            foreach (var h in heights ?? new[] { 0.4f, 1.0f, 1.6f, 2.6f })
                foreach (var o in new[] { -side, Vector3.zero, side })
                {
                    if (!Physics.Raycast(floor + Vector3.up * h + o, d, out var hit, 1.3f, ~0, QueryTriggerInteraction.Ignore)) return false;
                    if (GhostGrid.IsPassable(hit.collider) || Mathf.Abs(hit.normal.y) > 0.3f) return false;
                    if (first < 0f) first = hit.distance;
                    else if (Mathf.Abs(hit.distance - first) > 0.35f) return false; // not one flat wall
                }
            dist = first;
            return true;
        }

        /// <summary>The object does not cut into walls, other furniture, the ceiling or the roof.</summary>
        bool Fits(GameObject go) => Fits(go, out _);

        bool Fits(GameObject go, out string hitName)
        {
            hitName = "";
            Physics.SyncTransforms();
            Bounds b = RendererBounds(go);
            Vector3 half = Vector3.Max(b.extents - new Vector3(0.06f, 0.08f, 0.06f), Vector3.one * 0.02f);
            foreach (var h in Physics.OverlapBox(b.center + Vector3.up * 0.08f, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (!h.transform.IsChildOf(go.transform)) { hitName = h.name; return false; }
            return true;
        }

        void AddChairs(GameObject table)
        {
            if (chairs == null || chairs.Length == 0) return;
            Bounds tb = RendererBounds(table);
            float floorY = tb.min.y;
            float gap = 0.4f * Scale;
            Vector3[] sides = tb.size.x >= tb.size.z
                ? new[] { Vector3.forward * (tb.extents.z + gap), Vector3.back * (tb.extents.z + gap) }
                : new[] { Vector3.right * (tb.extents.x + gap), Vector3.left * (tb.extents.x + gap) };
            foreach (var side in sides)
            {
                var prefab = chairs[rng.Next(chairs.Length)];
                if (prefab == null) continue;
                Vector3 at = new Vector3(tb.center.x, floorY, tb.center.z) + side;
                var ch = Spawn(prefab, at, Quaternion.LookRotation(furnitureFacesPlusZ ? -side : side) * prefab.transform.rotation);
                foreach (var rb in ch.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                Bounds cb = RendererBounds(ch);
                ch.transform.position += new Vector3(at.x - cb.center.x, 0f, at.z - cb.center.z);
                SitOn(ch, floorY);
                if (!Fits(ch)) { DestroyImmediate(ch); continue; }
                EnsureCollider(ch);
                ch.AddComponent<GhostPassable>();
                Tag(ch, PropKind.Chair, prefab);
            }
        }

        /// <summary>Furniture must block the player: add a box collider if the model has none.</summary>
        static void EnsureCollider(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;
            Bounds b = RendererBounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            Vector3 size = go.transform.InverseTransformVector(b.size);
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }

        // ------------------------------------------------------------ ghosts

        public GhostAgent SpawnGhost(GhostType type)
        {
            GameObject prefab = type switch
            {
                GhostType.Wisp => wispPrefab,
                GhostType.BlueBoo => blueBooPrefab,
                GhostType.GreenSneak => greenSneakPrefab,
                _ => grandpaPrefab,
            };
            if (prefab == null) { Debug.LogWarning($"[SheetHappens] No prefab assigned for {type}."); return null; }
            int cell = SpawnCell(type);
            if (cell < 0) return null;
            Vector3 at = grid.PathPoint(cell);
            Vector3 look = gm.Player ? gm.Player.transform.position - at : Vector3.forward;
            look.y = 0f;
            var go = Instantiate(prefab, at, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            go.name = prefab.name;
            GhostAgent agent = type switch
            {
                GhostType.Wisp => go.AddComponent<WispAI>(),
                GhostType.BlueBoo => go.AddComponent<BlueBooAI>(),
                GhostType.GreenSneak => go.AddComponent<GreenSneakAI>(),
                _ => go.AddComponent<GrandpaAI>(),
            };
            agent.Init(gm, at);
            return agent;
        }

        int SpawnCell(GhostType type)
        {
            Vector3 player = gm.Player ? gm.Player.transform.position : Vector3.zero;
            Vector3 bell = grid.PathPoint(bellCell);
            switch (type)
            {
                case GhostType.Grandpa:
                    return grandpaCell;
                case GhostType.BlueBoo:
                    {
                        // Blue Boos appear inside a cupboard away from the player.
                        var free = Cupboard.All.FindAll(cb => cb.Free && (cb.FrontPoint - player).sqrMagnitude > 81f);
                        if (free.Count > 0)
                        {
                            var cb = free[rng.Next(free.Count)];
                            int cc = grid.NearestWalkable(cb.FrontPoint + Vector3.up * 0.4f);
                            if (cc >= 0) return cc;
                        }
                        int c = FindHidingCell(player, 9f, 999f);
                        return c >= 0 ? c : ground[rng.Next(ground.Count)];
                    }
                default:
                    {
                        if (customSpawns.Count > 0) return grid.NearestWalkable(customSpawns[rng.Next(customSpawns.Count)].position);
                        // Ground-floor furniture spots far from the bell and not right next to the player.
                        var list = groundFloor.FindAll(c => (grid.PathPoint(c) - player).sqrMagnitude > 36f);
                        if (list.Count == 0) list = groundFloor;
                        list.Sort((a, b) => (grid.PathPoint(b) - bell).sqrMagnitude.CompareTo((grid.PathPoint(a) - bell).sqrMagnitude));
                        int top = Mathf.Max(1, list.Count / 4);
                        return list[rng.Next(top)];
                    }
            }
        }

        /// <summary>A floor cell between minDist and maxDist from 'from', far from the player and out of the player's sight.</summary>
        public int FindHidingCell(Vector3 from, float minDistFromPlayer, float maxDist)
        {
            Vector3 eye = gm.Player ? gm.Player.EyePosition : from;
            int best = -1; float bestScore = float.MinValue;
            for (int k = 0; k < 50; k++)
            {
                int c = ground[rng.Next(ground.Count)];
                Vector3 p = grid.PathPoint(c);
                float dp = Vector3.Distance(p, eye), df = Vector3.Distance(p, from);
                if (dp < minDistFromPlayer || df > maxDist) continue;
                bool hidden = Physics.Linecast(p + Vector3.up * 0.8f, eye, ~0, QueryTriggerInteraction.Ignore);
                float score = (hidden ? 100f : 0f) + dp - df * 0.3f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        public int RandomGroundCell(Vector3 awayFrom, float minDist)
        {
            for (int k = 0; k < 30; k++)
            {
                int c = ground[rng.Next(ground.Count)];
                if ((grid.PathPoint(c) - awayFrom).sqrMagnitude >= minDist * minDist) return c;
            }
            return ground.Count > 0 ? ground[rng.Next(ground.Count)] : -1;
        }

        public void SpawnEnergyBalls()
        {
            if (grid == null || ground.Count == 0) return;
            var pool = flatCells.Count > 0 ? flatCells : ground;
            for (int i = 0; i < energyBallsPerWave; i++)
            {
                int c = pool[rng.Next(pool.Count)];
                EnergyBall.Create(FloorPoint(c) + Vector3.up * 0.7f);
            }
        }
    }
}
