/* Copyright (c) 2020-2022 Lyuma <xn.lyuma@gmail.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE. */
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

// [RequireComponent(typeof(Animator))]
public class LyumaAv3Runtime : MonoBehaviour
{
    static public Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController> animLayerToDefaultController = new Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController>();
    static public Dictionary<VRCAvatarDescriptor.AnimLayerType, AvatarMask> animLayerToDefaultAvaMask = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AvatarMask>();
    public delegate void UpdateSelectionFunc(GameObject obj);
    public static UpdateSelectionFunc updateSelectionDelegate;
    public delegate void AddRuntime(Component runtime);
    public static AddRuntime addRuntimeDelegate;
    public delegate void UpdateSceneLayersFunc(int layers);
    public static UpdateSceneLayersFunc updateSceneLayersDelegate;

    public LyumaAv3Runtime OriginalSourceClone = null;

    [Tooltip("Resets avatar state machine instantly")]
    public bool ResetAvatar;
    [Tooltip("Resets avatar state machine and waits until you uncheck this to start")]
    public bool ResetAndHold;
    [Tooltip("Simulates saving and reloading the avatar")]
    public bool KeepSavedParametersOnReset = true;
    [Tooltip("In VRChat, 8-bit float quantization only happens remotely. Check this to test your robustness to quantization locally, too. (example: 0.5 -> 0.503")]
    public bool locally8bitQuantizedFloats = false;
    public bool legacyMenuGUI;
    private bool lastLegacyMenuGUI;
    [Tooltip("Selects the playable layer to be visible in Unity's Animator window. Unless this is set to Base, creates duplicate playable layers with weight 0. It hopefully works the same.")] public VRCAvatarDescriptor.AnimLayerType AnimatorToDebug;
    private char PrevAnimatorToDebug;
    [HideInInspector] public string SourceObjectPath;
    [Header("Assign to non-local duplicate")]public LyumaAv3Runtime AvatarSyncSource;
    [Range(0.0f, 2.0f)] public float NonLocalSyncInterval = 0.1f;
    private float nextUpdateTime = 0.0f;
    private int CloneCount;
    public bool CreateNonLocalClone;
    public bool ViewMirrorReflection;
    public bool ViewBothRealAndMirror;
    private int mirState = -1;
    private LyumaAv3Runtime MirrorClone;
    private LyumaAv3Runtime ShadowClone;
    [HideInInspector] public VRCAvatarDescriptor avadesc;
    Avatar animatorAvatar;
    Animator animator;
    private RuntimeAnimatorController origAnimatorController;

    private Transform[] allTransforms;
    private Transform[] allMirrorTransforms;
    private Transform[] allShadowTransforms;
    private List<AnimatorControllerPlayable> playables = new List<AnimatorControllerPlayable>();
    private List<Dictionary<string, int>> playableParamterIds = new List<Dictionary<string, int>>();
    private List<Dictionary<int, float>> playableParamterFloats = new List<Dictionary<int, float>>();
    private List<Dictionary<int, int>> playableParamterInts = new List<Dictionary<int, int>>();
    private List<Dictionary<int, bool>> playableParamterBools = new List<Dictionary<int, bool>>();
    AnimationLayerMixerPlayable playableMixer;
    PlayableGraph playableGraph;
    VRCExpressionsMenu expressionsMenu;
    VRCExpressionParameters stageParameters;
    int sittingIndex, tposeIndex, ikposeIndex;
    int fxIndex, altFXIndex;
    int actionIndex, altActionIndex;
    int additiveIndex, altAdditiveIndex;
    int gestureIndex, altGestureIndex;

    private int mouthOpenBlendShapeIdx;
    private int[] visemeBlendShapeIdxs;

    public static float ClampFloatOnly(float val) {
        if (val < -1.0f) {
            val = -1.0f;
        }
        if (val > 1.0f) {
            val = 1.0f;
        }
        return val;
    }
    public static float ClampAndQuantizeFloat(float val) {
        val = ClampFloatOnly(val);
        val *= 127.00f;
        // if (val > 127.0f) {
        //     val = 127.0f;
        // }
        val = Mathf.Round(val);
        val = (((sbyte)val) / 127.0f);
        val = ClampFloatOnly(val);
        return val;
    }
    public static int ClampByte(int val) {
        if (val < 0) {
            val = 0;
        }
        if (val > 255) {
            val = 255;
        }
        return val;
    }

    public enum VisemeIndex {
        sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U
    }
    public enum GestureIndex {
        Neutral, Fist, HandOpen, Fingerpoint, Victory, RockNRoll, HandGun, ThumbsUp
    }
    public enum TrackingTypeIndex {
        Uninitialized, GenericRig, NoFingers, HeadHands, HeadHandsHip, HeadHandsHipFeet = 6
    }
    public static HashSet<string> BUILTIN_PARAMETERS = new HashSet<string> {
        "Viseme", "GestureLeft", "GestureLeftWeight", "GestureRight", "GestureRightWeight", "VelocityX", "VelocityY", "VelocityZ", "Upright", "AngularY", "Grounded", "Seated", "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation"
    };
    public static readonly HashSet<Type> MirrorCloneComponentBlacklist = new HashSet<Type> {
        typeof(Camera), typeof(FlareLayer), typeof(AudioSource), typeof(DynamicBone), typeof(DynamicBoneCollider), typeof(Rigidbody), typeof(Joint)
    };
    public static readonly HashSet<Type> ShadowCloneComponentBlacklist = new HashSet<Type> {
        typeof(Camera), typeof(FlareLayer), typeof(AudioSource), typeof(Light), typeof(ParticleSystemRenderer), typeof(DynamicBone), typeof(DynamicBoneCollider), typeof(Rigidbody), typeof(Joint)
    };
    [Header("Built-in inputs / Viseme")]
    public VisemeIndex Viseme;
    [Range(0, 15)] public int VisemeIdx;
    private int VisemeInt;
    [Header("Built-in inputs / Hand Gestures")]
    public GestureIndex GestureLeft;
    [Range(0, 9)] public int GestureLeftIdx;
    private char GestureLeftIdxInt;
    [Range(0, 1)] public float GestureLeftWeight;
    public GestureIndex GestureRight;
    [Range(0, 9)] public int GestureRightIdx;
    private char GestureRightIdxInt;
    [Range(0, 1)] public float GestureRightWeight;
    [Header("Built-in inputs / Locomotion")]
    public Vector3 Velocity;
    [Range(-400, 400)] public float AngularY;
    [Range(0, 1)] public float Upright;
    public bool Grounded;
    private bool PrevSeated, PrevTPoseCalibration, PrevIKPoseCalibration;
    public bool Seated;
    public bool AFK;
    public bool TPoseCalibration;
    public bool IKPoseCalibration;
    [Header("Built-in inputs / Tracking Setup and Other")]
    public TrackingTypeIndex TrackingType;
    [Range(0, 6)] public int TrackingTypeIdx;
    private char TrackingTypeIdxInt;
    public bool VRMode;
    public bool MuteSelf;
    public bool InStation;
    [HideInInspector] public int AvatarVersion = 3;
    private Component av3MenuComponent;

    [Header("Output State (Read-only)")]
    public bool IsLocal;
    public bool IsMirrorClone;
    public bool IsShadowClone;
    public bool LocomotionIsDisabled;
    private Vector3 HeadRelativeViewPosition;
    public Vector3 ViewPosition;
    float AvatarScaleFactor;
    public VRCAnimatorTrackingControl.TrackingType trackingHead;
    public VRCAnimatorTrackingControl.TrackingType trackingLeftHand;
    public VRCAnimatorTrackingControl.TrackingType trackingRightHand;
    public VRCAnimatorTrackingControl.TrackingType trackingHip;
    public VRCAnimatorTrackingControl.TrackingType trackingLeftFoot;
    public VRCAnimatorTrackingControl.TrackingType trackingRightFoot;
    public VRCAnimatorTrackingControl.TrackingType trackingLeftFingers;
    public VRCAnimatorTrackingControl.TrackingType trackingRightFingers;
    public VRCAnimatorTrackingControl.TrackingType trackingEyesAndEyelids;
    public VRCAnimatorTrackingControl.TrackingType trackingMouthAndJaw;

    [Serializable]
    public class FloatParam
    {
        [HideInInspector] public string stageName;
        public string name;
        [HideInInspector] public bool synced;
        [Range(-1, 1)] public float value;
        [HideInInspector] public float lastValue;
    }
    [Header("User-generated inputs")]
    public List<FloatParam> Floats = new List<FloatParam>();
    public Dictionary<string, int> FloatToIndex = new Dictionary<string, int>();

