using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SheetHappens.Ghosts.EditorTools
{
    /// <summary>
    /// Generates the animation clips, Animator controllers and animated prefab variants
    /// from the Core Developer's ghost / vacuum prefabs. Menu: Tools > Sheet Happens > Build Animations.
    ///
    /// The ghost meshes are rigid parts with their pivots at the model origin (the feet), so the
    /// clips use whole-body squash / stretch / lean / spin on the Visual object, small translations
    /// for facial motion, and active-toggles for blinks. The original prefabs are never modified:
    /// each animated prefab nests the original one as "Model".
    ///
    ///   Root (GhostAnimatorDriver)            &lt;- path follower moves this
    ///     FloatPivot (GhostFloat)             &lt;- procedural bob / tilt
    ///       Visual (Animator)                 &lt;- clips animate this and the parts below
    ///         Model (original prefab)
    /// </summary>
    public static class GhostAnimationBuilder
    {
        const string SourceDir = "Assets/_Project/Art/Prefabs/";
        const string AnimDir = "Assets/_Project/Animations/";
        const string PrefabDir = "Assets/_Project/Prefabs/Agents/";

        // ---------------------------------------------------------------- menu

        [MenuItem("Tools/Sheet Happens/Build Animations/1 Wisp")] public static void BuildWisp() => Build(Wisp);
        [MenuItem("Tools/Sheet Happens/Build Animations/2 Blue Boo")] public static void BuildBlueBoo() => Build(BlueBoo);
        [MenuItem("Tools/Sheet Happens/Build Animations/3 Green Sneak")] public static void BuildGreenSneak() => Build(GreenSneak);
        [MenuItem("Tools/Sheet Happens/Build Animations/4 Grandpa")] public static void BuildGrandpa() => Build(Grandpa);
        [MenuItem("Tools/Sheet Happens/Build Animations/5 Vacuum")] public static void BuildVacuum() => Build(Vacuum);

        [MenuItem("Tools/Sheet Happens/Build Animations/Build ALL")]
        public static void BuildAll()
        {
            foreach (CharacterDef def in new[] { Wisp, BlueBoo, GreenSneak, Grandpa, Vacuum }) Build(def);
        }

        /// <summary>Entry point for batch mode: Unity -batchmode -executeMethod ...BuildAllBatch</summary>
        public static void BuildAllBatch()
        {
            BuildAll();
            Debug.Log("[GhostAnimationBuilder] Batch build finished.");
        }

        // ---------------------------------------------------------------- definitions

        sealed class CharacterDef
        {
            public string Id;                 // folder / file prefix
            public string SourcePrefab;       // Core Developer's prefab
            public string OutPrefab;          // animated variant name
            public bool IsVacuum;
            public float Tempo = 1f;          // multiplies every key time (bigger = slower)
            public float Reach = 1f;          // multiplies position offsets (bigger ghost = bigger motion)
            public float BobAmplitude = 0.05f;
            public float BobFrequency = 0.5f;
            public string[] Triggers;         // one-shot clips, same names as the clips
            public Action<Ctx> Define;
        }

        static readonly CharacterDef Wisp = new CharacterDef
        {
            Id = "Wisp", SourcePrefab = "Ghost_Wisp_Unity", OutPrefab = "Ghost_Wisp_Animated",
            Tempo = 0.8f, Reach = 0.6f, BobAmplitude = 0.04f, BobFrequency = 0.9f,
            Triggers = new[] { "Spawn", "Dodge", "Startled", "Caught", "RingBell" },
            Define = DefineWisp
        };

        static readonly CharacterDef BlueBoo = new CharacterDef
        {
            Id = "BlueBoo", SourcePrefab = "Ghost_BlueBoo_Unity", OutPrefab = "Ghost_BlueBoo_Animated",
            Tempo = 1.3f, Reach = 1.1f, BobAmplitude = 0.07f, BobFrequency = 0.35f,
            Triggers = new[] { "Spawn", "Hit", "Boo", "Peek", "Caught", "RingBell" },
            Define = DefineBlueBoo
        };

        static readonly CharacterDef GreenSneak = new CharacterDef
        {
            Id = "GreenSneak", SourcePrefab = "Ghost_GreenSneak_Unity", OutPrefab = "Ghost_GreenSneak_Animated",
            Tempo = 1.0f, Reach = 0.8f, BobAmplitude = 0.03f, BobFrequency = 0.6f,
            Triggers = new[] { "Spawn", "Freeze", "Peek", "Caught", "RingBell" },
            Define = DefineGreenSneak
        };

        static readonly CharacterDef Grandpa = new CharacterDef
        {
            Id = "Grandpa", SourcePrefab = "Ghost_Grandpa_Unity", OutPrefab = "Ghost_Grandpa_Animated",
            Tempo = 1.1f, Reach = 1.2f, BobAmplitude = 0.06f, BobFrequency = 0.4f,
            Triggers = new[] { "Spawn", "Search", "Boo", "Hit", "AngryTurn", "ShieldShatter", "Caught", "RingBell" },
            Define = DefineGrandpa
        };

        static readonly CharacterDef Vacuum = new CharacterDef
        {
            Id = "Vacuum", SourcePrefab = "Ghost_Vacuum_V2", OutPrefab = "Ghost_Vacuum_Animated",
            IsVacuum = true, Tempo = 1f, Reach = 1f,
            Triggers = new[] { "Shoot", "SwitchMode", "Collect" },
            Define = DefineVacuum
        };

        // ---------------------------------------------------------------- build pipeline

        sealed class Ctx
        {
            public CharacterDef Def;
            public Transform Visual;
            public HashSet<string> Missing = new HashSet<string>();
            public readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
            public readonly List<string> ClipOrder = new List<string>();

            public ClipBuilder Clip(string name, float length, bool loop)
            {
                var b = new ClipBuilder(this, name, length, loop);
                Clips[name] = b.Clip;
                ClipOrder.Add(name);
                return b;
            }
        }

        static void Build(CharacterDef def)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceDir + def.SourcePrefab + ".prefab");
            if (source == null)
            {
                Debug.LogError($"[GhostAnimationBuilder] Missing source prefab {SourceDir}{def.SourcePrefab}.prefab. " +
                               "Merge the 'models' branch into agent-controller first.");
                return;
            }

            EnsureFolder("Assets/_Project/Animations");
            EnsureFolder("Assets/_Project/Prefabs");
            EnsureFolder(PrefabDir.TrimEnd('/'));
            string charDir = AnimDir + def.Id;
            EnsureFolder(charDir);

            // 1. Hierarchy
            var root = new GameObject(def.OutPrefab);
            Transform visual;
            GameObject pivot = null;
            if (def.IsVacuum)
            {
                visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
            }
            else
            {
                pivot = new GameObject("FloatPivot");
                pivot.transform.SetParent(root.transform, false);
                visual = new GameObject("Visual").transform;
                visual.SetParent(pivot.transform, false);
            }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, visual);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // 2. Clips
            var ctx = new Ctx { Def = def, Visual = visual };
            def.Define(ctx);

            foreach (string clipName in ctx.ClipOrder)
            {
                string clipPath = $"{charDir}/{def.Id}_{clipName}.anim";
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null) AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(ctx.Clips[clipName], clipPath);
            }

            // 3. Controller
            string controllerPath = $"{charDir}/AC_{def.Id}.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null) AssetDatabase.DeleteAsset(controllerPath);
            AnimatorController controller = def.IsVacuum
                ? BuildVacuumController(controllerPath, ctx)
                : BuildGhostController(controllerPath, ctx);

            // 4. Components
            var animator = visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            if (def.IsVacuum)
            {
                var va = root.AddComponent<VacuumAnimator>();
                va.animator = animator;
            }
            else
            {
                var driver = root.AddComponent<GhostAnimatorDriver>();
                driver.animator = animator;
                var fl = pivot.AddComponent<GhostFloat>();
                fl.bobAmplitude = def.BobAmplitude;
                fl.bobFrequency = def.BobFrequency;
            }

            // 5. Prefab
            string prefabPath = PrefabDir + def.OutPrefab + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string missing = ctx.Missing.Count > 0 ? " Parts not found (curves skipped): " + string.Join(", ", ctx.Missing) : "";
            Debug.Log($"[GhostAnimationBuilder] {def.Id}: {ctx.ClipOrder.Count} clips, controller and prefab written to {charDir} / {prefabPath}.{missing}");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------------------------------------------------------------- controllers

        static AnimatorController BuildGhostController(string path, Ctx c)
        {
            AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter(GhostAnimParams.Speed, AnimatorControllerParameterType.Float);
            foreach (string t in c.Def.Triggers) ac.AddParameter(t, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = ac.layers[0].stateMachine;
            AnimatorState idle = sm.AddState("Idle");
            idle.motion = c.Clips["Idle"];
            sm.defaultState = idle;

            AnimatorState move = sm.AddState("Move");
            move.motion = c.Clips["Move"];
            Between(idle, move, GhostAnimParams.Speed, AnimatorConditionMode.Greater, GhostAnimParams.MoveThreshold);
            Between(move, idle, GhostAnimParams.Speed, AnimatorConditionMode.Less, GhostAnimParams.MoveThreshold);

            foreach (string t in c.Def.Triggers)
            {
                AnimatorState s = sm.AddState(t);
                s.motion = c.Clips[t];
                FromAny(sm, s, t);
                if (t != "Caught") ReturnTo(s, idle); // Caught holds its last pose (ghost is shrunk to nothing)
            }
            return ac;
        }

        static AnimatorController BuildVacuumController(string path, Ctx c)
        {
            AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter(GhostAnimParams.Speed, AnimatorControllerParameterType.Float);
            ac.AddParameter(GhostAnimParams.Jammed, AnimatorControllerParameterType.Bool);
            foreach (string t in c.Def.Triggers) ac.AddParameter(t, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = ac.layers[0].stateMachine;
            AnimatorState idle = sm.AddState("Idle"); idle.motion = c.Clips["Idle"];
            AnimatorState walk = sm.AddState("Walk"); walk.motion = c.Clips["Walk"];
            AnimatorState run = sm.AddState("Run"); run.motion = c.Clips["Run"];
            AnimatorState jam = sm.AddState("Jam"); jam.motion = c.Clips["Jam"];
            sm.defaultState = idle;

            float move = GhostAnimParams.MoveThreshold, runAt = GhostAnimParams.RunThreshold;
            Between(idle, walk, GhostAnimParams.Speed, AnimatorConditionMode.Greater, move);
            Between(walk, idle, GhostAnimParams.Speed, AnimatorConditionMode.Less, move);
            Between(walk, run, GhostAnimParams.Speed, AnimatorConditionMode.Greater, runAt);
            Between(run, walk, GhostAnimParams.Speed, AnimatorConditionMode.Less, runAt);
            Between(idle, run, GhostAnimParams.Speed, AnimatorConditionMode.Greater, runAt);
            Between(run, idle, GhostAnimParams.Speed, AnimatorConditionMode.Less, move);

            // Jammed (bool) holds the Jam loop until it is cleared.
            AnimatorStateTransition toJam = sm.AddAnyStateTransition(jam);
            toJam.AddCondition(AnimatorConditionMode.If, 0, GhostAnimParams.Jammed);
            ConfigureQuick(toJam);
            AnimatorStateTransition fromJam = jam.AddTransition(idle);
            fromJam.AddCondition(AnimatorConditionMode.IfNot, 0, GhostAnimParams.Jammed);
            fromJam.hasExitTime = false;
            fromJam.duration = 0.1f;

            foreach (string t in c.Def.Triggers)
            {
                AnimatorState s = sm.AddState(t);
                s.motion = c.Clips[t];
                FromAny(sm, s, t);
                ReturnTo(s, idle);
            }
            return ac;
        }

        static void Between(AnimatorState from, AnimatorState to, string param, AnimatorConditionMode mode, float threshold)
        {
            AnimatorStateTransition tr = from.AddTransition(to);
            tr.AddCondition(mode, threshold, param);
            tr.hasExitTime = false;
            tr.duration = 0.12f;
        }

        static void FromAny(AnimatorStateMachine sm, AnimatorState to, string trigger)
        {
            AnimatorStateTransition tr = sm.AddAnyStateTransition(to);
            tr.AddCondition(AnimatorConditionMode.If, 0, trigger);
            ConfigureQuick(tr);
        }

        static void ConfigureQuick(AnimatorStateTransition tr)
        {
            tr.hasExitTime = false;
            tr.duration = 0.06f;
            tr.canTransitionToSelf = false;
        }

        static void ReturnTo(AnimatorState from, AnimatorState idle)
        {
            AnimatorStateTransition tr = from.AddTransition(idle);
            tr.hasExitTime = true;
            tr.exitTime = 1f;
            tr.duration = 0.12f;
        }

        // ---------------------------------------------------------------- clip authoring helper

        /// <summary>
        /// Fluent helper. Values are offsets from the part's rest pose:
        ///   px/py/pz = position added (scaled by the character's Reach)
        ///   rx/ry/rz = euler degrees added
        ///   sx/sy/sz = scale multiplier (1 = unchanged)
        /// Times are in seconds and are multiplied by the character's Tempo.
        /// A part name of "" means the Visual object itself (pivot at the feet).
        /// </summary>
        sealed class ClipBuilder
        {
            readonly Ctx _ctx;
            readonly float _length;
            readonly bool _loop;
            public readonly AnimationClip Clip;

            public ClipBuilder(Ctx ctx, string name, float length, bool loop)
            {
                _ctx = ctx;
                _length = length * ctx.Def.Tempo;
                _loop = loop;
                Clip = new AnimationClip { name = ctx.Def.Id + "_" + name, frameRate = 60f };
            }

            public ClipBuilder K(string part, string prop, params float[] timeValue)
            {
                Transform t = Find(part);
                if (t == null) return this;

                char kind = prop[0];
                char axis = prop[1];
                string attribute;
                float rest;
                bool multiply = false;
                float reach = 1f;
                switch (kind)
                {
                    case 'p': attribute = "localPosition." + axis; rest = Axis(t.localPosition, axis); reach = _ctx.Def.Reach; break;
                    case 'r': attribute = "localEulerAnglesRaw." + axis; rest = Axis(t.localEulerAngles, axis); break;
                    case 's': attribute = "localScale." + axis; rest = Axis(t.localScale, axis); multiply = true; break;
                    default: throw new ArgumentException("Unknown property " + prop);
                }

                var keys = new Keyframe[timeValue.Length / 2];
                for (int i = 0; i < keys.Length; i++)
                {
                    float v = timeValue[2 * i + 1];
                    keys[i] = new Keyframe(timeValue[2 * i] * _ctx.Def.Tempo, multiply ? rest * v : rest + v * reach);
                }
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
                Clip.SetCurve(PathOf(t), typeof(Transform), attribute, curve);
                return this;
            }

            /// <summary>Uniform scale (sx, sy and sz together).</summary>
            public ClipBuilder Scale(string part, params float[] timeValue)
            {
                return K(part, "sx", timeValue).K(part, "sy", timeValue).K(part, "sz", timeValue);
            }

            /// <summary>Stepped show/hide of a part. Values: 1 = visible, 0 = hidden.</summary>
            public ClipBuilder Active(string part, params float[] timeValue)
            {
                Transform t = Find(part, true);
                if (t == null || t == _ctx.Visual) return this;
                var keys = new Keyframe[timeValue.Length / 2];
                for (int i = 0; i < keys.Length; i++)
                    keys[i] = new Keyframe(timeValue[2 * i] * _ctx.Def.Tempo, timeValue[2 * i + 1], 0f, float.PositiveInfinity);
                Clip.SetCurve(PathOf(t), typeof(GameObject), "m_IsActive", new AnimationCurve(keys));
                return this;
            }

            /// <summary>Hide both eyes between two times, e.g. a blink.</summary>
            public ClipBuilder Blink(float from, float to)
            {
                foreach (string eye in new[] { "EyeGlow_L", "EyeGlow_R", "EyeShine_L", "EyeShine_R", "Glint_L", "Glint_R" })
                    Active(eye, 0f, 1f, from, 0f, to, 1f);
                return this;
            }

            Transform Find(string part, bool optional = false)
            {
                if (string.IsNullOrEmpty(part)) return _ctx.Visual;
                foreach (Transform t in _ctx.Visual.GetComponentsInChildren<Transform>(true))
                    if (t != _ctx.Visual && t.name == part) return t;
                if (!optional) _ctx.Missing.Add(part);
                return null;
            }

            string PathOf(Transform t)
            {
                if (t == _ctx.Visual) return "";
                var names = new List<string>();
                for (Transform c = t; c != _ctx.Visual; c = c.parent) names.Insert(0, c.name);
                return string.Join("/", names);
            }

            static float Axis(Vector3 v, char a) => a == 'x' ? v.x : a == 'y' ? v.y : v.z;

            /// <summary>Call last: applies loop settings once all curves exist.</summary>
            public void Done()
            {
                AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(Clip);
                s.loopTime = _loop;
                s.startTime = 0f;
                s.stopTime = _length;
                AnimationUtility.SetAnimationClipSettings(Clip, s);
            }
        }

        // ---------------------------------------------------------------- shared ghost clips

        static void Spawn(Ctx c, float length = 1.2f)
        {
            c.Clip("Spawn", length, false)
                .K("", "sx", 0f, 0.2f, 0.5f, 1.05f, 0.8f, 0.97f, length, 1f)
                .K("", "sy", 0f, 0.02f, 0.5f, 1.2f, 0.8f, 0.95f, length, 1f)
                .K("", "sz", 0f, 0.2f, 0.5f, 1.05f, 0.8f, 0.97f, length, 1f)
                .K("", "py", 0f, -0.4f, 0.5f, 0.1f, length, 0f)
                .Active("EyeGlow_L", 0f, 0f, 0.65f, 1f)
                .Active("EyeGlow_R", 0f, 0f, 0.65f, 1f)
                .Active("EyeShine_L", 0f, 0f, 0.65f, 1f)
                .Active("EyeShine_R", 0f, 0f, 0.65f, 1f)
                .Done();
        }

        static void Caught(Ctx c, float length = 1.0f)
        {
            c.Clip("Caught", length, false)
                .K("", "sx", 0f, 1f, 0.25f * length, 0.55f, length, 0f)
                .K("", "sy", 0f, 1f, 0.25f * length, 1.5f, length, 0f)
                .K("", "sz", 0f, 1f, 0.25f * length, 0.55f, length, 0f)
                .K("", "ry", 0f, 0f, length, 720f)
                .K("", "py", 0f, 0f, 0.25f * length, 0.1f, length, 0.5f)
                .Done();
        }

        static void RingBell(Ctx c)
        {
            c.Clip("RingBell", 1.0f, false)
                .K("", "py", 0f, 0f, 0.15f, 0.2f, 0.3f, 0f, 0.45f, 0.2f, 0.6f, 0f, 0.78f, 0.14f, 1f, 0f)
                .K("", "rz", 0f, 0f, 0.15f, 10f, 0.3f, -10f, 0.45f, 10f, 0.6f, -10f, 0.8f, 5f, 1f, 0f)
                .Done();
        }

        static void Idle(Ctx c, float length, float breathe, float lookShift)
        {
            c.Clip("Idle", length, true)
                .K("", "py", 0f, 0f, length * 0.5f, 0.02f, length, 0f)
                .K("", "sy", 0f, 1f, length * 0.5f, 1f + breathe, length, 1f)
                .K("", "sx", 0f, 1f, length * 0.5f, 1f - breathe * 0.5f, length, 1f)
                .K("Face", "px", 0f, 0f, length * 0.3f, lookShift, length * 0.7f, -lookShift, length, 0f)
                .Blink(length * 0.78f, length * 0.78f + 0.08f)
                .Done();
        }

        static void Move(Ctx c, float length, float lean, float flutter)
        {
            c.Clip("Move", length, true)
                .K("", "rx", 0f, lean, length * 0.25f, lean + flutter, length * 0.5f, lean, length * 0.75f, lean + flutter, length, lean)
                .K("", "sz", 0f, 1f, length * 0.5f, 1f + flutter * 0.01f, length, 1f)
                .K("", "py", 0f, 0.02f, length * 0.5f, 0.06f, length, 0.02f)
                .K("", "rz", 0f, 0f, length * 0.25f, flutter * 0.5f, length * 0.75f, -flutter * 0.5f, length, 0f)
                .Done();
        }

        static void Hit(Ctx c, float knockback)
        {
            c.Clip("Hit", 0.5f, false)
                .K("", "sy", 0f, 1f, 0.1f, 0.7f, 0.25f, 1.15f, 0.5f, 1f)
                .K("", "sx", 0f, 1f, 0.1f, 1.25f, 0.25f, 0.9f, 0.5f, 1f)
                .K("", "sz", 0f, 1f, 0.1f, 1.25f, 0.25f, 0.9f, 0.5f, 1f)
                .K("", "rz", 0f, 0f, 0.1f, -8f, 0.2f, 6f, 0.3f, -3f, 0.5f, 0f)
                .K("", "pz", 0f, 0f, 0.1f, -knockback, 0.5f, 0f)
                .K("Brow_L", "py", 0f, 0f, 0.1f, -0.03f, 0.5f, 0f)
                .K("Brow_R", "py", 0f, 0f, 0.1f, -0.03f, 0.5f, 0f)
                .Done();
        }

        static void Boo(Ctx c, float puff, float hatLift)
        {
            ClipBuilder b = c.Clip("Boo", 1.0f, false)
                .Scale("", 0f, 1f, 0.25f, puff, 0.7f, puff * 0.97f, 1f, 1f)
                .K("", "py", 0f, 0f, 0.25f, 0.12f, 1f, 0f)
                .K("Brow_L", "py", 0f, 0f, 0.2f, -0.04f, 0.8f, -0.04f, 1f, 0f)
                .K("Brow_R", "py", 0f, 0f, 0.2f, -0.04f, 0.8f, -0.04f, 1f, 0f);
            if (hatLift > 0f) b.K("Hat", "py", 0f, 0f, 0.25f, hatLift, 0.5f, 0f, 1f, 0f);
            b.Done();
        }

        // ---------------------------------------------------------------- per character

        static void DefineWisp(Ctx c)
        {
            Spawn(c);
            Idle(c, 1.6f, 0.02f, 0.015f);
            Move(c, 0.6f, 12f, 4f);
            Caught(c, 0.8f);
            RingBell(c);

            // Quick sideways dodge when aimed at, kept short so the player still gets a fair shot.
            c.Clip("Dodge", 0.4f, false)
                .K("", "px", 0f, 0f, 0.12f, 0.45f, 0.4f, 0f)
                .K("", "rz", 0f, 0f, 0.12f, -22f, 0.4f, 0f)
                .K("", "sy", 0f, 1f, 0.12f, 0.9f, 0.4f, 1f)
                .Done();

            // Startled: stretches up, eyes pop (blink gap), curl tip jolts.
            c.Clip("Startled", 0.5f, false)
                .K("", "sy", 0f, 1f, 0.1f, 1.25f, 0.5f, 1f)
                .K("", "sx", 0f, 1f, 0.1f, 0.85f, 0.5f, 1f)
                .K("", "sz", 0f, 1f, 0.1f, 0.85f, 0.5f, 1f)
                .K("", "py", 0f, 0f, 0.1f, 0.1f, 0.5f, 0f)
                .K("CurlTip", "px", 0f, 0f, 0.1f, 0.04f, 0.3f, -0.02f, 0.5f, 0f)
                .Blink(0.02f, 0.08f)
                .Done();
        }

        static void DefineBlueBoo(Ctx c)
        {
            Spawn(c, 1.5f);
            Idle(c, 2.4f, 0.025f, 0.02f);
            Move(c, 0.9f, 6f, 2.5f);
            Hit(c, 0.18f);
            Boo(c, 1.35f, 0f);
            Caught(c, 1.2f);
            RingBell(c);

            // Peek around a door / cupboard edge, hold, then slip back.
            c.Clip("Peek", 1.4f, false)
                .K("", "rz", 0f, 0f, 0.4f, 16f, 1.0f, 16f, 1.4f, 0f)
                .K("", "px", 0f, 0f, 0.4f, 0.2f, 1.0f, 0.2f, 1.4f, 0f)
                .K("Face", "px", 0f, 0f, 0.4f, 0.05f, 1.0f, 0.05f, 1.4f, 0f)
                .Blink(0.9f, 0.98f)
                .Done();
        }

        static void DefineGreenSneak(Ctx c)
        {
            Spawn(c, 1.0f);
            // Low, crouched floating posture.
            c.Clip("Idle", 1.8f, true)
                .K("", "sy", 0f, 0.94f, 0.9f, 0.96f, 1.8f, 0.94f)
                .K("", "sx", 0f, 1.04f, 0.9f, 1.03f, 1.8f, 1.04f)
                .K("Face", "px", 0f, 0f, 0.5f, 0.03f, 1.3f, -0.03f, 1.8f, 0f)
                .Blink(1.4f, 1.48f)
                .Done();
            c.Clip("Move", 0.7f, true)
                .K("", "rx", 0f, 18f, 0.35f, 21f, 0.7f, 18f)
                .K("", "sy", 0f, 0.92f, 0.7f, 0.92f)
                .K("", "py", 0f, 0.01f, 0.35f, 0.04f, 0.7f, 0.01f)
                .K("", "rz", 0f, 0f, 0.175f, 3f, 0.525f, -3f, 0.7f, 0f)
                .Done();
            Caught(c, 0.9f);
            RingBell(c);

            // Freeze: spotted, so stop dead and shiver. The mover pauses the ghost while this plays.
            c.Clip("Freeze", 0.7f, false)
                .Scale("", 0f, 1f, 0.06f, 1.08f, 0.12f, 1f, 0.7f, 1f)
                .K("", "rz", 0f, 0f, 0.1f, 2.5f, 0.2f, -2.5f, 0.3f, 2.5f, 0.4f, -2.5f, 0.5f, 2f, 0.6f, -1f, 0.7f, 0f)
                .Blink(0.2f, 0.28f)
                .Done();

            // Peek around cover.
            c.Clip("Peek", 1.2f, false)
                .K("", "rz", 0f, 0f, 0.35f, -18f, 0.85f, -18f, 1.2f, 0f)
                .K("", "px", 0f, 0f, 0.35f, -0.16f, 0.85f, -0.16f, 1.2f, 0f)
                .K("Face", "px", 0f, 0f, 0.35f, -0.04f, 0.85f, -0.04f, 1.2f, 0f)
                .K("", "sy", 0f, 1f, 0.35f, 0.92f, 0.85f, 0.92f, 1.2f, 1f)
                .Done();
        }

        static void DefineGrandpa(Ctx c)
        {
            Spawn(c, 1.6f);
            // Upright idle; the hat and moustache add character.
            c.Clip("Idle", 2.6f, true)
                .K("", "py", 0f, 0f, 1.3f, 0.02f, 2.6f, 0f)
                .K("", "sy", 0f, 1f, 1.3f, 1.015f, 2.6f, 1f)
                .K("Face", "px", 0f, 0f, 0.8f, 0.02f, 1.8f, -0.02f, 2.6f, 0f)
                .K("Hat", "rz", 0f, 0f, 1.3f, 2.5f, 2.6f, 0f)
                .K("Moustache", "px", 0f, 0f, 0.65f, 0.012f, 1.3f, 0f, 1.95f, -0.012f, 2.6f, 0f)
                .K("Moustache", "py", 0f, 0f, 1.3f, -0.006f, 2.6f, 0f)
                .Blink(2.0f, 2.08f)
                .Done();
            Move(c, 0.9f, 4f, 1.5f);
            Hit(c, 0.12f);
            Boo(c, 1.25f, 0.1f);
            RingBell(c);

            // Look left, then right, then centre.
            c.Clip("Search", 2.4f, false)
                .K("", "ry", 0f, 0f, 0.6f, -38f, 1.2f, -38f, 1.8f, 38f, 2.4f, 0f)
                .K("Hat", "rz", 0f, 0f, 0.6f, 4f, 1.8f, -4f, 2.4f, 0f)
                .K("Moustache", "px", 0f, 0f, 0.6f, 0.015f, 1.8f, -0.015f, 2.4f, 0f)
                .Done();

            // Spins around angrily when hit (ends facing the way it started).
            c.Clip("AngryTurn", 0.9f, false)
                .K("", "ry", 0f, 0f, 0.5f, 190f, 0.9f, 360f)
                .K("", "sx", 0f, 1f, 0.2f, 1.12f, 0.9f, 1f)
                .K("", "sy", 0f, 1f, 0.2f, 0.92f, 0.9f, 1f)
                .K("", "sz", 0f, 1f, 0.2f, 1.12f, 0.9f, 1f)
                .K("Brow_L", "py", 0f, 0f, 0.2f, -0.04f, 0.8f, -0.04f, 0.9f, 0f)
                .K("Brow_R", "py", 0f, 0f, 0.2f, -0.04f, 0.8f, -0.04f, 0.9f, 0f)
                .K("Hat", "py", 0f, 0f, 0.3f, 0.06f, 0.9f, 0f)
                .Done();

            // Reaction to the gold shield bursting. The shield cracks / flashes via particles elsewhere.
            c.Clip("ShieldShatter", 1.0f, false)
                .Scale("", 0f, 1f, 0.1f, 1.15f, 0.3f, 0.9f, 1f, 1f)
                .K("", "rz", 0f, 0f, 0.1f, 6f, 0.2f, -6f, 0.35f, 4f, 0.5f, 0f)
                .K("Hat", "py", 0f, 0f, 0.15f, 0.12f, 0.5f, 0f, 1f, 0f)
                .K("Hat", "rz", 0f, 0f, 0.15f, 15f, 0.5f, 0f)
                .Blink(0.05f, 0.12f)
                .Done();

            // Caught: tips the hat, smiles, then is sucked into the vacuum.
            c.Clip("Caught", 1.6f, false)
                .K("Hat", "rz", 0f, 0f, 0.4f, -28f, 0.9f, -28f, 1.1f, 0f, 1.6f, 0f)
                .K("Hat", "py", 0f, 0f, 0.4f, 0.1f, 0.9f, 0.1f, 1.1f, 0f, 1.6f, 0f)
                .K("Moustache", "py", 0f, 0f, 0.4f, 0.015f, 0.9f, 0.015f, 1.1f, 0f)
                .K("", "sx", 0f, 1f, 1.1f, 1f, 1.35f, 0.55f, 1.6f, 0f)
                .K("", "sy", 0f, 1f, 1.1f, 1f, 1.35f, 1.5f, 1.6f, 0f)
                .K("", "sz", 0f, 1f, 1.1f, 1f, 1.35f, 0.55f, 1.6f, 0f)
                .K("", "ry", 0f, 0f, 1.1f, 0f, 1.6f, 720f)
                .K("", "py", 0f, 0f, 1.1f, 0f, 1.35f, 0.1f, 1.6f, 0.5f)
                .Done();
        }

        static void DefineVacuum(Ctx c)
        {
            c.Clip("Idle", 3f, true)
                .K("", "py", 0f, 0f, 1.5f, 0.004f, 3f, 0f)
                .K("", "rx", 0f, 0f, 1.5f, 0.4f, 3f, 0f)
                .K("", "pz", 0f, 0f, 1.5f, -0.003f, 3f, 0f)
                .Done();

            c.Clip("Walk", 0.8f, true)
                .K("", "px", 0f, 0f, 0.2f, 0.012f, 0.4f, 0f, 0.6f, -0.012f, 0.8f, 0f)
                .K("", "py", 0f, 0f, 0.2f, -0.012f, 0.4f, 0f, 0.6f, -0.012f, 0.8f, 0f)
                .K("", "rz", 0f, 0f, 0.2f, 1.5f, 0.4f, 0f, 0.6f, -1.5f, 0.8f, 0f)
                .Done();

            // Run: the vacuum drops lower and sways more.
            c.Clip("Run", 0.55f, true)
                .K("", "px", 0f, 0f, 0.1375f, 0.02f, 0.275f, 0f, 0.4125f, -0.02f, 0.55f, 0f)
                .K("", "py", 0f, -0.03f, 0.1375f, -0.055f, 0.275f, -0.03f, 0.4125f, -0.055f, 0.55f, -0.03f)
                .K("", "rx", 0f, 6f, 0.275f, 7f, 0.55f, 6f)
                .K("", "rz", 0f, 0f, 0.1375f, 3f, 0.4125f, -3f, 0.55f, 0f)
                .Done();

            // Recoil kicks the vacuum back (-z) and tips the nozzle up.
            c.Clip("Shoot", 0.25f, false)
                .K("", "pz", 0f, 0f, 0.04f, -0.06f, 0.25f, 0f)
                .K("", "rx", 0f, 0f, 0.04f, -4f, 0.25f, 0f)
                .Scale("", 0f, 1f, 0.04f, 1.03f, 0.25f, 1f)
                .Done();

            c.Clip("SwitchMode", 0.6f, false)
                .K("", "py", 0f, 0f, 0.15f, -0.05f, 0.3f, -0.05f, 0.6f, 0f)
                .K("", "rx", 0f, 0f, 0.15f, 15f, 0.3f, 15f, 0.6f, 0f)
                .K("", "rz", 0f, 0f, 0.3f, -12f, 0.6f, 0f)
                .Done();

            c.Clip("Jam", 0.4f, true)
                .K("", "px", 0f, 0f, 0.05f, 0.008f, 0.15f, -0.008f, 0.25f, 0.008f, 0.35f, -0.008f, 0.4f, 0f)
                .K("", "rz", 0f, 0f, 0.1f, 2f, 0.3f, -2f, 0.4f, 0f)
                .Done();

            c.Clip("Collect", 0.5f, false)
                .Scale("", 0f, 1f, 0.15f, 1.08f, 0.5f, 1f)
                .K("", "pz", 0f, 0f, 0.15f, 0.015f, 0.5f, 0f)
                .Done();
        }
    }
}
