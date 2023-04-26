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
using System.Linq;
using VRC.SDK3.Avatars.Components;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Lyuma.Av3Emulator.Runtime
{
	[RequireComponent(typeof(Animator))]
	public class LyumaAv3Emulator : MonoBehaviour
	{
		static readonly ulong EMULATOR_VERSION = 0x3_01_03_00;

		[Header("Fake VR or Desktop mode selection")]
		public bool DefaultToVR = false;
		public DefaultPoseOptions DefaultPose = DefaultPoseOptions.Standing;
		public LyumaAv3Runtime.TrackingTypeIndex DefaultTrackingType = LyumaAv3Runtime.TrackingTypeIndex.HeadHands;
		[Header("Emulation")]
		public VRCAvatarDescriptor.AnimLayerType DefaultAnimatorToDebug = VRCAvatarDescriptor.AnimLayerType.Base; 
		public DescriptorCollidersSendersHelper.DescriptorExtractionType DescriptorColliders = DescriptorCollidersSendersHelper.DescriptorExtractionType.CollidersAndSenders;
		public bool RestartEmulator;
		private bool RestartingEmulator;
		[Tooltip("Simulate behavior with sub-animator parameter drivers prior to the 2021.1.1 patch (19 Jan 2021)")]
		public bool legacySubAnimatorParameterDriverMode = false;
		public bool disableRadialMenu = false;
		private bool lastDisableRadialMenu = false;
		[Header("Unity Integrations")]
		public bool RunPreprocessAvatarHook = false;
		public bool DisableAvatarDynamicsIntegration;
		public bool WorkaroundPlayModeScriptCompile = true;
		[Header("Networked and mirror clone emulation")]
		public bool CreateNonLocalClone;
		public int CreateNonLocalCloneCount;
		public bool DisableMirrorClone;
		public bool DisableShadowClone;
		private bool lastHead;
		public bool EnableHeadScaling;
		[Header("Clone visualization and position offset")]
		public bool ViewMirrorReflection;
		public bool ViewBothRealAndMirror;
		public bool VisuallyOffsetClonesOnly = true;
		public bool ApplyClonePositionOffset = false;

		static public LyumaAv3Emulator emulatorInstance;
		static public RuntimeAnimatorController EmptyController;

		public List<LyumaAv3Runtime> runtimes = new List<LyumaAv3Runtime>();
		public LinkedList<LyumaAv3Runtime> forceActiveRuntimes = new LinkedList<LyumaAv3Runtime>();
		public HashSet<VRCAvatarDescriptor> scannedAvatars = new HashSet<VRCAvatarDescriptor>();

		public enum DefaultPoseOptions
		{
			Standing,
			TPose,
			IKPose,
			AFK,
			InStationAndSeated,
			InStation
		}

		private void PreCull(Camera cam) {
			if (ApplyClonePositionOffset || VisuallyOffsetClonesOnly) {
				foreach (var runtime in runtimes) {
					if (runtime != null) {
						runtime.PreCullVisualOffset(cam);
						if (runtime.MirrorClone != null) {
							Vector3 oldOffset = runtime.MirrorClone.VisualOffset;
							if (ViewBothRealAndMirror) {
								if (runtime.MirrorClone.VisualOffset == new Vector3()) {
									runtime.MirrorClone.VisualOffset = new Vector3(0, 0.5f + runtime.IKTrackingOutputData.ViewPosition.y * 1.01f, 0);
								}
							}
							runtime.MirrorClone.PreCullVisualOffset(cam);
							runtime.MirrorClone.VisualOffset = oldOffset;
						}
						if (runtime.ShadowClone != null) {
							runtime.ShadowClone.PreCullVisualOffset(cam);
						}
					}
					if (runtime != null && runtime.NonLocalClones != null) {
						foreach (LyumaAv3Runtime nonLocalClone in runtime.NonLocalClones) {
							if (nonLocalClone != null) {
								nonLocalClone.PreCullVisualOffset(cam);
							}
						}
					}
				}
			}
		}
		private void PostRender(Camera cam) {
			if (!ApplyClonePositionOffset) {
				foreach (var runtime in runtimes) {
					if (runtime != null) {
						runtime.PostRenderVisualOffset(cam);
						if (runtime.MirrorClone != null) {
							runtime.MirrorClone.PostRenderVisualOffset(cam);
						}
						if (runtime.ShadowClone != null) {
							runtime.ShadowClone.PostRenderVisualOffset(cam);
						}
					}
					if (runtime != null && runtime.NonLocalClones != null) {
						foreach (LyumaAv3Runtime nonLocalClone in runtime.NonLocalClones) {
							if (nonLocalClone != null) {
								nonLocalClone.PostRenderVisualOffset(cam);
							}
						}
					}
				}
			}
		}

		private void Awake()
		{
			Camera.onPreCull += PreCull;
			Camera.onPostRender += PostRender;
			Animator animator = gameObject.GetOrAddComponent<Animator>();
			animator.enabled = false;
			animator.runtimeAnimatorController = EmptyController;
			emulatorInstance = this;
			ScanForAvatars();
			if (WorkaroundPlayModeScriptCompile) {
				LyumaAv3Runtime.ApplyOnEnableWorkaroundDelegate();
			}

			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
			ScanForAvatars();
		}

		private void ScanForAvatars() {
			VRCAvatarDescriptor[] avatars = FindObjectsOfType<VRCAvatarDescriptor>()
				.Where(avatar => !scannedAvatars.Contains(avatar))
				.ToArray();
			scannedAvatars.UnionWith(avatars);
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
					bool alreadyHadComponent = avadesc.gameObject.GetComponent<LyumaAv3Runtime>() != null;
					if (RunPreprocessAvatarHook && !alreadyHadComponent) {
						LyumaAv3Runtime.InvokeOnPreProcessAvatar(avadesc.gameObject);
						avadesc.gameObject.SetActive(true);
					}

					var oml = avadesc.gameObject.GetOrAddComponent<UnityEngine.AI.OffMeshLink>();
					oml.startTransform = this.transform;
					var runtime = avadesc.gameObject.GetOrAddComponent<LyumaAv3Runtime>();
					if (RunPreprocessAvatarHook && !alreadyHadComponent) {
						forceActiveRuntimes.AddLast(runtime);
					}
					if (oml != null) {
						GameObject.DestroyImmediate(oml);
					}

					switch (DefaultPose)
					{
						case DefaultPoseOptions.TPose:
							runtime.TPoseCalibration = true;
							break;
						case DefaultPoseOptions.IKPose:
							runtime.IKPoseCalibration = true;
							break;
						case DefaultPoseOptions.AFK:
							runtime.AFK = true;
							break;
						case DefaultPoseOptions.InStationAndSeated:
							runtime.Seated = true;
							runtime.InStation = true;
							break;
						case DefaultPoseOptions.InStation:
							runtime.InStation = true;
							break;
					}

					runtime.emulator = this;
					runtime.VRMode = DefaultToVR;
					runtime.TrackingType = DefaultTrackingType;
					runtime.DebugDuplicateAnimator = DefaultAnimatorToDebug;
					runtime.EnableHeadScaling = EnableHeadScaling;
					runtimes.Add(runtime);
					if (!alreadyHadComponent && !DisableShadowClone) {
						runtime.CreateShadowClone();
					}
					if (!alreadyHadComponent && !DisableMirrorClone) {
						runtime.CreateMirrorClone();
					}

					if (!alreadyHadComponent && (!DisableMirrorClone || !DisableShadowClone))
					{
						runtime.SetupCloneCaches();
					}
					runtime.DisableMirrorAndShadowClones = DisableShadowClone && DisableMirrorClone;

				} catch (System.Exception e) {
					Debug.LogException(e);
				}
			}
		}

		private void OnDestroy() {
			Camera.onPreCull -= PreCull;
			Camera.onPostRender -= PostRender;
			foreach (var runtime in runtimes) {
				Destroy(runtime);
			}
			runtimes.Clear();
			LyumaAv3Runtime.updateSceneLayersDelegate(~0);
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		private void Update() {
			if (RestartingEmulator) {
				RestartingEmulator = false;
				Awake();
			} else if (RestartEmulator) {
				RunPreprocessAvatarHook = false;
				RestartEmulator = false;
				OnDestroy();
				RestartingEmulator = true;
			}
			foreach (var runtime in forceActiveRuntimes) {
				runtime.gameObject.SetActive(true);
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
			if (lastDisableRadialMenu != disableRadialMenu) {
				lastDisableRadialMenu = disableRadialMenu;
				foreach (var runtime in runtimes) {
					foreach (var av3MenuComponent in runtime.GetComponents<LyumaAv3Menu>()) {
						av3MenuComponent.useLegacyMenu = disableRadialMenu;
					}
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
}
