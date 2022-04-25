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
using UnityEngine;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;

[RequireComponent(typeof(Animator))]
public class LyumaAv3Emulator : MonoBehaviour
{
    static readonly ulong EMULATOR_VERSION = 0x2_09_08_00;

    public bool DefaultToVR = false;
    public bool DefaultTestInStation = false;
    public LyumaAv3Runtime.TrackingTypeIndex DefaultTrackingType = LyumaAv3Runtime.TrackingTypeIndex.HeadHands;
    public VRCAvatarDescriptor.AnimLayerType DefaultAnimatorToDebug = VRCAvatarDescriptor.AnimLayerType.Base;
    public bool RestartEmulator;
    private bool RestartingEmulator;
    public bool CreateNonLocalClone;
    public int CreateNonLocalCloneCount;
    [Tooltip("Simulate behavior with sub-animator parameter drivers prior to the 2021.1.1 patch (19 Jan 2021)")]
    public bool legacySubAnimatorParameterDriverMode;
    public bool legacyMenuGUI = true;
    private bool lastLegacyMenuGUI = true;
    public bool DisableAvatarDynamicsIntegration;
    public bool WorkaroundPlayModeScriptCompile = true;
    public bool DisableMirrorClone;
    public bool DisableShadowClone;
    private bool lastHead;
    public bool EnableHeadScaling;
    public bool ViewMirrorReflection;
    public bool ViewBothRealAndMirror;

    static public LyumaAv3Emulator emulatorInstance;
    static public RuntimeAnimatorController EmptyController;

    public List<LyumaAv3Runtime> runtimes = new List<LyumaAv3Runtime>();

    private void Awake()
    {
        Animator animator = gameObject.GetOrAddComponent<Animator>();
        animator.enabled = false;
        animator.runtimeAnimatorController = EmptyController;
        emulatorInstance = this;
        VRCAvatarDescriptor[] avatars = FindObjectsOfType<VRCAvatarDescriptor>();
        Debug.Log(this.name + ": Setting up Av3Emulator on "+avatars.Length + " avatars.", this);
        foreach (var avadesc in avatars)
        {
            if (avadesc.GetComponent<PipelineSaver>() != null) {
                Debug.Log("Found PipelineSaver on " + avadesc.name + ". Disabling clones and mirror copy.", avadesc);
                DisableMirrorClone = true;
                DisableShadowClone = true;
                CreateNonLocalClone = false;
                EnableHeadScaling = false;
            }
            try {
                // Creates the playable director, and initializes animator.
                var oml = avadesc.gameObject.GetOrAddComponent<UnityEngine.AI.OffMeshLink>();
                oml.startTransform = this.transform;
                bool alreadyHadComponent = avadesc.gameObject.GetComponent<LyumaAv3Runtime>() != null;
                var runtime = avadesc.gameObject.GetOrAddComponent<LyumaAv3Runtime>();
                if (oml != null) {
                    GameObject.DestroyImmediate(oml);
                }
                runtime.emulator = this;
                runtime.VRMode = DefaultToVR;
                runtime.TrackingType = DefaultTrackingType;
                runtime.InStation = DefaultTestInStation;
                runtime.DebugDuplicateAnimator = DefaultAnimatorToDebug;
                runtime.EnableHeadScaling = EnableHeadScaling;
                runtimes.Add(runtime);
                if (!alreadyHadComponent && !DisableShadowClone) {
                    runtime.CreateShadowClone();
                }
                if (!alreadyHadComponent && !DisableMirrorClone) {
                    runtime.CreateMirrorClone();
                }
                runtime.DisableMirrorAndShadowClones = DisableShadowClone && DisableMirrorClone;
            } catch (System.Exception e) {
                Debug.LogException(e);
            }
        }
        if (WorkaroundPlayModeScriptCompile) {
            LyumaAv3Runtime.ApplyOnEnableWorkaroundDelegate();
        }
    }

    private void OnDestroy() {
        foreach (var runtime in runtimes) {
            Destroy(runtime);
        }
        runtimes.Clear();
        LyumaAv3Runtime.updateSceneLayersDelegate(~0);
    }

    private void Update() {
        if (RestartingEmulator) {
            RestartingEmulator = false;
            Awake();
        } else if (RestartEmulator) {
            RestartEmulator = false;
            OnDestroy();
            RestartingEmulator = true;
        }
        if (ViewBothRealAndMirror) {
            LyumaAv3Runtime.updateSceneLayersDelegate(~0);
        } else if (ViewMirrorReflection && !ViewBothRealAndMirror) {
            LyumaAv3Runtime.updateSceneLayersDelegate(~(1<<10));
        } else if (!ViewMirrorReflection && !ViewBothRealAndMirror) {
            LyumaAv3Runtime.updateSceneLayersDelegate(~(1<<18));
        }
        if (EnableHeadScaling != lastHead) {
            lastHead = EnableHeadScaling;
            foreach (var runtime in runtimes) {
                runtime.EnableHeadScaling = EnableHeadScaling;
            }
        }
        if (lastLegacyMenuGUI != legacyMenuGUI) {
            lastLegacyMenuGUI = legacyMenuGUI;
            foreach (var runtime in runtimes) {
                runtime.legacyMenuGUI = legacyMenuGUI;
            }
        }
        if (CreateNonLocalClone) {
            CreateNonLocalCloneCount -= 1;
            if (CreateNonLocalCloneCount <= 0) {
                CreateNonLocalClone = false;
            }
            foreach (var runtime in runtimes)
            {
                if (runtime.AvatarSyncSource == runtime)
                {
                    runtime.CreateNonLocalClone = true;
                }
            }
        }
    }

}
