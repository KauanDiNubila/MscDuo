using System.Collections.Generic;
using MSCLoader;
using UnityEngine;

namespace MscDuo.Sync
{
    internal class Avatar
    {
        const string WalkerPath = "Randomizer/Walkers/Kale/Pivot/Char";
        const string DriverPath = "KUSKI/LOD/Driver/skeleton";
        const string HeadPath = "skeleton/pelvis/spine_middle/spine_upper/HeadPivot/head";
        const string Walk = "fat_walk";
        const string Stand = "fat_standing";
        const float WalkAnimSpeed = 1.4f;
        const float NameHeight = 2.1f;
        const float EyesAhead = 0.08f;
        const float EyesBelowTop = 0.11f;
        const float DefaultEyesAboveHead = 0.1f;
        const float DefaultFeetBelowPivot = 0.28f;
        const float SkinWidth = 0.03f;
        const int PeePoints = 14;

        static readonly Dictionary<int, string[]> Outfits = new Dictionary<int, string[]>
        {
            { 1, new[] { "char_shirt01", "char_pants01", "char_face01" } },
            { 2, new[] { "char_shirt13", "char_pants05", "char_face03" } },
        };

        internal static readonly Dictionary<string, Vector3> BikePose = new Dictionary<string, Vector3>
        {
            { "thig_left", new Vector3(0f, 0f, 20f) },
            { "thig_right", new Vector3(0f, 0f, 20f) },
            { "thig_left/knee_left", new Vector3(0f, 0f, 40f) },
            { "thig_right/knee_right", new Vector3(0f, 0f, 40f) },
            { "pelvis/spine_middle/spine_upper/collar_left/shoulder_left", new Vector3(0f, 30f, 0f) },
            { "pelvis/spine_middle/spine_upper/collar_right/shoulder_right", new Vector3(0f, -30f, 0f) },
        };

        internal static readonly Dictionary<string, Vector3> PassengerArms = new Dictionary<string, Vector3>
        {
            { "pelvis/spine_middle/spine_upper/collar_left/shoulder_left", new Vector3(0f, 50f, 0f) },
            { "pelvis/spine_middle/spine_upper/collar_right/shoulder_right", new Vector3(0f, -50f, 0f) },
        };

        const string Arms = "skeleton/pelvis/spine_middle/spine_upper/";
        const string Punch = "fighter_right_hit_jaw";
        const string Finger = "fighter_right_fightpos_on";
        const float FingerFrame = 0.6f;
        const string Wave = "fat_waving_hello";
        const string Drink = "fat_handsright_drink";
        const string Smoke = "fat_handsleft_smoke";
        const string LookWatch = "fat_handsleft_offer_on";
        const string FingerHandPath = "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/MiddleFinger/Pivot";
        const string WatchPath = "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Watch/Animate/BreathAnim/WristwatchHand";

        internal static Vector3 FingerOffset = new Vector3(-0.34f, 0f, 0f);
        internal static Vector3 FingerRotation = new Vector3(0f, 0f, -90f);
        internal static float FingerScale = 1.6f;
        internal static Vector3 WatchOffset = new Vector3(-0.23f, 0f, 0f);
        internal static Vector3 WatchRotation = new Vector3(0f, 180f, 180f);
        internal static float WatchScale = 0.8f;

        static readonly string[][] GestureSources =
        {
            new[] { "HUMANS", "FighterPub/Fighter2/Pivot/Char/skeleton/pelvis/spine_middle/spine_upper/collar_right", Punch },
            new[] { "DANCEHALL", "Functions/FIGHTER/Fighter/Pivot/Char/skeleton/pelvis/spine_middle/spine_upper/collar_right", Punch },
            new[] { "HUMANS", "FighterPub/Fighter2/Pivot/Char/skeleton/pelvis/spine_middle/spine_upper/collar_right", Finger },
            new[] { "DANCEHALL", "Functions/FIGHTER/Fighter/Pivot/Char/skeleton/pelvis/spine_middle/spine_upper/collar_right", Finger },
            new[] { "JOBS", "HouseShit1/LOD/ShitNPC/ShitMan1/skeleton/pelvis/RotationPivot/spine_middle/spine_upper/collar_right", Drink },
            new[] { "JOBS", "HouseShit1/LOD/ShitNPC/ShitMan1/skeleton/pelvis/RotationPivot/spine_middle/spine_upper/collar_left", Smoke },
            new[] { "JOBS", "HouseShit1/LOD/ShitNPC/ShitMan1/skeleton/pelvis/RotationPivot/spine_middle/spine_upper/collar_left", LookWatch },
        };
        static readonly Dictionary<string, AnimationClip> gestureClips = new Dictionary<string, AnimationClip>();
        static AudioClip peeClip;

