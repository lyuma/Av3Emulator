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
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static Lyuma.Av3Emulator.Runtime.LyumaAv3Emulator;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using static VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters;

namespace Lyuma.Av3Emulator.Runtime
{
	// [RequireComponent(typeof(Animator))]
	[DefaultExecutionOrder(-10)]
	[HelpURL("https://github.com/lyuma/Av3Emulator")]
	public class LyumaAv3Runtime : MonoBehaviour
	{
		static public Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController> animLayerToDefaultController = new Dictionary<VRCAvatarDescriptor.AnimLayerType, RuntimeAnimatorController>();
		public delegate void UpdateSelectionFunc(UnityEngine.Object obj, int mode);
		public static UpdateSelectionFunc updateSelectionDelegate;
		public delegate void UpdateAnimatorWindowFunc(RuntimeAnimatorController rac);
		public static UpdateAnimatorWindowFunc updateAnimatorWindowDelegate;
		public delegate void AddRuntime(Component runtime);
		public static AddRuntime addRuntimeDelegate;
		public delegate void UpdateSceneLayersFunc(int layers);
		public static UpdateSceneLayersFunc updateSceneLayersDelegate;
		public delegate void ApplyOnEnableWorkaroundDelegateType();
		public static ApplyOnEnableWorkaroundDelegateType ApplyOnEnableWorkaroundDelegate;
		public delegate void ForceUpdateDescriptorCollidersFunc(VRCAvatarDescriptor descriptor);
		public static ForceUpdateDescriptorCollidersFunc forceUpdateDescriptorColliders;
		public delegate void ConvertDynamicBonesFunc (GameObject avatarObj);
		public static ConvertDynamicBonesFunc convertDynamicBones;

		// This is injected by Editor-scope scripts to give us access to VRCBuildPipelineCallbacks.
		public static Action<GameObject> InvokeOnPreProcessAvatar = (_) => { };

		public LyumaAv3Runtime OriginalSourceClone = null;

		[Tooltip("Resets avatar state machine instantly")]
		public bool ResetAvatar;
		[Tooltip("Resets avatar state machine and waits until you uncheck this to start")]
		public bool ResetAndHold;
		[Tooltip("Click if you modified your menu or parameter list")]
		public bool RefreshExpressionParams;
		[Tooltip("Simulates saving and reloading the avatar")]
		public bool KeepSavedParametersOnReset = true;
		//[Header("Animator to Debug. Unity is glitchy when not 'Base'.")]
		[Tooltip("Selects the playable layer to be visible with parameters in the Animator. If you view any other playable in the Animator window, parameters will say 0 and will not update.")]
		public VRCAvatarDescriptor.AnimLayerType DebugDuplicateAnimator;
		private char PrevAnimatorToDebug;
		[Tooltip("Selects the playable layer to be visible in Unity's Animator window. Does not reset avatar. Unless this is set to Base, will cause 'Invalid Layer Index' logspam; layers will show wrong weight and parameters will all be 0.")]
		public VRCAvatarDescriptor.AnimLayerType ViewAnimatorOnlyNoParams;
		private char PrevAnimatorToViewLiteParamsShow0;
		[HideInInspector] public string SourceObjectPath;
		[HideInInspector] public LyumaAv3Runtime AvatarSyncSource;
		private float nextUpdateTime = 0.0f;
		//[Header("OSC (double click OSC Controller for debug and port settings)")]
		public bool EnableAvatarOSC = false;
		public bool LogOSCWarnings = false;
		public LyumaAv3Osc OSCController = null;
		public A3EOSCConfiguration OSCConfigurationFile = new A3EOSCConfiguration();

		//[Header("Network Clones and Sync")]
		public bool CreateNonLocalClone;
		[Tooltip("In VRChat, 8-bit float quantization only happens remotely. Check this to test your robustness to quantization locally, too. (example: 0.5 -> 0.503")]
		public bool locally8bitQuantizedFloats = false;
		private int CloneCount;
		[Range(0.0f, 2.0f)] public float NonLocalSyncInterval = 0.2f;
		[Tooltip("Parameters visible in the radial menu will IK sync")] public bool IKSyncRadialMenu = true;
		//[Header("PlayerLocal and MirrorReflection")]
		public bool EnableHeadScaling;
		public bool DisableMirrorAndShadowClones;
		[HideInInspector] public LyumaAv3Runtime MirrorClone;
		[HideInInspector] public LyumaAv3Runtime ShadowClone;
		[HideInInspector] public List<LyumaAv3Runtime> NonLocalClones = new List<LyumaAv3Runtime>();
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
		private List<Dictionary<string, AnimatorControllerParameterType>> playableParamterTypes = new List<Dictionary<string, AnimatorControllerParameterType>>();
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
		private int blinkBlendShapeIdx = -1;
		private int lookDownBlendShapeIdx = -1;
		private int lookUpBlendShapeIdx = -1;

		public Dictionary<String, object> DataLastShovedIntoOSCAnyway = new Dictionary<String, object>();
		public Dictionary<String, object> DataToShoveIntoOSCAnyway = new Dictionary<String, object>();

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
					object oldObj = null;
			runtime.DataLastShovedIntoOSCAnyway.TryGetValue(paramName, out oldObj);
					if ((object)value != oldObj) {
						runtime.DataToShoveIntoOSCAnyway[paramName] = value;
						runtime.DataLastShovedIntoOSCAnyway[paramName] = value;
					}
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
					object oldObj = null;
			runtime.DataLastShovedIntoOSCAnyway.TryGetValue(paramName, out oldObj);
					if ((object)value != oldObj) {
						runtime.DataToShoveIntoOSCAnyway[paramName] = value;
						runtime.DataLastShovedIntoOSCAnyway[paramName] = value;
					}
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
					object oldObj = null;
			runtime.DataLastShovedIntoOSCAnyway.TryGetValue(paramName, out oldObj);
					if ((object)value != oldObj) {
						runtime.DataToShoveIntoOSCAnyway[paramName] = value;
						runtime.DataLastShovedIntoOSCAnyway[paramName] = value;
					}
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

		private bool isBeingSynced(string paramName)
		{
			int idx;
			if (IntToIndex.TryGetValue(paramName, out idx)) {
				if (Ints[idx].synced) {
					return true;
				}
			}
			if (FloatToIndex.TryGetValue(paramName, out idx)) {
				if (Floats[idx].synced) {
					return true;
				}
			}
			if (BoolToIndex.TryGetValue(paramName, out idx)) {
				if (Bools[idx].synced) {
					return true;
				}
			}
			return false;
		}

