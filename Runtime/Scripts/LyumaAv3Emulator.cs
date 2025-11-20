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
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using UnityEngine.SceneManagement;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Lyuma.Av3Emulator.Runtime
{
	[DefaultExecutionOrder(-10)]
	[HelpURL("https://github.com/lyuma/Av3Emulator")]
	public class LyumaAv3Emulator : MonoBehaviour
	{
		public static readonly ulong EMULATOR_VERSION = 0x3_04_10_00;
		public const string EMULATOR_VERSION_STRING = "Avatar 3.0 Emulator Version 3.4.10";
		public const string GIT_REPO = "https://github.com/lyuma/Av3Emulator";
		public static readonly String BUG_TRACKER_URL = GIT_REPO + "/issues";
		public const string CREDIT1 = "By Lyuma, hai-vr, jellejurre, anatawa12,";
		public const string CREDIT2 = "Dreadrith, BlackStartX, bd_, Mysteryem,";
		public const string CREDIT3 = "NotAKidoS, and V-Sekai contributors";
		public const string CREDITS = CREDIT1 + "\n" + CREDIT2 + "\n" + CREDIT3;

		public static TextAsset READMEAsset;
		public static TextAsset CHANGELOGAsset;
		public static TextAsset LICENSEAsset;

		[Space(12)][Header(CREDITS)][Header(EMULATOR_VERSION_STRING)]
		// They added multiline [Header] in Unity 2021
		//[Header(CREDITX)]
		public string VisitOurGithub = GIT_REPO;
		public bool ViewREADMEManual;
		public bool ViewChangelog;[Space(-12)]
		[Header("Lyuma's Av3Emulator is open source!")]
		public bool ViewMITLicense;
		public bool SendBugsOrFeedback;
		[Header("Fake VR or Desktop mode selection")]
		public bool DefaultToVR = false;
		public DefaultPoseOptions DefaultPose = DefaultPoseOptions.Standing;
		public LyumaAv3Runtime.TrackingTypeIndex DefaultTrackingType = LyumaAv3Runtime.TrackingTypeIndex.HeadHands;
		[Header("Emulation")]
		public VRCAvatarDescriptor.AnimLayerType DefaultAnimatorToDebug = VRCAvatarDescriptor.AnimLayerType.Base; 
		public bool SelectAssetOnChangeAnimatorToDebug = false;
		public bool SelectAvatarOnStartup = true;
		public DescriptorCollidersSendersHelper.DescriptorExtractionType DescriptorColliders = DescriptorCollidersSendersHelper.DescriptorExtractionType.SendersOnly;
		// VRCFury accesses "RestartEmulator" using reflection, so do not rename this field:
		public bool RestartEmulator;
		private bool RestartingEmulator;
		public bool disableRadialMenu = false;
		private bool lastDisableRadialMenu = false;
		public bool EnableAvatarScaling = true;
		public bool EnablePlayerContactPermissions = false;
		public bool HaveEyesFollowMouse = true;
		[Header("Unity Integrations")]
		[Tooltip("Runs the same function as the Avatar Descriptor Editor Inspector. May disrupt custom usages of colliders in Debug mode.")]
		public bool ForceUpdateDescriptorColliders = false;
		public bool RunPreprocessAvatarHook = true;
		public bool DisableAvatarDynamicsIntegration;
		public bool WorkaroundPlayModeScriptCompile = true;
		public bool DisableParentConstraintOffsetScaling = true;
		public bool EnableClothScalingFix = true;
		[Header("Networked and mirror clone emulation")]
		public bool CreateNonLocalClone;
		public int CreateNonLocalCloneCount;
		public bool ClonesAreOnFriendsList = true;
		public bool DisableMirrorClone;
		public bool DisableShadowClone;
		private bool lastHead;
		public bool EnableHeadScaling;
		[Header("Clone visualization and position offset")]
		public bool ViewMirrorReflection;
		public bool ViewBothRealAndMirror;
		public bool VisuallyOffsetClonesOnly = true;
		public bool ApplyClonePositionOffset = false;
		[Header("Legacy options")]
		[Tooltip("Simulate behavior with sub-animator parameter drivers prior to the 2021.1.1 patch (19 Jan 2021)")]
		public bool legacySubAnimatorParameterDriverMode = false;
		[Tooltip("Initialize during Awake() instead of Start(). May avoid rare glitches with clones but will break VRCFury and may cause crash on Unity 2019. May only be changed prior to starting.")]
		public bool LegacyInitializeOnAwake = false;

		static public LyumaAv3Emulator emulatorInstance;
		static public RuntimeAnimatorController EmptyController;

		// VRCFury uses reflection to access "runtimes", so do not rename:
		[NonSerialized] public List<LyumaAv3Runtime> runtimes = new List<LyumaAv3Runtime>();
		// VRCFury uses reflection to access "forceActiveRuntimes", so do not rename:
		[NonSerialized] public LinkedList<LyumaAv3Runtime> forceActiveRuntimes = new LinkedList<LyumaAv3Runtime>();
		// VRCFury uses reflection to access "scannedAvatars", so we must keep this field empty:
		[NonSerialized] public HashSet<VRCAvatarDescriptor> scannedAvatars = new HashSet<VRCAvatarDescriptor>();
		// We keep track of the list of enabled avatars from the previous time.
		[NonSerialized] public HashSet<VRCAvatarDescriptor> scannedAvatars2 = new HashSet<VRCAvatarDescriptor>();
		[NonSerialized] public HashSet<VRCAvatarDescriptor> enabledAvatars = new HashSet<VRCAvatarDescriptor>();

		private bool usedLegacyAwake = false;

		public bool IsLegacyAwakeUsed() {
			return usedLegacyAwake;
		}

		private void OnValidate()
		{
			if (VisitOurGithub != GIT_REPO) {
				VisitOurGithub = GIT_REPO;
			}
			if (SendBugsOrFeedback) {
				SendBugsOrFeedback = false;
				Application.OpenURL(BUG_TRACKER_URL);
			}
			if (ViewREADMEManual) {
				ViewREADMEManual = false;
				LyumaAv3Runtime.updateSelectionDelegate(READMEAsset, 0);
			}
			if (ViewChangelog) {
				ViewChangelog = false;
				LyumaAv3Runtime.updateSelectionDelegate(CHANGELOGAsset, 0);
			}
			if (ViewMITLicense) {
				ViewMITLicense = false;
				LyumaAv3Runtime.updateSelectionDelegate(LICENSEAsset, 0);
			}
		}

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
			emulatorInstance = this;
			if (WorkaroundPlayModeScriptCompile) {
				LyumaAv3Runtime.ApplyOnEnableWorkaroundDelegate();
			}
			if (LegacyInitializeOnAwake) {
				ScanForAvatars();
				usedLegacyAwake = true;
				SceneManager.sceneLoaded += LegacyOnSceneLoaded;
			}

		}

		private void LegacyOnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (!usedLegacyAwake) {
				throw new Exception("Unreachable code when in non-legacy mode");
			}
			ScanForAvatars();
		}

		private void Start()
		{
			ScanForAvatars();
			foreach (var avadesc in scannedAvatars2) {
				if (!enabledAvatars.Contains(avadesc)) {
					avadesc.gameObject.SetActive(true);
				}
			}
			enabledAvatars.UnionWith(scannedAvatars2);
		}

		private void ScanForAvatars() {
			HashSet<VRCAvatarDescriptor> avatars = new HashSet<VRCAvatarDescriptor>();
			avatars.UnionWith(FindObjectsOfType<VRCAvatarDescriptor>()
				.Where(avatar => !scannedAvatars2.Contains(avatar)));
			Debug.Log(this.name + ": Setting up Av3Emulator on " + avatars.Count + " avatars.", this);
			ScanForAvatarsFromSet(avatars);
		}

		private void ScanForAvatarsFromSet(HashSet<VRCAvatarDescriptor> avatars) {
			scannedAvatars2.UnionWith(avatars);
			foreach (var avadesc in avatars)
			{
				if (avadesc.GetComponents<Component>().Any(x => x.GetType().Name == "PipelineSaver")) {
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
						GameObject origClone = GameObject.Instantiate(avadesc.gameObject);
						origClone.name = avadesc.gameObject.name;
						avadesc.gameObject.name = origClone.name + "(Clone)";
						LyumaAv3Runtime.InvokeOnPreProcessAvatar(avadesc.gameObject);
						avadesc.gameObject.name = origClone.name;
						GameObject.DestroyImmediate(origClone);
						avadesc.gameObject.SetActive(true);
					}

					var oml = GetOrAddComponent<UnityEngine.AI.OffMeshLink>(avadesc.gameObject);
					oml.startTransform = this.transform;
					var runtime = GetOrAddComponent<LyumaAv3Runtime>(avadesc.gameObject);
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
					runtime.EnableAvatarScaling = EnableAvatarScaling;
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
			if (usedLegacyAwake) {
				SceneManager.sceneLoaded -= LegacyOnSceneLoaded;
				usedLegacyAwake = false;
			}
		}

		private void Update() {
			if (RestartingEmulator) {
				RestartingEmulator = false;
				Awake();
				Start();
			} else if (RestartEmulator) {
				RunPreprocessAvatarHook = false;
				RestartEmulator = false;
				OnDestroy();
				RestartingEmulator = true;
				runtimes.Clear();
				forceActiveRuntimes.Clear();
				Debug.Log(this.name + ": Restarting Av3Emulator on " + scannedAvatars2.Count + " avatars.", this);
				ScanForAvatarsFromSet(scannedAvatars2);
				enabledAvatars.Clear();
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
		
		public static T GetOrAddComponent<T>(GameObject go) where T : Component
		{
			T component = go.GetComponent<T>();
			if (component == null)
				component = go.AddComponent<T>();
			return component;
		}
		
	}
}