        static GameObject template;
        static Dictionary<string, KeyValuePair<Vector3, Quaternion>> seatedPose;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static float feetBelowPivot = -1f;

        readonly string name;
        GameObject root;
        TextMesh label;
        Transform body;
        Transform head;
        SkinnedMeshRenderer mesh;
        Animation legs;
        Animation[] animations;
        readonly Dictionary<Animation, string> idles = new Dictionary<Animation, string>();
        readonly List<KeyValuePair<Transform, string>> bones = new List<KeyValuePair<Transform, string>>();
        readonly List<KeyValuePair<Vector3, Quaternion>> rest = new List<KeyValuePair<Vector3, Quaternion>>();
        int skin;
        int wanted = 1;
        bool seated;
        Animation collarLeft;
        Animation collarRight;
        string idleLeft;
        string idleRight;
        string playingLeft;
        string playingRight;
        int voiceCount = -1;
        Transform pelvis;
        LineRenderer pee;
        AudioSource peeAudio;
        AudioSource voice;
        readonly Dictionary<Transform, Transform[]> grips = new Dictionary<Transform, Transform[]>();
        GameObject fingerHand;
        Transform handRight;
        Transform watch;
        readonly List<int> legBones = new List<int>();
        Transform ankleLeft;
        Transform ankleRight;
        float restAnkle;
        bool crouching;
        Vector3 lastFeet;
        bool hasLast;
        float speed;
        float eyesAboveHead = DefaultEyesAboveHead;
        bool eyesMeasured;

        internal Avatar(string name)
        {
            this.name = name;
        }

        void Build()
        {
            root = new GameObject("MscDuo_Avatar_" + name);
            skin = 0;
            seated = false;
            hasLast = false;

            var tag = new GameObject("Name");
            tag.transform.parent = root.transform;
            tag.transform.localPosition = new Vector3(0f, NameHeight, 0f);
            label = tag.AddComponent<TextMesh>();
            label.text = name;
            label.anchor = TextAnchor.MiddleCenter;
            label.characterSize = 0.018f;
            label.fontSize = 48;
            label.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.font = font;
            tag.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            if (!LoadTemplate()) return;
            var copy = (GameObject)Object.Instantiate(template);
            copy.name = "Char";
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(behaviour);
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) Object.Destroy(collider);
            body = copy.transform;
            body.parent = root.transform;
            body.localPosition = template.transform.localPosition;
            body.localRotation = template.transform.localRotation;
            body.localScale = template.transform.localScale;
            copy.SetActive(true);

            head = body.Find(HeadPath);
            mesh = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0];
            mesh.enabled = true;
            mesh.updateWhenOffscreen = true;
            var skeleton = body.Find("skeleton");
            legs = skeleton != null ? skeleton.GetComponent<Animation>() : null;
            animations = copy.GetComponentsInChildren<Animation>(true);
            idles.Clear();
            foreach (var anim in animations)
            {
                anim.enabled = true;
                anim.cullingType = AnimationCullingType.AlwaysAnimate;
                anim.wrapMode = WrapMode.Loop;
                if (anim == legs) continue;
                foreach (AnimationState state in anim) { idles[anim] = state.name; anim.Play(state.name); break; }
            }
            if (legs != null && legs[Stand] != null) legs.Play(Stand);

            bones.Clear();
            rest.Clear();
            if (skeleton != null && legs != null && legs[Stand] != null) legs[Stand].clip.SampleAnimation(skeleton.gameObject, 0f);
            if (skeleton != null) Collect(skeleton, "");

