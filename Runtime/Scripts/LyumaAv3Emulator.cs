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
using System.Reflection;
using UnityEditor;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Lyuma.Av3Emulator.Runtime
{
	[RequireComponent(typeof(Animator))]
	public class LyumaAv3Emulator : MonoBehaviour
	{
		static readonly ulong EMULATOR_VERSION = 0x2_09_08_00;

		[Header("Fake VR or Desktop mode selection")]
		public bool DefaultToVR = false;
		public bool DefaultTestInStation = false;
		public LyumaAv3Runtime.TrackingTypeIndex DefaultTrackingType = LyumaAv3Runtime.TrackingTypeIndex.HeadHands;
		[Header("Emulation")]
		public VRCAvatarDescriptor.AnimLayerType DefaultAnimatorToDebug = VRCAvatarDescriptor.AnimLayerType.Base; 
        public DescriptorExtractionType DescriptorColliders = DescriptorExtractionType.CollidersAndSenders;
		public bool RestartEmulator;
		private bool RestartingEmulator;
		[Tooltip("Simulate behavior with sub-animator parameter drivers prior to the 2021.1.1 patch (19 Jan 2021)")]
		public bool legacySubAnimatorParameterDriverMode = false;
		public bool legacyMenuGUI = true;
		private bool lastLegacyMenuGUI = true;
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
		public bool ViewMirrorReflection;
		public bool ViewBothRealAndMirror;

		static public LyumaAv3Emulator emulatorInstance;
		static public RuntimeAnimatorController EmptyController;

		public List<LyumaAv3Runtime> runtimes = new List<LyumaAv3Runtime>();
		public LinkedList<LyumaAv3Runtime> forceActiveRuntimes = new LinkedList<LyumaAv3Runtime>();

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
                    if (DescriptorColliders != DescriptorExtractionType.None)
                    {
                        ForceUpdateDescriptorColliders(avadesc);
                        ExtractDescriptorColliders(avadesc, DescriptorColliders);
                    }

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

        private void ForceUpdateDescriptorColliders(VRCAvatarDescriptor descriptor)
        {
            Editor tempEditor = null;
            try
            {
				
                var descriptorEditor = System.Type.GetType("AvatarDescriptorEditor3, Assembly-CSharp-Editor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
                if (descriptorEditor == null) descriptorEditor = System.Type.GetType("AvatarDescriptorEditor3, VRC.SDK3A.Editor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
                var editorAvatarField = descriptorEditor.GetField("avatarDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
                var updateMethod = descriptorEditor.GetMethod("UpdateAutoColliders", BindingFlags.NonPublic | BindingFlags.Instance);

                tempEditor = Editor.CreateEditor(descriptor, descriptorEditor);
                editorAvatarField.SetValue(tempEditor, descriptor);
                updateMethod.Invoke(tempEditor, null);
                DestroyImmediate(tempEditor);
            }
            catch
            {
                Debug.LogError("Failed to force update Descriptor Colliders through reflection.");
                if (tempEditor != null) DestroyImmediate(tempEditor);
            }

        }

        public enum DescriptorExtractionType
        {
            None = 0,
            Colliders = 1 << 0,
			Senders = 1 << 1,
            CollidersAndSenders = ~0,
        }
        private void ExtractDescriptorColliders(VRCAvatarDescriptor descriptor, DescriptorExtractionType extractionType)
        {
			var animator = descriptor.GetComponent<Animator>();
            if (!animator)
            {
                Debug.LogError($"No animator found on {descriptor.gameObject.name}; Can't extract descriptor colliders.");
				return;
            }

            List<VRCPhysBoneCollider> createdColliders = new List<VRCPhysBoneCollider>();
            var collidingBones = descriptor.GetComponentsInChildren<VRCPhysBone>().Where(p => p.allowCollision);

            void ExtractCollider(VRCAvatarDescriptor.ColliderConfig config, HumanBodyBones matchedBone, string collisionName, bool isSenderOnly = false)
            {
                if (config.state != VRCAvatarDescriptor.ColliderConfig.State.Disabled)
                {
                    var target = config.transform;

                    if (target)
                    {
						//For some reason the visuals followed the parent of finger bones but has the local position of the finger as offset
						//Haven't yet confirmed if this is translated in-game but it should be
						Vector3 localOffset = Vector3.zero;
                        if (IsFingerBone(matchedBone))
                        {
							//Sometimes users switch the transform to be something that isn't a finger
							//Would have to go through the animator's hierarchy and double check whether this is still a non proximal finger bone
							localOffset = target.transform.localPosition;
                            target = target.parent;
                        }

                        var collider = target.gameObject.AddComponent<VRCPhysBoneCollider>();
                        var sender = target.gameObject.AddComponent<VRCContactSender>();

                        collider.radius = sender.radius = config.radius;
                        collider.height = sender.height = config.height;
                        collider.position = sender.position = config.position + localOffset;
                        collider.rotation = sender.rotation = config.rotation;
                        collider.rootTransform = sender.rootTransform = target;

                        collider.shapeType = VRCPhysBoneColliderBase.ShapeType.Capsule;
                        sender.shapeType = ContactBase.ShapeType.Capsule;

                        sender.collisionTags.Add(collisionName);
                        if (HasMirrorBone(matchedBone))
                            sender.collisionTags.Add(collisionName + (IsLeftBone(matchedBone) ? "L" : "R"));

                        if (isSenderOnly || !extractionType.HasFlag(DescriptorExtractionType.Colliders)) DestroyImmediate(collider);
                        else createdColliders.Add(collider);

						if (!extractionType.HasFlag(DescriptorExtractionType.Senders)) 
                            DestroyImmediate(sender);
                    }
                }
            }

            ExtractCollider(descriptor.collider_head, HumanBodyBones.Head, "Head", true);
            ExtractCollider(descriptor.collider_torso, HumanBodyBones.Chest, "Torso", true);
            ExtractCollider(descriptor.collider_footR, HumanBodyBones.RightToes, "Foot", true);
            ExtractCollider(descriptor.collider_footL, HumanBodyBones.LeftToes, "Foot", true);
            ExtractCollider(descriptor.collider_handR, HumanBodyBones.RightHand, "Hand");
            ExtractCollider(descriptor.collider_fingerLittleR, HumanBodyBones.RightLittleIntermediate, "FingerLittle");
            ExtractCollider(descriptor.collider_fingerIndexR, HumanBodyBones.RightIndexIntermediate, "FingerIndex");
            ExtractCollider(descriptor.collider_fingerMiddleR, HumanBodyBones.RightMiddleIntermediate, "FingerMiddle");
            ExtractCollider(descriptor.collider_fingerRingR, HumanBodyBones.RightRingIntermediate, "FingerRing");
            ExtractCollider(descriptor.collider_handL, HumanBodyBones.LeftHand, "Hand");
            ExtractCollider(descriptor.collider_fingerLittleL, HumanBodyBones.LeftLittleIntermediate, "FingerLittle");
            ExtractCollider(descriptor.collider_fingerIndexL, HumanBodyBones.LeftIndexIntermediate, "FingerIndex");
            ExtractCollider(descriptor.collider_fingerMiddleL, HumanBodyBones.LeftMiddleIntermediate, "FingerMiddle");
            ExtractCollider(descriptor.collider_fingerRingL, HumanBodyBones.LeftRingIntermediate, "FingerRing");

            if (extractionType.HasFlag( DescriptorExtractionType.Colliders))
                foreach (var p in collidingBones)
                    p.colliders.AddRange(createdColliders);
        }

        internal static Transform SmartGetBone(Animator animator, HumanBodyBones humanBone)
        {
            int boneIndex = (int)humanBone;
            Transform b = animator.GetBoneTransform((HumanBodyBones)boneIndex);
            if (b) return b;

            if (IsFingerBone(humanBone) && boneIndex%3 != 0)
                b = SmartGetBone(animator, (HumanBodyBones)(--boneIndex));
            else if (IsToeBone(humanBone))
            {
                boneIndex -= 14;
                b = animator.GetBoneTransform((HumanBodyBones)boneIndex);
            }

            return b;
        }

        internal static bool IsFingerBone(HumanBodyBones bone) => (int)bone >= 24 && (int)bone <= 53;
        internal static bool IsToeBone(HumanBodyBones bone) => (int) bone == 19 || (int) bone == 20;
        internal static bool IsLeftBone(HumanBodyBones bone) => bone.ToString().Contains("Left");

        internal static bool HasMirrorBone(HumanBodyBones bone)
        {
            string name = bone.ToString();
            return name.Contains("Left") || name.Contains("Right");
        }

    }
}