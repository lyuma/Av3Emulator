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
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

// [RequireComponent(typeof(Animator))]
public class LyumaAv3Runtime : MonoBehaviour
{
    static public Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController> animLayerToDefaultController = new Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController>();
    static public Dictionary<VRCAvatarDescriptor.AnimLayerType, AvatarMask> animLayerToDefaultAvaMask = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AvatarMask>();
    public delegate void UpdateSelectionFunc(UnityEngine.Object obj);
    public static UpdateSelectionFunc updateSelectionDelegate;
    public delegate void AddRuntime(Component runtime);
    public static AddRuntime addRuntimeDelegate;
    public delegate void UpdateSceneLayersFunc(int layers);
    public static UpdateSceneLayersFunc updateSceneLayersDelegate;
    public delegate void ApplyOnEnableWorkaroundDelegateType();
    public static ApplyOnEnableWorkaroundDelegateType ApplyOnEnableWorkaroundDelegate;

    public LyumaAv3Runtime OriginalSourceClone = null;

    [Tooltip("Resets avatar state machine instantly")]
    public bool ResetAvatar;
    [Tooltip("Resets avatar state machine and waits until you uncheck this to start")]
    public bool ResetAndHold;
    [Tooltip("Click if you modified your menu or parameter list")]
    public bool RefreshExpressionParams;
    [Tooltip("Simulates saving and reloading the avatar")]
    public bool KeepSavedParametersOnReset = true;
    [HideInInspector] public bool legacyMenuGUI = true;
    private bool lastLegacyMenuGUI = true;
    [Header("Animator to Debug. Unity is glitchy when not 'Base'.")]
    [Tooltip("Selects the playable layer to be visible with parameters in the Animator. If you view any other playable in the Animator window, parameters will say 0 and will not update.")]
    public VRCAvatarDescriptor.AnimLayerType DebugDuplicateAnimator;
    private char PrevAnimatorToDebug;
    [Tooltip("Selects the playable layer to be visible in Unity's Animator window. Does not reset avatar. Unless this is set to Base, will cause 'Invalid Layer Index' logspam; layers will show wrong weight and parameters will all be 0.")]
    public VRCAvatarDescriptor.AnimLayerType ViewAnimatorOnlyNoParams;
    private char PrevAnimatorToViewLiteParamsShow0;
    [HideInInspector] public string SourceObjectPath;
    [HideInInspector] public LyumaAv3Runtime AvatarSyncSource;
    private float nextUpdateTime = 0.0f;
    [Header("OSC (double click OSC Controller for debug and port settings)")]
    public bool EnableAvatarOSC = false;
    public LyumaAv3Osc OSCController = null;
    public A3EOSCConfiguration OSCConfigurationFile = new A3EOSCConfiguration();

    [Header("Network Clones and Sync")]
    public bool CreateNonLocalClone;
    [Tooltip("In VRChat, 8-bit float quantization only happens remotely. Check this to test your robustness to quantization locally, too. (example: 0.5 -> 0.503")]
    public bool locally8bitQuantizedFloats = false;
    private int CloneCount;
    [Range(0.0f, 2.0f)] public float NonLocalSyncInterval = 0.2f;
    [Tooltip("Parameters visible in the radial menu will IK sync")] public bool IKSyncRadialMenu = true;
    [Header("PlayerLocal and MirrorReflection")]
    public bool EnableHeadScaling;
    public bool DisableMirrorAndShadowClones;
    [HideInInspector] public LyumaAv3Runtime MirrorClone;
    [HideInInspector] public LyumaAv3Runtime ShadowClone;
    [Tooltip("To view both copies at once")] public bool DebugOffsetMirrorClone = false;
    public bool ViewMirrorReflection;
    private bool LastViewMirrorReflection;
    public bool ViewBothRealAndMirror;
    private bool LastViewBothRealAndMirror;
    [HideInInspector] public VRCAvatarDescriptor avadesc;
    Avatar animatorAvatar;
    Animator animator;
    private RuntimeAnimatorController origAnimatorController;
    public Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController> allControllers = new Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController>();

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

    [NonSerialized] public VRCPhysBone[] AvDynamicsPhysBones = new VRCPhysBone[]{};
    [NonSerialized] public VRCContactReceiver[] AvDynamicsContactReceivers = new VRCContactReceiver[]{};

    public class Av3EmuParameterAccess : VRC.SDKBase.IAnimParameterAccess {
        public LyumaAv3Runtime runtime;
        public string paramName;
        public bool boolVal {
            get {
                // Debug.Log(paramName + " GETb");
                int idx;
                if (runtime.IntToIndex.TryGetValue(paramName, out idx)) return runtime.Ints[idx].value != 0;
                if (runtime.FloatToIndex.TryGetValue(paramName, out idx))return runtime.Floats[idx].exportedValue != 0.0f;
                if (runtime.BoolToIndex.TryGetValue(paramName, out idx)) return runtime.Bools[idx].value;
                return false;
            }
            set {
                // Debug.Log(paramName + " SETb " + value);
                int idx;
                if (runtime.IntToIndex.TryGetValue(paramName, out idx)) runtime.Ints[idx].value = value ? 1 : 0;
                if (runtime.FloatToIndex.TryGetValue(paramName, out idx)) {
                    runtime.Floats[idx].value = value ? 1.0f : 0.0f;
                    runtime.Floats[idx].exportedValue = runtime.Floats[idx].value;
                }
                if (runtime.BoolToIndex.TryGetValue(paramName, out idx)) runtime.Bools[idx].value = value;
            }
        }
        public int intVal {
            get {
                int idx;
                // Debug.Log(paramName + " GETi");
                if (runtime.IntToIndex.TryGetValue(paramName, out idx)) return runtime.Ints[idx].value;
                if (runtime.FloatToIndex.TryGetValue(paramName, out idx)) return (int)runtime.Floats[idx].exportedValue;
                if (runtime.BoolToIndex.TryGetValue(paramName, out idx)) return runtime.Bools[idx].value ? 1 : 0;
                return 0;
            }
            set {
                // Debug.Log(paramName + " SETi " + value);
                int idx;
                if (runtime.IntToIndex.TryGetValue(paramName, out idx)) runtime.Ints[idx].value = value;
                if (runtime.FloatToIndex.TryGetValue(paramName, out idx)) {
                    runtime.Floats[idx].value = (float)value;
                    runtime.Floats[idx].exportedValue = runtime.Floats[idx].value;
                }
                if (runtime.BoolToIndex.TryGetValue(paramName, out idx)) runtime.Bools[idx].value = value != 0;
            }
        }
        public float floatVal {
            get {
                // Debug.Log(paramName + " GETf");
                int idx;
                if (runtime.IntToIndex.TryGetValue(paramName, out idx)) return (float)runtime.Ints[idx].value;
                if (runtime.FloatToIndex.TryGetValue(paramName, out idx)) return runtime.Floats[idx].exportedValue;
                if (runtime.BoolToIndex.TryGetValue(paramName, out idx)) return runtime.Bools[idx].value ? 1.0f : 0.0f;
                return 0.0f;
            }
            set {
                // Debug.Log(paramName + " SETf " + value);
                int idx;
                if (runtime.IntToIndex.TryGetValue(paramName, out idx)) runtime.Ints[idx].value = (int)value;
                if (runtime.FloatToIndex.TryGetValue(paramName, out idx)) {
                    runtime.Floats[idx].value = value;
                    runtime.Floats[idx].exportedValue = value;
                }
                if (runtime.BoolToIndex.TryGetValue(paramName, out idx)) runtime.Bools[idx].value = value != 0.0f;
            }
        }
    }