		public void assignContactParameters(VRCContactReceiver[] behaviours) {
			AvDynamicsContactReceivers = behaviours;
			foreach (var mb in AvDynamicsContactReceivers) {
				if (!IsLocal && (mb.localOnly || isBeingSynced(mb.parameter)))
				{
					continue;
				}				
				string parameter = mb.parameter;
				Av3EmuParameterAccess accessInst = new Av3EmuParameterAccess();
				accessInst.runtime = this;
				accessInst.paramName = parameter;
				mb.paramAccess = accessInst;
				accessInst.floatVal = mb.paramValue;
			}
		}
		public void assignPhysBoneParameters(VRCPhysBone[] behaviours) {
			AvDynamicsPhysBones = behaviours;
			foreach (var mb in AvDynamicsPhysBones) {
				string parameter = mb.parameter;
				Av3EmuParameterAccess accessInst = new Av3EmuParameterAccess();
				accessInst.runtime = this;
				accessInst.paramName = parameter + VRCPhysBone.PARAM_ANGLE;
				if (IsLocal || !isBeingSynced(accessInst.paramName))
				{
					mb.param_Angle = accessInst;
					accessInst.floatVal = mb.param_AngleValue;
				}
				accessInst = new Av3EmuParameterAccess();
				accessInst.runtime = this;
				accessInst.paramName = parameter + VRCPhysBone.PARAM_ISGRABBED;
				if (IsLocal || !isBeingSynced(accessInst.paramName))
				{
					mb.param_IsGrabbed = accessInst;
					accessInst.boolVal = mb.param_IsGrabbedValue;
				}
				accessInst = new Av3EmuParameterAccess();
				accessInst.runtime = this;
				accessInst.paramName = parameter + VRCPhysBone.PARAM_STRETCH;
				if (IsLocal || !isBeingSynced(accessInst.paramName))
				{
					mb.param_Stretch = accessInst;
					accessInst.floatVal = mb.param_StretchValue;
				}
				FieldInfo posedParam = typeof(VRCPhysBoneBase).GetField("PARAM_ISPOSED", BindingFlags.Public | BindingFlags.Static);
				if (posedParam != null)
				{
					accessInst = new Av3EmuParameterAccess();
					accessInst.runtime = this;
					accessInst.paramName = parameter + posedParam.GetValue(null);
					if (IsLocal || !isBeingSynced(accessInst.paramName))
					{
						typeof(VRCPhysBoneBase).GetField("param_IsPosed").SetValue(mb, accessInst);
						accessInst.boolVal = (bool)typeof(VRCPhysBoneBase).GetField("param_IsPosedValue").GetValue(mb);
					}
				}
				
				FieldInfo squishParam = typeof(VRCPhysBoneBase).GetField("PARAM_SQUISH", BindingFlags.Public | BindingFlags.Static);
				if (squishParam != null)
				{
					accessInst = new Av3EmuParameterAccess();
					accessInst.runtime = this;
					accessInst.paramName = parameter + squishParam.GetValue(null);
					if (IsLocal || !isBeingSynced(accessInst.paramName))
					{
						typeof(VRCPhysBoneBase).GetField("param_Squish").SetValue(mb, accessInst);
						accessInst.floatVal = (float)typeof(VRCPhysBoneBase).GetField("param_SquishValue").GetValue(mb);
					}
				}
				// Debug.Log("Assigned strech access " + physBoneState.param_Stretch.GetValue(mb) + " to param " + parameter + ": was " + old_value);
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

		public class BuiltinParameterDefinition
		{
			public BuiltinParameterDefinition(string name, VRCExpressionParameters.ValueType type, Func<LyumaAv3Runtime, object> valueGetter, Action<LyumaAv3Runtime, object> valueSetter)
			{
				this.name = name;
				this.type = type;
				this.valueGetter = valueGetter;
				this.valueSetter = valueSetter;
			}

			public string GetTypeString()
			{
				if (type == VRCExpressionParameters.ValueType.Bool){
					return "Bool";
				}
				if (type == VRCExpressionParameters.ValueType.Float)
				{
					return "Float";
				}
				return "Int";
			}
			
			public string name;
			public VRCExpressionParameters.ValueType type;
			public Func<LyumaAv3Runtime, object> valueGetter;
			public Action<LyumaAv3Runtime, object> valueSetter;
			public bool warned = false;
		};
		
		public static HashSet<BuiltinParameterDefinition> BUILTIN_PARAMETERS = new HashSet<BuiltinParameterDefinition> {
			new BuiltinParameterDefinition("IsLocal", VRCExpressionParameters.ValueType.Bool,
				runtime => runtime.IsLocal || runtime.IsMirrorClone || runtime.IsShadowClone,
				(runtime, value) => runtime.IsLocal = (bool)value),
			new BuiltinParameterDefinition("Viseme", VRCExpressionParameters.ValueType.Int, 
				runtime => runtime.VisemeInt, 
				(runtime, value) => runtime.VisemeInt = (int)value),
			new BuiltinParameterDefinition("Voice", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.Voice,
				(runtime, value) => runtime.Voice = (float)value),
			new BuiltinParameterDefinition("GestureLeft", VRCExpressionParameters.ValueType.Int, 
				runtime => (int)runtime.GestureLeft,
				(runtime, value) => runtime.GestureLeft = (GestureIndex)value),
			new BuiltinParameterDefinition("GestureRight", VRCExpressionParameters.ValueType.Int, 
				runtime => (int)runtime.GestureRight,
				(runtime, value) => runtime.GestureRight = (GestureIndex)value),
			new BuiltinParameterDefinition("GestureLeftWeight", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.GestureLeftWeight,
				(runtime, value) => runtime.GestureLeftWeight = (float)value),
			new BuiltinParameterDefinition("GestureRightWeight", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.GestureRightWeight,
				(runtime, value) => runtime.GestureRightWeight = (float)value),
			new BuiltinParameterDefinition("AngularY", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.AngularY,
				(runtime, value) => runtime.AngularY = (float)value),
			new BuiltinParameterDefinition("VelocityX", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.Velocity.x,
				(runtime, value) => runtime.Velocity = new Vector3((float)value, runtime.Velocity.y, runtime.Velocity.z)),
			new BuiltinParameterDefinition("VelocityY", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.Velocity.y,
				(runtime, value) => runtime.Velocity = new Vector3(runtime.Velocity.x, (float)value, runtime.Velocity.z)),
			new BuiltinParameterDefinition("VelocityZ", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.Velocity.z,
				(runtime, value) => runtime.Velocity = new Vector3(runtime.Velocity.x, runtime.Velocity.y, (float)value)),
			new BuiltinParameterDefinition("VelocityMagnitude", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.Velocity.magnitude,
				(runtime, value) => runtime.Velocity = runtime.Velocity.normalized * (float)value),
			new BuiltinParameterDefinition("Upright", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.Upright,
				(runtime, value) => runtime.Upright = (float)value),
			new BuiltinParameterDefinition("Grounded", VRCExpressionParameters.ValueType.Bool, 
				runtime => runtime.Grounded,
				(runtime, value) => runtime.Grounded = (bool)value),
			new BuiltinParameterDefinition("Seated", VRCExpressionParameters.ValueType.Bool, 
				runtime => runtime.Seated,
				(runtime, value) => runtime.Seated = (bool)value),
			new BuiltinParameterDefinition("AFK", VRCExpressionParameters.ValueType.Bool, 
				runtime => runtime.AFK,
				(runtime, value) => runtime.AFK = (bool)value),
			new BuiltinParameterDefinition("TrackingType", VRCExpressionParameters.ValueType.Int, 
				runtime => (int)runtime.TrackingType,
				(runtime, value) => runtime.TrackingType = (TrackingTypeIndex)value),
			new BuiltinParameterDefinition("VRMode", VRCExpressionParameters.ValueType.Int,
				runtime => runtime.VRMode ? 1 : 0,
				(runtime, value) => runtime.VRMode = (int)value == 1),
			new BuiltinParameterDefinition("MuteSelf", VRCExpressionParameters.ValueType.Bool, 
				runtime => runtime.MuteSelf,
				(runtime, value) => runtime.MuteSelf = (bool)value),
			new BuiltinParameterDefinition("InStation", VRCExpressionParameters.ValueType.Bool, 
				runtime => runtime.InStation,
				(runtime, value) => runtime.InStation = (bool)value),
			new BuiltinParameterDefinition("Earmuffs", VRCExpressionParameters.ValueType.Bool, 
				runtime => runtime.Earmuffs,
				(runtime, value) => runtime.Earmuffs = (bool)value),
			new BuiltinParameterDefinition("IsAnimatorEnabled", VRCExpressionParameters.ValueType.Bool,
				runtime => runtime.IsAnimatorEnabled,
				(runtime, value) => runtime.IsAnimatorEnabled = (bool)value),
			new BuiltinParameterDefinition("PreviewMode", VRCExpressionParameters.ValueType.Int,
				runtime => runtime.PreviewMode ? 1 : 0,
				(runtime, value) => runtime.PreviewMode = (int)value == 1),
			new BuiltinParameterDefinition("ScaleModified", VRCExpressionParameters.ValueType.Bool, 
				runtime => runtime.EnableAvatarScaling && runtime.AvatarHeight != runtime.DefaultViewPosition.y,
				(runtime, value) => { }), //Modifying this doesn't make sense, this field is un-editable in VRC
			new BuiltinParameterDefinition("ScaleFactor", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.EnableAvatarScaling ? runtime.AvatarHeight / runtime.DefaultViewPosition.y : 1.0f,
				(runtime, value) => { if (runtime.EnableAvatarScaling) runtime.AvatarHeight = runtime.DefaultViewPosition.y * (float)value; }),
			new BuiltinParameterDefinition("ScaleFactorInverse", VRCExpressionParameters.ValueType.Float, 
				runtime =>  runtime.EnableAvatarScaling ? 1.0f / (runtime.AvatarHeight / runtime.DefaultViewPosition.y) : 1.0f,
				(runtime, value) => { if (runtime.EnableAvatarScaling) runtime.AvatarHeight = runtime.DefaultViewPosition.y / (float)value; }),
			new BuiltinParameterDefinition("EyeHeightAsMeters", VRCExpressionParameters.ValueType.Float, 
				runtime => runtime.avadesc.ViewPosition.y,
				(runtime, value) => { }), //Modifying this doesn't make sense, this field is un-editable in VRC
			new BuiltinParameterDefinition("EyeHeightAsPercent", VRCExpressionParameters.ValueType.Float, 
				runtime => Mathf.Clamp((runtime.avadesc.ViewPosition.y  - 0.2f)/ (5.0f - 0.2f), 0.0f, 1.0f),
				(runtime, value) => { }), //Modifying this doesn't make sense, this field is un-editable in VRC
			new BuiltinParameterDefinition("IsOnFriendsList", VRCExpressionParameters.ValueType.Bool, 
				runtime => !runtime.IsLocal && !runtime.IsMirrorClone && !runtime.IsShadowClone && runtime.IsOnFriendsList, // true for remote and false for local
				(runtime, value) => { }), //Modifying this doesn't make sense, this field is un-editable in VRC
		};
		
		public static readonly Type[] MirrorCloneComponentBlacklist = new Type[] {
			typeof(Camera), typeof(FlareLayer), typeof(AudioSource), typeof(Rigidbody), typeof(Joint)
		};
		public static readonly Type[] ShadowCloneComponentBlacklist = new Type[] {
			typeof(Camera), typeof(FlareLayer), typeof(AudioSource), typeof(Light), typeof(ParticleSystemRenderer), typeof(Rigidbody), typeof(Joint)
		
		};
		public static readonly HashSet<string> CloneStringComponentBlacklist = new HashSet<string>() { "DynamicBone", "VRCContact", "VRCPhysBone", "VRCSpatialAudioSource", "VRCHeadChop" };

		//[Header("Built-in inputs / Viseme")]
		public VisemeIndex Viseme;
		[Range(0, 15)] public int VisemeIdx;
		private int VisemeInt;
		[Tooltip("Voice amount from 0.0f to 1.0f for the current viseme")]
		[Range(0,1)] public float Voice;
		[Range(0,1)] public float BlinkRate = 0.1f;

		[Range(-1,1)] public float EyeTargetX = 0.0f;
		[Range(-1,1)] public float EyeTargetY = 0.0f;
		//[Header("Built-in inputs / Hand Gestures")]
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
		//[Header("Built-in inputs / Locomotion")]
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
		//[Header("Built-in inputs / Tracking Setup and Other")]
		public TrackingTypeIndex TrackingType;
		[Range(0, 6)] public int TrackingTypeIdx;
		private char TrackingTypeIdxInt;
		public bool VRMode;
		public bool MuteSelf;
		private bool MuteTogglerOn;
		public bool Earmuffs;
		public bool PreviewMode;
		public bool IsAnimatorEnabled = true;
		public bool InStation;
		[HideInInspector] public int AvatarVersion = 3;
		public bool EnableAvatarScaling = false;
		private Vector3 defaultHeadScale;
		[Range(0.2f, 5f)] public float AvatarHeight = 1;
		private Vector3 DefaultViewPosition;
		private Vector3 DefaultAvatarScale;
		public Vector3 VisualOffset;
		private Vector3 SavedPosition;
		private bool IsCurrentlyVisuallyOffset = false;
		public bool IsOnFriendsList = false;

		//[Header("Output State (Read-only)")]
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
			[HideInInspector] public float? lastValue;
		}
		//[Header("User-generated inputs")]
		public List<FloatParam> Floats = new List<FloatParam>();
		public Dictionary<string, int> FloatToIndex = new Dictionary<string, int>();

		[Serializable]
		public class IntParam
		{
			[HideInInspector] public string stageName;
			public string name;
			[HideInInspector] public bool synced;
			public int value;
			[HideInInspector] public int? lastValue;
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
			[HideInInspector] public bool? lastValue;
			[HideInInspector] public bool[] hasTrigger;
			[HideInInspector] public bool[] hasBool;
		}
		public List<BoolParam> Bools = new List<BoolParam>();
		public Dictionary<string, int> BoolToIndex = new Dictionary<string, int>();

		[Space(10)][Header(LyumaAv3Emulator.CREDIT3)][Space(-12)][Header(LyumaAv3Emulator.CREDIT2)][Space(-12)][Header(LyumaAv3Emulator.CREDIT1)][Header(LyumaAv3Emulator.EMULATOR_VERSION_STRING)]
		public string VisitOurGithub = LyumaAv3Emulator.GIT_REPO;
		public bool ViewREADMEManual;
		public bool ViewChangelog;
		[Header("Lyuma's Av3Emulator is open source!")][Space(-12)]
		public bool ViewMITLicense;
		public bool SendBugsOrFeedback;

		public HashSet<string> ParameterNames = new HashSet<string>();

		[HideInInspector] public LyumaAv3Emulator emulator;

		static public Dictionary<Animator, LyumaAv3Runtime> animatorToTopLevelRuntime = new Dictionary<Animator, LyumaAv3Runtime>();
		private HashSet<Animator> attachedAnimators;
		private HashSet<string> duplicateParameterAdds = new HashSet<string>();
		private List<object> vrcPlayAudios = new List<object>();

		private (ParentConstraint, Vector3[])[] ParentConstraints;
		private Cloth[] ClothComponents;
		private Component[] headChops; 
		
		private Dictionary<Transform, HeadChopDataStorage> headChopData;
		private static int globalContactPlayerId;
		private  int contactPlayerId;

		private Transform head = null;
		private class HeadChopDataStorage
		{
			public Vector3 originalLocalPosition; 
			public Vector3 originalGlobalHeadOffset;
			public Vector3 originalLocalScale;
			public Vector3 originalGlobalScale;
		}
		
		const float BASE_HEIGHT = 1.4f;

		public IEnumerator DelayedEnterPoseSpace(bool setView, float time) {
			yield return new WaitForSeconds(time);
			if (setView) {
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
					if (!runtime)
					{
						return;
					}
					if (runtime.IsMirrorClone || runtime.IsShadowClone) {
						return;
					}
					if (behaviour.debugString != null && behaviour.debugString.Length > 0)
					{
						Debug.Log("[VRCAvatarParameterDriver:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
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
								foreach (var p in runtime.playables){
									int paramId = 0;
									if (runtime.playableParamterIds[whichController].TryGetValue(actualName, out paramId))
									{
										runtime.SetTypeWithMismatch(p, paramId, newValue, runtime.playableParamterTypes[whichController][actualName]);
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
					if (!runtime)
					{
						return;
					}
					if (runtime.IsMirrorClone && runtime.IsShadowClone) {
						return;
					}
					if (behaviour.debugString != null && behaviour.debugString.Length > 0)
					{
						Debug.Log("[VRCPlayableLayerControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
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
					if (!runtime)
					{
						return;
					}
					if (runtime.IsMirrorClone) {
						return;
					}
					if (behaviour.debugString != null && behaviour.debugString.Length > 0)
					{
						Debug.Log("[VRCAnimatorLayerControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
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
					if (!runtime)
					{
						return;
					}
					if (runtime.IsMirrorClone && runtime.IsShadowClone) {
						return;
					}
					if (behaviour.debugString != null && behaviour.debugString.Length > 0)
					{
						Debug.Log("[VRCAnimatorLocomotionControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
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
					if (!runtime)
					{
						return;
					}
					if (runtime.IsMirrorClone && runtime.IsShadowClone) {
						return;
					}
					if (behaviour.debugString != null && behaviour.debugString.Length > 0)
					{
						Debug.Log("[VRCAnimatorSetView:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
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
					if (!runtime)
					{
						return;
					}
					if (runtime.IsMirrorClone && runtime.IsShadowClone) {
						return;
					}
					if (behaviour.debugString != null && behaviour.debugString.Length > 0)
					{
						Debug.Log("[VRCAnimatorTrackingControl:" + (runtime == null ? "null" : runtime.name) + "]" + behaviour.name + ": " + behaviour.debugString, behaviour);
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
			
			Type audioType = Type.GetType("VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio, VRCSDK3A, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			Type audioSubType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			if (audioType != null)
			{
				FieldInfo initialize = audioSubType.GetField("Initialize");
				Type initializationDelegateType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio+InitializationDelegate, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
				initialize.SetValue(null, Delegate.Combine((Delegate)initialize.GetValue(null), Delegate.CreateDelegate(initializationDelegateType, typeof(LyumaAv3Runtime).GetMethod(nameof(SetupPlayAudio), BindingFlags.Static | BindingFlags.Public))));
			}
		}
		public static void SetupPlayAudio(object x) {
			Type audioSubType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			
			FieldInfo enterState = audioSubType.GetField("EnterState");
			Type enterDelegateType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio+EnterStateDelegate, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			enterState.SetValue(x, Delegate.Combine((Delegate)enterState.GetValue(x), Delegate.CreateDelegate(enterDelegateType, typeof(LyumaAv3Runtime).GetMethod(nameof(SetupEnterState), BindingFlags.Static | BindingFlags.Public))));

			FieldInfo exitState = audioSubType.GetField("ExitState");
			Type exitDelegateType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio+ExitStateDelegate, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			exitState.SetValue(x, Delegate.Combine((Delegate)exitState.GetValue(x), Delegate.CreateDelegate(exitDelegateType, typeof(LyumaAv3Runtime).GetMethod(nameof(SetupExitState), BindingFlags.Static | BindingFlags.Public))));
		}

		public static void SetupEnterState(object playAudio, Animator animator)
		{
			Type audioSubType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			Type applySettingsType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio+ApplySettings, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			Type randomOrderType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio+Order, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			LyumaAv3Runtime runtime;
			if (!getTopLevelRuntime("VRCAnimatorPlayAudio", animator, out runtime)) {
				return;
			}
			if (!runtime)
			{
				return;
			}
			if (runtime.IsMirrorClone && runtime.IsShadowClone) {
				return;
			}

			int index = runtime.vrcPlayAudios.IndexOf(playAudio);
			if (index == -1)
			{
				index = runtime.vrcPlayAudios.Count;
				runtime.vrcPlayAudios.Add(playAudio);
			}
			if (runtime.DebugDuplicateAnimator != VRCAvatarDescriptor.AnimLayerType.Base && index % 2 == 0)
			{
				return; // We add a copy of every state behaviour, which is fine when operations are idempotent, but since these are not, we need to ignore every second SetupEnterState, since those get called on both behaviours in succession.
			}

			AudioSource audioSource = animator.transform.Find((string)audioSubType.GetField("SourcePath").GetValue(playAudio))?.GetComponent<AudioSource>();
			if (audioSource == null)
			{
				return;
			}

			if ((bool)audioSubType.GetField("StopOnEnter").GetValue(playAudio))
			{
				audioSource.Stop();
			}

			object alwaysApply = applySettingsType.GetEnumValues().GetValue(0);
			object applyIfStopped = applySettingsType.GetEnumValues().GetValue(1);

			if (audioSubType.GetField("LoopApplySettings").GetValue(playAudio).ToString() == alwaysApply.ToString() ||
			    audioSubType.GetField("LoopApplySettings").GetValue(playAudio).ToString() == applyIfStopped.ToString() &&
			    !audioSource.isPlaying)
			{
				audioSource.loop = (bool)audioSubType.GetField("Loop").GetValue(playAudio);
			}

			if (audioSubType.GetField("PitchApplySettings").GetValue(playAudio).ToString() == alwaysApply.ToString() ||
			    audioSubType.GetField("PitchApplySettings").GetValue(playAudio).ToString() == applyIfStopped.ToString() &&
			    !audioSource.isPlaying)
			{
				Vector2 pitch = (Vector2)audioSubType.GetField("Pitch").GetValue(playAudio);
				audioSource.pitch = UnityEngine.Random.Range(pitch.x, pitch.y);
			}
			
			if (audioSubType.GetField("VolumeApplySettings").GetValue(playAudio).ToString() == alwaysApply.ToString() ||
			    audioSubType.GetField("VolumeApplySettings").GetValue(playAudio).ToString() == applyIfStopped.ToString() &&
			    !audioSource.isPlaying)
			{
				Vector2 volume = (Vector2)audioSubType.GetField("Volume").GetValue(playAudio);
				audioSource.volume = UnityEngine.Random.Range(volume.x, volume.y);
			}

			AudioClip[] clips = (AudioClip[])audioSubType.GetField("Clips").GetValue(playAudio);
			if (clips.Length > 0 && 
			    (audioSubType.GetField("ClipsApplySettings").GetValue(playAudio).ToString() == alwaysApply.ToString() ||
			     audioSubType.GetField("ClipsApplySettings").GetValue(playAudio).ToString() == applyIfStopped.ToString() && !audioSource.isPlaying))
			{
				AudioClip clip = null;
				
				object Random = randomOrderType.GetEnumValues().GetValue(0);
				object UniqueRandom = randomOrderType.GetEnumValues().GetValue(1);
				object Roundabout = randomOrderType.GetEnumValues().GetValue(2);
				object Parameter = randomOrderType.GetEnumValues().GetValue(3);
				object playbackOrder = audioSubType.GetField("PlaybackOrder").GetValue(playAudio);
				FieldInfo playbackIndex = audioSubType.GetField("playbackIndex");
				if (playbackOrder.ToString() == Random.ToString())
				{
					int newPlayIndex = UnityEngine.Random.Range(0, clips.Length);
					playbackIndex.SetValue(playAudio, newPlayIndex);
					clip = clips[newPlayIndex];
				}

				else if (playbackOrder == UniqueRandom)
				{
					int newPlayIndex = UnityEngine.Random.Range(0, clips.Length);
					while (newPlayIndex == (int)playbackIndex.GetValue(playAudio) && clips.Length > 1)
					{
						newPlayIndex = UnityEngine.Random.Range(0, clips.Length);
					}
					playbackIndex.SetValue(playAudio, newPlayIndex);
					clip = clips[newPlayIndex];
				}

				else if (playbackOrder == Roundabout)
				{
					int newPlayIndex = ((int)playbackIndex.GetValue(playAudio) + 1) % clips.Length;
					playbackIndex.SetValue(playAudio, newPlayIndex);
					clip = clips[newPlayIndex];
				}
				
				else if (playbackOrder == Parameter)
				{
					string parameterName = (string)audioSubType.GetField("ParameterName").GetValue(playAudio);
					int? newPlayIndex = runtime.Ints.FirstOrDefault(x => x.name == parameterName)?.value;
					if (newPlayIndex.HasValue && newPlayIndex.Value < clips.Length)
					{
						playbackIndex.SetValue(playAudio, newPlayIndex.Value);
						clip = clips[newPlayIndex.Value];
					}
				}
				audioSource.clip = clip;
			}


			if ((bool)audioSubType.GetField("PlayOnEnter").GetValue(playAudio) && audioSource.clip != null)
			{
				audioSource.PlayDelayed((float)audioSubType.GetField("DelayInSeconds").GetValue(playAudio));
			}
		}

		public static void SetupExitState(object playAudio, Animator animator)
		{
			Type audioSubType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio, VRCSDKBase, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
			LyumaAv3Runtime runtime;
			if (!getTopLevelRuntime("VRCAnimatorPlayAudio", animator, out runtime)) {
				return;
			}
			if (!runtime)
			{
				return;
			}
			if (runtime.IsMirrorClone && runtime.IsShadowClone) {
				return;
			}
				
			int index = runtime.vrcPlayAudios.IndexOf(playAudio);
			if (index == -1)
			{
				runtime.vrcPlayAudios.Add(playAudio);
				index = runtime.vrcPlayAudios.IndexOf(playAudio);
				if (index % 2 == 0)
				{
					return;
				}
			}

			AudioSource audioSource = animator.transform.Find((string)audioSubType.GetField("SourcePath").GetValue(playAudio))?.GetComponent<AudioSource>();
			if (audioSource == null)
			{
				return;
			}
				
			if ((bool)audioSubType.GetField("StopOnExit").GetValue(playAudio))
			{
				audioSource.Stop();
			}

			if ((bool)audioSubType.GetField("PlayOnExit").GetValue(playAudio))
			{
				audioSource.Play();
			}
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
			if (MirrorClone) {
				GameObject.Destroy(MirrorClone.gameObject);
				MirrorClone = null;
			}
			if (ShadowClone) {
				GameObject.Destroy(ShadowClone.gameObject);
				ShadowClone = null;
			}
			if (OriginalSourceClone) {
				GameObject.Destroy(OriginalSourceClone.gameObject);
				OriginalSourceClone = null;
			}
			foreach (LyumaAv3Runtime clone in NonLocalClones) {
				GameObject.Destroy(clone.gameObject);
			}
			NonLocalClones.Clear();
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
				GameObject srcGO = GameObject.Find(SourceObjectPath);
				if (srcGO == null) {
					AvatarSyncSource = this;
					UnityEngine.Object.Destroy(this.gameObject);
				} else {
					AvatarSyncSource = srcGO.GetComponent<LyumaAv3Runtime>();
				}
			}

			IsLocal = AvatarSyncSource == this;
			
			if (this.emulator != null) {
				DebugDuplicateAnimator = this.emulator.DefaultAnimatorToDebug;
				ViewAnimatorOnlyNoParams = this.emulator.DefaultAnimatorToDebug;
			}

			animator = GetComponent<Animator>();
			if (animator == null)
			{
				animator = gameObject.AddComponent<Animator>();
			}
			else if (this.emulator == null || !this.emulator.IsLegacyAwakeUsed())
			{
				// legacy Awake() mode doesn't recreate the Animator for... legacy reasons.
				// (the assumption is that Awake() runs before the Animator records defaults)

				// But in the new version which uses Start(), we do recreate the Animator.
				// Supposedly this is needed to workaround some Animator glitch caused by VRCFury
				// to prevent the animator from baking defaults from before VRCFury build runs.
				var controller = animator.runtimeAnimatorController;
				var avatar = animator.avatar;
				var applyRootMotion = animator.applyRootMotion;
				var updateMode = animator.updateMode;
				var cullingMode = animator.cullingMode;
				DestroyImmediate(animator);
				animator = gameObject.AddComponent<Animator>();
				animator.applyRootMotion = applyRootMotion;
				animator.updateMode = updateMode;
				animator.cullingMode = cullingMode;
				animator.avatar = avatar;
				animator.runtimeAnimatorController = controller;
			}

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
			if (avadesc.customEyeLookSettings.eyelidsSkinnedMesh != null) {
				if (avadesc.customEyeLookSettings.eyelidsBlendshapes.Length > 0) {
					blinkBlendShapeIdx = avadesc.customEyeLookSettings.eyelidsBlendshapes[0];
				}
				if (avadesc.customEyeLookSettings.eyelidsBlendshapes.Length > 2) {
					lookUpBlendShapeIdx = avadesc.customEyeLookSettings.eyelidsBlendshapes[1];
					lookDownBlendShapeIdx = avadesc.customEyeLookSettings.eyelidsBlendshapes[2];
				}
			}
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
			if (!IsMirrorClone && !IsShadowClone && AvatarSyncSource == this) {
				convertDynamicBones(this.gameObject);
				if (this.emulator != null) {
					if (this.emulator.DescriptorColliders != DescriptorCollidersSendersHelper.DescriptorExtractionType.None) {
						if (this.emulator.ForceUpdateDescriptorColliders) {
							forceUpdateDescriptorColliders(avadesc);
						}
						DescriptorCollidersSendersHelper.ExtractDescriptorColliders(avadesc, this.emulator.DescriptorColliders);
					}
				}
			}
			bool shouldClone = false;
			if (OriginalSourceClone == null) {
				OriginalSourceClone = this;
				shouldClone = true;
			}
			if (shouldClone && GetComponents<Component>().All(x => x.GetType().Name != "PipelineSaver")) {
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

			AvatarHeight = avadesc.ViewPosition.y;
			DefaultViewPosition = avadesc.ViewPosition;
			DefaultAvatarScale = gameObject.transform.localScale;

			ParentConstraints = gameObject.GetComponentsInChildren<ParentConstraint>(true)
				.Select(constraint => (constraint, constraint.translationOffsets.Select(offset =>
				{
					Vector3 lossyScale = constraint.transform.lossyScale;
					return new Vector3(offset.x / lossyScale.x, offset.y / lossyScale.y, offset.z / lossyScale.z); // We want offsets to be in world space;
				}).ToArray())).ToArray();
			ClothComponents = gameObject.GetComponentsInChildren<Cloth>(true).ToArray();

			if (this.IsMirrorClone || this.IsShadowClone)
			{
				contactPlayerId = this.AvatarSyncSource.contactPlayerId;
			}
			else
			{
				contactPlayerId = globalContactPlayerId++;
			}

			if (this.emulator.EnablePlayerContactPermissions)
			{
				ContactBase[] contacts = gameObject.GetComponentsInChildren<ContactBase>(true).ToArray();
				if (!IsLocal)
				{
					foreach (ContactBase contact in contacts)
					{
						if (contact is ContactReceiver receiver)
						{
							if (receiver.localOnly) GameObject.DestroyImmediate(contact);
						}
					}
				}
				else
				{
					foreach (ContactBase contact in contacts) contact.playerId = contactPlayerId;
				}

			}
		}

		public void CreateMirrorClone() {
			if (IsMirrorClone || IsShadowClone)
			{
				throw new InvalidOperationException("Mirror/shadow clones cannot create a mirror clone");
			}
			if (AvatarSyncSource == this && GetComponents<Component>().All(x => x.GetType().Name != "PipelineSaver"))
			{
				OriginalSourceClone.IsMirrorClone = true;
				MirrorClone = GameObject.Instantiate(OriginalSourceClone.gameObject).GetComponent<LyumaAv3Runtime>();
				MirrorClone.gameObject.hideFlags = HideFlags.NotEditable;
				MirrorClone.GetComponent<Animator>().avatar = null;
				OriginalSourceClone.IsMirrorClone = false;
				GameObject o = MirrorClone.gameObject;
				o.name = gameObject.name + " (MirrorReflection)";
				o.SetActive(true);
				allMirrorTransforms = MirrorClone.gameObject.GetComponentsInChildren<Transform>(true);
				foreach (Transform t in allMirrorTransforms)
				{
					foreach (Component component in t.GetComponents<Component>().Reverse())
					{
						if (!component || CloneComponentIsBlacklisted(component, MirrorCloneComponentBlacklist))
						{
							DestroyImmediate(component);
						}
					}
				}
			}
		}

		public void CreateShadowClone() {
			if (IsMirrorClone || IsShadowClone)
			{
				throw new InvalidOperationException("Mirror/shadow clones cannot create a shadow clone");
			}
			if (AvatarSyncSource == this && GetComponents<Component>().All(x => x.GetType().Name != "PipelineSaver")) {
				OriginalSourceClone.IsShadowClone = true;
				ShadowClone = GameObject.Instantiate(OriginalSourceClone.gameObject).GetComponent<LyumaAv3Runtime>();
				ShadowClone.gameObject.hideFlags = HideFlags.NotEditable;
				ShadowClone.GetComponent<Animator>().avatar = null;
				OriginalSourceClone.IsShadowClone = false;
				GameObject o = ShadowClone.gameObject;
				o.name = gameObject.name + " (ShadowClone)";
				o.SetActive(true);
				allShadowTransforms = ShadowClone.gameObject.GetComponentsInChildren<Transform>(true);
				foreach (Transform t in allShadowTransforms)
				{
					foreach(Component component in t.GetComponents<Component>().Reverse())
						if (!component || CloneComponentIsBlacklisted(component, ShadowCloneComponentBlacklist))
						{
							DestroyImmediate(component);
						}
						else if (component.GetType() == typeof(SkinnedMeshRenderer) || component.GetType() == typeof(MeshRenderer))
						{
							Renderer renderer = component as Renderer;
							renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; // ShadowCastingMode.TwoSided isn't accounted for and does not work locally
						}
				}

				foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true)) {
					renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // ShadowCastingMode.TwoSided isn't accounted for and does not work locally
				}
			}
		}

		public bool CloneComponentIsBlacklisted(Component component, Type[] typeBlacklist)
		{
			var type = component.GetType();
			return typeBlacklist.Any(bt => type.IsSubclassOf(bt) || type == bt) || CloneStringComponentBlacklist.Any(bt => type.ToString().Contains(bt));
		}

		private void InitializeAnimator()
		{
			ResetAvatar = false;
			PrevAnimatorToDebug = (char)(int)DebugDuplicateAnimator;
			ViewAnimatorOnlyNoParams = DebugDuplicateAnimator;

			animator = GetOrAddComponent<Animator>(this.gameObject);
			animator.avatar = animatorAvatar;
			animator.applyRootMotion = false;
			animator.updateMode = AnimatorUpdateMode.Normal;
			animator.cullingMode = (this == AvatarSyncSource || IsMirrorClone || IsShadowClone) ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullCompletely;
			animator.runtimeAnimatorController = null;

			avadesc = this.gameObject.GetComponent<VRCAvatarDescriptor>();
			IKTrackingOutputData.ViewPosition = avadesc.ViewPosition;
			IKTrackingOutputData.AvatarScaleFactorGuess = IKTrackingOutputData.ViewPosition.magnitude / BASE_HEIGHT; // mostly guessing...
			IKTrackingOutputData.HeadRelativeViewPosition = IKTrackingOutputData.ViewPosition;
			if (animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
			{
				head = animator.GetBoneTransform(HumanBodyBones.Head);
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
			//	 if (AnimatorToDebug == cal.type) {
			//		 allLayers.Add(cal);
			//	 }
			// }
			// foreach (VRCAvatarDescriptor.CustomAnimLayer cal in speciallayers) {
			//	 if (AnimatorToDebug == cal.type) {
			//		 allLayers.Add(cal);
			//	 }
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
				AvatarMask mask = null;
				if (vrcAnimLayer.isDefault) {
					ac = animLayerToDefaultController[vrcAnimLayer.type];
				} else
				{
					ac = vrcAnimLayer.animatorController;
					mask = vrcAnimLayer.mask;
				}
				if (vrcAnimLayer.isDefault || (mask == null && vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.FX)) {
					//defaults to a mask that prevents muscle overrides.
					if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.FX) {
						mask = LyumaAv3Masks.emptyMask;
					} else if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.Gesture) {
						mask = LyumaAv3Masks.handsOnly;
					} else if (vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.TPose || vrcAnimLayer.type == VRCAvatarDescriptor.AnimLayerType.IKPose) {
						mask = LyumaAv3Masks.musclesOnly;
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
			ParameterNames.Clear();
			IntToIndex.Clear();
			FloatToIndex.Clear();
			BoolToIndex.Clear();
			playableParamterFloats.Clear();
			playableParamterTypes.Clear();
			playableParamterIds.Clear();
			playableParamterInts.Clear();
			playableParamterBools.Clear();
			return stageNameToValue;
		}
		void LateRefreshExpressionParameters(Dictionary<string, float> stageNameToValue) {
			HashSet<string> usedparams = new HashSet<string>();
			int i = 0;
			if (stageParameters != null)
			{
				int stageId = 0;
				foreach (var stageParam in stageParameters.parameters)
				{
					stageId++; // one-indexed
					bool networkSynced = true;
					FieldInfo field = stageParam.GetType().GetField("networkSynced");
					if (field != null)
					{
						networkSynced = (bool) field.GetValue(stageParam);
					}
					if (stageParam.name == null || stageParam.name.Length == 0) {
						continue;
					}
					string stageName = stageParam.name + (networkSynced ? stageParam.saved ? " (saved/SYNCED)" : " (SYNCED)" : stageParam.saved ? " (saved/local)" : " (local)"); //"Stage" + stageId;
					float lastDefault = stageParam.defaultValue;
					if ((AvatarSyncSource == this || !networkSynced) && stageParam.saved && KeepSavedParametersOnReset && stageNameToValue.ContainsKey(stageName)) {
						lastDefault = stageNameToValue[stageName];
					}
					if (ParameterNames.Contains(stageParam.name)) {
						Debug.LogWarning("Duplicate Expression Parameter Found: " + stageName + ", using the first one in the Expression Parameters.");
						continue;
					}
					ParameterNames.Add(stageParam.name);
					if ((int)stageParam.valueType == 0)
					{
						IntParam param = new IntParam();
						param.stageName = stageName;
						param.synced = networkSynced;
						param.name = stageParam.name;
						param.value = (int)lastDefault;
						param.lastValue = null;
						IntToIndex[param.name] = Ints.Count;
						Ints.Add(param);
					}
					else if ((int)stageParam.valueType == 1)
					{
						FloatParam param = new FloatParam();
						param.stageName = stageName;
						param.synced = networkSynced;
						param.name = stageParam.name;
						param.value = lastDefault;
						param.exportedValue = lastDefault;
						param.lastValue = null;
						FloatToIndex[param.name] = Floats.Count;
						Floats.Add(param);
					}
					else if ((int)stageParam.valueType == 2)
					{
						BoolParam param = new BoolParam();
						param.stageName = stageName;
						param.synced = networkSynced;
						param.name = stageParam.name;
						param.value = lastDefault != 0.0;
						param.lastValue = null;
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
			playableParamterTypes.Clear();
			foreach (AnimatorControllerPlayable playable in playables) {
				Dictionary<string, int> parameterIndices = new Dictionary<string, int>();
				Dictionary<string, AnimatorControllerParameterType> parameterTypes = new Dictionary<string, AnimatorControllerParameterType>();
				playableParamterInts.Add(new Dictionary<int, int>());
				playableParamterFloats.Add(new Dictionary<int, float>());
				playableParamterBools.Add(new Dictionary<int, bool>());
				// Debug.Log("SETUP index " + whichcontroller + " len " + playables.Count);
				playableParamterIds.Add(parameterIndices);
				playableParamterTypes.Add(parameterTypes);
				int pcnt = playable.IsValid() ? playable.GetParameterCount() : 0;
				for (i = 0; i < pcnt; i++) {
					AnimatorControllerParameter aparam = playable.GetParameter(i);
					string actualName = aparam.name;
					parameterIndices[actualName] = aparam.nameHash;
					parameterTypes[actualName] = aparam.type;
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
			foreach (var comp in avadesc.gameObject.GetComponents<GestureManagerAv3Menu>()) {
				UnityEngine.Object.Destroy(comp);
			}
			foreach (var comp in avadesc.gameObject.GetComponents<LyumaAv3Menu>()) {
				UnityEngine.Object.Destroy(comp);
			}
			LyumaAv3Menu mainMenu;
			mainMenu = avadesc.gameObject.AddComponent<GestureManagerAv3Menu>();
			if (emulator != null) {
				mainMenu.useLegacyMenu = emulator.disableRadialMenu;
			}
			mainMenu.Runtime = this;
			mainMenu.RootMenu = avadesc.expressionsMenu;
		}


		private (MeshRenderer, MeshRenderer, MeshRenderer)[] rendererCache;
		private (SkinnedMeshRenderer, SkinnedMeshRenderer, SkinnedMeshRenderer)[] skinnedRendererCache;
		private int frameIndex = 0;
		public void SetupCloneCaches()
		{
			if (rendererCache != null)
			{
				return;
			}
			if (MirrorClone != null) {
				allMirrorTransforms = MirrorClone.gameObject.GetComponentsInChildren<Transform>(true);
			}
			if (ShadowClone != null) {
				allShadowTransforms = ShadowClone.gameObject.GetComponentsInChildren<Transform>(true);
			}
			List<(MeshRenderer, MeshRenderer, MeshRenderer)> renderers = new List<(MeshRenderer, MeshRenderer, MeshRenderer)>();
			List<(SkinnedMeshRenderer, SkinnedMeshRenderer, SkinnedMeshRenderer)> skinnedRenderers = new List<(SkinnedMeshRenderer, SkinnedMeshRenderer, SkinnedMeshRenderer)>();
			for (int i = 0; i < allTransforms.Length; i++)
			{
				MeshRenderer baseRenderer = allTransforms[i].GetComponent<MeshRenderer>();
				if (baseRenderer != null)
				{
					MeshRenderer mirrorRenderer = null;
					if (allMirrorTransforms != null && allMirrorTransforms.Length > i) {
						mirrorRenderer = allMirrorTransforms[i].GetComponent<MeshRenderer>();
					}
					MeshRenderer shadowRenderer = null;
					if (allShadowTransforms != null && allShadowTransforms.Length > i)
					{
						shadowRenderer = allShadowTransforms[i].GetComponent<MeshRenderer>();
					}

					if (mirrorRenderer != null || shadowRenderer != null)
					{
						renderers.Add((baseRenderer, mirrorRenderer, shadowRenderer));
					}
				}
				
				SkinnedMeshRenderer skinnedBaseRenderer = allTransforms[i].GetComponent<SkinnedMeshRenderer>();
				if (skinnedBaseRenderer != null)
				{
					SkinnedMeshRenderer skinnedMirrorRenderer = null;
					if (allMirrorTransforms != null && allMirrorTransforms.Length > i) {
						skinnedMirrorRenderer = allMirrorTransforms[i].GetComponent<SkinnedMeshRenderer>();
					}
					SkinnedMeshRenderer skinnedShadowRenderer = null;
					if (allShadowTransforms != null && allShadowTransforms.Length > i)
					{
						skinnedShadowRenderer = allShadowTransforms[i].GetComponent<SkinnedMeshRenderer>();
					}

					if (skinnedMirrorRenderer != null || skinnedShadowRenderer != null)
					{
						skinnedRenderers.Add((skinnedBaseRenderer, skinnedMirrorRenderer, skinnedShadowRenderer));
					}
				}
			}
			rendererCache = renderers.ToArray();
			skinnedRendererCache = skinnedRenderers.ToArray();
		}
		
		private bool isResetting;
		private bool isResettingHold;
		private bool isResettingSel;
		void LateUpdate() {
			if (VisitOurGithub != LyumaAv3Emulator.GIT_REPO) {
				VisitOurGithub = LyumaAv3Emulator.GIT_REPO;
			}
			if (ViewREADMEManual) {
				ViewREADMEManual = false;
				updateSelectionDelegate(LyumaAv3Emulator.READMEAsset, 0);
			}
			if (ViewChangelog) {
				ViewChangelog = false;
				updateSelectionDelegate(LyumaAv3Emulator.CHANGELOGAsset, 0);
			}
			if (ViewMITLicense) {
				ViewMITLicense = false;
				updateSelectionDelegate(LyumaAv3Emulator.LICENSEAsset, 0);
			}
			if (SendBugsOrFeedback) {
				SendBugsOrFeedback = false;
				Application.OpenURL(LyumaAv3Emulator.BUG_TRACKER_URL);
			}
			if (ResetAndHold || (emulator != null && (!emulator.enabled || !emulator.gameObject.activeInHierarchy))) {
				return;
			}
			if (IsMirrorClone || IsShadowClone) {
				// Experimental. Attempt to reproduce the 1-frame desync in some cases between normal and mirror copy.
				NormalUpdate();
			}
			UpdateVisemeBlendShapes();
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
				if (rendererCache == null || rendererCache.Length == 0) {
					SetupCloneCaches();
				}
				foreach (Transform[] allXTransforms in new Transform[][]{allMirrorTransforms, allShadowTransforms}) {
					if (allXTransforms != null) {
						for(int i = 0; i < allTransforms.Length && i < allXTransforms.Length; i++) {
							if (allXTransforms[i] == null || allTransforms[i] == this.transform) {
								continue;
							}
							if (allTransforms[i].localPosition == allTransforms[i].localPosition) {
								// self-comparison to prevent copying NaN. See issue #215
								allXTransforms[i].localPosition = allTransforms[i].localPosition;
							}
							if (allTransforms[i].localRotation == allTransforms[i].localRotation) {
								// self-comparison to prevent copying NaN. See issue #215
								allXTransforms[i].localRotation = allTransforms[i].localRotation;
							}
							if(allTransforms[i] == head && EnableHeadScaling) {
								allXTransforms[i].localScale = new Vector3(1.0f, 1.0f, 1.0f);
							} else {
								if (allTransforms[i].localScale == allTransforms[i].localScale) {
									// self-comparison to prevent copying NaN. See issue #215
									allXTransforms[i].localScale = allTransforms[i].localScale;
								}
							}
							bool theirs = allTransforms[i].gameObject.activeSelf;
							if (allXTransforms[i].gameObject.activeSelf != theirs) {
								allXTransforms[i].gameObject.SetActive(theirs);
							}
						}
					}
				}
				
				foreach (var (baseR, shadowR, mirrorR) in rendererCache)
				{
					Material[] baseMaterials = baseR.sharedMaterials;
					if (shadowR != null)
					{
						Material[] shadowMaterials = shadowR.sharedMaterials;
						for (int mri = 0; mri < shadowMaterials.Length && mri < baseMaterials.Length; mri++)
						{
							shadowMaterials[mri] = baseMaterials[mri];
						}
						shadowR.sharedMaterials = shadowMaterials;
					}
					if (mirrorR != null)
					{
						Material[] mirrorMaterials = mirrorR.sharedMaterials;
						for (int mri = 0; mri < mirrorMaterials.Length && mri < baseMaterials.Length; mri++)
						{
							mirrorMaterials[mri] = baseMaterials[mri];
						}
						mirrorR.sharedMaterials = mirrorMaterials;
					} 
				}
				
				foreach (var (baseR, shadowR, mirrorR) in skinnedRendererCache)
				{
					Material[] baseMaterials = baseR.sharedMaterials;
					if (shadowR != null)
					{
						Material[] shadowMaterials = shadowR.sharedMaterials;
						for (int mri = 0; mri < shadowMaterials.Length && mri < baseMaterials.Length; mri++)
						{
							shadowMaterials[mri] = baseMaterials[mri];
						}
						shadowR.sharedMaterials = shadowMaterials;
					}
					if (mirrorR != null)
					{
						Material[] mirrorMaterials = mirrorR.sharedMaterials;
						for (int mri = 0; mri < mirrorMaterials.Length && mri < baseMaterials.Length; mri++)
						{
							mirrorMaterials[mri] = baseMaterials[mri];
						}
						mirrorR.sharedMaterials = mirrorMaterials;
					} 
				}
			}
		}

		public void PreCullVisualOffset(Camera cam) {
			if (!IsCurrentlyVisuallyOffset) {
				SavedPosition = transform.position;
				transform.position += VisualOffset;
				IsCurrentlyVisuallyOffset = true;
			}
		}
		public void PostRenderVisualOffset(Camera cam) {
			if (IsCurrentlyVisuallyOffset) {
				transform.position = SavedPosition;
				IsCurrentlyVisuallyOffset = false;
			}
		}

		void UpdateVisemeBlendShapes() {
			if (IKTrackingOutputData.trackingMouthAndJaw != VRC_AnimatorTrackingControl.TrackingType.Animation) {
				PerformLipSyncVisemesTracking();
			}
			if (IKTrackingOutputData.trackingEyesAndEyelids != VRC_AnimatorTrackingControl.TrackingType.Animation) {
				PerformEyesAndEyeLidsTracking();
			}
		}

		void PerformLipSyncVisemesTracking() {
			if (avadesc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone && avadesc.lipSyncJawBone != null) {
				if (Viseme == VisemeIndex.sil) {
					avadesc.lipSyncJawBone.transform.localRotation = avadesc.lipSyncJawClosed;
				} else {
					avadesc.lipSyncJawBone.transform.localRotation = avadesc.lipSyncJawOpen;
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

		void PerformEyesAndEyeLidsTracking() {
			float xysum = Mathf.Sqrt(EyeTargetX * EyeTargetX + EyeTargetY * EyeTargetY);
			float xabs = Mathf.Abs(EyeTargetX);
			float yabs = Mathf.Abs(EyeTargetY);
			bool blink = false;
			int interval = (int)(10 + 40.0f / (BlinkRate * BlinkRate));
			if (BlinkRate > 0.00001f && interval > 0 && (frameIndex % interval) < 60 && ((frameIndex % interval) % 30) < 10) {
				blink = true;
			}
			if (BlinkRate > 0.99999f) {
				blink = true;
			}
			if (avadesc.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes && avadesc.customEyeLookSettings.eyelidsSkinnedMesh != null) {
				if (blinkBlendShapeIdx != -1) {
					avadesc.customEyeLookSettings.eyelidsSkinnedMesh.SetBlendShapeWeight(blinkBlendShapeIdx, blink ? 100.0f : 0.0f);
				}
				if (lookDownBlendShapeIdx != -1) {
					avadesc.customEyeLookSettings.eyelidsSkinnedMesh.SetBlendShapeWeight(lookDownBlendShapeIdx,
							-100.0f * Mathf.Clamp(EyeTargetY, -1.0f, 0.0f));
				}
				if (lookUpBlendShapeIdx != -1) {
					avadesc.customEyeLookSettings.eyelidsSkinnedMesh.SetBlendShapeWeight(lookUpBlendShapeIdx,
							100.0f * Mathf.Clamp(EyeTargetY, 0.0f, 1.0f));
				}
			}
			if (avadesc.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Bones) {
				VRCAvatarDescriptor.CustomEyeLookSettings.EyelidRotations rotations = EyeTargetY < 0 ? avadesc.customEyeLookSettings.eyelidsLookingDown : avadesc.customEyeLookSettings.eyelidsLookingUp;
				if (avadesc.customEyeLookSettings.upperLeftEyelid != null) {
					avadesc.customEyeLookSettings.upperLeftEyelid.localRotation = Quaternion.Slerp(
						Quaternion.Slerp(
							avadesc.customEyeLookSettings.eyelidsDefault.upper.left,
							rotations.upper.left,
							yabs),
						avadesc.customEyeLookSettings.eyelidsClosed.upper.left,
						blink ? 100.0f : 0.0f);
				}
				if (avadesc.customEyeLookSettings.upperRightEyelid != null) {
					avadesc.customEyeLookSettings.upperRightEyelid.localRotation = Quaternion.Slerp(
						Quaternion.Slerp(
							avadesc.customEyeLookSettings.eyelidsDefault.upper.right,
							rotations.upper.right,
							yabs),
						avadesc.customEyeLookSettings.eyelidsClosed.upper.right,
						blink ? 100.0f : 0.0f);
				}
				if (avadesc.customEyeLookSettings.lowerLeftEyelid != null) {
					avadesc.customEyeLookSettings.lowerLeftEyelid.localRotation = Quaternion.Slerp(
						Quaternion.Slerp(
							avadesc.customEyeLookSettings.eyelidsDefault.lower.left,
							rotations.lower.left,
							yabs),
						avadesc.customEyeLookSettings.eyelidsClosed.lower.left,
						blink ? 100.0f : 0.0f);
				}
				if (avadesc.customEyeLookSettings.lowerRightEyelid != null) {
					avadesc.customEyeLookSettings.lowerRightEyelid.localRotation = Quaternion.Slerp(
						Quaternion.Slerp(
							avadesc.customEyeLookSettings.eyelidsDefault.lower.right,
							rotations.lower.right,
							yabs),
						avadesc.customEyeLookSettings.eyelidsClosed.lower.right,
						blink ? 100.0f : 0.0f);
				}

			}
			VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations leftright = EyeTargetX < 0 ? avadesc.customEyeLookSettings.eyesLookingLeft : avadesc.customEyeLookSettings.eyesLookingRight;
			VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations updown = EyeTargetY < 0 ? avadesc.customEyeLookSettings.eyesLookingDown : avadesc.customEyeLookSettings.eyesLookingUp;
			if (avadesc.customEyeLookSettings.leftEye != null) {
				avadesc.customEyeLookSettings.leftEye.localRotation = Quaternion.Slerp(
						avadesc.customEyeLookSettings.eyesLookingStraight.left,
						Quaternion.Slerp(leftright.left, updown.left, yabs / (0.00001f + xabs + yabs)),
						Mathf.Clamp(xysum, 0.0f, 1.0f));
			}
			if (avadesc.customEyeLookSettings.rightEye != null) {
				avadesc.customEyeLookSettings.rightEye.localRotation = Quaternion.Slerp(
						avadesc.customEyeLookSettings.eyesLookingStraight.right,
						Quaternion.Slerp(leftright.right, updown.right, yabs / (0.00001f + xabs + yabs)),
						Mathf.Clamp(xysum, 0.0f, 1.0f));
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

		private Vector3 lastMousePosition;

		void Update() {
			if (!(IsMirrorClone || IsShadowClone) && frameIndex == 1) {
				playableGraph.Play();
				if (updateSelectionDelegate != null && AvatarSyncSource == this && emulator != null && emulator.SelectAvatarOnStartup) {
					updateSelectionDelegate(this.gameObject, 2);
				}
			}
			if ((IsMirrorClone || IsShadowClone) && frameIndex == 2) {
				playableGraph.Play();
			}
			frameIndex += 1;
			if (animator == null)
			{
				animator = GetOrAddComponent<Animator>(this.gameObject);
			}
			if (broadcastStartNextFrame) {
				Debug.Log("BROADCASTING START!");
				broadcastStartNextFrame = false;
				BroadcastMessage("Start");
			}
			if (emulator != null && (!emulator.enabled || !emulator.gameObject.activeInHierarchy)) {
				return;
			}
			if (Input.mousePosition != lastMousePosition && emulator.HaveEyesFollowMouse) {
				lastMousePosition = Input.mousePosition;
				EyeTargetX = Mathf.Clamp(-2.0f * (Input.mousePosition.x / (Screen.width + 1) - 0.5f), -1.0f, 1.0f);
				EyeTargetY = Mathf.Clamp(2.0f * (Input.mousePosition.y / (Screen.height + 1) - 0.5f), -1.0f, 1.0f);
				// NaNs can be produced when using "Play Unfocused"
				if (float.IsNaN(EyeTargetX)) EyeTargetX = 0f;
				if (float.IsNaN(EyeTargetY)) EyeTargetY = 0f;
			}
			if (!IsMirrorClone && !IsShadowClone) {
				NormalUpdate();
			}
		}

		// Update is called once per frame
		void NormalUpdate()
		{
			if (AvatarSyncSource == this) {
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
			}
			if (isResettingSel) {
				isResettingSel = false;
				if (updateSelectionDelegate != null && AvatarSyncSource == this) {
					updateSelectionDelegate(this.gameObject, 1);
					PrevAnimatorToViewLiteParamsShow0 = (char)126;
				}
			}
			if (isResettingHold && (!ResetAvatar || !ResetAndHold)) {
				ResetAndHold = ResetAvatar = false;
				isResettingSel = true;
				if (updateSelectionDelegate != null && AvatarSyncSource == this) {
					updateSelectionDelegate(this.emulator != null ? this.emulator.gameObject : null, 1);
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
				frameIndex = 0;
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
					updateSelectionDelegate(this.emulator != null ? this.emulator.gameObject : null, 1);
				}
				isResetting = true;
				isResettingSel = true;
				return;
			}
			if (PrevAnimatorToViewLiteParamsShow0 == (char)127) {
				updateSelectionDelegate(this, 1);
				// ViewAnimatorOnlyNoParams = (VRCAvatarDescriptor.AnimLayerType)(int)126;
				PrevAnimatorToViewLiteParamsShow0 = (char)(int)ViewAnimatorOnlyNoParams;
			}
			if ((char)(int)ViewAnimatorOnlyNoParams != PrevAnimatorToViewLiteParamsShow0) {
				PrevAnimatorToViewLiteParamsShow0 = (char)127;
				RuntimeAnimatorController rac = null;
				allControllers.TryGetValue(ViewAnimatorOnlyNoParams, out rac);
				updateAnimatorWindowDelegate(rac);
			}
			if (RefreshExpressionParams) {
				RefreshExpressionParams = false;
				Dictionary<string, float> stageNameToValue = EarlyRefreshExpressionParameters();
				LateRefreshExpressionParameters(stageNameToValue);
			}
			if(this == AvatarSyncSource || IsMirrorClone || IsShadowClone) {
				IsOnFriendsList = false;
			}
			if(this == AvatarSyncSource && !IsMirrorClone && !IsShadowClone && animator.avatar != null) {
				if (head != null) {
					if (defaultHeadScale == new Vector3(0, 0, 0))
					{
						defaultHeadScale = head.localScale;
					}
					head.localScale = EnableHeadScaling ? new Vector3(0.0001f, 0.0001f, 0.0001f) : defaultHeadScale; // head bone is set to 0.0001 locally (not multiplied

					Type headChopType = Type.GetType("VRC.SDK3.Avatars.Components.VRCHeadChop, VRCSDK3A, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
					Type headChopBoneType = Type.GetType("VRC.SDK3.Avatars.Components.VRCHeadChop+HeadChopBone, VRCSDK3A, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
					if (headChopType != null)
					{
						Vector3 div(Vector3 x, Vector3 y) => new Vector3(x.x / y.x, x.y / y.y, x.z / y.z);
						Vector3 mul(Vector3 x, Vector3 y) => new Vector3(x.x * y.x, x.y * y.y, x.z * y.z);

						if (headChopData == null)
						{
							headChops = GetComponentsInChildren(headChopType, true);
							headChopData = new Dictionary<Transform, HeadChopDataStorage>();
							for (var i = 0; i < headChops.Length; i++)
							{
								object[] bones = (object[]) headChopType.GetField("targetBones").GetValue(headChops[i]);
								for (var j = 0; j < bones.Length; j++)
								{
									object bone = bones[j];
									Transform t = (Transform)headChopBoneType.GetField("transform").GetValue(bone);
									if (t == null) continue;
									headChopData[t] = new HeadChopDataStorage()
									{
										originalLocalPosition = t.localPosition,
										originalGlobalHeadOffset = mul(avadesc.transform.InverseTransformPoint(t.position), avadesc.transform.lossyScale) - head.transform.position,
										originalLocalScale = t.localScale,
										originalGlobalScale = t.lossyScale
									};
								}
							}
						}
						for (var i = 0; i < headChops.Length; i++)
						{
							object headChop = headChops[i];
							object[] bones = (object[]) headChopType.GetField("targetBones").GetValue(headChops[i]);
							for (var j = 0; j < bones.Length; j++)
							{
								object bone = bones[j];
								Transform t = (Transform)headChopBoneType.GetField("transform").GetValue(bone);
								if (t == null) continue;
								bool canApply = (bool)headChopBoneType.GetMethod("CanApply").Invoke(bone, new object[] { VRMode });
								float scaleFactor = (float)headChopBoneType.GetField("scaleFactor").GetValue(bone);
								float globalScaleFactor = (float)headChopType.GetField("globalScaleFactor").GetValue(headChops[i]);
								
								float desiredScaleFactor = scaleFactor * globalScaleFactor;

								var data = headChopData[t];
								Vector3 originalLocalPosition = data.originalLocalPosition;
								Vector3 originalRootSpacePosition = head.position + head.rotation * data.originalGlobalHeadOffset;
								Vector3 originalLocalScale = data.originalLocalScale;
								Vector3 originalGlobalScale = data.originalGlobalScale;

								
								if (EnableHeadScaling && canApply && ((Behaviour)headChop).isActiveAndEnabled)
								{
									Vector3 originalParentGlobalScale = div(originalGlobalScale, originalLocalScale);
									Vector3 targetLocalScaleComparedToOldParentScale = (originalLocalScale * desiredScaleFactor);
									Vector3 targetGlobalScale = mul(originalParentGlobalScale, targetLocalScaleComparedToOldParentScale);
									Vector3 unclampedTargetLocalScale = div(targetGlobalScale, t.parent.lossyScale);
									
									
									t.localScale = new Vector3(Mathf.Max(unclampedTargetLocalScale.x, 0.0001f), Mathf.Max(unclampedTargetLocalScale.y, 0.0001f), Mathf.Max(unclampedTargetLocalScale.z, 0.0001f));
									t.position = originalRootSpacePosition - avadesc.transform.position;
								}
								else
								{
									t.localScale = originalLocalScale;
									t.localPosition = originalLocalPosition;
								}
							}
						}
					}
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
			if (!DisableMirrorAndShadowClones && !IsMirrorClone && !IsShadowClone && MirrorClone == null && ShadowClone == null && AvatarSyncSource == this) {
				CreateMirrorClone();
				CreateShadowClone();
				SetupCloneCaches();
			}
			if (emulator != null && AvatarSyncSource == this) {
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
					osc = GetOrAddComponent<LyumaAv3Osc>(emulator.gameObject);
					osc.openSocket = true;
					osc.avatarDescriptor = avadesc;
					osc.enabled = true;
					OSCController = osc;
					// updateSelectionDelegate(osc.gameObject);
				}
			}

			if (CreateNonLocalClone) {
				CreateNonLocalClone = false;
				AvatarSyncSource.CloneCount++;
				Vector3 oldOffset = VisualOffset;
				if (emulator != null) {
					OriginalSourceClone.IsOnFriendsList = emulator.ClonesAreOnFriendsList;
				}
				OriginalSourceClone.VisualOffset = AvatarSyncSource.CloneCount * new Vector3(0.4f, 0.0f, 0.4f);
				GameObject go = GameObject.Instantiate(OriginalSourceClone.gameObject);
				go.hideFlags = 0;
				go.name = go.name.Substring(0, go.name.Length - 7) + " (Non-Local " + AvatarSyncSource.CloneCount + ")";
				go.SetActive(true);
				OriginalSourceClone.VisualOffset = new Vector3(0,0,0);
				NonLocalClones.Add(go.GetComponent<LyumaAv3Runtime>());
			}
			NonLocalSyncInterval = AvatarSyncSource.NonLocalSyncInterval;
			if (nextUpdateTime == 0.0f) {
				nextUpdateTime = Time.time + NonLocalSyncInterval;
			}
			bool ShouldSyncThisFrame = (AvatarSyncSource != this && (Time.time >= nextUpdateTime || NonLocalSyncInterval <= 0.0f));
			if (AvatarSyncSource != this && !IsMirrorClone && !IsShadowClone) {
				IKSyncRadialMenu = AvatarSyncSource.IKSyncRadialMenu;
				LyumaAv3Menu[] menus = AvatarSyncSource.GetComponents<LyumaAv3Menu>();
				for (int i = 0; i < Ints.Count; i++) {
					if (ParameterNames.Contains(Ints[i].name)) {
						// Simulate IK sync of open gesture parameter.
						if (ShouldSyncThisFrame || (IKSyncRadialMenu && menus.Length >= 1 && menus[0].IsControlIKSynced(Ints[i].name))
								|| (IKSyncRadialMenu && menus.Length >= 2 && menus[1].IsControlIKSynced(Ints[i].name))) {
							if (AvatarSyncSource.Ints[i].synced)
							{
								Ints[i].value = ClampByte(AvatarSyncSource.Ints[i].value);
							}
						}
					}
				}
				for (int i = 0; i < Floats.Count; i++) {
					if (ParameterNames.Contains(Floats[i].name)) {
						// Simulate IK sync of open gesture parameter.
						if (ShouldSyncThisFrame || (IKSyncRadialMenu && menus.Length >= 1 && menus[0].IsControlIKSynced(Floats[i].name))
								|| (IKSyncRadialMenu && menus.Length >= 2 && menus[1].IsControlIKSynced(Floats[i].name))) {
							if (AvatarSyncSource.Floats[i].synced)
							{
								Floats[i].exportedValue = ClampAndQuantizeFloat(AvatarSyncSource.Floats[i].exportedValue);
								Floats[i].value = Floats[i].exportedValue;
							}
						}
					}
				}
				for (int i = 0; i < Bools.Count; i++) {
					if (ParameterNames.Contains(Bools[i].name)) {
						if (ShouldSyncThisFrame) {
							if (AvatarSyncSource.Bools[i].synced)
							{
								Bools[i].value = AvatarSyncSource.Bools[i].value;
							}
						}
					}
				}
				if (ShouldSyncThisFrame) {
					nextUpdateTime = Time.time + NonLocalSyncInterval;
				}
			}

			if ((IsShadowClone || IsMirrorClone) && AvatarSyncSource != null)
			{
				for (int i = 0; i < Ints.Count && i < AvatarSyncSource.Ints.Count; i++)
				{
					Ints[i].value = AvatarSyncSource.Ints[i].value;
				}
				for (int i = 0; i < Floats.Count && i < AvatarSyncSource.Floats.Count; i++) { 
					Floats[i].value = AvatarSyncSource.Floats[i].value;
					Floats[i].exportedValue = AvatarSyncSource.Floats[i].exportedValue;
				}
				for (int i = 0; i < Bools.Count && i < AvatarSyncSource.Bools.Count; i++) {
					Bools[i].value = AvatarSyncSource.Bools[i].value;
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
				BlinkRate = AvatarSyncSource.BlinkRate;
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
				Earmuffs = AvatarSyncSource.Earmuffs;
				IsAnimatorEnabled = AvatarSyncSource.IsAnimatorEnabled;
				PreviewMode = AvatarSyncSource.PreviewMode;
				InStation = AvatarSyncSource.InStation;
				EnableAvatarScaling = AvatarSyncSource.EnableAvatarScaling;
				AvatarHeight = AvatarSyncSource.AvatarHeight;
			}
			
			foreach (BuiltinParameterDefinition definition in BUILTIN_PARAMETERS)
			{
				if (definition.type == VRCExpressionParameters.ValueType.Bool)
				{
					var param = Bools.FirstOrDefault(x => x.name == definition.name);
					if (param != null) param.value = (bool)definition.valueGetter(this);
				}
				if (definition.type == VRCExpressionParameters.ValueType.Int)
				{
					var param = Ints.FirstOrDefault(x => x.name == definition.name);
					if (param != null) param.value = (int)definition.valueGetter(this);
				}
				if (definition.type == VRCExpressionParameters.ValueType.Float)
				{
					var param = Floats.FirstOrDefault(x => x.name == definition.name);
					if (param != null) param.value = (float)definition.valueGetter(this);
				}
			}
			
			for (int i = 0; i < Floats.Count; i++) {
				if (Floats[i].expressionValue != Floats[i].lastExpressionValue_) {
					Floats[i].exportedValue = Floats[i].expressionValue;
					Floats[i].lastExpressionValue_ = Floats[i].expressionValue;
				}
				if (ParameterNames.Contains(Floats[i].name) && Floats[i].synced) {
					if (locally8bitQuantizedFloats) {
						Floats[i].exportedValue = ClampAndQuantizeFloat(Floats[i].exportedValue);
					} else {
						Floats[i].exportedValue = ClampFloatOnly(Floats[i].exportedValue);
					}
				}
				if (ParameterNames.Contains(Floats[i].name)) {
					Floats[i].value = Floats[i].exportedValue;
				}
			}
			for (int i = 0; i < Ints.Count; i++) {
				if (ParameterNames.Contains(Ints[i].name) && Ints[i].synced) {
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
			if (IKTrackingOutputData.trackingMouthAndJaw == VRC_AnimatorTrackingControl.TrackingType.Animation)
			{
				// In VRC, when you set the Mouth & Jaw to Animation, it will freeze the viseme value. This replicates that behaviour
				// See https://github.com/lyuma/Av3Emulator/issues/109
				Viseme = (VisemeIndex)VisemeInt;
				VisemeIdx = VisemeInt;
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
				if (GestureLeftWeight > 0.01f && (GestureLeftIdx == 0 || GestureLeftWeight < 0.99f)) {
					GestureLeftIdx = 1;
				} 
			}
			if (GestureRightWeight != OldGestureRightWeight) {
				OldGestureRightWeight = GestureRightWeight;
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
			float scale = 1.0f;
			if (EnableAvatarScaling) {
				scale = AvatarHeight / DefaultViewPosition.y;
			}

			Vector3 oldScale = gameObject.transform.localScale;
			
			gameObject.transform.localScale = DefaultAvatarScale * scale;
			avadesc.ViewPosition = DefaultViewPosition * scale;

			bool scaleChanged = oldScale != gameObject.transform.localScale;
			
			if (emulator.DisableParentConstraintOffsetScaling)
			{
				foreach ((ParentConstraint constraint, Vector3[] offsets) in ParentConstraints)
				{
					constraint.translationOffsets = offsets.Select(original =>
						new Vector3(original.x * constraint.transform.lossyScale.x,
							original.y * constraint.transform.lossyScale.y,
							original.z * constraint.transform.lossyScale.z)).ToArray();
				}
			}

			if (emulator.EnableClothScalingFix)
			{
				if (scaleChanged)
				{
					foreach (Cloth cloth in ClothComponents)
					{
						if (cloth.enabled)
						{
							cloth.enabled = false;
							cloth.enabled = true;	
						}
					}
				}
			}

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
				Dictionary<string, AnimatorControllerParameterType> parameterTypes = playableParamterTypes[whichcontroller];
				int paramid;
				foreach (FloatParam param in Floats)
				{
					if (parameterIndices.TryGetValue(param.name, out paramid))
					{
						if (param.value != param.lastValue) {
							AnimatorControllerParameterType outType = parameterTypes[param.name];
							SetTypeWithMismatch(playable, paramid, param.value, outType);
							// playable.SetFloat(paramid, param.value);
						}
					}
				}
				foreach (IntParam param in Ints)
				{
					if (parameterIndices.TryGetValue(param.name, out paramid))
					{
						if (param.value != param.lastValue)
						{
							AnimatorControllerParameterType outType = parameterTypes[param.name];
							SetTypeWithMismatch(playable, paramid, param.value, outType);
							// playable.SetInteger(paramid, param.value);
						}
					}
				}
				foreach (BoolParam param in Bools)
				{
					if (parameterIndices.TryGetValue(param.name, out paramid))
					{
						if (param.value != param.lastValue)
						{
							AnimatorControllerParameterType outType = parameterTypes[param.name];
							// playable.SetBool(paramid, param.value); // also sets triggers.
							SetTypeWithMismatch(playable, paramid, param.value, outType);
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
				Dictionary<string, AnimatorControllerParameterType> parameterTypes = playableParamterTypes[whichcontroller];
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
						AnimatorControllerParameterType inType = parameterTypes[param.name];
						if (paramterFloats.TryGetValue(paramid, out fparam))
						{
							float f = (float)GetTypeWithMismatch(playable, paramid, inType, AnimatorControllerParameterType.Float);
							if (fparam != f) {
								param.value = f;
								param.lastValue = param.value;
								if (!playable.IsParameterControlledByCurve(paramid)) {
									param.exportedValue = param.value;
								}
							}
							paramterFloats[paramid] = param.value;
						}
					}
				}
				foreach (IntParam param in Ints)
				{
					if (parameterIndices.TryGetValue(param.name, out paramid))
					{
						AnimatorControllerParameterType inType = parameterTypes[param.name];
						if (paramterInts.TryGetValue(paramid, out iparam)) {
							int i = (int)GetTypeWithMismatch(playable, paramid, inType, AnimatorControllerParameterType.Int);
							if (iparam != i) {
								param.value = i;
								param.lastValue = param.value;
							}
							paramterInts[paramid] = param.value;
						}
					}
				}
				foreach (BoolParam param in Bools)
				{
					if (param.hasBool[whichcontroller] && parameterIndices.TryGetValue(param.name, out paramid))
					{
						AnimatorControllerParameterType inType = parameterTypes[param.name];
						if (paramterBools.TryGetValue(paramid, out bparam)) {
							bool b = (bool)GetTypeWithMismatch(playable, paramid, inType, AnimatorControllerParameterType.Bool);
							if (bparam != b) {
								param.value = b;
								param.lastValue = param.value;
							}
							paramterBools[paramid] = param.value;
						}
					}
				}
				
				foreach (var builtinParam in BUILTIN_PARAMETERS)
				{
					if (parameterIndices.TryGetValue(builtinParam.name, out paramid))
					{
						AnimatorControllerParameterType inType = parameterTypes[builtinParam.name];
						SetTypeWithMismatch(playable, paramid, builtinParam.valueGetter(this), inType);
						if (builtinParam.type == VRCExpressionParameters.ValueType.Bool)
						{
							paramterBools[paramid] = (bool)builtinParam.valueGetter(this);
						}

						if (builtinParam.type == VRCExpressionParameters.ValueType.Float)
						{
							paramterFloats[paramid] = (float)builtinParam.valueGetter(this);
						}

						if (builtinParam.type == VRCExpressionParameters.ValueType.Int)
						{
							paramterInts[paramid] = (int)builtinParam.valueGetter(this);
						}
					}
				}
				
				
				if (parameterIndices.TryGetValue("AvatarVersion", out paramid)) {
					AnimatorControllerParameterType inType = parameterTypes["AvatarVersion"];
					SetTypeWithMismatch(playable, paramid, AvatarVersion, inType);
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
			UpdateVisemeBlendShapes();
		}

		void SetTypeWithMismatch(AnimatorControllerPlayable playable, int id, object value, AnimatorControllerParameterType outType)
		{
			if (playable.IsParameterControlledByCurve(id))
			{
				return;
			}
			if (value is float floatValue)
			{
				switch (outType)
				{
					case AnimatorControllerParameterType.Bool:
						playable.SetBool(id, floatValue != 0.0f);
						break;
					case AnimatorControllerParameterType.Float:
						playable.SetFloat(id, floatValue);
						break;
					case AnimatorControllerParameterType.Int:
						playable.SetInteger(id, (int)Math.Round(floatValue, 0));
						break;
					case AnimatorControllerParameterType.Trigger:
						playable.SetTrigger(id);
						break;
				}
			}
			if (value is int intValue)
			{
				switch (outType)
				{
					case AnimatorControllerParameterType.Bool:
						playable.SetBool(id, intValue != 0.0f);
						break;
					case AnimatorControllerParameterType.Float:
						playable.SetFloat(id, (float)intValue);
						break;
					case AnimatorControllerParameterType.Int:
						playable.SetInteger(id, intValue);
						break;
					case AnimatorControllerParameterType.Trigger:
						playable.SetTrigger(id);
						break;
				}
			}
			if (value is bool boolValue)
			{
				switch (outType)
				{
					case AnimatorControllerParameterType.Bool:
						playable.SetBool(id, boolValue);
						break;
					case AnimatorControllerParameterType.Float:
						playable.SetFloat(id, boolValue ? 1.0f : 0.0f);
						break;
					case AnimatorControllerParameterType.Int:
						playable.SetInteger(id, boolValue ? 1 : 0);
						break;
					case AnimatorControllerParameterType.Trigger:
						playable.SetTrigger(id);
						break;
				}
			}
		}

		object GetTypeWithMismatch(AnimatorControllerPlayable playable, int id, AnimatorControllerParameterType inType, AnimatorControllerParameterType outType)
		{
			switch (inType)
			{
				case AnimatorControllerParameterType.Float:
					float floatValue = playable.GetFloat(id);
					switch (outType)
					{
						case AnimatorControllerParameterType.Bool:
							return floatValue != 0.0f;
						case AnimatorControllerParameterType.Float:
							return floatValue;
						case AnimatorControllerParameterType.Int:
							return (int)Math.Round(floatValue, 0);
					}
					return null;
				case AnimatorControllerParameterType.Int:
					int intValue = playable.GetInteger(id);
					switch (outType)
					{
						case AnimatorControllerParameterType.Bool:
							return intValue != 0;
						case AnimatorControllerParameterType.Float:
							return (float)intValue;
						case AnimatorControllerParameterType.Int:
							return intValue;
					}
					return null;
				case AnimatorControllerParameterType.Bool:
					bool boolValue = playable.GetBool(id);
					switch (outType)
					{
						case AnimatorControllerParameterType.Bool: 
							return boolValue;
						case AnimatorControllerParameterType.Float:
							return boolValue ? 1.0f : 0.0f; 
						case AnimatorControllerParameterType.Int:
							return boolValue ? 1 : 0;
					}
					return null;
			}
			return null;
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

		private HashSet<string> warnedParams = new HashSet<string>();

		public void GetOSCDataInto(List<A3ESimpleOSC.OSCMessage> messages) {
			messages.Add(new A3ESimpleOSC.OSCMessage {
				arguments = new object[1] {(object)OSCConfigurationFile.OSCAvatarID},
				path="/avatar/change",
				time = new Vector2Int(-1,-1),
			});
			foreach (var prop in DataToShoveIntoOSCAnyway) {
				messages.Add(new A3ESimpleOSC.OSCMessage {
					arguments = new object[1] {prop.Value},
					path = "/avatar/parameters/" + prop.Key,
					time = new Vector2Int(-1,-1),
				});
			}
			DataToShoveIntoOSCAnyway.Clear();
			if (OSCConfigurationFile.SendRecvAllParamsNotInJSON) {
				foreach (var b in Bools) {
					messages.Add(new A3ESimpleOSC.OSCMessage {
						arguments = new object[1] {(object)(int)((bool)b.value ? 1 : 0)},
						path = "/avatar/parameters/" + b.name,
						time = new Vector2Int(-1,-1),
					});
				}
				foreach (var i in Ints) {
					messages.Add(new A3ESimpleOSC.OSCMessage {
						arguments = new object[1] {(object)(int)i.value},
						path = "/avatar/parameters/" + i.name,
						time = new Vector2Int(-1,-1),
					});
				}
				foreach (var f in Floats) {
					messages.Add(new A3ESimpleOSC.OSCMessage {
						arguments = new object[1] {(object)(float)f.value},
						path = "/avatar/parameters/" + f.name,
						time = new Vector2Int(-1,-1),
					});
				}
			} else {
				foreach (var prop in OSCConfigurationFile.OSCJsonConfig.parameters) {
					if (prop.name != null && prop.name.Length > 0 && prop.output.address != null && prop.output.address.Length > 0) {
						string addr = prop.output.address;
						float outputf = 0.0f;
						string typ = "?";
						if (BoolToIndex.TryGetValue(prop.name, out var bidx)) {
							outputf = Bools[bidx].value ? 1.0f : 0.0f;
							typ = "bool";
						} else if (IntToIndex.TryGetValue(prop.name, out var iidx)) {
							outputf = (float)Ints[iidx].value;
							typ = "int";
						} else if (FloatToIndex.TryGetValue(prop.name, out var fidx)) {
							outputf = Floats[fidx].value;
							typ = "float";
						} else {
							float scale = 1.0f;
							if (EnableAvatarScaling) {
								scale = AvatarHeight / DefaultViewPosition.y;
							}

							BuiltinParameterDefinition oscParam =
								BUILTIN_PARAMETERS.FirstOrDefault(x => x.name == prop.name);
							if (oscParam == null)
							{
								if (!warnedParams.Contains(prop.name)) {
									Debug.LogWarning("Unrecognized OSC param " + prop.name);
									warnedParams.Add(prop.name);
								}
							}
							else
							{
								object value = oscParam.valueGetter(this);
								if (value is float f)
								{
									outputf = f;
								}

								if (value is int i)
								{
									outputf = i;
								}

								if (value is bool b)
								{
									outputf = b ? 1.0f : 0.0f;
								}
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
				if (!warnedParams.Contains(ParamName)) {
					Debug.LogWarning("Unrecognized OSC input command " + ParamName);
					warnedParams.Add(ParamName);
				}
				break;
			}
		}

		public void ProcessOSCBuiltinParamInputMessage(BuiltinParameterDefinition param, LyumaAv3Runtime runtime, object input)
		{
			if (param.type == VRCExpressionParameters.ValueType.Bool)
			{
				bool argBool = isObjectTrue(input);
				param.valueSetter(runtime, argBool);
			}

			if (param.type == VRCExpressionParameters.ValueType.Float)
			{
				float argFloat = getObjectFloat(input);
				param.valueSetter(runtime, argFloat);
			}

			if (param.type == VRCExpressionParameters.ValueType.Int)
			{
				int argInt = getObjectInt(input);
				param.valueSetter(runtime, argInt);
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
				string ParamName = "";
				if (msgPath.StartsWith("/input/")) {
					ParamName = msgPath.Split(new char[]{'/'}, 3)[2];
					processOSCInputMessage(ParamName, arguments[0]);
				} else {
					if (OSCConfigurationFile.SendRecvAllParamsNotInJSON) {
						if (msgPath.StartsWith("/avatar/parameters/")) {
							ParamName = msgPath.Split(new char[]{'/'}, 4)[3];
						}
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
						if (msgPath.StartsWith("/avatar/parameters/")) {
							ParamName = msgPath.Split(new char[]{'/'}, 4)[3];
						}

						BuiltinParameterDefinition messageParameter =
							BUILTIN_PARAMETERS.FirstOrDefault(x => x.name == ParamName);
						if (messageParameter != null) {
							if (!messageParameter.warned)
							{
								Debug.LogWarning("Setting built-in OSC " + msgPath + " isn't respected in game. Consider using /input/ paths instead.");
								messageParameter.warned = true;
							}
							ProcessOSCBuiltinParamInputMessage(messageParameter, this, arguments[0]);
						} else if (!IntToIndex.ContainsKey(ParamName) && !BoolToIndex.ContainsKey(ParamName) && !FloatToIndex.ContainsKey(ParamName)) {
							if (LogOSCWarnings) { //if (!ParamName.EndsWith("_Angle") && !ParamName.EndsWith("_IsGrabbed") && !ParamName.EndsWith("_Stretch")) {
								Debug.LogWarning("Address " + msgPath + " not found for input in JSON.");
							}
							continue;
						}
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
}