            collarLeft = Collar("collar_left", out idleLeft);
            collarRight = Collar("collar_right", out idleRight);
            handRight = body.Find(Arms + "collar_right/shoulder_right/arm_right");
            var upperRight = body.Find(Arms + "collar_right/shoulder_right");
            var player = GameObject.Find("PLAYER");
            var sourceHand = player != null ? player.transform.Find(FingerHandPath) : null;
            if (sourceHand != null && upperRight != null)
            {
                fingerHand = new GameObject("MiddleFingerHand");
                fingerHand.transform.parent = upperRight;
                var hand = (GameObject)Object.Instantiate(sourceHand.gameObject);
                var anim = hand.GetComponent<Animation>();
                if (anim != null)
                {
                    foreach (AnimationState state in anim) { state.clip.SampleAnimation(hand, state.clip.length * FingerFrame); break; }
                    Object.Destroy(anim);
                }
                hand.transform.parent = fingerHand.transform;
                hand.transform.localPosition = Vector3.zero;
                hand.transform.localRotation = Quaternion.identity;
                hand.SetActive(true);
                foreach (var renderer in hand.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { renderer.enabled = true; renderer.updateWhenOffscreen = true; }
                fingerHand.SetActive(false);
            }
            var forearmLeft = body.Find(Arms + "collar_left/shoulder_left/arm_left");
            var sourceWatch = player != null ? player.transform.Find(WatchPath) : null;
            if (sourceWatch != null && forearmLeft != null)
            {
                var watchCopy = (GameObject)Object.Instantiate(sourceWatch.gameObject);
                watchCopy.name = "Wristwatch";
                watch = watchCopy.transform;
                watch.parent = forearmLeft;
                watchCopy.SetActive(true);
                foreach (var renderer in watchCopy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
            }
            legBones.Clear();
            for (int i = 0; i < bones.Count; i++)
                if (bones[i].Value.StartsWith("thig_")) legBones.Add(i);
            ankleLeft = body.Find("skeleton/thig_left/knee_left/ankle_left");
            ankleRight = body.Find("skeleton/thig_right/knee_right/ankle_right");
            restAnkle = ankleLeft != null && ankleRight != null ? Mathf.Min(ankleLeft.position.y, ankleRight.position.y) - root.transform.position.y : 0.08f;
            crouching = false;
            playingLeft = playingRight = null;
            pelvis = body.Find("skeleton/pelvis");
            head = head ?? body;

            var stream = new GameObject("Pee");
            stream.transform.parent = root.transform;
            pee = stream.AddComponent<LineRenderer>();
            pee.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Diffuse"));
            pee.SetColors(new Color(1f, 0.92f, 0.35f, 0.75f), new Color(1f, 0.92f, 0.35f, 0.35f));
            pee.SetWidth(0.03f, 0.045f);
            pee.SetVertexCount(PeePoints);
            pee.enabled = false;
            peeAudio = stream.AddComponent<AudioSource>();
            peeAudio.clip = peeClip;
            peeAudio.loop = true;
            peeAudio.spatialBlend = 1f;
            peeAudio.maxDistance = 15f;

            voice = head.gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 2f;
            voice.maxDistance = 40f;
        }

        Animation Collar(string side, out string idle)
        {
            idle = null;
            var bone = body.Find(Arms + side);
            var anim = bone != null ? bone.GetComponent<Animation>() : null;
            if (anim == null) return null;
            foreach (AnimationState state in anim) { idle = state.name; break; }
            foreach (var pair in gestureClips)
                if (anim[pair.Key] == null) anim.AddClip(pair.Value, pair.Key);
            return anim;
        }

        void Collect(Transform t, string path)
        {
            foreach (Transform child in t)
            {
                var childPath = path.Length == 0 ? child.name : path + "/" + child.name;
                bones.Add(new KeyValuePair<Transform, string>(child, childPath));
                rest.Add(new KeyValuePair<Vector3, Quaternion>(child.localPosition, child.localRotation));
                Collect(child, childPath);
            }
        }

        internal void SetSkin(int value)
        {
            wanted = value;
            if (root == null || mesh == null || value == skin) return;
            string[] outfit;
            if (!Outfits.TryGetValue(value, out outfit)) return;
            var current = mesh.sharedMaterials;
            for (int i = 0; i < outfit.Length && i < current.Length; i++)
            {
                var material = FindMaterial(outfit[i]);
                if (material != null) current[i] = material;
            }
            mesh.sharedMaterials = current;
            skin = value;
        }

        internal void Apply(Vector3 pivot, float yaw, float pitch)
        {
            if (!Ready()) return;
            if (seated) GetUp();
            var feet = pivot - Vector3.up * FeetBelowPivot();
            root.transform.parent = null;
            root.transform.position = feet;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var dt = Time.deltaTime;
            if (hasLast && dt > 0f)
            {
                var moved = feet - lastFeet;
                moved.y = 0f;
                speed = Mathf.Lerp(speed, moved.magnitude / dt, Mathf.Clamp01(dt * 8f));
            }
            if (hasLast && !eyesMeasured && !crouching && head != body)
            {
                eyesMeasured = true;
                eyesAboveHead = mesh.bounds.max.y - EyesBelowTop - head.position.y;
                ModConsole.Print($"<color=orange>[MSC Duo]</color> avatar {name}: olhos {eyesAboveHead:0.000} m acima do osso da cabeça.");
            }
            lastFeet = feet;
            hasLast = true;

            if (legs != null && legs[Walk] != null)
            {
                if (speed > 0.3f)
                {
                    legs[Walk].speed = Mathf.Clamp(speed / WalkAnimSpeed, 0.6f, 3f);
                    legs.CrossFade(Walk, 0.2f);
                }
                else legs.CrossFade(Stand, 0.3f);
            }
            FaceCamera();
        }

        internal void Sit(Transform vehicle, Vector3 headWorld, bool bike, bool passenger)
        {
            if (!Ready()) return;
            if (!seated)
            {
                seated = true;
                hasLast = false;
                foreach (var anim in animations) { anim.Stop(); anim.enabled = false; }
                playingLeft = playingRight = null;
            }
            if (seatedPose != null)
                foreach (var bone in bones)
                {
                    KeyValuePair<Vector3, Quaternion> pose;
                    if (!seatedPose.TryGetValue(bone.Value, out pose)) continue;
                    var bend = Vector3.zero;
                    bone.Key.localPosition = pose.Key;
                    var adjusted = passenger && PassengerArms.TryGetValue(bone.Value, out bend) || bike && BikePose.TryGetValue(bone.Value, out bend);
                    bone.Key.localRotation = adjusted ? pose.Value * Quaternion.Euler(bend) : pose.Value;
                }
            root.transform.parent = vehicle;
            root.transform.rotation = vehicle.rotation;
            root.transform.position = headWorld;
            if (head != null) root.transform.position += headWorld - vehicle.forward * EyesAhead - vehicle.up * eyesAboveHead - head.position;
            if (bike && !passenger) HoldHandlebar(vehicle);
            FaceCamera();
        }

        void HoldHandlebar(Transform vehicle)
        {
            Transform[] ends;
            if (!grips.TryGetValue(vehicle, out ends))
            {
                var steering = Find(vehicle, "SteeringPivot");
                grips[vehicle] = ends = steering == null ? null : new[] { Find(steering, "Clutch"), Find(steering, "Throttle") };
            }
            if (ends == null) return;
            Reach("left", ends[0]);
            Reach("right", ends[1]);
        }

        void Reach(string side, Transform target)
        {
            if (target == null) return;
            var upper = body.Find(Arms + "collar_" + side + "/shoulder_" + side);
            var lower = upper != null ? upper.Find("arm_" + side) : null;
            var hand = lower != null ? lower.Find("hand_" + side) : null;
            if (hand == null) return;
            var a = (lower.position - upper.position).magnitude;
            var b = (hand.position - lower.position).magnitude;
            var c = Mathf.Clamp((target.position - upper.position).magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.01f);
            var toShoulder = upper.position - lower.position;
            var toHand = hand.position - lower.position;
            var axis = Vector3.Cross(toShoulder, toHand);
            if (axis.sqrMagnitude < 1e-6f) axis = root.transform.right;
            var current = Vector3.Angle(toShoulder, toHand);
            var wanted = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            lower.rotation = Quaternion.AngleAxis(wanted - current, axis.normalized) * lower.rotation;
            upper.rotation = Quaternion.FromToRotation(hand.position - upper.position, target.position - upper.position) * upper.rotation;
        }

        internal void Express(ushort flags, byte voiceId, byte count)
        {
            if (root == null || body == null) return;
            Attach();
            string right = null;
            if ((flags & Gestures.Punch) != 0) right = Punch;
            else if ((flags & Gestures.MiddleFinger) != 0) right = Finger;
            else if ((flags & Gestures.Wave) != 0) right = Wave;
            else if ((flags & Gestures.Drink) != 0) right = Drink;
            Drive(collarRight, right, idleRight, ref playingRight);
            Drive(collarLeft, (flags & Gestures.Smoke) != 0 ? Smoke : (flags & Gestures.Watch) != 0 ? LookWatch : null, idleLeft, ref playingLeft);

            var finger = (flags & Gestures.MiddleFinger) != 0 && fingerHand != null;
            if (fingerHand != null && finger != fingerHand.activeSelf) fingerHand.SetActive(finger);
            if (handRight != null) handRight.localScale = finger ? Vector3.zero : Vector3.one;

            Crouch((flags & Gestures.CrouchLow) != 0 ? 1f : (flags & Gestures.Crouch) != 0 ? 0.5f : 0f);

            var peeing = (flags & Gestures.Pee) != 0 && pelvis != null;
            pee.enabled = peeing;
            if (peeing)
            {
                var forward = root.transform.forward;
                var start = pelvis.position + forward * 0.16f - Vector3.up * 0.08f;
                var velocity = forward * 1.5f + Vector3.up * 0.5f;
                for (int i = 0; i < PeePoints; i++)
                {
                    var t = i * 0.045f;
                    pee.SetPosition(i, start + velocity * t + 0.5f * Physics.gravity * t * t);
                }
                if (!peeAudio.isPlaying && peeAudio.clip != null) peeAudio.Play();
            }
            else if (peeAudio.isPlaying) peeAudio.Stop();

            if (voiceCount >= 0 && count != voiceCount)
            {
                var clip = Gestures.VoiceClip(voiceId);
                if (clip != null) voice.PlayOneShot(clip);
            }
            voiceCount = count;
        }

        internal Transform Bone(string path) => body != null ? body.Find(path) : null;

        void Attach()
        {
            if (fingerHand != null)
            {
                fingerHand.transform.localPosition = FingerOffset;
                fingerHand.transform.localRotation = Quaternion.Euler(FingerRotation);
                fingerHand.transform.localScale = Vector3.one * FingerScale;
            }
            if (watch != null)
            {
                watch.localPosition = WatchOffset;
                watch.localRotation = Quaternion.Euler(WatchRotation);
                watch.localScale = Vector3.one * WatchScale;
            }
        }

        void Crouch(float amount)
        {
            if (seated || seatedPose == null) return;
            if (amount <= 0f)
            {
                if (!crouching) return;
                crouching = false;
                foreach (var i in legBones) { bones[i].Key.localPosition = rest[i].Key; bones[i].Key.localRotation = rest[i].Value; }
                if (legs != null) { legs.enabled = true; if (legs[Stand] != null) legs.CrossFade(Stand, 0.2f); }
                return;
            }
            crouching = true;
            if (legs != null) legs.enabled = false;
            foreach (var i in legBones)
            {
                KeyValuePair<Vector3, Quaternion> pose;
                if (!seatedPose.TryGetValue(bones[i].Value, out pose)) continue;
                var share = bones[i].Value.Contains("knee") ? Mathf.Min(1f, amount * 1.8f) : amount;
                bones[i].Key.localRotation = Quaternion.Slerp(rest[i].Value, pose.Value, share);
            }
            if (ankleLeft == null || ankleRight == null) return;
            var lowest = Mathf.Min(ankleLeft.position.y, ankleRight.position.y) - root.transform.position.y;
            root.transform.position += Vector3.up * (restAnkle - lowest);
        }

        void Drive(Animation anim, string clip, string idle, ref string current)
        {
            if (anim == null) return;
            var want = clip != null && anim[clip] != null ? clip : null;
            if (seated) anim.enabled = want != null;
            if (want == current) return;
            current = want;
            if (want != null)
            {
                anim[want].wrapMode = want == Wave || want == Drink || want == Smoke ? WrapMode.Loop : WrapMode.ClampForever;
                anim.CrossFade(want, 0.15f);
            }
            else if (!seated && idle != null) anim.CrossFade(idle, 0.3f);
        }

        void GetUp()
        {
            seated = false;
            for (int i = 0; i < bones.Count; i++)
            {
                bones[i].Key.localPosition = rest[i].Key;
                bones[i].Key.localRotation = rest[i].Value;
            }
            playingLeft = playingRight = null;
            foreach (var anim in animations)
            {
                anim.enabled = true;
                string idle;
                if (anim != legs && idles.TryGetValue(anim, out idle)) anim.Play(idle);
            }
            if (legs != null && legs[Stand] != null) legs.Play(Stand);
        }

        bool Ready()
        {
            if (root == null)
            {
                Build();
                SetSkin(wanted);
            }
            return body != null;
        }

        void FaceCamera()
        {
            var camera = Camera.main;
            if (camera != null)
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - camera.transform.position);
        }

