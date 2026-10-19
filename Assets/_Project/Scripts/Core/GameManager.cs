using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SheetHappens
{
    public enum GameState { Playing, Won, Lost }
    public enum GhostType { Wisp, BlueBoo, GreenSneak, Grandpa }

    [System.Serializable]
    public class SpawnEvent
    {
        [Tooltip("Seconds after midnight (real time).")] public float time;
        public GhostType type;
        public int count = 1;
    }

    /// <summary>
    /// Runs one night: 12:00 AM to 6:00 AM, three waves, hearts, the bell and win/lose.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        [Header("Night")]
        [Tooltip("Real seconds from 12:00 AM to 6:00 AM.")] public float nightLengthSeconds = 360f;
        public int maxHearts = 3;
        public float hitInvulnerability = 1.5f;
        public float jamSeconds = 3f;

        [Header("Ghost tuning")]
        public float wispSpeed = 1.1f;
        public float blueBooSpeed = 1.6f;
        public float greenSneakSpeed = 1.1f;
        public float grandpaSpeed = 2.2f;
        public int grandpaHits = 5;

        [Header("Spawn schedule (Grandpa is added automatically at 5 AM)")]
        public List<SpawnEvent> schedule = new List<SpawnEvent>
        {
            new SpawnEvent { time = 15,  type = GhostType.Wisp,       count = 2 },
            new SpawnEvent { time = 50,  type = GhostType.Wisp,       count = 2 },
            new SpawnEvent { time = 90,  type = GhostType.Wisp,       count = 3 },
            new SpawnEvent { time = 124, type = GhostType.BlueBoo,    count = 2 },
            new SpawnEvent { time = 140, type = GhostType.Wisp,       count = 2 },
            new SpawnEvent { time = 185, type = GhostType.Wisp,       count = 3 },
            new SpawnEvent { time = 215, type = GhostType.BlueBoo,    count = 1 },
            new SpawnEvent { time = 244, type = GhostType.GreenSneak, count = 1 },
            new SpawnEvent { time = 262, type = GhostType.Wisp,       count = 2 },
            new SpawnEvent { time = 285, type = GhostType.GreenSneak, count = 1 },
            new SpawnEvent { time = 325, type = GhostType.Wisp,       count = 2 },
        };

        public GameState State { get; private set; } = GameState.Playing;
        public float Elapsed { get; private set; }
        public int Hearts { get; private set; }
        public string EndReason { get; private set; }
        public string Banner { get; private set; }
        public int Wave => Elapsed < nightLengthSeconds / 3f ? 1 : Elapsed < nightLengthSeconds * 2f / 3f ? 2 : 3;
        public float GrandpaTime => nightLengthSeconds * 5f / 6f;

        public PlayerController Player { get; private set; }
        public Vacuum Vacuum { get; private set; }
        public Bell Bell { get; set; }
        public GhostGrid Grid { get; private set; }
        public LevelSetup Level { get; private set; }
        public readonly List<GhostAgent> Ghosts = new List<GhostAgent>();
        public bool Ready { get; private set; }

        float bannerUntil;
        float lastHit = -99f;
        int nextEvent;
        int lastWave;
        bool grandpaSpawned;

        void Awake()
        {
            I = this;
            Hearts = maxHearts;
            Grid = GetComponent<GhostGrid>();
            Level = GetComponent<LevelSetup>();
        }

        void Start()
        {
            Player = FindAnyObjectByType<PlayerController>();
            Vacuum = FindAnyObjectByType<Vacuum>();
            Physics.SyncTransforms();
            var night = GetComponent<NightLighting>();
            if (night == null) night = gameObject.AddComponent<NightLighting>();
            if (Level.urpTemplate) Mats.Template = Level.urpTemplate; // so new materials render in URP
            night.ApplySky();
            Level.PrepareDoors();
            Level.PrepareSavedLayout();
            Grid.Build();
            if (!Grid.Built)
            {
                Debug.LogError("[SheetHappens] Ghost grid could not be built. Is the level missing colliders?");
                return;
            }
            Level.Setup(this);
            night.PlaceLamps(Grid, Grid.GroundCells);
            var ambience = GetComponent<NightAmbience>();
            if (ambience == null) ambience = gameObject.AddComponent<NightAmbience>();
            var floorPoints = new List<Vector3>();
            foreach (int c in Grid.GroundCells) floorPoints.Add(Grid.PathPoint(c));
            ambience.Begin(floorPoints);
            if (!GetComponent<DebugKeys>()) gameObject.AddComponent<DebugKeys>();
            if (Bell && Player)
            {
                var route = new List<int>();
                bool ok = Grid.FindPath(Player.transform.position, Bell.RingPoint, null, route);
                float gap = ok ? Vector3.Distance(Grid.PathPoint(route[route.Count - 1]), Bell.RingPoint) : -1f;
                Debug.Log($"[SheetHappens] Ghost route to the bell: {(ok ? route.Count + " cells, ends " + gap.ToString("0.0") + " m from the bell" : "NONE")}");
                if (ok)
                {
                    // Where does the route rise into the attic? (should be the ladder hatch)
                    float bellY = Bell.RingPoint.y;
                    for (int k = 1; k < route.Count; k++)
                    {
                        Vector3 a = Grid.PathPoint(route[k - 1]), b = Grid.PathPoint(route[k]);
                        if (a.y < bellY - 0.5f && b.y >= bellY - 0.5f)
                        {
                            var lad = FindAnyObjectByType<Ladder>();
                            string ladTxt = lad ? $", ladder at ({lad.LaneCenter.x:0.0}, {lad.LaneCenter.z:0.0})" : "";
                            Debug.Log($"[SheetHappens] Route enters the attic at ({b.x:0.0}, {b.y:0.0}, {b.z:0.0}){ladTxt}; bell target y={bellY:0.0}");
                        }
                    }
                }
            }
            schedule.Sort((a, b) => a.time.CompareTo(b.time));
            Ready = true;
            ShowBanner("Midnight. Keep the ghosts away from the attic bell!", 4f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (State != GameState.Playing)
            {
                if (kb != null && kb.rKey.wasPressedThisFrame)
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }
            if (!Ready) return;

            Elapsed += Time.deltaTime;

            if (Wave != lastWave)
            {
                lastWave = Wave;
                if (Wave > 1) ShowBanner(Wave == 2 ? "Wave 2: Blue Boos are hiding in the house." : "Wave 3: Green Sneaks are sneaking to the bell.", 4f);
                Level.SpawnEnergyBalls();
            }

            while (nextEvent < schedule.Count && schedule[nextEvent].time <= Elapsed)
            {
                StartCoroutine(SpawnGroup(schedule[nextEvent]));
                nextEvent++;
            }

            if (!grandpaSpawned && Elapsed >= GrandpaTime)
            {
                grandpaSpawned = true;
                Level.SpawnGhost(GhostType.Grandpa);
                ShowBanner("5 AM: Grandpa Ghost has woken up. Capture him before 6 AM!", 5f);
            }

            if (Elapsed >= nightLengthSeconds)
                Lose("6 AM: Grandpa Ghost got away.");
        }

        IEnumerator SpawnGroup(SpawnEvent ev)
        {
            for (int i = 0; i < ev.count; i++)
            {
                if (State != GameState.Playing) yield break;
                Level.SpawnGhost(ev.type);
                yield return new WaitForSeconds(3f);
            }
        }

        public void Register(GhostAgent g) { if (!Ghosts.Contains(g)) Ghosts.Add(g); }

        public void ShowBanner(string text, float seconds = 2.5f)
        {
            Banner = text;
            bannerUntil = Time.time + seconds;
        }

        public bool BannerVisible => !string.IsNullOrEmpty(Banner) && Time.time < bannerUntil;

        public string ClockText
        {
            get
            {
                float hours = Mathf.Clamp(Elapsed / nightLengthSeconds * 6f, 0f, 6f);
                int h = Mathf.FloorToInt(hours);
                int m = Mathf.FloorToInt((hours - h) * 60f);
                if (h >= 6) { h = 6; m = 0; }
                return $"{(h == 0 ? 12 : h)}:{m:00} AM";
            }
        }

        public void DamagePlayer(GhostAgent source)
        {
            if (State != GameState.Playing || Player == null) return;
            if (Time.time - lastHit < hitInvulnerability) return;
            lastHit = Time.time;
            Hearts = Mathf.Max(0, Hearts - 1);
            if (Vacuum) Vacuum.Jam(jamSeconds);
            Player.Shake(0.4f, 0.12f);
            ShowBanner($"BOO! {source.DisplayName} got you. Vacuum jammed!", 2f);
            if (Hearts <= 0) Lose($"{source.DisplayName} scared you out of the house.");
        }

        public void BellRung(GhostAgent source)
        {
            if (State != GameState.Playing) return;
            if (Bell) Bell.Ring();
            Lose($"The bell rang! A {source.DisplayName} reached the attic.");
        }

        public void OnGhostCaptured(GhostAgent g)
        {
            Ghosts.Remove(g);
            if (State != GameState.Playing) return;
            if (g.Type == GhostType.Grandpa) Win();
            else ShowBanner($"{g.DisplayName} captured!", 1.2f);
        }

        void Win()
        {
            State = GameState.Won;
            EndReason = $"You captured Grandpa Ghost at {ClockText}. The house is safe!";
            UnlockCursor();
        }

        void Lose(string reason)
        {
            if (State != GameState.Playing) return;
            State = GameState.Lost;
            EndReason = reason;
            UnlockCursor();
        }

        static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