    [Serializable]
    public class IntParam
    {
        [HideInInspector] public string stageName;
        public string name;
        [HideInInspector] public bool synced;
        public int value;
        [HideInInspector] public int lastValue;
    }
    public List<IntParam> Ints = new List<IntParam>();
    public Dictionary<string, int> IntToIndex = new Dictionary<string, int>();

    [Serializable]
    public class BoolParam
    {
        [HideInInspector] public string stageName;

        public string name;
        [HideInInspector] public bool synced;
        public bool value;
        [HideInInspector] public bool lastValue;
        [HideInInspector] public bool[] hasTrigger;
        [HideInInspector] public bool[] hasBool;
    }
    public List<BoolParam> Bools = new List<BoolParam>();
    public Dictionary<string, int> BoolToIndex = new Dictionary<string, int>();

    public Dictionary<string, string> StageParamterToBuiltin = new Dictionary<string, string>();

    public LyumaAv3Emulator emulator;

    static public Dictionary<Animator, LyumaAv3Runtime> animatorToTopLevelRuntime = new Dictionary<Animator, LyumaAv3Runtime>();
    private HashSet<Animator> attachedAnimators;
    private HashSet<string> duplicateParameterAdds = new HashSet<string>();

    const float BASE_HEIGHT = 1.4f;