        internal void Destroy()
        {
            if (root != null) Object.Destroy(root);
        }

        static bool LoadTemplate()
        {
            if (template != null) return true;
            var humans = FindRoot("HUMANS");
            var walker = humans != null ? humans.Find(WalkerPath) : null;
            if (walker == null)
            {
                ModConsole.Warning("[MSC Duo] pedestre HUMANS/" + WalkerPath + " não encontrado; avatar sem corpo.");
                return false;
            }
            template = walker.gameObject;

            var cars = FindRoot("NPC_CARS");
            var driver = cars != null ? cars.Find(DriverPath) : null;
            seatedPose = null;
            if (driver != null)
            {
                seatedPose = new Dictionary<string, KeyValuePair<Vector3, Quaternion>>();
                Record(driver, "");
            }
            else ModConsole.Warning("[MSC Duo] motorista NPC_CARS/" + DriverPath + " não encontrado; avatar sentado fica na pose em pé.");

            gestureClips.Clear();
            foreach (var source in GestureSources)
            {
                if (gestureClips.ContainsKey(source[2])) continue;
                var top = FindRoot(source[0]);
                var bone = top != null ? top.Find(source[1]) : null;
                var anim = bone != null ? bone.GetComponent<Animation>() : null;
                if (anim != null && anim[source[2]] != null) gestureClips[source[2]] = anim[source[2]].clip;
            }
            foreach (var source in GestureSources)
                if (!gestureClips.ContainsKey(source[2])) ModConsole.Warning("[MSC Duo] gesto " + source[2] + " não encontrado; o boneco não faz esse gesto.");
            var player = GameObject.Find("PLAYER");
            var sound = player != null ? player.transform.Find("PeeSound") : null;
            peeClip = sound != null ? sound.GetComponent<AudioSource>().clip : null;
            return true;
        }

        static void Record(Transform t, string path)
        {
            foreach (Transform child in t)
            {
                var childPath = path.Length == 0 ? child.name : path + "/" + child.name;
                seatedPose[childPath] = new KeyValuePair<Vector3, Quaternion>(child.localPosition, child.localRotation);
                Record(child, childPath);
            }
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static Transform FindRoot(string rootName)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.parent == null && t.hideFlags == HideFlags.None && t.name == rootName) return t;
            return null;
        }

        static Material FindMaterial(string materialName)
        {
            Material found;
            if (materials.TryGetValue(materialName, out found) && found != null) return found;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m.name == materialName) { materials[materialName] = m; return m; }
            ModConsole.Warning("[MSC Duo] material " + materialName + " não encontrado.");
            return null;
        }

        static float FeetBelowPivot()
        {
            if (feetBelowPivot >= 0f) return feetBelowPivot;
            var player = GameObject.Find("PLAYER");
            var controller = player != null ? player.GetComponent<CharacterController>() : null;
            if (controller == null || !controller.enabled) return DefaultFeetBelowPivot;
            feetBelowPivot = player.transform.position.y - controller.bounds.min.y + SkinWidth;
            return feetBelowPivot;
        }
    }
}