    public void assignContactParameters(VRCContactReceiver[] behaviours) {
        AvDynamicsContactReceivers = behaviours;
        foreach (var mb in AvDynamicsContactReceivers) {
            var old_value = mb.paramAccess;
            if (old_value == null || old_value.GetType() != typeof(Av3EmuParameterAccess)) {
                string parameter = mb.parameter;
                Av3EmuParameterAccess accessInst = new Av3EmuParameterAccess();
                accessInst.runtime = this;
                accessInst.paramName = parameter;
                mb.paramAccess = accessInst;
                accessInst.floatVal = mb.paramValue;
                // Debug.Log("Assigned access " + contactReceiverState.paramAccess.GetValue(mb) + " to param " + parameter + ": was " + old_value);
            }
        }
    }
    public void assignPhysBoneParameters(VRCPhysBone[] behaviours) {
        AvDynamicsPhysBones = behaviours;
        foreach (var mb in AvDynamicsPhysBones) {
            var old_value = mb.param_Stretch;
            if (old_value == null || old_value.GetType() != typeof(Av3EmuParameterAccess)) {
                string parameter = mb.parameter;
                Av3EmuParameterAccess accessInst = new Av3EmuParameterAccess();
                accessInst.runtime = this;
                accessInst.paramName = parameter + VRCPhysBone.PARAM_ANGLE;
                mb.param_Angle = accessInst;
                accessInst.floatVal = mb.param_AngleValue;
                accessInst = new Av3EmuParameterAccess();
                accessInst.runtime = this;
                accessInst.paramName = parameter + VRCPhysBone.PARAM_ISGRABBED;
                mb.param_IsGrabbed = accessInst;
                accessInst.boolVal = mb.param_IsGrabbedValue;
                accessInst = new Av3EmuParameterAccess();
                accessInst.runtime = this;
                accessInst.paramName = parameter + VRCPhysBone.PARAM_STRETCH;
                mb.param_Stretch = accessInst;
                accessInst.floatVal = mb.param_StretchValue;
                // Debug.Log("Assigned strech access " + physBoneState.param_Stretch.GetValue(mb) + " to param " + parameter + ": was " + old_value);
            }
        }
    }

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
        typeof(Camera), typeof(FlareLayer), typeof(AudioSource), typeof(Rigidbody), typeof(Joint)
    };
    public static readonly HashSet<Type> ShadowCloneComponentBlacklist = new HashSet<Type> {
        typeof(Camera), typeof(FlareLayer), typeof(AudioSource), typeof(Light), typeof(ParticleSystemRenderer), typeof(Rigidbody), typeof(Joint)
    };
    [Header("Built-in inputs / Viseme")]
    public VisemeIndex Viseme;
    [Range(0, 15)] public int VisemeIdx;
    private int VisemeInt;
    [Tooltip("Voice amount from 0.0f to 1.0f for the current viseme")]
    [Range(0,1)] public float Voice;
    [Header("Built-in inputs / Hand Gestures")]
    public GestureIndex GestureLeft;
    [Range(0, 9)] public int GestureLeftIdx;
    private char GestureLeftIdxInt;
    [Range(0, 1)] public float GestureLeftWeight;
    private float OldGestureLeftWeight;
    public GestureIndex GestureRight;
    [Range(0, 9)] public int GestureRightIdx;
    private char GestureRightIdxInt;
    [Range(0, 1)] public float GestureRightWeight;
    private float OldGestureRightWeight;
    [Header("Built-in inputs / Locomotion")]
    public Vector3 Velocity;
    [Range(-400, 400)] public float AngularY;
    [Range(0, 1)] public float Upright;
    public bool Grounded;
    public bool Jump;
    public float JumpPower = 5;
    public float RunSpeed = 0.0f;
    private bool WasJump;
    private Vector3 JumpingHeight;
    private Vector3 JumpingVelocity;
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
    private bool MuteTogglerOn;
    public bool InStation;
    [HideInInspector] public int AvatarVersion = 3;

    [Header("Output State (Read-only)")]
    public bool IsLocal;
    [HideInInspector] public bool IsMirrorClone;
    [HideInInspector] public bool IsShadowClone;
    public bool LocomotionIsDisabled;

    [Serializable]
    public struct IKTrackingOutput {
        public Vector3 HeadRelativeViewPosition;
        public Vector3 ViewPosition;
        public float AvatarScaleFactorGuess;
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
    }
    public IKTrackingOutput IKTrackingOutputData;

    [Serializable]
    public class FloatParam
    {
        [HideInInspector] public string stageName;
        public string name;
        [HideInInspector] public bool synced;
        [Range(-1, 1)] public float expressionValue;
        [HideInInspector] public float lastExpressionValue_;
        [Range(-1, 1)] public float value;
        [HideInInspector] private float exportedValue_;
        public float exportedValue {
            get {
                return exportedValue_;
            } set {
                this.exportedValue_ = value;
                this.value = value;
                this.lastExpressionValue_ = value;
                this.expressionValue = value;
            }
        }
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

    [HideInInspector] public LyumaAv3Emulator emulator;

    static public Dictionary<Animator, LyumaAv3Runtime> animatorToTopLevelRuntime = new Dictionary<Animator, LyumaAv3Runtime>();
    private HashSet<Animator> attachedAnimators;
    private HashSet<string> duplicateParameterAdds = new HashSet<string>();

    const float BASE_HEIGHT = 1.4f;

    public IEnumerator DelayedEnterPoseSpace(bool setView, float time) {
        yield return new WaitForSeconds(time);
        if (setView) {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) {
                IKTrackingOutputData.ViewPosition = animator.transform.InverseTransformPoint(head.TransformPoint(IKTrackingOutputData.HeadRelativeViewPosition));
            }
        } else {
            IKTrackingOutputData.ViewPosition = avadesc.ViewPosition;
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

    static HashSet<Animator> issuedWarningAnimators = new HashSet<Animator>();
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

        if (!issuedWarningAnimators.Contains(innerAnimator))
        {
            issuedWarningAnimators.Add(innerAnimator);
            Debug.LogWarning("[" + component + "]: outermost Animator is not known: " + innerAnimator + ". If you changed something, consider resetting avatar", innerAnimator);
        }

        return false;
    }

    float getAdjustedParameterAsFloat(string paramName, bool convertRange=false, float srcMin=0.0f, float srcMax=0.0f, float dstMin=0.0f, float dstMax=0.0f) {
        float newValue = 0;
        int idx;
        if (FloatToIndex.TryGetValue(paramName, out idx)) {
            newValue = Floats[idx].exportedValue;
        } else if (IntToIndex.TryGetValue(paramName, out idx)) {
            newValue = (float)Ints[idx].value;
        } else if (BoolToIndex.TryGetValue(paramName, out idx)) {
            newValue = Bools[idx].value ? 1.0f : 0.0f;
        }
        if (convertRange) {
            if (dstMax != dstMin) {
                newValue = Mathf.Lerp(dstMin, dstMax, Mathf.Clamp01(Mathf.InverseLerp(srcMin, srcMax, newValue)));
            } else {
                newValue = dstMin;
            }
        }
        return newValue;
    }

    static LyumaAv3Runtime() {
        VRCAvatarParameterDriver.OnApplySettings += (behaviour, animator) =>
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
                    if (runtime.DebugDuplicateAnimator != VRCAvatarDescriptor.AnimLayerType.Base && !runtime.IsMirrorClone && !runtime.IsShadowClone && (parameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add || parameter.type == VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random)) {
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
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy:
                                runtime.Ints[idx].value = (int)runtime.getAdjustedParameterAsFloat(parameter.source, parameter.convertRange, parameter.sourceMin, parameter.sourceMax, parameter.destMin, parameter.destMax);
                                break;
                        }
                    }
                    if (runtime.FloatToIndex.TryGetValue(actualName, out idx)) {
                        switch (parameter.type) {
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set:
                                runtime.Floats[idx].exportedValue = parameter.value;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add:
                                runtime.Floats[idx].exportedValue += parameter.value;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random:
                                runtime.Floats[idx].exportedValue = UnityEngine.Random.Range(parameter.valueMin, parameter.valueMax);
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy:
                                runtime.Floats[idx].exportedValue = runtime.getAdjustedParameterAsFloat(parameter.source, parameter.convertRange, parameter.sourceMin, parameter.sourceMax, parameter.destMin, parameter.destMax);
                                break;
                        }
                        runtime.Floats[idx].value = runtime.Floats[idx].exportedValue;
                    }
                    if (runtime.BoolToIndex.TryGetValue(actualName, out idx)) {
                        bool newValue;
                        BoolParam bp = runtime.Bools[idx];
                        int whichController;
                        // bp.value = parameter.value != 0;
                        switch (parameter.type) {
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set:
                                newValue = parameter.value != 0.0f;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add:
                                /* editor script treats it as random, but it is its own operation */
                                newValue = ((bp.value ? 1.0 : 0.0) + parameter.value) != 0.0f; // weird but ok...
                                // Debug.Log("Add bool " + bp.name + " to " + newValue + ", " + (bp.value ? 1.0 : 0.0) + ", " + parameter.value);
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random:
                                // random is *not* idempotent.
                                newValue = UnityEngine.Random.Range(0.0f, 1.0f) < parameter.chance;
                                break;
                            case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy:
                                newValue = runtime.getAdjustedParameterAsFloat(parameter.source, parameter.convertRange, parameter.sourceMin, parameter.sourceMax, parameter.destMin, parameter.destMax) != 0.0f;
                                break;
                            default:
                                continue;
                        }
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
                    }
                }
                foreach (var key in deleteParameterAdds) {
                    runtime.duplicateParameterAdds.Remove(key);
                }
                foreach (var key in newParameterAdds) {
                    runtime.duplicateParameterAdds.Add(key);
                }
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
                    runtime.IKTrackingOutputData.trackingMouthAndJaw = behaviour.trackingMouth;
                }
                if (behaviour.trackingHead != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingHead = behaviour.trackingHead;
                }
                if (behaviour.trackingRightFingers != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingRightFingers = behaviour.trackingRightFingers;
                }
                if (behaviour.trackingEyes != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingEyesAndEyelids = behaviour.trackingEyes;
                }
                if (behaviour.trackingLeftFingers != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingLeftFingers = behaviour.trackingLeftFingers;
                }
                if (behaviour.trackingLeftFoot != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingLeftFoot = behaviour.trackingLeftFoot;
                }
                if (behaviour.trackingHip != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingHip = behaviour.trackingHip;
                }
                if (behaviour.trackingRightHand != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingRightHand = behaviour.trackingRightHand;
                }
                if (behaviour.trackingLeftHand != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingLeftHand = behaviour.trackingLeftHand;
                }
                if (behaviour.trackingRightFoot != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.IKTrackingOutputData.trackingRightFoot = behaviour.trackingRightFoot;
                }
            };
        };
    }

    void OnDestroy () {
        if (this.playableGraph.IsValid()) {
            this.playableGraph.Destroy();
        }
        if (attachedAnimators != null) {
            foreach (var anim in attachedAnimators) {
                LyumaAv3Runtime runtime;
                if (animatorToTopLevelRuntime.TryGetValue(anim, out runtime) && runtime == this)
                {
                    animatorToTopLevelRuntime.Remove(anim);
                }
            }
        }
        if (animator != null) {
            if (animator.playableGraph.IsValid())
            {
                animator.playableGraph.Destroy();
            }
            animator.runtimeAnimatorController = origAnimatorController;
        }
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
        // Debug.Log("AWOKEN " + gameObject.name, this);
        attachedAnimators = new HashSet<Animator>();
        if (AvatarSyncSource == null) {
            var oml = GetComponent<UnityEngine.AI.OffMeshLink>();
            if (oml != null && oml.startTransform != null) {
                this.emulator = oml.startTransform.GetComponent<LyumaAv3Emulator>();
                GameObject.DestroyImmediate(oml);
            }
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

        if (this.emulator != null) {
            DebugDuplicateAnimator = this.emulator.DefaultAnimatorToDebug;
            ViewAnimatorOnlyNoParams = this.emulator.DefaultAnimatorToDebug;
        }

        animator = this.gameObject.GetOrAddComponent<Animator>();
        if (animatorAvatar != null && animator.avatar == null) {
            animator.avatar = animatorAvatar;
        } else {
            animatorAvatar = animator.avatar;
        }
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
        bool shouldClone = false;
        if (OriginalSourceClone == null) {
            OriginalSourceClone = this;
            shouldClone = true;
        }
        if (shouldClone && GetComponent<PipelineSaver>() == null) {
            GameObject cloned = GameObject.Instantiate(gameObject);
            cloned.hideFlags = HideFlags.HideAndDontSave;
            cloned.SetActive(false);
            OriginalSourceClone = cloned.GetComponent<LyumaAv3Runtime>();
            Debug.Log("Spawned a hidden source clone " + OriginalSourceClone, OriginalSourceClone);
            OriginalSourceClone.OriginalSourceClone = OriginalSourceClone;
        }
        foreach (var smr in gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            smr.updateWhenOffscreen = (AvatarSyncSource == this || IsMirrorClone || IsShadowClone);
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
        if (this.AvatarSyncSource != this || IsMirrorClone || IsShadowClone) {
            PrevAnimatorToViewLiteParamsShow0 = (char)(int)ViewAnimatorOnlyNoParams;
        }
        if (!IsMirrorClone && !IsShadowClone && AvatarSyncSource == this) {
            var pipelineManager = avadesc.GetComponent<VRC.Core.PipelineManager>();
            string avatarid = pipelineManager != null ? pipelineManager.blueprintId : null;
           OSCConfigurationFile.EnsureOSCJSONConfig(avadesc.expressionParameters, avatarid, this.gameObject.name);
        }
    }

    public void CreateMirrorClone() {
        if (AvatarSyncSource == this && GetComponent<PipelineSaver>() == null) {
            OriginalSourceClone.IsMirrorClone = true;
            MirrorClone = GameObject.Instantiate(OriginalSourceClone.gameObject).GetComponent<LyumaAv3Runtime>();
            MirrorClone.GetComponent<Animator>().avatar = null;
            OriginalSourceClone.IsMirrorClone = false;
            GameObject o = MirrorClone.gameObject;
            o.name = gameObject.name + " (MirrorReflection)";
            o.SetActive(true);
            allMirrorTransforms = MirrorClone.gameObject.GetComponentsInChildren<Transform>(true);
            foreach (Component component in MirrorClone.gameObject.GetComponentsInChildren<Component>(true)) {
                if (MirrorCloneComponentBlacklist.Contains(component.GetType()) || component.GetType().ToString().Contains("DynamicBone")
                         || component.GetType().ToString().Contains("VRCContact") || component.GetType().ToString().Contains("VRCPhysBone")) {
                    UnityEngine.Object.Destroy(component);
                }
            }
        }
    }

    public void CreateShadowClone() {
        if (AvatarSyncSource == this && GetComponent<PipelineSaver>() == null) {
            OriginalSourceClone.IsShadowClone = true;
            ShadowClone = GameObject.Instantiate(OriginalSourceClone.gameObject).GetComponent<LyumaAv3Runtime>();
            ShadowClone.GetComponent<Animator>().avatar = null;
            OriginalSourceClone.IsShadowClone = false;
            GameObject o = ShadowClone.gameObject;
            o.name = gameObject.name + " (ShadowClone)";
            o.SetActive(true);
            allShadowTransforms = ShadowClone.gameObject.GetComponentsInChildren<Transform>(true);
            foreach (Component component in ShadowClone.gameObject.GetComponentsInChildren<Component>(true)) {
                if (ShadowCloneComponentBlacklist.Contains(component.GetType()) || component.GetType().ToString().Contains("DynamicBone")
                         || component.GetType().ToString().Contains("VRCContact") || component.GetType().ToString().Contains("VRCPhysBone")) {
                    UnityEngine.Object.Destroy(component);
                    continue;
                }
                if (component.GetType() == typeof(SkinnedMeshRenderer) || component.GetType() == typeof(MeshRenderer)) {
                    Renderer renderer = component as Renderer;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; // ShadowCastingMode.TwoSided isn't accounted for and does not work locally
                }
            }
            foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true)) {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // ShadowCastingMode.TwoSided isn't accounted for and does not work locally
            }
        }
    }

    private void InitializeAnimator()
    {
        ResetAvatar = false;
        PrevAnimatorToDebug = (char)(int)DebugDuplicateAnimator;
        ViewAnimatorOnlyNoParams = DebugDuplicateAnimator;

        animator = this.gameObject.GetOrAddComponent<Animator>();
        animator.avatar = animatorAvatar;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = (this == AvatarSyncSource || IsMirrorClone || IsShadowClone) ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullCompletely;
        animator.runtimeAnimatorController = null;

        avadesc = this.gameObject.GetComponent<VRCAvatarDescriptor>();
        IKTrackingOutputData.ViewPosition = avadesc.ViewPosition;
        IKTrackingOutputData.AvatarScaleFactorGuess = IKTrackingOutputData.ViewPosition.magnitude / BASE_HEIGHT; // mostly guessing...
        IKTrackingOutputData.HeadRelativeViewPosition = IKTrackingOutputData.ViewPosition;
        if (animator.avatar != null)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) {
                IKTrackingOutputData.HeadRelativeViewPosition = head.InverseTransformPoint(animator.transform.TransformPoint(IKTrackingOutputData.ViewPosition));
            }
        }
        expressionsMenu = avadesc.expressionsMenu;
        stageParameters = avadesc.expressionParameters;
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
        if (DebugDuplicateAnimator != VRCAvatarDescriptor.AnimLayerType.Base && !IsMirrorClone && !IsShadowClone) {
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in baselayers) {
                if (DebugDuplicateAnimator == cal.type) {
                    i++;
                    allLayers.Add(cal);
                    break;
                }
            }
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in speciallayers) {
                if (DebugDuplicateAnimator == cal.type) {
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
                if (DebugDuplicateAnimator != cal.type) {
                    i++;
                    allLayers.Add(cal);
                }
            }
            foreach (VRCAvatarDescriptor.CustomAnimLayer cal in speciallayers) {
                if (DebugDuplicateAnimator != cal.type) {
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
        foreach (Animator anim in animators)
        {
            attachedAnimators.Add(anim);
            animatorToTopLevelRuntime.Add(anim, this);
        }

        Dictionary<string, float> stageNameToValue = EarlyRefreshExpressionParameters();
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
                Debug.Log(vrcAnimLayer.type + " controller is null: continue.");
                // i was incremented, but one of the playableMixer inputs is left unconnected.
                continue;
            }
            allControllers[vrcAnimLayer.type] = ac;
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

            // Keep weight 1.0 if (i < dupeOffset).
            // Layers have incorrect AAP values if playable weight is 0.0...
            // and the duplicate layers will be overridden later anyway by Base.
        }

        LateRefreshExpressionParameters(stageNameToValue);

        // Plays the Graph.
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        Debug.Log(this.name + " : " + GetType() + " awoken and ready to Play.", this);
        playableGraph.Play();
    }

    Dictionary<string, float> EarlyRefreshExpressionParameters() {
        Dictionary<string, float> stageNameToValue = new Dictionary<string, float>();
        if (IsLocal) {
            foreach (var val in Ints) {
                stageNameToValue[val.stageName] = val.value;
            }
            foreach (var val in Floats) {
                stageNameToValue[val.stageName] = val.exportedValue;
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
        return stageNameToValue;
    }
    void LateRefreshExpressionParameters(Dictionary<string, float> stageNameToValue) {
        HashSet<string> usedparams = new HashSet<string>(BUILTIN_PARAMETERS);
        int i = 0;
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
                    param.exportedValue = lastDefault;
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
        } else {
            IntParam param = new IntParam();
            param.stageName = "VRCEmote";
            param.synced = true;
            param.name = "VRCEmote";
            Ints.Add(param);
            usedparams.Add("VRCEmote");
            FloatParam fparam = new FloatParam();
            fparam.stageName = "VRCFaceBlendH";
            fparam.synced = true;
            fparam.name = "VRCFaceBlendH";
            Floats.Add(fparam);
            usedparams.Add("VRCFaceBlendH");
            fparam = new FloatParam();
            fparam.stageName = "VRCFaceBlendV";
            fparam.synced = true;
            fparam.name = "VRCFaceBlendV";
            Floats.Add(fparam);
            usedparams.Add("VRCFaceBlendV");
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
                    param.exportedValue = aparam.defaultFloat;
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
        LyumaAv3Menu mainMenu;
        if (gestureManagerMenu != null) {
            mainMenu = (LyumaAv3Menu)avadesc.gameObject.AddComponent(gestureManagerMenu);
            mainMenu.useLegacyMenu = legacyMenuGUI;
        } else {
            mainMenu = avadesc.gameObject.AddComponent<LyumaAv3Menu>();
        }
        mainMenu.Runtime = this;
        mainMenu.RootMenu = avadesc.expressionsMenu;
    }


    private bool isResetting;
    private bool isResettingHold;
    private bool isResettingSel;
    void LateUpdate() {
        if (ResetAndHold || (emulator != null && (!emulator.enabled || !emulator.gameObject.activeInHierarchy))) {
            return;
        }
        if (IsMirrorClone || IsShadowClone) {
            // Experimental. Attempt to reproduce the 1-frame desync in some cases between normal and mirror copy.
            NormalUpdate();
        }
        if(animator != null && this == AvatarSyncSource && !IsMirrorClone && !IsShadowClone) {
            if (MirrorClone != null) {
                MirrorClone.gameObject.SetActive(true);
                MirrorClone.transform.localRotation = transform.localRotation;
                MirrorClone.transform.localScale = transform.localScale;
                MirrorClone.transform.position = transform.position + (DebugOffsetMirrorClone ? new Vector3(0.0f, 1.3f * avadesc.ViewPosition.y, 0.0f) : Vector3.zero);
            }
            if (ShadowClone != null) {
                ShadowClone.gameObject.SetActive(true);
                ShadowClone.transform.localRotation = transform.localRotation;
                ShadowClone.transform.localScale = transform.localScale;
                ShadowClone.transform.position = transform.position;
            }
            foreach (Transform[] allXTransforms in new Transform[][]{allMirrorTransforms, allShadowTransforms}) {
                if (allXTransforms != null) {
                    Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                    for(int i = 0; i < allTransforms.Length && i < allXTransforms.Length; i++) {
                        if (allXTransforms[i] == null || allTransforms[i] == this.transform) {
                            continue;
                        }
                        MeshRenderer mr = allTransforms[i].GetComponent<MeshRenderer>();
                        MeshRenderer xmr = allXTransforms[i].GetComponent<MeshRenderer>();
                        if (mr != null && xmr != null) {
                            for (int mri = 0; mri < mr.sharedMaterials.Length && mri < xmr.sharedMaterials.Length; mri++) {
                                xmr.sharedMaterials[mri] = mr.sharedMaterials[mri];
                            }
                        }
                        allXTransforms[i].localPosition = allTransforms[i].localPosition;
                        allXTransforms[i].localRotation = allTransforms[i].localRotation;
                        if(allTransforms[i] == head && EnableHeadScaling) {
                            allXTransforms[i].localScale = new Vector3(1.0f, 1.0f, 1.0f);
                        } else {
                            allXTransforms[i].localScale = allTransforms[i].localScale;
                        }
                        bool theirs = allTransforms[i].gameObject.activeSelf;
                        if (allXTransforms[i].gameObject.activeSelf != theirs) {
                            allXTransforms[i].gameObject.SetActive(theirs);
                        }
                    }
                }
            }
        }
    }

    void FixedUpdate() {
        if (Jump && !WasJump && Grounded) {
            JumpingVelocity = new Vector3(0.0f, JumpPower, 0.0f);
            JumpingHeight += JumpingVelocity;
            Grounded = false;
        }
        WasJump = Jump;
        if (JumpingHeight != Vector3.zero) {
            JumpingHeight += JumpingVelocity;
            JumpingVelocity += Physics.gravity * Time.fixedDeltaTime;
            if (JumpingHeight.y <= 0.0f) {
                JumpingHeight = Vector3.zero;
                JumpingVelocity = Vector3.zero;
                Grounded = true;
                Jump = false;
                WasJump = false;
            }
            Velocity.y = JumpingVelocity.y;
 
        }
    }

    private bool broadcastStartNextFrame;
    void OnEnable() {
        if (emulator != null && emulator.WorkaroundPlayModeScriptCompile) {
            ApplyOnEnableWorkaroundDelegate();
        }
        if (attachedAnimators == null && AvatarSyncSource != null) {
            broadcastStartNextFrame = true;
        }
    }

    void Update() {
        if (broadcastStartNextFrame) {
            Debug.Log("BROADCASTING START!");
            broadcastStartNextFrame = false;
            BroadcastMessage("Start");
        }
        if (emulator != null && (!emulator.enabled || !emulator.gameObject.activeInHierarchy)) {
            return;
        }
        if (!IsMirrorClone && !IsShadowClone) {
            NormalUpdate();
        }
    }

    // Update is called once per frame
    void NormalUpdate()
    {
        if (OSCConfigurationFile.OSCAvatarID == null) {
            OSCConfigurationFile.OSCAvatarID = A3EOSCConfiguration.AVTR_EMULATOR_PREFIX + "Default";
        }
        if ((OSCConfigurationFile.UseRealPipelineIdJSONFile && OSCConfigurationFile.OSCAvatarID.StartsWith(A3EOSCConfiguration.AVTR_EMULATOR_PREFIX)) ||
                (!OSCConfigurationFile.UseRealPipelineIdJSONFile && !OSCConfigurationFile.OSCAvatarID.StartsWith(A3EOSCConfiguration.AVTR_EMULATOR_PREFIX))) {
            var pipelineManager = avadesc.GetComponent<VRC.Core.PipelineManager>();
            string avatarid = pipelineManager != null ? pipelineManager.blueprintId : null;
            OSCConfigurationFile.EnsureOSCJSONConfig(avadesc.expressionParameters, avatarid, this.gameObject.name);
        }
        if (OSCConfigurationFile.SaveOSCConfig) {
            OSCConfigurationFile.SaveOSCConfig = false;
            A3EOSCConfiguration.WriteJSON(OSCConfigurationFile.OSCFilePath, OSCConfigurationFile.OSCJsonConfig);
        }
        if (OSCConfigurationFile.LoadOSCConfig) {
            OSCConfigurationFile.LoadOSCConfig = false;
            OSCConfigurationFile.OSCJsonConfig = A3EOSCConfiguration.ReadJSON(OSCConfigurationFile.OSCFilePath);
        }
        if (OSCConfigurationFile.GenerateOSCConfig) {
            OSCConfigurationFile.GenerateOSCConfig = false;
            OSCConfigurationFile.OSCJsonConfig = A3EOSCConfiguration.GenerateOuterJSON(avadesc.expressionParameters, OSCConfigurationFile.OSCAvatarID, this.gameObject.name);
        }
        if (lastLegacyMenuGUI != legacyMenuGUI && AvatarSyncSource == this) {
            lastLegacyMenuGUI = legacyMenuGUI;
            foreach (var av3MenuComponent in GetComponents<LyumaAv3Menu>()) {
                av3MenuComponent.useLegacyMenu = legacyMenuGUI;
            }
        }
        if (isResettingSel) {
            isResettingSel = false;
            if (updateSelectionDelegate != null && AvatarSyncSource == this) {
                updateSelectionDelegate(this.gameObject);
                PrevAnimatorToViewLiteParamsShow0 = (char)126;
            }
        }
        if (isResettingHold && (!ResetAvatar || !ResetAndHold)) {
            ResetAndHold = ResetAvatar = false;
            isResettingSel = true;
            if (updateSelectionDelegate != null && AvatarSyncSource == this) {
                updateSelectionDelegate(this.emulator != null ? this.emulator.gameObject : null);
                PrevAnimatorToViewLiteParamsShow0 = (char)126;
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
            if (attachedAnimators == null) {
                if (AvatarSyncSource == this) {
                    AvatarSyncSource = null;
                }
                Awake();
                isResetting = false;
                isResettingHold = false;
                return;
            } else {
                InitializeAnimator();
            }
            isResetting = false;
            isResettingHold = false;
        }
        if (PrevAnimatorToDebug != (char)(int)DebugDuplicateAnimator || ResetAvatar || attachedAnimators == null) {
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
            if (updateSelectionDelegate != null && AvatarSyncSource == this) {
                updateSelectionDelegate(this.emulator != null ? this.emulator.gameObject : null);
            }
            isResetting = true;
            isResettingSel = true;
            return;
        }
        if (PrevAnimatorToViewLiteParamsShow0 == (char)127) {
            updateSelectionDelegate(this);
            ViewAnimatorOnlyNoParams = (VRCAvatarDescriptor.AnimLayerType)(int)126;
            PrevAnimatorToViewLiteParamsShow0 = (char)(int)ViewAnimatorOnlyNoParams;
        }
        if ((char)(int)ViewAnimatorOnlyNoParams != PrevAnimatorToViewLiteParamsShow0) {
            PrevAnimatorToViewLiteParamsShow0 = (char)127;
            RuntimeAnimatorController rac = null;
            allControllers.TryGetValue(ViewAnimatorOnlyNoParams, out rac);
            updateSelectionDelegate(rac == null ? (UnityEngine.Object)this.emulator : (UnityEngine.Object)rac);
        }
        if (RefreshExpressionParams) {
            RefreshExpressionParams = false;
            Dictionary<string, float> stageNameToValue = EarlyRefreshExpressionParameters();
            LateRefreshExpressionParameters(stageNameToValue);
        }
        if(this == AvatarSyncSource && !IsMirrorClone && !IsShadowClone) {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) {
                head.localScale = EnableHeadScaling ? new Vector3(0.0001f, 0.0001f, 0.0001f) : new Vector3(1.0f, 1.0f, 1.0f); // head bone is set to 0.0001 locally (not multiplied
            }
        }
        if (DisableMirrorAndShadowClones && (MirrorClone != null || ShadowClone != null)) {
            allMirrorTransforms = null;
            allShadowTransforms = null;
            GameObject.Destroy(MirrorClone.gameObject);
            MirrorClone = null;
            GameObject.Destroy(ShadowClone.gameObject);
            ShadowClone = null;
        }
        if (!DisableMirrorAndShadowClones && MirrorClone == null && ShadowClone == null) {
            CreateMirrorClone();
            CreateShadowClone();
        }
        if (emulator != null) {
            if (LastViewMirrorReflection != ViewMirrorReflection) {
                emulator.ViewMirrorReflection = ViewMirrorReflection;
            } else {
                ViewMirrorReflection = emulator.ViewMirrorReflection;
            }
            LastViewMirrorReflection = ViewMirrorReflection;
            if (LastViewBothRealAndMirror != ViewBothRealAndMirror) {
                emulator.ViewBothRealAndMirror = ViewBothRealAndMirror;
            } else {
                ViewBothRealAndMirror = emulator.ViewBothRealAndMirror;
            }
            LastViewBothRealAndMirror = ViewBothRealAndMirror;
            var osc = emulator.GetComponent<LyumaAv3Osc>();
            if (OSCController != null && EnableAvatarOSC && (!osc.openSocket || osc.avatarDescriptor != avadesc)) {
                EnableAvatarOSC = false;
                OSCController = null;
            }
            if (OSCController != null && !EnableAvatarOSC) {
                osc.openSocket = false;
                OSCController = null;
            }
            if (OSCController == null && EnableAvatarOSC) {
                osc = emulator.gameObject.GetOrAddComponent<LyumaAv3Osc>();
                osc.openSocket = true;
                osc.avatarDescriptor = avadesc;
                osc.enabled = true;
                OSCController = osc;
                // updateSelectionDelegate(osc.gameObject);
            }
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
        bool ShouldSyncThisFrame = (AvatarSyncSource != this && (Time.time >= nextUpdateTime || NonLocalSyncInterval <= 0.0f));
        if (AvatarSyncSource != this) {
            IKSyncRadialMenu = AvatarSyncSource.IKSyncRadialMenu;
            LyumaAv3Menu[] menus = AvatarSyncSource.GetComponents<LyumaAv3Menu>();
            for (int i = 0; i < Ints.Count; i++) {
                if (StageParamterToBuiltin.ContainsKey(Ints[i].stageName)) {
                    // Simulate IK sync of open gesture parameter.
                    if (ShouldSyncThisFrame || (IKSyncRadialMenu && menus.Length >= 1 && menus[0].IsControlIKSynced(Ints[i].name))
                            || (IKSyncRadialMenu && menus.Length >= 2 && menus[1].IsControlIKSynced(Ints[i].name))) {
                        Ints[i].value = ClampByte(AvatarSyncSource.Ints[i].value);
                    }
                }
            }
            for (int i = 0; i < Floats.Count; i++) {
                if (StageParamterToBuiltin.ContainsKey(Floats[i].stageName)) {
                    // Simulate IK sync of open gesture parameter.
                    if (ShouldSyncThisFrame || (IKSyncRadialMenu && menus.Length >= 1 && menus[0].IsControlIKSynced(Floats[i].name))
                            || (IKSyncRadialMenu && menus.Length >= 2 && menus[1].IsControlIKSynced(Floats[i].name))) {
                        Floats[i].exportedValue = ClampAndQuantizeFloat(AvatarSyncSource.Floats[i].exportedValue);
                        Floats[i].value = Floats[i].exportedValue;
                    }
                }
            }
            for (int i = 0; i < Bools.Count; i++) {
                if (StageParamterToBuiltin.ContainsKey(Bools[i].stageName)) {
                    if (ShouldSyncThisFrame) {
                        Bools[i].value = AvatarSyncSource.Bools[i].value;
                    }
                }
            }
            if (ShouldSyncThisFrame) {
                nextUpdateTime = Time.time + NonLocalSyncInterval;
            }
        }
        if (AvatarSyncSource != this) {
            // Simulate more continuous "IK sync" of these parameters.
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
            if (Floats[i].expressionValue != Floats[i].lastExpressionValue_) {
                Floats[i].exportedValue = Floats[i].expressionValue;
                Floats[i].lastExpressionValue_ = Floats[i].expressionValue;
            }
            if (StageParamterToBuiltin.ContainsKey(Floats[i].stageName)) {
                if (locally8bitQuantizedFloats) {
                    Floats[i].exportedValue = ClampAndQuantizeFloat(Floats[i].exportedValue);
                } else {
                    Floats[i].exportedValue = ClampFloatOnly(Floats[i].exportedValue);
                }
                Floats[i].value = Floats[i].exportedValue;
            }
        }
        for (int i = 0; i < Ints.Count; i++) {
            if (StageParamterToBuiltin.ContainsKey(Ints[i].stageName)) {
                Ints[i].value = ClampByte(Ints[i].value);
            }
        }
        if (Seated != PrevSeated && sittingIndex >= 0 && playableBlendingStates[sittingIndex] != null)
        {
            playableBlendingStates[sittingIndex].StartBlend(playableMixer.GetInputWeight(sittingIndex + 1), Seated ? 1f : 0f, 0.25f);
            PrevSeated = Seated;
        }
        if (TPoseCalibration != PrevTPoseCalibration && tposeIndex >= 0 && playableBlendingStates[tposeIndex] != null) {
            playableBlendingStates[tposeIndex].StartBlend(playableMixer.GetInputWeight(tposeIndex + 1), TPoseCalibration ? 1f : 0f, 0.0f);
            PrevTPoseCalibration = TPoseCalibration;
        }
        if (IKPoseCalibration != PrevIKPoseCalibration && ikposeIndex >= 0 && playableBlendingStates[ikposeIndex] != null) {
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
        if (GestureLeftWeight != OldGestureLeftWeight) {
            OldGestureLeftWeight = GestureLeftWeight;
            if (GestureLeftWeight < 0.01f) {
                GestureLeftIdx = 0;
            }
            if (GestureLeftWeight > 0.01f && (GestureLeftIdx == 0 || GestureLeftWeight < 0.99f)) {
                GestureLeftIdx = 1;
            } 
        }
        if (GestureRightWeight != OldGestureRightWeight) {
            OldGestureRightWeight = GestureRightWeight;
            if (GestureRightWeight < 0.01f) {
                GestureRightIdx = 0;
            }
            if (GestureRightWeight > 0.01f && (GestureRightIdx == 0 || GestureRightWeight < 0.99f)) {
                GestureRightIdx = 1;
            } 
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
                whichcontroller++;
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
                whichcontroller++;
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
                            if (!playable.IsParameterControlledByCurve(paramid)) {
                                param.exportedValue = param.value;
                            }
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

        if (((emulator != null && !emulator.DisableAvatarDynamicsIntegration)
            || (AvatarSyncSource?.emulator != null && !AvatarSyncSource.emulator.DisableAvatarDynamicsIntegration)) &&
            !IsMirrorClone && !IsShadowClone)
        {
            assignContactParameters(avadesc.gameObject.GetComponentsInChildren<VRCContactReceiver>());
            assignPhysBoneParameters(avadesc.gameObject.GetComponentsInChildren<VRCPhysBone>());
        }

        for (int i = 0; i < playableBlendingStates.Count; i++) {
            var pbs = playableBlendingStates[i];
            if (pbs == null) {
                continue;
            }
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

    float getObjectFloat(object o) {
        switch (o) {
            // case bool b:
            //     return b ? 1.0f : 0.0f;
            // case int i:
            //     return (float)i;
            // case long l:
            //     return (float)l;
            case float f:
                return f;
            // case double d:
            //     return (float)d;
        }
        return 0.0f;
    }
    int getObjectInt(object o) {
        switch (o) {
            // case bool b:
            //     return b ? 1 : 0;
            case int i:
                return i;
            // case long l:
            //     return (int)l;
            // case float f:
            //     return (int)f;
            // case double d:
            //     return (int)d;
        }
        return 0;
    }
    bool isObjectTrue(object o) {
        switch (o) {
            case bool b:
                return b;
            case int i:
                return i == 1;
            // case long l:
            //     return l == 1;
            // case float f:
            //     return f == 1.0f;
            // case double d:
            //     return d == 1.0;
        }
        return false;
    }

    public void GetOSCDataInto(List<A3ESimpleOSC.OSCMessage> messages) {
        messages.Add(new A3ESimpleOSC.OSCMessage {
            arguments = new object[1] {(object)OSCConfigurationFile.OSCAvatarID},
            path="/avatar/change",
            time = new Vector2Int(-1,-1),
        });
        if (OSCConfigurationFile.SendRecvAllParamsNotInJSON) {
            foreach (var b in Bools) {
                if (b.synced) {
                    messages.Add(new A3ESimpleOSC.OSCMessage {
                        arguments = new object[1] {(object)(int)((bool)b.value ? 1 : 0)},
                        path = "/avatar/parameters/" + b.name,
                        time = new Vector2Int(-1,-1),
                    });
                }
            }
            foreach (var i in Ints) {
                if (i.synced) {
                    messages.Add(new A3ESimpleOSC.OSCMessage {
                        arguments = new object[1] {(object)(int)i.value},
                        path = "/avatar/parameters/" + i.name,
                        time = new Vector2Int(-1,-1),
                    });
                }
            }
            foreach (var f in Floats) {
                if (f.synced) {
                    messages.Add(new A3ESimpleOSC.OSCMessage {
                        arguments = new object[1] {(object)(float)f.value},
                        path = "/avatar/parameters/" + f.name,
                        time = new Vector2Int(-1,-1),
                    });
                }
            }
        } else {
            foreach (var prop in OSCConfigurationFile.OSCJsonConfig.parameters) {
                if (prop.name != null && prop.name.Length > 0 && prop.output.address != null && prop.output.address.Length > 0) {
                    string addr = prop.output.address;
                    float outputf = 0.0f;
                    string typ = "?";
                    if (BoolToIndex.TryGetValue(prop.name, out var bidx)) {
                        if (!Bools[bidx].synced) {
                            continue;
                        }
                        outputf = Bools[bidx].value ? 1.0f : 0.0f;
                        typ = "bool";
                    } else if (IntToIndex.TryGetValue(prop.name, out var iidx)) {
                        if (!Ints[iidx].synced) {
                            continue;
                        }
                        outputf = (float)Ints[iidx].value;
                        typ = "int";
                    } else if (FloatToIndex.TryGetValue(prop.name, out var fidx)) {
                        if (!Floats[fidx].synced) {
                            continue;
                        }
                        outputf = Floats[fidx].value;
                        typ = "float";
                    } else {
                        switch (prop.name) {
                            case "VelocityZ":
                                outputf = Velocity.z;
                                break;
                            case "VelocityY":
                                outputf = Velocity.y;
                                break;
                            case "VelocityX":
                                outputf = Velocity.x;
                                break;
                            case "InStation":
                                outputf = InStation ? 1.0f : 0.0f;
                                break;
                            case "Seated":
                                outputf = Seated ? 1.0f : 0.0f;
                                break;
                            case "AFK":
                                outputf = AFK ? 1.0f : 0.0f;
                                break;
                            case "Upright":
                                outputf = Upright;
                                break;
                            case "AngularY":
                                outputf = AngularY;
                                break;
                            case "Grounded":
                                outputf = Grounded ? 1.0f : 0.0f;
                                break;
                            case "MuteSelf":
                                outputf = MuteSelf ? 1.0f : 0.0f;
                                break;
                            case "VRMode":
                                outputf = VRMode ? 1.0f : 0.0f;
                                break;
                            case "TrackingType":
                                outputf = TrackingTypeIdxInt;
                                break;
                            case "GestureRightWeight":
                                outputf = GestureRightWeight;
                                break;
                            case "GestureRight":
                                outputf = GestureRightIdxInt;
                                break;
                            case "GestureLeftWeight":
                                outputf = GestureLeftWeight;
                                break;
                            case "GestureLeft":
                                outputf = GestureLeftIdxInt;
                                break;
                            case "Voice":
                                outputf = Voice;
                                break;
                            case "Viseme":
                                outputf = VisemeInt;
                                break;
                            default:
                                Debug.LogWarning("Unrecognized built in param");
                                break;
                        }
                    }
                    object output;
                    switch (prop.output.type) {
                        case "Float":
                            output = (object)(float)outputf;
                            break;
                        case "Int":
                            output = (object)(int)outputf;
                            break;
                        case "Bool":
                            output = (object)(outputf != 0.0f);
                            break;
                        default:
                            Debug.LogError("Unrecognized JSON type " + prop.input.type + " for address " + addr + " for output " + typ + " parameter " +
                            prop.name + ". Should be \"Float\", \"Int\" or \"Bool\".");
                            continue;
                    }
                    messages.Add(new A3ESimpleOSC.OSCMessage {
                        arguments = new object[1] {(object)output},
                        path = addr,
                        time = new Vector2Int(-1,-1),
                    });
                }
            }
        }
    }
    public void processOSCInputMessage(string ParamName, object arg0) {
        float argFloat = getObjectFloat(arg0);
        int argInt = getObjectInt(arg0);
        bool argBool = isObjectTrue(arg0);
        switch (ParamName) {
        case "Vertical":
            Velocity.z = (3.0f + RunSpeed) * argFloat;
            break;
        case "Horizontal":
            Velocity.x = (3.0f + RunSpeed) * (float)arg0;
            break;
        case "LookHorizontal":
            AngularY = argFloat;
            break;
        case "UseAxisRight":
        case "GrabAxisRight":
        case "MoveHoldFB":
        case "SpinHoldCwCcw":
        case "SpinHoldUD":
        case "SpinHoldLR":
            break;
        case "MoveForward":
            Velocity.z = argBool ? 5.0f : 0.0f;
            break;
        case "MoveBackward":
            Velocity.z = argBool ? -5.0f : 0.0f;
            break;
        case "MoveLeft":
            Velocity.x = argBool ? -5.0f : 0.0f;
            break;
        case "MoveRight":
            Velocity.x = argBool ? 5.0f : 0.0f;
            break;
        case "LookLeft":
            AngularY = argBool ? -1.0f : 0.0f;
            break;
        case "LookRight":
            AngularY = argBool ? 1.0f : 0.0f;
            break;
        case "Jump":
            Jump = argBool;
            break;
        case "Run":
            RunSpeed = argBool ? 3.0f : 0.0f;
            break;
        case "ComfortLeft":
        case "ComfortRight":
        case "DropRight":
        case "UseRight":
        case "GrabRight":
        case "DropLeft":
        case "UseLeft":
        case "GrabLeft":
        case "PanicButton":
        case "QuickMenuToggleLeft":
        case "QuickMenuToggleRight":
            break;
        case "Voice":
            if (argBool && !MuteTogglerOn) {
                MuteSelf = !MuteSelf;
            }
            MuteTogglerOn = argBool;
            break;
        default:
            Debug.LogWarning("Unrecognized OSC input command " + ParamName);
            break;
        }
    }
    public void HandleOSCMessages(List<A3ESimpleOSC.OSCMessage> messages) {
        var innerProperties = new Dictionary<string, A3EOSCConfiguration.InnerJson>();
        foreach (var ij in OSCConfigurationFile.OSCJsonConfig.parameters) {
            if (ij.input.address != null && ij.input.address.Length > 0) {
                innerProperties[ij.input.address] = ij;
            }
        }
        foreach (var msg in messages) {
            string msgPath = msg.path;
            object[] arguments = msg.arguments;
            if (AvatarSyncSource != this || !IsLocal || IsMirrorClone || IsShadowClone) {
                return;
            }
            float argFloat = getObjectFloat(arguments[0]);
            int argInt = getObjectInt(arguments[0]);
            bool argBool = isObjectTrue(arguments[0]);
            if (msgPath.StartsWith("/input/")) {
                string ParamName = msgPath.Split(new char[]{'/'}, 3)[2];
                processOSCInputMessage(ParamName, arguments[0]);
            } else {
                string ParamName;
                if (OSCConfigurationFile.SendRecvAllParamsNotInJSON) {
                    ParamName = msgPath.Split(new char[]{'/'}, 4)[3];
                } else if (innerProperties.ContainsKey(msgPath)) {
                    ParamName = innerProperties[msgPath].name;
                    if (innerProperties[msgPath].input.type == "Float") {
                        if (arguments[0].GetType() != typeof(float)) {
                            Debug.LogWarning("Address " + msgPath + " for parameter " + ParamName + " expected float in JSON but received " + arguments[0].GetType());
                            continue;
                        }
                    } else if (innerProperties[msgPath].input.type == "Int" || innerProperties[msgPath].input.type == "Bool") {
                        if (arguments[0].GetType() != typeof(int) && arguments[0].GetType() != typeof(bool)) {
                            Debug.LogWarning("Address " + msgPath + " for parameter " + ParamName + " expected int/bool in JSON but received " + arguments[0].GetType());
                            continue;
                        }
                    } else {
                        Debug.LogError("Unrecognized JSON type " + innerProperties[msgPath].input.type + " for address " + msgPath + " for inupt parameter " +
                                ParamName + " but received " + arguments[0].GetType() + ". Should be \"Float\", \"Int\" or \"Bool\".");
                        continue;
                    }
                } else {
                    Debug.LogWarning("Address " + msgPath + " not found for input in JSON.");
                    continue;
                }
                if (OSCController != null && OSCController.debugPrintReceivedMessages) {
                    Debug.Log("Recvd "+ParamName + ": " + msg);
                }
                if (arguments.Length > 0 && arguments[0].GetType() == typeof(bool)) {
                    int idx;
                    if (BoolToIndex.TryGetValue(ParamName, out idx)) {
                        Bools[idx].value = (bool)(arguments[0]);
                    }
                    if (IntToIndex.TryGetValue(ParamName, out idx)) {
                        Ints[idx].value = (int)(arguments[0]);
                    }
                }
                if (arguments.Length > 0 && arguments[0].GetType() == typeof(int)) {
                    int idx;
                    if (BoolToIndex.TryGetValue(ParamName, out idx)) {
                        Bools[idx].value = ((int)(arguments[0])) != 0;
                    }
                    if (IntToIndex.TryGetValue(ParamName, out idx)) {
                        Ints[idx].value = (int)(arguments[0]);
                    }
                }
                if (arguments.Length > 0 && arguments[0].GetType() == typeof(float)) {
                    int idx;
                    if (FloatToIndex.TryGetValue(ParamName, out idx)) {
                        Floats[idx].value = (float)(arguments[0]);
                        Floats[idx].exportedValue = Floats[idx].value;
                    }
                }
            }
        }
    }
}