    public IEnumerator DelayedEnterPoseSpace(bool setView, float time) {
        yield return new WaitForSeconds(time);
        if (setView) {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) {
                ViewPosition = animator.transform.InverseTransformPoint(head.TransformPoint(HeadRelativeViewPosition));
            }
        } else {
            ViewPosition = avadesc.ViewPosition;
        }
    }

    class BlendingState {
        float startWeight;
        float goalWeight;
        float blendStartTime;
        float blendDuration;
        public bool blending;

        public float UpdateBlending() {
            if (blendDuration <= 0) {
                blending = false;
                return goalWeight;
            }
            float amt = (Time.time - blendStartTime) / blendDuration;
            if (amt >= 1) {
                blending = false;
                return goalWeight;
            }
            return Mathf.Lerp(startWeight, goalWeight, amt);
        }
        public void StartBlend(float startWeight, float goalWeight, float duration) {
            this.startWeight = startWeight;
            this.blendDuration = duration;
            this.blendStartTime = Time.time;
            this.goalWeight = goalWeight;
            this.blending = true;
        }
    }
    class PlayableBlendingState : BlendingState {
        public List<BlendingState> layerBlends = new List<BlendingState>();

    }
    List<PlayableBlendingState> playableBlendingStates = new List<PlayableBlendingState>();

    static bool getTopLevelRuntime(string component, Animator innerAnimator, out LyumaAv3Runtime runtime) {
        if (animatorToTopLevelRuntime.TryGetValue(innerAnimator, out runtime)) {
            return true;
        }
        Transform transform = innerAnimator.transform;
        while (transform != null && runtime == null) {
            runtime = transform.GetComponent<LyumaAv3Runtime>();
            transform = transform.parent;
        }
        if (runtime != null) {
            if (runtime.attachedAnimators != null) {
                Debug.Log("[" + component + "]: " + innerAnimator + " found parent runtime after it was Awoken! Adding to cache. Did you move me?");
                animatorToTopLevelRuntime.Add(innerAnimator, runtime);
                runtime.attachedAnimators.Add(innerAnimator);
            } else {
                Debug.Log("[" + component + "]: " + innerAnimator + " found parent runtime without being Awoken! Wakey Wakey...", runtime);
                runtime.Awake();
            }
            return true;
        }
        Debug.LogError("[" + component + "]: outermost Animator is not known: " + innerAnimator + ". If you changed something, consider resetting avatar", innerAnimator);

        return false;
    }

    static LyumaAv3Runtime() {
        VRCAvatarParameterDriver.Initialize += (x) => {
            x.ApplySettings += (behaviour, animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!getTopLevelRuntime("VRCAvatarParameterDriver", animator, out runtime)) {
                    return;
                }
                if (runtime.IsMirrorClone || runtime.IsShadowClone) {
                    return;
                }
                if (behaviour.debugString != null && behaviour.debugString.Length > 0)
                {
                    Debug.Log("[VRCAvatarParameterDriver:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
                }
                if (!runtime)
                {
                    return;
                }
                if (animator != runtime.animator && (!runtime.emulator || !runtime.emulator.legacySubAnimatorParameterDriverMode)) {
                    return;
                }
                if (!runtime.IsLocal && behaviour.localOnly) {
                    return;
                }
                HashSet<string> newParameterAdds = new HashSet<string>();
                HashSet<string> deleteParameterAdds = new HashSet<string>();
                foreach (var parameter in behaviour.parameters) {
                    if (runtime.AnimatorToDebug != VRCAvatarDescriptor.AnimLayerType.Base && !runtime.IsMirrorClone && !runtime.IsShadowClone && (parameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add || parameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random)) {
                        string dupeKey = parameter.value + ((parameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add) ? "add " : "rand ") + parameter.name;
                        if (!runtime.duplicateParameterAdds.Contains(dupeKey)) {
                            newParameterAdds.Add(dupeKey);
                            continue;
                        }
                        deleteParameterAdds.Add(dupeKey);
                    }
                    string actualName = parameter.name;
                    int idx;
                    if (runtime.IntToIndex.TryGetValue(actualName, out idx)) {
                        switch (parameter.type) {
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set:
                                runtime.Ints[idx].value = (int)parameter.value;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add:
                                runtime.Ints[idx].value += (int)parameter.value;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random:
                                runtime.Ints[idx].value = UnityEngine.Random.Range((int)parameter.valueMin, (int)parameter.valueMax + 1);
                                break;
                        }
                    }
                    if (runtime.FloatToIndex.TryGetValue(actualName, out idx)) {
                        switch (parameter.type) {
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set:
                                runtime.Floats[idx].value = parameter.value;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add:
                                runtime.Floats[idx].value += parameter.value;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random:
                                runtime.Floats[idx].value = UnityEngine.Random.Range(parameter.valueMin, parameter.valueMax);
                                break;
                        }
                    }
                    if (runtime.BoolToIndex.TryGetValue(actualName, out idx)) {
                        bool newValue;
                        BoolParam bp = runtime.Bools[idx];
                        int whichController;
                        // bp.value = parameter.value != 0;
                        switch (parameter.type) {
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set:
                                newValue = parameter.value != 0.0f;
                                if (!bp.synced) {
                                    // Triggers ignore alue and Set unconditionally.
                                    whichController = 0;
                                    foreach (var p in runtime.playables) {
                                        if (bp.hasBool[whichController]) {
                                            p.SetBool(actualName, newValue);
                                        }
                                        whichController++;
                                    }
                                    whichController = 0;
                                    foreach (var p in runtime.playables) {
                                        if (bp.hasTrigger[whichController]) {
                                            p.SetTrigger(actualName);
                                        }
                                        whichController++;
                                    }
                                    bp.lastValue = newValue;
                                }
                                bp.value = newValue;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add:
                                /* editor script treats it as random, but it is its own operation */
                                newValue = ((bp.value ? 1.0 : 0.0) + parameter.value) != 0.0f; // weird but ok...
                                Debug.Log("Add bool " + bp.name + " to " + newValue + ", " + (bp.value ? 1.0 : 0.0) + ", " + parameter.value);
                                if (!bp.synced) {
                                    newValue = parameter.value != 0.0f;
                                    whichController = 0;
                                    // Triggers ignore value and Set unconditionally.
                                    foreach (var p in runtime.playables) {
                                        if (bp.hasBool[whichController]) {
                                            p.SetBool(actualName, newValue);
                                        }
                                        whichController++;
                                    }
                                    whichController = 0;
                                    foreach (var p in runtime.playables) {
                                        if (bp.hasTrigger[whichController]) {
                                            p.SetTrigger(actualName);
                                        }
                                        whichController++;
                                    }
                                    bp.lastValue = newValue;
                                }
                                bp.value = newValue;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random:
                                // random is *not* idempotent.
                                newValue = UnityEngine.Random.Range(0.0f, 1.0f) < parameter.chance;
                                if (!bp.synced) {
                                    whichController = 0;
                                    foreach (var p in runtime.playables) {
                                        if (bp.hasBool[whichController]) {
                                            p.SetBool(actualName, newValue);
                                        }
                                        whichController++;
                                    }
                                    if (newValue) {
                                        whichController = 0;
                                        foreach (var p in runtime.playables) {
                                            if (bp.hasTrigger[whichController]) {
                                                p.SetTrigger(actualName);
                                            }
                                            whichController++;
                                        }
                                    }
                                    bp.lastValue = newValue;
                                }
                                bp.value = newValue;
                                break;
                        }
                    }
                }
                foreach (var key in deleteParameterAdds) {
                    runtime.duplicateParameterAdds.Remove(key);
                }
                foreach (var key in newParameterAdds) {
                    runtime.duplicateParameterAdds.Add(key);
                }
            };
        };
        VRCPlayableLayerControl.Initialize += (x) => {
            x.ApplySettings += (behaviour, animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!getTopLevelRuntime("VRCPlayableLayerControl", animator, out runtime)) {
                    return;
                }
                if (runtime.IsMirrorClone && runtime.IsShadowClone) {
                    return;
                }
                if (behaviour.debugString != null && behaviour.debugString.Length > 0)
                {
                    Debug.Log("[VRCPlayableLayerControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
                }
                if (!runtime)
                {
                    return;
                }
                int idx = -1;
                switch (behaviour.layer)
                {
                    case VRCPlayableLayerControl.BlendableLayer.Action:
                        idx = runtime.actionIndex;
                        break;
                    case VRCPlayableLayerControl.BlendableLayer.Additive:
                        idx = runtime.additiveIndex;
                        break;
                    case VRCPlayableLayerControl.BlendableLayer.FX:
                        idx = runtime.fxIndex;
                        break;
                    case VRCPlayableLayerControl.BlendableLayer.Gesture:
                        idx = runtime.gestureIndex;
                        break;
                }
                if (idx >= 0 && idx < runtime.playableBlendingStates.Count)
                {
                    runtime.playableBlendingStates[idx].StartBlend(runtime.playableMixer.GetInputWeight(idx + 1), behaviour.goalWeight, behaviour.blendDuration);
                    // Debug.Log("Start blend of whole playable " + idx + " from " + runtime.playableMixer.GetInputWeight(idx + 1) + " to " + behaviour.goalWeight);
                }
            };
        };
        VRCAnimatorLayerControl.Initialize += (x) => {
            x.ApplySettings += (behaviour, animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!getTopLevelRuntime("VRCAnimatorLayerControl", animator, out runtime)) {
                    return;
                }
                if (runtime.IsMirrorClone) {
                    return;
                }
                if (behaviour.debugString != null && behaviour.debugString.Length > 0)
                {
                    Debug.Log("[VRCAnimatorLayerControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
                }
                if (!runtime)
                {
                    return;
                }
                int idx = -1, altidx = -1;
                switch (behaviour.playable)
                {
                    case VRCAnimatorLayerControl.BlendableLayer.Action:
                        idx = runtime.actionIndex;
                        altidx = runtime.altActionIndex;
                        break;
                    case VRCAnimatorLayerControl.BlendableLayer.Additive:
                        idx = runtime.additiveIndex;
                        altidx = runtime.altAdditiveIndex;
                        break;
                    case VRCAnimatorLayerControl.BlendableLayer.FX:
                        idx = runtime.fxIndex;
                        altidx = runtime.altFXIndex;
                        break;
                    case VRCAnimatorLayerControl.BlendableLayer.Gesture:
                        idx = runtime.gestureIndex;
                        altidx = runtime.altGestureIndex;
                        break;
                }
                if (idx >= 0 && idx < runtime.playableBlendingStates.Count)
                {
                    if (behaviour.layer >= 0 && behaviour.layer < runtime.playableBlendingStates[idx].layerBlends.Count)
                    {
                        runtime.playableBlendingStates[idx].layerBlends[behaviour.layer].StartBlend(runtime.playables[idx].GetLayerWeight(behaviour.layer), behaviour.goalWeight, behaviour.blendDuration);
                        // Debug.Log("Start blend of playable " + idx + " layer " + behaviour.layer + " from " + runtime.playables[idx].GetLayerWeight(behaviour.layer) + " to " + behaviour.goalWeight);
                        if (altidx >= 0) {
                            runtime.playableBlendingStates[altidx].layerBlends[behaviour.layer].StartBlend(runtime.playables[altidx].GetLayerWeight(behaviour.layer), behaviour.goalWeight, behaviour.blendDuration);
                            // Debug.Log("Start blend of alt playable " + altidx + " layer " + behaviour.layer + " from " + runtime.playables[altidx].GetLayerWeight(behaviour.layer) + " to " + behaviour.goalWeight);
                        }
                    }
                }
            };
        };
        VRCAnimatorLocomotionControl.Initialize += (x) => {
            x.ApplySettings += (behaviour, animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!getTopLevelRuntime("VRCAnimatorLocomotionControl", animator, out runtime)) {
                    return;
                }
                if (runtime.IsMirrorClone && runtime.IsShadowClone) {
                    return;
                }
                if (behaviour.debugString != null && behaviour.debugString.Length > 0)
                {
                    Debug.Log("[VRCAnimatorLocomotionControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
                }
                if (!runtime)
                {
                    return;
                }
                // I legit don't know
                runtime.LocomotionIsDisabled = behaviour.disableLocomotion;
            };
        };
        VRCAnimatorTemporaryPoseSpace.Initialize += (x) => {
            x.ApplySettings += (behaviour, animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!getTopLevelRuntime("VRCAnimatorSetView", animator, out runtime)) {
                    return;
                }
                if (runtime.IsMirrorClone && runtime.IsShadowClone) {
                    return;
                }
                if (behaviour.debugString != null && behaviour.debugString.Length > 0)
                {
                    Debug.Log("[VRCAnimatorSetView:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
                }
                if (!runtime)
                {
                    return;
                }
                // fixedDelay: Is the delay fixed or normalized...
                // The layerIndex is not passed into the delegate, so we cannot reimplement fixedDelay.
                runtime.StartCoroutine(runtime.DelayedEnterPoseSpace(behaviour.enterPoseSpace, behaviour.delayTime));
            };
        };
        VRCAnimatorTrackingControl.Initialize += (x) => {
            x.ApplySettings += (behaviour, animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!getTopLevelRuntime("VRCAnimatorTrackingControl", animator, out runtime)) {
                    return;
                }
                if (runtime.IsMirrorClone && runtime.IsShadowClone) {
                    return;
                }
                if (behaviour.debugString != null && behaviour.debugString.Length > 0)
                {
                    Debug.Log("[VRCAnimatorTrackingControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
                }
                if (!runtime)
                {
                    return;
                }

                if (behaviour.trackingMouth != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingMouthAndJaw = behaviour.trackingMouth;
                }
                if (behaviour.trackingHead != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingHead = behaviour.trackingHead;
                }
                if (behaviour.trackingRightFingers != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingRightFingers = behaviour.trackingRightFingers;
                }
                if (behaviour.trackingEyes != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingEyesAndEyelids = behaviour.trackingEyes;
                }
                if (behaviour.trackingLeftFingers != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingLeftFingers = behaviour.trackingLeftFingers;
                }
                if (behaviour.trackingLeftFoot != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingLeftFoot = behaviour.trackingLeftFoot;
                }
                if (behaviour.trackingHip != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingHip = behaviour.trackingHip;
                }
                if (behaviour.trackingRightHand != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingRightHand = behaviour.trackingRightHand;
                }
                if (behaviour.trackingLeftHand != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingLeftHand = behaviour.trackingLeftHand;
                }
                if (behaviour.trackingRightFoot != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingRightFoot = behaviour.trackingRightFoot;
                }
            };
        };
    }

    void OnDestroy () {
        if (this.playableGraph.IsValid()) {
            this.playableGraph.Destroy();
        }
        foreach (var anim in attachedAnimators) {
            LyumaAv3Runtime runtime;
            if (animatorToTopLevelRuntime.TryGetValue(anim, out runtime) && runtime == this)
            {
                animatorToTopLevelRuntime.Remove(anim);
            }
        }
        if (animator.playableGraph.IsValid())
        {
            animator.playableGraph.Destroy();
        }
        animator.runtimeAnimatorController = origAnimatorController;
    }

    void Awake()
    {
        if (AvatarSyncSource != null && OriginalSourceClone == null) {
            Debug.Log("Awake returning early for " + gameObject.name, this);
            return;
        }
        if (attachedAnimators != null) {
            Debug.Log("Deduplicating Awake() call if we already got awoken by our children.", this);
            return;
        }
        Debug.Log("AWOKEN " + gameObject.name, this);
        attachedAnimators = new HashSet<Animator>();
        if (AvatarSyncSource == null) {
            Transform transform = this.transform;
            SourceObjectPath = "";
            while (transform != null) {
                SourceObjectPath = "/" + transform.name + SourceObjectPath;
                transform = transform.parent;
            }
            AvatarSyncSource = this;
        } else {
            AvatarSyncSource = GameObject.Find(SourceObjectPath).GetComponent<LyumaAv3Runtime>();
        }

        AnimatorToDebug = VRCAvatarDescriptor.AnimLayerType.Base;

        if (LyumaAv3Emulator.emulatorInstance == null) {
            Debug.LogError("LyumaAv3Runtime awoken without an LyumaAv3Emulator instance!", this);
        } else {
            this.VRMode = LyumaAv3Emulator.emulatorInstance.DefaultToVR;
            this.TrackingType = LyumaAv3Emulator.emulatorInstance.DefaultTrackingType;
            this.InStation = LyumaAv3Emulator.emulatorInstance.DefaultTestInStation;
            this.AnimatorToDebug = LyumaAv3Emulator.emulatorInstance.DefaultAnimatorToDebug;
        }

        animator = this.gameObject.GetOrAddComponent<Animator>();
        animatorAvatar = animator.avatar;
        // Default values.
        Grounded = true;
        Upright = 1.0f;
        if (!animator.isHuman) {
            TrackingType = TrackingTypeIndex.GenericRig;
        } else if (!VRMode) {
            TrackingType = TrackingTypeIndex.HeadHands;
        }
        avadesc = this.gameObject.GetComponent<VRCAvatarDescriptor>();
        if (avadesc.VisemeSkinnedMesh == null) {
            mouthOpenBlendShapeIdx = -1;
            visemeBlendShapeIdxs = new int[0];
        } else {
            mouthOpenBlendShapeIdx = avadesc.VisemeSkinnedMesh.sharedMesh.GetBlendShapeIndex(avadesc.MouthOpenBlendShapeName);
            visemeBlendShapeIdxs = new int[avadesc.VisemeBlendShapes == null ? 0 : avadesc.VisemeBlendShapes.Length];
            if (avadesc.VisemeBlendShapes != null) {
                for (int i = 0; i < avadesc.VisemeBlendShapes.Length; i++) {
                    visemeBlendShapeIdxs[i] = avadesc.VisemeSkinnedMesh.sharedMesh.GetBlendShapeIndex(avadesc.VisemeBlendShapes[i]);
                }
            }
        }
        if (OriginalSourceClone == null) {
            OriginalSourceClone = this;
            GameObject cloned = GameObject.Instantiate(gameObject);
            cloned.hideFlags = HideFlags.HideAndDontSave;
            cloned.SetActive(false);
            OriginalSourceClone = cloned.GetComponent<LyumaAv3Runtime>();
            Debug.Log("ORIGINAL SOURCE CLONE IS " + OriginalSourceClone, this);
            OriginalSourceClone.OriginalSourceClone = OriginalSourceClone;
            if (LyumaAv3Emulator.emulatorInstance != null && !LyumaAv3Emulator.emulatorInstance.DisableMirrorClone) {
                OriginalSourceClone.IsMirrorClone = true;
                MirrorClone = GameObject.Instantiate(cloned).GetComponent<LyumaAv3Runtime>();
                MirrorClone.GetComponent<Animator>().avatar = null;
                OriginalSourceClone.IsMirrorClone = false;
                GameObject o;
                (o = MirrorClone.gameObject).SetActive(true);
                o.name = gameObject.name + " (MirrorReflection)";
                allMirrorTransforms = MirrorClone.gameObject.GetComponentsInChildren<Transform>(true);
                foreach (Component component in MirrorClone.gameObject.GetComponentsInChildren<Component>(true)) {
                    if (MirrorCloneComponentBlacklist.Contains(component.GetType())) {
                        UnityEngine.Object.Destroy(component);
                    }
                }
            }
            if (LyumaAv3Emulator.emulatorInstance != null && !LyumaAv3Emulator.emulatorInstance.DisableShadowClone) {
                OriginalSourceClone.IsShadowClone = true;
                ShadowClone = GameObject.Instantiate(cloned).GetComponent<LyumaAv3Runtime>();
                ShadowClone.GetComponent<Animator>().avatar = null;
                OriginalSourceClone.IsShadowClone = false;
                GameObject o;
                (o = ShadowClone.gameObject).SetActive(true);
                o.name = gameObject.name + " (ShadowClone)";
                allShadowTransforms = ShadowClone.gameObject.GetComponentsInChildren<Transform>(true);
                foreach (Component component in ShadowClone.gameObject.GetComponentsInChildren<Component>(true)) {
                    if (ShadowCloneComponentBlacklist.Contains(component.GetType())) {
                        UnityEngine.Object.Destroy(component);
                        continue;
                    }
                    if (component.GetType() == typeof(SkinnedMeshRenderer) || component.GetType() == typeof(MeshRenderer)) {
                        Renderer renderer = component as Renderer;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; // ShadowCastingMode.TwoSided isn't accounted for and does not work locally
                    }
                }
            }
        }
        foreach (var smr in gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            smr.updateWhenOffscreen = (AvatarSyncSource == this || IsMirrorClone || IsShadowClone);
        }
        if(!LyumaAv3Emulator.emulatorInstance.DisableShadowClone && !IsShadowClone && !IsMirrorClone) {
            foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true)) {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // ShadowCastingMode.TwoSided isn't accounted for and does not work locally
            }
        }
        int desiredLayer = 9;
        if (AvatarSyncSource == this) {
            desiredLayer = 10;
        }
        if (IsMirrorClone) {
            desiredLayer = 18;
        }
        if (IsShadowClone) {
            desiredLayer = 9; // the Shadowclone is always on playerLocal and never on UI Menu
        }
        if (gameObject.layer != 12 || desiredLayer == 18) {
            gameObject.layer = desiredLayer;
        }
        allTransforms = gameObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in allTransforms) {
            if (t.gameObject.layer != 12 || desiredLayer == 18) {
                t.gameObject.layer = desiredLayer;
            }
        }
        InitializeAnimator();
        if (addRuntimeDelegate != null) {
            addRuntimeDelegate(this);
        }
        if (AvatarSyncSource == this) {
            CreateAv3MenuComponent();
        }
    }

    private void InitializeAnimator()
    {
        ResetAvatar = false;
        PrevAnimatorToDebug = (char)(int)AnimatorToDebug;

        animator = this.gameObject.GetOrAddComponent<Animator>();
        animator.avatar = animatorAvatar;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = (this == AvatarSyncSource || IsMirrorClone || IsShadowClone) ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullCompletely;
        animator.runtimeAnimatorController = null;

        avadesc = this.gameObject.GetComponent<VRCAvatarDescriptor>();
        ViewPosition = avadesc.ViewPosition;
        AvatarScaleFactor = ViewPosition.magnitude / BASE_HEIGHT; // mostly guessing...
        HeadRelativeViewPosition = ViewPosition;
        if (animator.avatar != null)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) {
                HeadRelativeViewPosition = head.InverseTransformPoint(animator.transform.TransformPoint(ViewPosition));
            }
        }
        expressionsMenu = avadesc.expressionsMenu;
        if (expressionsMenu != null)
        {
            stageParameters = avadesc.expressionParameters;
        }
        if (origAnimatorController != null) {
            origAnimatorController = animator.runtimeAnimatorController;
        }

        VRCAvatarDescriptor.CustomAnimLayer[] baselayers = avadesc.baseAnimationLayers;
        VRCAvatarDescriptor.CustomAnimLayer[] speciallayers = avadesc.specialAnimationLayers;
        List<VRCAvatarDescriptor.CustomAnimLayer> allLayers = new List<VRCAvatarDescriptor.CustomAnimLayer>();
        // foreach (VRCAvatarDescriptor.CustomAnimLayer cal in baselayers) {
        //     if (AnimatorToDebug == cal.type) {
        //         allLayers.Add(cal);
        //     }
        // }
        // foreach (VRCAvatarDescriptor.CustomAnimLayer cal in speciallayers) {
        //     if (AnimatorToDebug == cal.type) {
        //         allLayers.Add(cal);
        //     }
        // }
        int i = 0;
        if (AnimatorToDebug != VRCAvatarDescriptor.AnimLayerType.Base && !IsMirrorClone && !IsShadowClone) {
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in baselayers) {
                if (AnimatorToDebug == cal.type) {
                    i++;
                    allLayers.Add(cal);
                    break;
                }
            }
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in speciallayers) {
                if (AnimatorToDebug == cal.type) {
                    i++;
                    allLayers.Add(cal);
                    break;
                }
            }
            // WE ADD ALL THE LAYERS A SECOND TIME BECAUSE!
            // Add and Random Parameter drivers are not idepotent.
            // To solve this, we ignore every other invocation.
            // Therefore, we must add all layers twice, not just the one we are debugging...???
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in baselayers) {
                if (AnimatorToDebug != cal.type) {
                    i++;
                    allLayers.Add(cal);
                }
            }
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in speciallayers) {
                if (AnimatorToDebug != cal.type) {
                    i++;
                    allLayers.Add(cal);
                }
            }
        }
        int dupeOffset = i;
        if (!IsMirrorClone && !IsShadowClone) {
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in baselayers) {
                if (cal.type == VRCAvatarDescriptor.AnimLayerType.Base || cal.type == VRCAvatarDescriptor.AnimLayerType.Additive) {
                    i++;
                    allLayers.Add(cal);
                }
            }
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in speciallayers) {
                i++;
                allLayers.Add(cal);
            }
        }
        foreach (VRCAvatarDescriptor.CustomAnimLayer cal in baselayers) {
            if (IsMirrorClone || IsShadowClone) {
                if (cal.type == VRCAvatarDescriptor.AnimLayerType.FX) {
                    i++;
                    allLayers.Add(cal);
                }
            } else if (!(cal.type == VRCAvatarDescriptor.AnimLayerType.Base || cal.type == VRCAvatarDescriptor.AnimLayerType.Additive)) {
                i++;
                allLayers.Add(cal);
            }
        }

        if (playableGraph.IsValid()) {
            playableGraph.Destroy();
        }
        playables.Clear();
        playableBlendingStates.Clear();

        for (i = 0; i < allLayers.Count; i++) {
            playables.Add(new AnimatorControllerPlayable());
            playableBlendingStates.Add(null);
        }

        actionIndex = fxIndex = gestureIndex = additiveIndex = sittingIndex = ikposeIndex = tposeIndex = -1;
        altActionIndex = altFXIndex = altGestureIndex = altAdditiveIndex = -1;

        foreach (var anim in attachedAnimators) {
            LyumaAv3Runtime runtime;
            if (animatorToTopLevelRuntime.TryGetValue(anim, out runtime) && runtime == this)
            {
                animatorToTopLevelRuntime.Remove(anim);
            }
        }
        attachedAnimators.Clear();
        Animator[] animators = this.gameObject.GetComponentsInChildren<Animator>(true);
        Debug.Log("anim len "+animators.Length);
        foreach (Animator anim in animators)
        {
            attachedAnimators.Add(anim);
            animatorToTopLevelRuntime.Add(anim, this);
        }

        Dictionary<string, float> stageNameToValue = new Dictionary<string, float>();
        if (IsLocal) {
            foreach (var val in Ints) {
                stageNameToValue[val.stageName] = val.value;
            }
            foreach (var val in Floats) {
                stageNameToValue[val.stageName] = val.value;
            }
            foreach (var val in Bools) {
                stageNameToValue[val.stageName] = val.value ? 1.0f : 0.0f;
            }
        }
        Ints.Clear();
        Bools.Clear();
        Floats.Clear();
        StageParamterToBuiltin.Clear();
        IntToIndex.Clear();
        FloatToIndex.Clear();
        BoolToIndex.Clear();
        playableParamterFloats.Clear();
        playableParamterIds.Clear();
        playableParamterInts.Clear();
        playableParamterBools.Clear();
        HashSet<string> usedparams = new HashSet<string>(BUILTIN_PARAMETERS);
        i = 0;
        if (stageParameters != null)
        {
            int stageId = 0;
            foreach (var stageParam in stageParameters.parameters)
            {
                stageId++; // one-indexed
                if (stageParam.name == null || stageParam.name.Length == 0) {
                    continue;
                }
                string stageName = stageParam.name + (stageParam.saved ? " (saved/SYNCED)" : " (SYNCED)"); //"Stage" + stageId;
                float lastDefault = 0.0f;
                if (AvatarSyncSource == this) {
                    lastDefault = (stageParam.saved && KeepSavedParametersOnReset && stageNameToValue.ContainsKey(stageName) ? stageNameToValue[stageName] : stageParam.defaultValue);
                }
                StageParamterToBuiltin.Add(stageName, stageParam.name);
                if ((int)stageParam.valueType == 0)
                {
                    IntParam param = new IntParam();
                    param.stageName = stageName;
                    param.synced = true;
                    param.name = stageParam.name;
                    param.value = (int)lastDefault;
                    param.lastValue = 0;
                    IntToIndex[param.name] = Ints.Count;
                    Ints.Add(param);
                }
                else if ((int)stageParam.valueType == 1)
                {
                    FloatParam param = new FloatParam();
                    param.stageName = stageName;
                    param.synced = true;
                    param.name = stageParam.name;
                    param.value = lastDefault;
                    param.lastValue = 0;
                    FloatToIndex[param.name] = Floats.Count;
                    Floats.Add(param);
                }
                else if ((int)stageParam.valueType == 2)
                {
                    BoolParam param = new BoolParam();
                    param.stageName = stageName;
                    param.synced = true;
                    param.name = stageParam.name;
                    param.value = lastDefault != 0.0;
                    param.lastValue = false;
                    param.hasBool = new bool[playables.Count];
                    param.hasTrigger = new bool[playables.Count];
                    BoolToIndex[param.name] = Bools.Count;
                    Bools.Add(param);
                }
                usedparams.Add(stageParam.name);
                i++;
            }
        }

        if (animator.playableGraph.IsValid())
        {
            animator.playableGraph.Destroy();
        }
        // var director = avadesc.gameObject.GetComponent<PlayableDirector>();
        playableGraph = PlayableGraph.Create("LyumaAvatarRuntime - " + this.gameObject.name);
        var externalOutput = AnimationPlayableOutput.Create(playableGraph, "ExternalAnimator", animator);
        playableMixer = AnimationLayerMixerPlayable.Create(playableGraph, allLayers.Count + 1);
        externalOutput.SetSourcePlayable(playableMixer);
        animator.applyRootMotion = false;

        i = 0;
        // playableMixer.ConnectInput(0, AnimatorControllerPlayable.Create(playableGraph, allLayers[layerToDebug - 1].animatorController), 0, 0);
        foreach (VRCAvatarDescriptor.CustomAnimLayer vrcAnimLayer in allLayers)
        {
            i++; // Ignore zeroth layer.
            bool additive = (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Additive);
            RuntimeAnimatorController ac = null;
            AvatarMask mask;
            if (vrcAnimLayer.isDefault) {
                ac = animLayerToDefaultController[vrcAnimLayer.type];
                mask = animLayerToDefaultAvaMask[vrcAnimLayer.type];
            } else
            {
                ac = vrcAnimLayer.animatorController;
                mask = vrcAnimLayer.mask;
                if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.FX) {
                    mask = animLayerToDefaultAvaMask[vrcAnimLayer.type]; // Force mask to prevent muscle overrides.
                }
            }
            if (ac == null) {
                // i is not incremented.
                continue;
            }
            AnimatorControllerPlayable humanAnimatorPlayable = AnimatorControllerPlayable.Create(playableGraph, ac);
            PlayableBlendingState pbs = new PlayableBlendingState();
            for (int j = 0; j < humanAnimatorPlayable.GetLayerCount(); j++)
            {
                pbs.layerBlends.Add(new BlendingState());
            }

            // If we are debugging a particular layer, we must put that first.
            // The Animator Controller window only shows the first layer.
            int effectiveIdx = i;

            playableMixer.ConnectInput((int)effectiveIdx, humanAnimatorPlayable, 0, 1);
            playables[effectiveIdx - 1] = humanAnimatorPlayable;
            playableBlendingStates[effectiveIdx - 1] = pbs;
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Sitting) {
                if (i >= dupeOffset) {
                    sittingIndex = effectiveIdx - 1;
                }
                playableMixer.SetInputWeight(effectiveIdx, 0f);
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.IKPose)
            {
                if (i >= dupeOffset) {
                    ikposeIndex = effectiveIdx - 1;
                }
                playableMixer.SetInputWeight(effectiveIdx, 0f);
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.TPose)
            {
                if (i >= dupeOffset) {
                    tposeIndex = effectiveIdx - 1;
                }
                playableMixer.SetInputWeight(effectiveIdx, 0f);
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Action)
            {
                playableMixer.SetInputWeight(i, 0f);
                if (i < dupeOffset) {
                    altActionIndex = effectiveIdx - 1;
                } else {
                    actionIndex = effectiveIdx - 1;
                }

            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Gesture) {
                if (i < dupeOffset) {
                    altGestureIndex = effectiveIdx - 1;
                } else {
                    gestureIndex = effectiveIdx - 1;
                }

            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Additive)
            {
                if (i < dupeOffset) {
                    altAdditiveIndex = effectiveIdx - 1;
                } else {
                    additiveIndex = effectiveIdx - 1;
                }

            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.FX)
            {
                if (i < dupeOffset) {
                    altFXIndex = effectiveIdx - 1;
                } else {
                    fxIndex = effectiveIdx - 1;
                }
            }
            // AnimationControllerLayer acLayer = new AnimationControllerLayer()
            if (mask != null)
            {
                playableMixer.SetLayerMaskFromAvatarMask((uint)effectiveIdx, mask);
            }
            if (additive)
            {
                playableMixer.SetLayerAdditive((uint)effectiveIdx, true);
            }

            if (i < dupeOffset) {//i == 0 && AnimatorToDebug != VRCAvatarDescriptor.AnimLayerType.Base) {
                playableMixer.SetInputWeight(i, 0f);
            }
        }

        //playableParamterIds
        int whichcontroller = 0;
        playableParamterIds.Clear();
        foreach (AnimatorControllerPlayable playable in playables) {
            Dictionary<string, int> parameterIndices = new Dictionary<string, int>();
            playableParamterInts.Add(new Dictionary<int, int>());
            playableParamterFloats.Add(new Dictionary<int, float>());
            playableParamterBools.Add(new Dictionary<int, bool>());
            // Debug.Log("SETUP index " + whichcontroller + " len " + playables.Count);
            playableParamterIds.Add(parameterIndices);
            int pcnt = playable.IsValid() ? playable.GetParameterCount() : 0;
            for (i = 0; i < pcnt; i++) {
                AnimatorControllerParameter aparam = playable.GetParameter(i);
                string actualName;
                if (!StageParamterToBuiltin.TryGetValue(aparam.name, out actualName)) {
                    actualName = aparam.name;
                }
                parameterIndices[actualName] = aparam.nameHash;
                if (usedparams.Contains(actualName)) {
                    if (BoolToIndex.ContainsKey(aparam.name) && aparam.type == AnimatorControllerParameterType.Bool) {
                        Bools[BoolToIndex[aparam.name]].hasBool[whichcontroller] = true;
                    }
                    if (BoolToIndex.ContainsKey(aparam.name) && aparam.type == AnimatorControllerParameterType.Trigger) {
                        Bools[BoolToIndex[aparam.name]].hasTrigger[whichcontroller] = true;
                    }
                    continue;
                }
                if (aparam.type == AnimatorControllerParameterType.Int) {
                    IntParam param = new IntParam();
                    param.stageName = aparam.name + " (local)";
                    param.synced = false;
                    param.name = aparam.name;
                    param.value = aparam.defaultInt;
                    param.lastValue = param.value;
                    IntToIndex[param.name] = Ints.Count;
                    Ints.Add(param);
                    usedparams.Add(aparam.name);
                } else if (aparam.type == AnimatorControllerParameterType.Float) {
                    FloatParam param = new FloatParam();
                    param.stageName = aparam.name + " (local)";
                    param.synced = false;
                    param.name = aparam.name;
                    param.value = aparam.defaultFloat;
                    param.lastValue = param.value;
                    FloatToIndex[param.name] = Floats.Count;
                    Floats.Add(param);
                    usedparams.Add(aparam.name);
                } else if (aparam.type == AnimatorControllerParameterType.Trigger || aparam.type == AnimatorControllerParameterType.Bool) {
                    BoolParam param = new BoolParam();
                    param.stageName = aparam.name + " (local)";
                    param.synced = false;
                    param.name = aparam.name;
                    param.value = aparam.defaultBool;
                    param.lastValue = param.value;
                    param.hasBool = new bool[playables.Count];
                    param.hasTrigger = new bool[playables.Count];
                    param.hasBool[whichcontroller] = aparam.type == AnimatorControllerParameterType.Bool;
                    param.hasTrigger[whichcontroller] = aparam.type == AnimatorControllerParameterType.Trigger;
                    BoolToIndex[param.name] = Bools.Count;
                    Bools.Add(param);
                    usedparams.Add(aparam.name);
                }
            }
            whichcontroller++;
        }

        // Plays the Graph.
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        Debug.Log(this.gameObject.name + " : Awoken and ready to Play.");
        playableGraph.Play();
        Debug.Log(this.gameObject.name + " : Playing.");
    }


    void CreateAv3MenuComponent() {
        System.Type gestureManagerMenu = System.Type.GetType("GestureManagerAv3Menu");
        if (gestureManagerMenu != null) {
            foreach (var comp in avadesc.gameObject.GetComponents(gestureManagerMenu)) {
                UnityEngine.Object.Destroy(comp);
            }
        }
        foreach (var comp in avadesc.gameObject.GetComponents<LyumaAv3Menu>()) {
            UnityEngine.Object.Destroy(comp);
        }
        if (gestureManagerMenu != null && !legacyMenuGUI) {
            var mainMenu = avadesc.gameObject.AddComponent(gestureManagerMenu);
            var runtimeField = gestureManagerMenu.GetField("Runtime");
            var rootMenuField = gestureManagerMenu.GetField("RootMenu");
            runtimeField.SetValue(mainMenu, this);
            rootMenuField.SetValue(mainMenu, this.avadesc.expressionsMenu);
        } else {
            var mainMenu = avadesc.gameObject.AddComponent<LyumaAv3Menu>();
            mainMenu.Runtime = this;
            mainMenu.RootMenu = avadesc.expressionsMenu;
        }
    }


    private bool isResetting;
    private bool isResettingHold;
    private bool isResettingSel;
    void LateUpdate()
    {
        if (MirrorClone != null) {
            MirrorClone.gameObject.SetActive(true);
            MirrorClone.transform.localRotation = transform.localRotation;
            MirrorClone.transform.localScale = transform.localScale;
            if (mirState != 2 && ViewBothRealAndMirror) {
                mirState = 2;
                MirrorClone.transform.position = transform.position + new Vector3(0.0f, -1.3f * avadesc.ViewPosition.y, 0.0f);
                updateSceneLayersDelegate(-1);
            } else if (mirState != 1 && ViewMirrorReflection && !ViewBothRealAndMirror) {
                mirState = 1;
                updateSceneLayersDelegate(~(1<<10));
                MirrorClone.transform.position = transform.position;
            } else if (mirState != 0 && !ViewMirrorReflection && !ViewBothRealAndMirror) {
                mirState = 0;
                updateSceneLayersDelegate(~(1<<18));
                MirrorClone.transform.position = transform.position;
            }
            for(int i = 0; i < allTransforms.Length; i++) {
                if (allTransforms[i] == this.transform) {
                    continue;
                }
                allMirrorTransforms[i].localPosition = allTransforms[i].localPosition;
                allMirrorTransforms[i].localRotation = allTransforms[i].localRotation;
                allMirrorTransforms[i].localScale = allTransforms[i].localScale;
                bool theirs = allTransforms[i].gameObject.activeSelf;
                if (allMirrorTransforms[i].gameObject.activeSelf != theirs) {
                    allMirrorTransforms[i].gameObject.SetActive(theirs);
                }
            }
        }
    }
    // Update is called once per frame
    void Update()
    {
        if (lastLegacyMenuGUI != legacyMenuGUI && AvatarSyncSource == this) {
            lastLegacyMenuGUI = legacyMenuGUI;
            UnityEngine.Object.Destroy(av3MenuComponent);
            CreateAv3MenuComponent();
        }
        if (isResettingSel) {
            isResettingSel = false;
            if (updateSelectionDelegate != null) {
                updateSelectionDelegate(this.gameObject);
            }
        }
        if (isResettingHold && (!ResetAvatar || !ResetAndHold)) {
            ResetAndHold = ResetAvatar = false;
            isResettingSel = true;
            if (updateSelectionDelegate != null) {
                updateSelectionDelegate(null);
            }
        }
        if (ResetAvatar && ResetAndHold) {
            return;
        }
        if (ResetAndHold && !ResetAvatar && !isResetting) {
            ResetAvatar = true;
            isResettingHold = true;
        }
        if (isResetting && !ResetAndHold) {
            InitializeAnimator();
            isResetting = false;
            isResettingHold = false;
        }
        if (PrevAnimatorToDebug != (char)(int)AnimatorToDebug || ResetAvatar) {
            actionIndex = fxIndex = gestureIndex = additiveIndex = sittingIndex = ikposeIndex = tposeIndex = -1;
            altActionIndex = altFXIndex = altGestureIndex = altAdditiveIndex = -1;
            // animator.runtimeAnimatorController = null;
            if (playableGraph.IsValid()) {
                playableGraph.Destroy();
            }
            if (animator.playableGraph.IsValid()) {
                animator.playableGraph.Destroy();
            }
            animator.Update(0);
            animator.Rebind();
            animator.Update(0);
            animator.StopPlayback();
            GameObject.DestroyImmediate(animator);
            // animator.runtimeAnimatorController = EmptyController;
            if (updateSelectionDelegate != null) {
                updateSelectionDelegate(null);
            }
            isResetting = true;
            isResettingSel = true;
            return;
        }
        if (CreateNonLocalClone) {
            CreateNonLocalClone = false;
            GameObject go = GameObject.Instantiate(OriginalSourceClone.gameObject);
            go.hideFlags = 0;
            AvatarSyncSource.CloneCount++;
            go.name = go.name.Substring(0, go.name.Length - 7) + " (Non-Local " + AvatarSyncSource.CloneCount + ")";
            go.transform.position = go.transform.position + AvatarSyncSource.CloneCount * new Vector3(0.4f, 0.0f, 0.4f);
            go.SetActive(true);
        }
        if (IsMirrorClone || IsShadowClone) {
            NonLocalSyncInterval = 0.0f;
        } else {
            NonLocalSyncInterval = AvatarSyncSource.NonLocalSyncInterval;
        }
        if (nextUpdateTime == 0.0f) {
            nextUpdateTime = Time.time + NonLocalSyncInterval;
        }
        if (AvatarSyncSource != this && (Time.time >= nextUpdateTime || NonLocalSyncInterval <= 0.0f)) {
            nextUpdateTime = Time.time + NonLocalSyncInterval;
            for (int i = 0; i < Ints.Count; i++) {
                if (StageParamterToBuiltin.ContainsKey(Ints[i].stageName)) {
                    Ints[i].value = ClampByte(AvatarSyncSource.Ints[i].value);
                }
            }
            for (int i = 0; i < Floats.Count; i++) {
                if (StageParamterToBuiltin.ContainsKey(Floats[i].stageName)) {
                    Floats[i].value = ClampAndQuantizeFloat(AvatarSyncSource.Floats[i].value);
                }
            }
            for (int i = 0; i < Bools.Count; i++) {
                if (StageParamterToBuiltin.ContainsKey(Bools[i].stageName)) {
                    Bools[i].value = AvatarSyncSource.Bools[i].value;
                }
            }
            VisemeInt = VisemeIdx = AvatarSyncSource.VisemeInt;
            Viseme = (VisemeIndex)VisemeInt;
            GestureLeft = AvatarSyncSource.GestureLeft;
            GestureLeftIdx = AvatarSyncSource.GestureLeftIdx;
            GestureLeftIdxInt = AvatarSyncSource.GestureLeftIdxInt;
            GestureLeftWeight = AvatarSyncSource.GestureLeftWeight;
            GestureRight = AvatarSyncSource.GestureRight;
            GestureRightIdx = AvatarSyncSource.GestureRightIdx;
            GestureRightIdxInt = AvatarSyncSource.GestureRightIdxInt;
            GestureRightWeight = AvatarSyncSource.GestureRightWeight;
            Velocity = AvatarSyncSource.Velocity;
            AngularY = AvatarSyncSource.AngularY;
            Upright = AvatarSyncSource.Upright;
            Grounded = AvatarSyncSource.Grounded;
            Seated = AvatarSyncSource.Seated;
            AFK = AvatarSyncSource.AFK;
            TrackingType = AvatarSyncSource.TrackingType;
            TrackingTypeIdx = AvatarSyncSource.TrackingTypeIdx;
            TrackingTypeIdxInt = AvatarSyncSource.TrackingTypeIdxInt;
            VRMode = AvatarSyncSource.VRMode;
            MuteSelf = AvatarSyncSource.MuteSelf;
            InStation = AvatarSyncSource.InStation;
        }
        for (int i = 0; i < Floats.Count; i++) {
            if (StageParamterToBuiltin.ContainsKey(Floats[i].stageName)) {
                if (locally8bitQuantizedFloats) {
                    Floats[i].value = ClampAndQuantizeFloat(Floats[i].value);
                } else {
                    Floats[i].value = ClampFloatOnly(Floats[i].value);
                }
            }
        }
        for (int i = 0; i < Ints.Count; i++) {
            if (StageParamterToBuiltin.ContainsKey(Ints[i].stageName)) {
                Ints[i].value = ClampByte(Ints[i].value);
            }
        }
        if (Seated != PrevSeated && sittingIndex >= 0)
        {
            playableBlendingStates[sittingIndex].StartBlend(playableMixer.GetInputWeight(sittingIndex + 1), Seated ? 1f : 0f, 0.25f);
            PrevSeated = Seated;
        }
        if (TPoseCalibration != PrevTPoseCalibration) {
            playableBlendingStates[tposeIndex].StartBlend(playableMixer.GetInputWeight(tposeIndex + 1), TPoseCalibration ? 1f : 0f, 0.0f);
            PrevTPoseCalibration = TPoseCalibration;
        }
        if (IKPoseCalibration != PrevIKPoseCalibration) {
            playableBlendingStates[ikposeIndex].StartBlend(playableMixer.GetInputWeight(ikposeIndex + 1), IKPoseCalibration ? 1f : 0f, 0.0f);
            PrevIKPoseCalibration = IKPoseCalibration;
        }
        if (VisemeIdx != VisemeInt) {
            VisemeInt = VisemeIdx;
            Viseme = (VisemeIndex)VisemeInt;
        }
        if ((int)Viseme != VisemeInt) {
            VisemeInt = (int)Viseme;
            VisemeIdx = VisemeInt;
        }
        if (GestureLeftIdx != GestureLeftIdxInt) {
            GestureLeft = (GestureIndex)GestureLeftIdx;
            GestureLeftIdx = (int)GestureLeft;
            GestureLeftIdxInt = (char)GestureLeftIdx;
        }
        if ((int)GestureLeft != (int)GestureLeftIdxInt) {
            GestureLeftIdx = (int)GestureLeft;
            GestureLeftIdxInt = (char)GestureLeftIdx;
        }
        if (GestureRightIdx != GestureRightIdxInt) {
            GestureRight = (GestureIndex)GestureRightIdx;
            GestureRightIdx = (int)GestureRight;
            GestureRightIdxInt = (char)GestureRightIdx;
        }
        if ((int)GestureRight != (int)GestureRightIdxInt) {
            GestureRightIdx = (int)GestureRight;
            GestureRightIdxInt = (char)GestureRightIdx;
        }
        if (GestureLeft == GestureIndex.Neutral) {
            GestureLeftWeight = 0;
        } else if (GestureLeft != GestureIndex.Fist) {
            GestureLeftWeight = 1;
        }
        if (GestureRight == GestureIndex.Neutral) {
            GestureRightWeight = 0;
        } else if (GestureRight != GestureIndex.Fist) {
            GestureRightWeight = 1;
        }
        if (TrackingTypeIdx != TrackingTypeIdxInt) {
            TrackingType = (TrackingTypeIndex)TrackingTypeIdx;
            TrackingTypeIdx = (int)TrackingType;
            TrackingTypeIdxInt = (char)TrackingTypeIdx;
        }
        if ((int)TrackingType != TrackingTypeIdxInt) {
            TrackingTypeIdx = (int)TrackingType;
            TrackingTypeIdxInt = (char)TrackingTypeIdx;
        }
        IsLocal = AvatarSyncSource == this;

        int whichcontroller;
        whichcontroller = 0;
        foreach (AnimatorControllerPlayable playable in playables)
        {
            if (!playable.IsValid()) {
                continue;
            }
            // Debug.Log("Index " + whichcontroller + " len " + playables.Count);
            Dictionary<string, int> parameterIndices = playableParamterIds[whichcontroller];
            int paramid;
            foreach (FloatParam param in Floats)
            {
                if (parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (param.value != param.lastValue) {
                        playable.SetFloat(paramid, param.value);
                    }
                }
            }
            foreach (IntParam param in Ints)
            {
                if (parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (param.value != param.lastValue) {
                        playable.SetInteger(paramid, param.value);
                    }
                }
            }
            foreach (BoolParam param in Bools)
            {
                if (parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (param.value != param.lastValue) {
                        playable.SetBool(paramid, param.value); // also sets triggers.
                        // if (param.value) {
                        //     playable.SetTrigger(paramid);
                        // }
                    }
                }
            }
            whichcontroller++;
        }
        foreach (FloatParam param in Floats) {
            param.lastValue = param.value;
        }
        foreach (IntParam param in Ints) {
            param.lastValue = param.value;
        }
        foreach (BoolParam param in Bools) {
            param.lastValue = param.value;
        }

        whichcontroller = 0;
        foreach (AnimatorControllerPlayable playable in playables)
        {
            if (!playable.IsValid()) {
                continue;
            }
            // Debug.Log("Index " + whichcontroller + " len " + playables.Count);
            Dictionary<string, int> parameterIndices = playableParamterIds[whichcontroller];
            Dictionary<int, int> paramterInts = playableParamterInts[whichcontroller];
            Dictionary<int, float> paramterFloats = playableParamterFloats[whichcontroller];
            Dictionary<int, bool> paramterBools = playableParamterBools[whichcontroller];
            int paramid;
            float fparam;
            int iparam;
            bool bparam;
            foreach (FloatParam param in Floats)
            {
                if (parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (paramterFloats.TryGetValue(paramid, out fparam)) {
                        if (fparam != playable.GetFloat(paramid)) {
                            param.value = param.lastValue = playable.GetFloat(paramid);
                        }
                    }
                    paramterFloats[paramid] = param.value;
                }
            }
            foreach (IntParam param in Ints)
            {
                if (parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (paramterInts.TryGetValue(paramid, out iparam)) {
                        if (iparam != playable.GetInteger(paramid)) {
                            param.value = param.lastValue = playable.GetInteger(paramid);
                        }
                    }
                    paramterInts[paramid] = param.value;
                }
            }
            foreach (BoolParam param in Bools)
            {
                if (param.hasBool[whichcontroller] && parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (paramterBools.TryGetValue(paramid, out bparam)) {
                        if (bparam != (playable.GetBool(paramid))) {
                            param.value = param.lastValue = playable.GetBool(paramid);
                        }
                    }
                    paramterBools[paramid] = param.value;
                }
            }
            if (parameterIndices.TryGetValue("Viseme", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    VisemeInt = VisemeIdx = playable.GetInteger(paramid);
                    Viseme = (VisemeIndex)VisemeInt;
                }
                playable.SetInteger(paramid, VisemeInt);
                paramterInts[paramid] = VisemeInt;
            }
            if (parameterIndices.TryGetValue("GestureLeft", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    GestureLeftIdx = playable.GetInteger(paramid);
                    GestureLeftIdxInt = (char)GestureLeftIdx;
                    GestureLeft = (GestureIndex)GestureLeftIdx;
                }
                playable.SetInteger(paramid, (int)GestureLeft);
                paramterInts[paramid] = (int)GestureLeft;
            }
            if (parameterIndices.TryGetValue("GestureLeftWeight", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    GestureLeftWeight = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, GestureLeftWeight);
                paramterFloats[paramid] = GestureLeftWeight;
            }
            if (parameterIndices.TryGetValue("GestureRight", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    GestureRightIdx = playable.GetInteger(paramid);
                    GestureRightIdxInt = (char)GestureRightIdx;
                    GestureRight = (GestureIndex)GestureRightIdx;
                }
                playable.SetInteger(paramid, (int)GestureRight);
                paramterInts[paramid] = (int)GestureRight;
            }
            if (parameterIndices.TryGetValue("GestureRightWeight", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    GestureRightWeight = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, GestureRightWeight);
                paramterFloats[paramid] = GestureRightWeight;
            }
            if (parameterIndices.TryGetValue("VelocityX", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    Velocity.x = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, Velocity.x);
                paramterFloats[paramid] = Velocity.x;
            }
            if (parameterIndices.TryGetValue("VelocityY", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    Velocity.y = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, Velocity.y);
                paramterFloats[paramid] = Velocity.y;
            }
            if (parameterIndices.TryGetValue("VelocityZ", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    Velocity.z = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, Velocity.z);
                paramterFloats[paramid] = Velocity.z;
            }
            if (parameterIndices.TryGetValue("AngularY", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    AngularY = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, AngularY);
                paramterFloats[paramid] = AngularY;
            }
            if (parameterIndices.TryGetValue("Upright", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    Upright = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, Upright);
                paramterFloats[paramid] = Upright;
            }
            if (parameterIndices.TryGetValue("IsLocal", out paramid))
            {
                playable.SetBool(paramid, IsLocal);
            }
            if (parameterIndices.TryGetValue("Grounded", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != (playable.GetBool(paramid) ? 1 : 0)) {
                    Grounded = playable.GetBool(paramid);
                }
                playable.SetBool(paramid, Grounded);
                paramterInts[paramid] = Grounded ? 1 : 0;
            }
            if (parameterIndices.TryGetValue("Seated", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != (playable.GetBool(paramid) ? 1 : 0)) {
                    Seated = playable.GetBool(paramid);
                }
                playable.SetBool(paramid, Seated);
                paramterInts[paramid] = Seated ? 1 : 0;
            }
            if (parameterIndices.TryGetValue("AFK", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != (playable.GetBool(paramid) ? 1 : 0)) {
                    AFK = playable.GetBool(paramid);
                }
                playable.SetBool(paramid, AFK);
                paramterInts[paramid] = AFK ? 1 : 0;
            }
            if (parameterIndices.TryGetValue("TrackingType", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    TrackingTypeIdx = playable.GetInteger(paramid);
                    TrackingTypeIdxInt = (char)TrackingTypeIdx;
                    TrackingType = (TrackingTypeIndex)TrackingTypeIdx;
                }
                playable.SetInteger(paramid, (int)TrackingType);
                paramterInts[paramid] = (int)TrackingType;
            }
            if (parameterIndices.TryGetValue("VRMode", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    VRMode = playable.GetInteger(paramid) != 0;
                }
                playable.SetInteger(paramid, VRMode ? 1 : 0);
                paramterInts[paramid] = VRMode ? 1 : 0;
            }
            if (parameterIndices.TryGetValue("MuteSelf", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != (playable.GetBool(paramid) ? 1 : 0)) {
                    MuteSelf = playable.GetBool(paramid);
                }
                playable.SetBool(paramid, MuteSelf);
                paramterInts[paramid] = MuteSelf ? 1 : 0;
            }
            if (parameterIndices.TryGetValue("InStation", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != (playable.GetBool(paramid) ? 1 : 0))
                {
                    InStation = playable.GetBool(paramid);
                }
                playable.SetBool(paramid, InStation);
                paramterInts[paramid] = InStation ? 1 : 0;
            }
            if (parameterIndices.TryGetValue("AvatarVersion", out paramid)) {
                playable.SetInteger(paramid, AvatarVersion);
            }
            whichcontroller++;
        }
        for (int i = 0; i < playableBlendingStates.Count; i++) {
            var pbs = playableBlendingStates[i];
            if (pbs.blending) {
                float newWeight = pbs.UpdateBlending();
                playableMixer.SetInputWeight(i + 1, newWeight);
                // Debug.Log("Whole playable " + i + " is blending to " + newWeight);
            }
            for (int j = 0; j < pbs.layerBlends.Count; j++) {
                if (pbs.layerBlends[j].blending) {
                    float newWeight = pbs.layerBlends[j].UpdateBlending();
                    playables[i].SetLayerWeight(j, newWeight);
                    // Debug.Log("Playable " + i + " layer " + j + " is blending to " + newWeight);
                }
            }
        }
        if (avadesc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone && avadesc.lipSyncJawBone != null) {
            if (Viseme == VisemeIndex.sil) {
                avadesc.lipSyncJawBone.transform.rotation = avadesc.lipSyncJawClosed;
            } else {
                avadesc.lipSyncJawBone.transform.rotation = avadesc.lipSyncJawOpen;
            }
        } else if (avadesc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape && avadesc.VisemeSkinnedMesh != null && mouthOpenBlendShapeIdx != -1) {
            if (Viseme == VisemeIndex.sil) {
                avadesc.VisemeSkinnedMesh.SetBlendShapeWeight(mouthOpenBlendShapeIdx, 0.0f);
            } else {
                avadesc.VisemeSkinnedMesh.SetBlendShapeWeight(mouthOpenBlendShapeIdx, 100.0f);
            }
        } else if (avadesc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape && avadesc.VisemeSkinnedMesh != null) {
            for (int i = 0; i < visemeBlendShapeIdxs.Length; i++) {
                if (visemeBlendShapeIdxs[i] != -1) {
                    avadesc.VisemeSkinnedMesh.SetBlendShapeWeight(visemeBlendShapeIdxs[i], (i == VisemeIdx ? 100.0f : 0.0f));
                }
            }
        }
    }
}
