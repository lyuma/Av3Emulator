/* Copyright (c) 2020 Lyuma <xn.lyuma@gmail.com>

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
using VRC.SDK3.Components;
using VRC.SDK3.ScriptableObjects;

[RequireComponent(typeof(Animator))]
public class LyumaAv3Runtime : MonoBehaviour
{
    static public Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController> animLayerToDefaultController = new Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController>();
    static public Dictionary<VRCAvatarDescriptor.AnimLayerType, AvatarMask> animLayerToDefaultAvaMask = new Dictionary<VRCAvatarDescriptor.AnimLayerType, AvatarMask>();

    [HideInInspector] public string SourceObjectPath;
    [Header("Assign to non-local duplicate")]public LyumaAv3Runtime AvatarSyncSource;
    private int CloneCount;
    public bool CreateNonLocalClone;
    VRCAvatarDescriptor avadesc;
    Animator animator;
    private RuntimeAnimatorController origAnimatorController;

    private List<AnimatorControllerPlayable> playables = new List<AnimatorControllerPlayable>();
    private List<Dictionary<string, int>> playableParamterIds = new List<Dictionary<string, int>>();
    private List<Dictionary<int, float>> playableParamterFloats = new List<Dictionary<int, float>>();
    private List<Dictionary<int, int>> playableParamterInts = new List<Dictionary<int, int>>();
    AnimationLayerMixerPlayable playableMixer;
    PlayableGraph playableGraph;
    VRCExpressionsMenu expressionsMenu;
    VRCStageParameters stageParameters;
    int sittingIndex;
    int fxIndex;
    int actionIndex;
    int additiveIndex;
    int gestureIndex;

    public static float ClampFloat(float val) {
        if (val < -1.0f) {
            val = -1.0f;
        }
        if (val > 1.0f) {
            val = 1.0f;
        }
        if (val > 0.0f) {
            val *= 128f / 127; // apply bias.
        }
        val = (((sbyte)((val) * -127.0f)) / -127.0f);
        if (val > 1.0f)
        {
            val = 1.0f;
        }
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
    static HashSet<string> BUILTIN_PARAMETERS = new HashSet<string> {
        "Viseme", "GestureLeft", "GestureLeftWeight", "GestureRight", "GestureRightWeight", "VelocityX", "VelocityY", "VelocityZ", "LocomotionMode", "Upright", "AngularY", "GroundProximity"
    };
    [Header("Built-in locomotion inputs")]
    public int VisemeI;
    public VisemeIndex VisemeDD;
    private int Viseme;
    public GestureIndex GestureLeft;
    [Range(0, 1)] public float GestureLeftWeight;
    public GestureIndex GestureRight;
    [Range(0, 1)] public float GestureRightWeight;
    public Vector3 Velocity;
    [Range(-1, 1)] public float AngularY; // Not documented
    [Range(-1, 1)] public float Upright; // Not documented
    [Range(-1, 1)] public float GroundProximity; // Not documented
    public int LocomotionMode; // Not documented
    public bool Grounded;
    private bool PrevSeated;
    public bool Seated;
    public bool AFK;
    //TODO:
    public bool Supine; // Not documented
    public bool FootstepDisable; // Not documented

    [Header("Output State (Read-only)")]
    public bool IsLocal;
    public bool LocomotionIsDisabled;
    private Vector3 HeadRelativeViewPosition;
    public Vector3 ViewPosition;
    public VRCAnimatorTrackingControl.TrackingType trackingRightFingers;
    public VRCAnimatorTrackingControl.TrackingType trackingLeftFingers;
    public VRCAnimatorTrackingControl.TrackingType trackingEyes;
    public VRCAnimatorTrackingControl.TrackingType trackingLeftFoot;
    public VRCAnimatorTrackingControl.TrackingType trackingHip;
    public VRCAnimatorTrackingControl.TrackingType trackingRightHand;
    public VRCAnimatorTrackingControl.TrackingType trackingLeftHand;
    public VRCAnimatorTrackingControl.TrackingType trackingRightFoot;

    [Serializable]
    public class FloatParam
    {
        public string name;
        public bool synced;
        [Range(-1, 1)] public float value;
    }
    [Header("User-generated inputs")]
    public List<FloatParam> Floats = new List<FloatParam>();
    public Dictionary<string, int> FloatToIndex = new Dictionary<string, int>();

    [Serializable]
    public class IntParam
    {
        public string name;
        public bool synced;
        public int value;
    }
    public List<IntParam> Ints = new List<IntParam>();
    public Dictionary<string, int> IntToIndex = new Dictionary<string, int>();

    static public Dictionary<Animator, LyumaAv3Runtime> animatorToTopLevelRuntime = new Dictionary<Animator, LyumaAv3Runtime>();
    private List<Animator> attachedAnimators = new List<Animator>();

    public IEnumerator DelayedSetView(bool setView, float time) {
        yield return new WaitForSeconds(time);
        if (setView) {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            ViewPosition = animator.transform.InverseTransformPoint(head.TransformPoint(HeadRelativeViewPosition));
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

    static LyumaAv3Runtime () {
        VRCAvatarParameterDriver.Initialize += (x) => {
            x.ApplySettings += (VRC.SDKBase.VRC_AvatarParameterDriver behaviour, Animator animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!animatorToTopLevelRuntime.TryGetValue(animator, out runtime)) {
                    Debug.LogError("[VRCAvatarParameterDriver: animator is not known: " + animator, animator);
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
                foreach (var parameter in behaviour.parameters)
                {
                    int idx;
                    if (runtime.IntToIndex.TryGetValue(parameter.name, out idx)) {
                        runtime.Ints[idx].value = (int)parameter.value;
                    }
                    if (runtime.FloatToIndex.TryGetValue(parameter.name, out idx)) {
                        runtime.Floats[idx].value = parameter.value;
                    }
                }
            };
        };
        VRCPlayableLayerControl.Initialize += (x) => {
            x.ApplySettings += (VRC.SDKBase.VRC_PlayableLayerControl behaviour, Animator animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!animatorToTopLevelRuntime.TryGetValue(animator, out runtime)) {
                    Debug.LogError("[VRCPlayableLayerControl: animator is not known: " + animator, animator);
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
                    runtime.playableBlendingStates[idx].StartBlend(runtime.playableMixer.GetInputWeight(idx), behaviour.goalWeight, behaviour.blendDuration);
                }
            };
        };
        VRCAnimatorLayerControl.Initialize += (x) => {
            x.ApplySettings += (VRC.SDKBase.VRC_AnimatorLayerControl behaviour, Animator animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!animatorToTopLevelRuntime.TryGetValue(animator, out runtime)) {
                    Debug.LogError("[VRCAnimatorLayerControl: animator is not known: " + animator, animator);
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
                int idx = -1;
                switch (behaviour.playable)
                {
                    case VRCAnimatorLayerControl.BlendableLayer.Action:
                        idx = runtime.actionIndex;
                        break;
                    case VRCAnimatorLayerControl.BlendableLayer.Additive:
                        idx = runtime.additiveIndex;
                        break;
                    case VRCAnimatorLayerControl.BlendableLayer.FX:
                        idx = runtime.fxIndex;
                        break;
                    case VRCAnimatorLayerControl.BlendableLayer.Gesture:
                        idx = runtime.gestureIndex;
                        break;
                }
                if (idx >= 0 && idx < runtime.playableBlendingStates.Count)
                {
                    if (behaviour.layer >= 0 && behaviour.layer < runtime.playableBlendingStates[idx].layerBlends.Count)
                    {
                        runtime.playableBlendingStates[idx].layerBlends[behaviour.layer].StartBlend(runtime.playables[idx].GetLayerWeight(behaviour.layer), behaviour.goalWeight, behaviour.blendDuration);
                    }
                }
            };
        };
        VRCAnimatorLocomotionControl.Initialize += (x) => {
            x.ApplySettings += (VRC.SDKBase.VRC_AnimatorLocomotionControl behaviour, Animator animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!animatorToTopLevelRuntime.TryGetValue(animator, out runtime)) {
                    Debug.LogError("[VRCAnimatorLocomotionControl: animator is not known: " + animator, animator);
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
        VRCAnimatorSetView.Initialize += (x) => {
            x.ApplySettings += (VRC.SDKBase.VRC_AnimatorSetView behaviour, Animator animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!animatorToTopLevelRuntime.TryGetValue(animator, out runtime)) {
                    Debug.LogError("[VRCAnimatorSetView: animator is not known: " + animator, animator);
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
                runtime.StartCoroutine(runtime.DelayedSetView(behaviour.setView, behaviour.delayTime));
            };
        };
        VRCAnimatorTrackingControl.Initialize += (x) => {
            x.ApplySettings += (VRC.SDKBase.VRC_AnimatorTrackingControl behaviour, Animator animator) =>
            {
                LyumaAv3Runtime runtime;
                if (!animatorToTopLevelRuntime.TryGetValue(animator, out runtime)) {
                    Debug.LogError("[VRCAnimatorTrackingControl: animator is not known: " + animator, animator);
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

                if (behaviour.trackingRightFingers != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingRightFingers = behaviour.trackingRightFingers;
                }
                if (behaviour.trackingEyes != VRCAnimatorTrackingControl.TrackingType.NoChange)
                {
                    runtime.trackingEyes = behaviour.trackingEyes;
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
        this.playableGraph.Destroy();
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

        animator = this.gameObject.GetComponent<Animator>();
        avadesc = this.gameObject.GetComponent<VRCAvatarDescriptor>();
        ViewPosition = avadesc.ViewPosition;
        HeadRelativeViewPosition = ViewPosition;
        if (animator.avatar != null)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);;
            HeadRelativeViewPosition = head.InverseTransformPoint(animator.transform.TransformPoint(ViewPosition));
        }
        expressionsMenu = avadesc.expressionsMenu;
        if (expressionsMenu != null)
        {
            stageParameters = expressionsMenu.stageParameters;
        }
        origAnimatorController = animator.runtimeAnimatorController;
        animator.runtimeAnimatorController = null;
        if (animator.playableGraph.IsValid())
        {
            animator.playableGraph.Destroy();
        }
        animator.applyRootMotion = false;

        VRCAvatarDescriptor.CustomAnimLayer[] baselayers = avadesc.baseAnimationLayers;
        VRCAvatarDescriptor.CustomAnimLayer[] speciallayers = avadesc.specialAnimationLayers;
        List<VRCAvatarDescriptor.CustomAnimLayer> allLayers = new List<VRCAvatarDescriptor.CustomAnimLayer>();
        allLayers.AddRange(baselayers);
        allLayers.AddRange(speciallayers);

        // var director = avadesc.gameObject.GetComponent<PlayableDirector>();
        playableGraph = PlayableGraph.Create("LyumaAvatarRuntime - " + this.gameObject.name);
        var externalOutput = AnimationPlayableOutput.Create(playableGraph, "ExternalAnimator", animator);
        playableMixer = AnimationLayerMixerPlayable.Create(playableGraph, allLayers.Count + 1);
        externalOutput.SetSourcePlayable(playableMixer);
        int i;
        playables.Clear();
        playableBlendingStates.Clear();
        actionIndex = fxIndex = gestureIndex = additiveIndex = sittingIndex = -1;

        Animator[] animators = this.gameObject.GetComponentsInChildren<Animator>();
        Debug.Log("anim len "+animators.Length);
        foreach (Animator anim in animators)
        {
            attachedAnimators.Add(anim);
            animatorToTopLevelRuntime.Add(anim, this);
        }

        // Default values.
        Grounded = true;
        Upright = 1.0f;

        Ints.Clear();
        Floats.Clear();
        HashSet<string> usedparams = new HashSet<string>(BUILTIN_PARAMETERS);
        i = 0;
        if (stageParameters != null)
        {
            foreach (var stageParam in stageParameters.stageParameters)
            {
                if (stageParam.name == null || stageParam.name.Length == 0) {
                    continue;
                }
                if ((int)stageParam.valueType == 0)
                {
                    IntParam param = new IntParam();
                    param.synced = true;
                    param.name = stageParam.name;
                    param.value = 0;
                    IntToIndex[param.name] = Ints.Count;
                    Ints.Add(param);
                }
                else
                {
                    FloatParam param = new FloatParam();
                    param.synced = true;
                    param.name = stageParam.name;
                    param.value = 0;
                    FloatToIndex[param.name] = Floats.Count;
                    Floats.Add(param);
                }
                usedparams.Add(stageParam.name);
                i++;
            }
        }

        i = 0;
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
            }
            if (ac == null) {
                // i is not incremented.
                continue;
            }

            AnimatorControllerPlayable humanAnimatorPlayable = AnimatorControllerPlayable.Create(playableGraph, ac);
            PlayableBlendingState pbs = new PlayableBlendingState();
            for (int j = 0; j < humanAnimatorPlayable.GetLayerCount(); j++)
            {
                humanAnimatorPlayable.SetLayerWeight(j, 1f);
                pbs.layerBlends.Add(new BlendingState());
            }
            playableMixer.ConnectInput((int)i, humanAnimatorPlayable, 0, 1);
            playables.Add(humanAnimatorPlayable);
            playableBlendingStates.Add(pbs);
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Sitting) {
                sittingIndex = i;
                playableMixer.SetInputWeight(i, 0f);
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.IKPose)
            {
                playableMixer.SetInputWeight(i, 0f);
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.TPose)
            {
                playableMixer.SetInputWeight(i, 0f);
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Action)
            {
                playableMixer.SetInputWeight(i, 0f);
                actionIndex = i;
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Gesture)
            {
                gestureIndex = i;
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Additive)
            {
                additiveIndex = i;
            }
            if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.FX)
            {
                fxIndex = i;
            }
            // AnimationControllerLayer acLayer = new AnimationControllerLayer()
            if (mask != null)
            {
                playableMixer.SetLayerMaskFromAvatarMask((uint)i, mask);
            }
            if (additive)
            {
                playableMixer.SetLayerAdditive((uint)i, true);
            }
        }

        //playableParamterIds
        int whichcontroller = 0;
        playableParamterIds.Clear();
        foreach (AnimatorControllerPlayable playable in playables) {
            int pcnt = playable.GetParameterCount();
            Dictionary<string, int> parameterIndices = new Dictionary<string, int>();
            playableParamterInts.Add(new Dictionary<int, int>());
            playableParamterFloats.Add(new Dictionary<int, float>());
            // Debug.Log("SETUP index " + whichcontroller + " len " + playables.Count);
            playableParamterIds.Add(parameterIndices);
            for (i = 0; i < pcnt; i++) {
                AnimatorControllerParameter aparam = playable.GetParameter(i);
                parameterIndices[aparam.name] = aparam.nameHash;
                if (usedparams.Contains(aparam.name)) {
                    continue;
                }
                if (aparam.type == AnimatorControllerParameterType.Int) {
                    IntParam param = new IntParam();
                    param.synced = false;
                    param.name = aparam.name;
                    param.value = aparam.defaultInt;
                    IntToIndex[param.name] = Ints.Count;
                    Ints.Add(param);
                    usedparams.Add(aparam.name);
                } else if (aparam.type == AnimatorControllerParameterType.Float) {
                    FloatParam param = new FloatParam();
                    param.synced = false;
                    param.name = aparam.name;
                    param.value = aparam.defaultFloat;
                    FloatToIndex[param.name] = Floats.Count;
                    Floats.Add(param);
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

    // Update is called once per frame
    void Update()
    {
        if (CreateNonLocalClone) {
            CreateNonLocalClone = false;
            GameObject go = GameObject.Instantiate(AvatarSyncSource.gameObject);
            AvatarSyncSource.CloneCount++;
            go.name = go.name.Substring(0, go.name.Length - 7) + " (Non-Local " + AvatarSyncSource.CloneCount + ")";
            go.transform.position = go.transform.position + AvatarSyncSource.CloneCount * new Vector3(0.4f, 0.0f, 0.4f);
        }
        if (AvatarSyncSource != this) {
            for (int i = 0; i < Ints.Count; i++) {
                if (Ints[i].synced) {
                    Ints[i].value = ClampByte(AvatarSyncSource.Ints[i].value);
                }
            }
            for (int i = 0; i < Floats.Count; i++) {
                if (Floats[i].synced) {
                    Floats[i].value = ClampFloat(AvatarSyncSource.Floats[i].value);
                }
            }
            Viseme = VisemeI = AvatarSyncSource.Viseme;
            VisemeDD = (VisemeIndex)Viseme;
            GestureLeft = AvatarSyncSource.GestureLeft;
            GestureLeftWeight = AvatarSyncSource.GestureLeftWeight;
            GestureRight = AvatarSyncSource.GestureRight;
            GestureRightWeight = AvatarSyncSource.GestureRightWeight;
            Velocity = AvatarSyncSource.Velocity;
            AngularY = AvatarSyncSource.AngularY;
            Upright = AvatarSyncSource.Upright;
            LocomotionMode = AvatarSyncSource.LocomotionMode;
            GroundProximity = AvatarSyncSource.GroundProximity;
            Grounded = AvatarSyncSource.Grounded;
            Seated = AvatarSyncSource.Seated;
            AFK = AvatarSyncSource.AFK;
            Supine = AvatarSyncSource.Supine;
            FootstepDisable = AvatarSyncSource.FootstepDisable;
        }
        foreach (FloatParam param in Floats)
        {
            param.value = ClampFloat(param.value);
        }
        foreach (IntParam param in Ints)
        {
            param.value = ClampByte(param.value);
        }
        if (Seated != PrevSeated && sittingIndex >= 0)
        {
            playableBlendingStates[sittingIndex].StartBlend(playableMixer.GetInputWeight(sittingIndex), Seated ? 1f : 0f, 0.5f);
            PrevSeated = Seated;
        }
        if (VisemeI != Viseme) {
            Viseme = VisemeI;
            VisemeDD = (VisemeIndex)Viseme;
        }
        if ((int)VisemeDD != Viseme) {
            Viseme = (int)VisemeDD;
            VisemeI = Viseme;
        }
        IsLocal = AvatarSyncSource == this;
        int whichcontroller = 0;
        foreach (AnimatorControllerPlayable playable in playables)
        {
            // Debug.Log("Index " + whichcontroller + " len " + playables.Count);
            Dictionary<string, int> parameterIndices = playableParamterIds[whichcontroller];
            Dictionary<int, int> paramterInts = playableParamterInts[whichcontroller];
            Dictionary<int, float> paramterFloats = playableParamterFloats[whichcontroller];
            int paramid;
            float fparam;
            int iparam;
            foreach (FloatParam param in Floats)
            {
                if (parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                        // Debug.Log("Reflecting change in float parameter " + param.name + " from " + param.value + " to " + playable.GetFloat(paramid));
                        param.value = playable.GetFloat(paramid);
                    }
                    playable.SetFloat(paramid, param.value);
                    paramterFloats[paramid] = param.value;
                }
            }
            foreach (IntParam param in Ints)
            {
                if (parameterIndices.TryGetValue(param.name, out paramid))
                {
                    if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                        // Debug.Log("Reflecting change in int parameter " + param.name + " from " + param.value + " to " + playable.GetFloat(paramid));
                        param.value = playable.GetInteger(paramid);
                    }
                    playable.SetInteger(param.name, param.value);
                    paramterInts[paramid] = param.value;
                }
            }
            if (parameterIndices.TryGetValue("Viseme", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    Viseme = VisemeI = playable.GetInteger(paramid);
                    VisemeDD = (VisemeIndex)Viseme;
                }
                playable.SetInteger(paramid, Viseme);
                paramterInts[paramid] = Viseme;
            }
            if (parameterIndices.TryGetValue("GestureLeft", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    GestureLeft = (GestureIndex)playable.GetInteger(paramid);
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
                    GestureRight = (GestureIndex)playable.GetInteger(paramid);
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
            if (parameterIndices.TryGetValue("GroundProximity", out paramid))
            {
                if (paramterFloats.TryGetValue(paramid, out fparam) && fparam != playable.GetFloat(paramid)) {
                    GroundProximity = playable.GetFloat(paramid);
                }
                playable.SetFloat(paramid, GroundProximity);
                paramterFloats[paramid] = GroundProximity;
            }
            if (parameterIndices.TryGetValue("LocomotionMode", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != playable.GetInteger(paramid)) {
                    LocomotionMode = playable.GetInteger(paramid);
                }
                playable.SetInteger(paramid, LocomotionMode);
                paramterInts[paramid] = LocomotionMode;
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
            if (parameterIndices.TryGetValue("Supine", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != (playable.GetBool(paramid) ? 1 : 0)) {
                    Supine = playable.GetBool(paramid);
                }
                playable.SetBool(paramid, Supine);
                paramterInts[paramid] = Supine ? 1 : 0;
            }
            if (parameterIndices.TryGetValue("FootstepDisable", out paramid))
            {
                if (paramterInts.TryGetValue(paramid, out iparam) && iparam != (playable.GetBool(paramid) ? 1 : 0)) {
                    FootstepDisable = playable.GetBool(paramid);
                }
                playable.SetBool(paramid, FootstepDisable);
                paramterInts[paramid] = FootstepDisable ? 1 : 0;
            }
            whichcontroller++;
        }
        for (int i = 0; i < playableBlendingStates.Count; i++) {
            var pbs = playableBlendingStates[i];
            if (pbs.blending) {
                float newWeight = pbs.UpdateBlending();
                playableMixer.SetInputWeight(i, newWeight);
            }
            for (int j = 0; j < pbs.layerBlends.Count; j++) {
                if (pbs.layerBlends[j].blending) {
                    float newWeight = pbs.layerBlends[j].UpdateBlending();
                    playables[i].SetLayerWeight(j, newWeight);
                }
            }
        }
    }
}
