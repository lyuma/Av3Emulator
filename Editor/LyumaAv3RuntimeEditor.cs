using System;
using Lyuma.Av3Emulator.Runtime;
using UnityEditor;
using UnityEngine;

namespace Lyuma.Av3Emulator.Editor
{
	[CustomEditor(typeof(LyumaAv3Runtime))]
	public class LyumaAv3RuntimeEditor : UnityEditor.Editor
	{
		#region SerializedProperties
		public SerializedProperty OriginalSourceClone;
		public SerializedProperty ResetAvatar;
		public SerializedProperty ResetAndHold;
		public SerializedProperty RefreshExpressionParams;
		public SerializedProperty KeepSavedParametersOnReset;
		public SerializedProperty DebugDuplicateAnimator;
		public SerializedProperty ViewAnimatorOnlyNoParams;
		public SerializedProperty SourceObjectPath;
		public SerializedProperty AvatarSyncSource;
		public SerializedProperty EnableAvatarOSC;
		public SerializedProperty LogOSCWarnings;
		public SerializedProperty OSCController;
		public SerializedProperty OSCConfigurationFile;
		public SerializedProperty CreateNonLocalClone;
		public SerializedProperty locally8bitQuantizedFloats;
		public SerializedProperty NonLocalSyncInterval;
		public SerializedProperty IKSyncRadialMenu;
		public SerializedProperty EnableHeadScaling;
		public SerializedProperty DisableMirrorAndShadowClones;
		public SerializedProperty MirrorClone;
		public SerializedProperty ShadowClone;
		public SerializedProperty NonLocalClones;
		public SerializedProperty DebugOffsetMirrorClone;
		public SerializedProperty ViewMirrorReflection;
		public SerializedProperty ViewBothRealAndMirror;
		public SerializedProperty avadesc;
		public SerializedProperty Viseme;
		public SerializedProperty VisemeIdx;
		public SerializedProperty Voice;
		public SerializedProperty GestureLeft;
		public SerializedProperty GestureLeftIdx;
		public SerializedProperty GestureLeftWeight;
		public SerializedProperty GestureRight;
		public SerializedProperty GestureRightIdx;
		public SerializedProperty GestureRightWeight;
		public SerializedProperty Velocity;
		public SerializedProperty AngularY;
		public SerializedProperty Upright;
		public SerializedProperty Grounded;
		public SerializedProperty Jump;
		public SerializedProperty JumpPower;
		public SerializedProperty RunSpeed;
		public SerializedProperty Seated;
		public SerializedProperty AFK;
		public SerializedProperty TPoseCalibration;
		public SerializedProperty IKPoseCalibration;
		public SerializedProperty TrackingType;
		public SerializedProperty TrackingTypeIdx;
		public SerializedProperty VRMode;
		public SerializedProperty MuteSelf;
		public SerializedProperty Earmuffs;
		public SerializedProperty InStation;
		public SerializedProperty AvatarVersion;
		public SerializedProperty EnableAvatarScaling;
		public SerializedProperty AvatarHeight;
		public SerializedProperty VisualOffset;
		public SerializedProperty IsOnFriendsList;
		public SerializedProperty IsLocal;
		public SerializedProperty IsMirrorClone;
		public SerializedProperty IsShadowClone;
		public SerializedProperty LocomotionIsDisabled;
		public SerializedProperty IKTrackingOutputData;
		public SerializedProperty Floats;
		public SerializedProperty Ints;
		public SerializedProperty Bools;
		public SerializedProperty VisitOurGithub;
		public SerializedProperty ViewREADMEManual;
		public SerializedProperty ViewChangelog;
		public SerializedProperty ViewMITLicense;
		public SerializedProperty SendBugsOrFeedback;
		//public SerializedProperty emulator;

		private void RefreshSerializedProperties()
		{
			OriginalSourceClone = serializedObject.FindProperty("OriginalSourceClone");
			ResetAvatar = serializedObject.FindProperty("ResetAvatar");
			ResetAndHold = serializedObject.FindProperty("ResetAndHold");
			RefreshExpressionParams = serializedObject.FindProperty("RefreshExpressionParams");
			KeepSavedParametersOnReset = serializedObject.FindProperty("KeepSavedParametersOnReset");
			DebugDuplicateAnimator = serializedObject.FindProperty("DebugDuplicateAnimator");
			ViewAnimatorOnlyNoParams = serializedObject.FindProperty("ViewAnimatorOnlyNoParams");
			SourceObjectPath = serializedObject.FindProperty("SourceObjectPath");
			AvatarSyncSource = serializedObject.FindProperty("AvatarSyncSource");
			EnableAvatarOSC = serializedObject.FindProperty("EnableAvatarOSC");
			LogOSCWarnings = serializedObject.FindProperty("LogOSCWarnings");
			OSCController = serializedObject.FindProperty("OSCController");
			OSCConfigurationFile = serializedObject.FindProperty("OSCConfigurationFile");
			CreateNonLocalClone = serializedObject.FindProperty("CreateNonLocalClone");
			locally8bitQuantizedFloats = serializedObject.FindProperty("locally8bitQuantizedFloats");
			NonLocalSyncInterval = serializedObject.FindProperty("NonLocalSyncInterval");
			IKSyncRadialMenu = serializedObject.FindProperty("IKSyncRadialMenu");
			EnableHeadScaling = serializedObject.FindProperty("EnableHeadScaling");
			DisableMirrorAndShadowClones = serializedObject.FindProperty("DisableMirrorAndShadowClones");
			MirrorClone = serializedObject.FindProperty("MirrorClone");
			ShadowClone = serializedObject.FindProperty("ShadowClone");
			NonLocalClones = serializedObject.FindProperty("NonLocalClones");
			DebugOffsetMirrorClone = serializedObject.FindProperty("DebugOffsetMirrorClone");
			ViewMirrorReflection = serializedObject.FindProperty("ViewMirrorReflection");
			ViewBothRealAndMirror = serializedObject.FindProperty("ViewBothRealAndMirror");
			avadesc = serializedObject.FindProperty("avadesc");
			Viseme = serializedObject.FindProperty("Viseme");
			VisemeIdx = serializedObject.FindProperty("VisemeIdx");
			Voice = serializedObject.FindProperty("Voice");
			GestureLeft = serializedObject.FindProperty("GestureLeft");
			GestureLeftIdx = serializedObject.FindProperty("GestureLeftIdx");
			GestureLeftWeight = serializedObject.FindProperty("GestureLeftWeight");
			GestureRight = serializedObject.FindProperty("GestureRight");
			GestureRightIdx = serializedObject.FindProperty("GestureRightIdx");
			GestureRightWeight = serializedObject.FindProperty("GestureRightWeight");
			Velocity = serializedObject.FindProperty("Velocity");
			AngularY = serializedObject.FindProperty("AngularY");
			Upright = serializedObject.FindProperty("Upright");
			Grounded = serializedObject.FindProperty("Grounded");
			Jump = serializedObject.FindProperty("Jump");
			JumpPower = serializedObject.FindProperty("JumpPower");
			RunSpeed = serializedObject.FindProperty("RunSpeed");
			Seated = serializedObject.FindProperty("Seated");
			AFK = serializedObject.FindProperty("AFK");
			TPoseCalibration = serializedObject.FindProperty("TPoseCalibration");
			IKPoseCalibration = serializedObject.FindProperty("IKPoseCalibration");
			TrackingType = serializedObject.FindProperty("TrackingType");
			TrackingTypeIdx = serializedObject.FindProperty("TrackingTypeIdx");
			VRMode = serializedObject.FindProperty("VRMode");
			MuteSelf = serializedObject.FindProperty("MuteSelf");
			Earmuffs = serializedObject.FindProperty("Earmuffs");
			InStation = serializedObject.FindProperty("InStation");
			AvatarVersion = serializedObject.FindProperty("AvatarVersion");
			EnableAvatarScaling = serializedObject.FindProperty("EnableAvatarScaling");
			AvatarHeight = serializedObject.FindProperty("AvatarHeight");
			VisualOffset = serializedObject.FindProperty("VisualOffset");
			IsOnFriendsList = serializedObject.FindProperty("IsOnFriendsList");
			IsLocal = serializedObject.FindProperty("IsLocal");
			IsMirrorClone = serializedObject.FindProperty("IsMirrorClone");
			IsShadowClone = serializedObject.FindProperty("IsShadowClone");
			LocomotionIsDisabled = serializedObject.FindProperty("LocomotionIsDisabled");
			IKTrackingOutputData = serializedObject.FindProperty("IKTrackingOutputData");
			Floats = serializedObject.FindProperty("Floats");
			Ints = serializedObject.FindProperty("Ints");
			Bools = serializedObject.FindProperty("Bools");
			VisitOurGithub = serializedObject.FindProperty("VisitOurGithub");
			ViewREADMEManual = serializedObject.FindProperty("ViewREADMEManual");
			ViewChangelog = serializedObject.FindProperty("ViewChangelog");
			ViewMITLicense = serializedObject.FindProperty("ViewMITLicense");
			SendBugsOrFeedback = serializedObject.FindProperty("SendBugsOrFeedback");
			//emulator = serializedObject.FindProperty("emulator");
		}
		#endregion

		#region Foldout Variables
		private static bool resetAndRefreshFoldout;
		private static bool animatorToDebugFoldout;
		private static bool OSCFoldout;
		private static bool networkClonesAndSyncFoldout;
		private static bool playerLocalAndMirrorReflectionFoldout;
		private static bool builtInInputsFoldout;
		private static bool visemeFoldout;
		private static bool handGestureFoldout;
		private static bool locomotionFoldout;
		private static bool trackingSetupAndOtherFoldout;
		private static bool userInputsFoldout;
		private static bool outputStateFoldout;
		private static bool creditsAndLinksFoldout;
		#endregion
		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			RefreshSerializedProperties();
			EditorGUILayout.PropertyField(OriginalSourceClone);
			EditorGUILayout.Space();

			DrawFoldout("Reset and Refresh", ref resetAndRefreshFoldout, DrawResetAndRefreshGUI);
			DrawFoldout("Animator to Debug", ref animatorToDebugFoldout, DrawAnimatorToDebugGUI);
			DrawFoldout("OSC", ref OSCFoldout, DrawOSCGUI);
			DrawFoldout("Network Clones and Sync", ref networkClonesAndSyncFoldout, DrawNetworkClonesAndSyncGUI);
			DrawFoldout("Player Local and Mirror Reflection", ref playerLocalAndMirrorReflectionFoldout, DrawPlayerLocalAndMirrorReflectionGUI);
			DrawFoldout("Built-in Inputs", ref builtInInputsFoldout, DrawBuiltInInputsGUI);
			DrawFoldout("User Inputs", ref userInputsFoldout, DrawUserInputsGUI);
			DrawFoldout("Output State (Read-Only)", ref outputStateFoldout, DrawOutputStateGUI);
			DrawFoldout("Credits and Links", ref creditsAndLinksFoldout, DrawCreditsAndLinksGUI);
			//Not sure why this is visible in base inspector? It's set automatically
			//EditorGUILayout.PropertyField(emulator);

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawBuiltInInputsGUI()
		{
			DrawFoldout("Viseme", ref visemeFoldout, DrawVisemeGUI);
			DrawFoldout("Hand Gesture", ref handGestureFoldout, DrawHandGestureGUI);
			DrawFoldout("Locomotion", ref locomotionFoldout, DrawLocomotionGUI);
			DrawFoldout("Tracking Setup and Other", ref trackingSetupAndOtherFoldout, DrawTrackingSetupAndOtherGUI);
		}

		private void DrawUserInputsGUI()
		{
			EditorGUILayout.PropertyField(Floats);
			EditorGUILayout.PropertyField(Ints);
			EditorGUILayout.PropertyField(Bools);
		}

		private void DrawResetAndRefreshGUI()
		{
			EditorGUILayout.PropertyField(ResetAvatar);
			EditorGUILayout.PropertyField(ResetAndHold);
			EditorGUILayout.PropertyField(RefreshExpressionParams);
			EditorGUILayout.PropertyField(KeepSavedParametersOnReset);
		}

		private void DrawAnimatorToDebugGUI()
		{
			EditorGUILayout.PropertyField(DebugDuplicateAnimator);
			EditorGUILayout.PropertyField(ViewAnimatorOnlyNoParams);
			EditorGUILayout.PropertyField(SourceObjectPath);
			EditorGUILayout.PropertyField(AvatarSyncSource);
		}

		private void DrawOSCGUI()
		{
			EditorGUILayout.PropertyField(EnableAvatarOSC);
			EditorGUILayout.PropertyField(LogOSCWarnings);
			EditorGUILayout.PropertyField(OSCController);
			EditorGUILayout.PropertyField(OSCConfigurationFile);
		}

		private void DrawNetworkClonesAndSyncGUI()
		{
			EditorGUILayout.PropertyField(CreateNonLocalClone);
			EditorGUILayout.PropertyField(locally8bitQuantizedFloats);
			EditorGUILayout.PropertyField(NonLocalSyncInterval);
			EditorGUILayout.PropertyField(IKSyncRadialMenu);
		}

		private void DrawPlayerLocalAndMirrorReflectionGUI()
		{
			EditorGUILayout.PropertyField(EnableHeadScaling);
			EditorGUILayout.PropertyField(DisableMirrorAndShadowClones);
			EditorGUILayout.PropertyField(MirrorClone);
			EditorGUILayout.PropertyField(ShadowClone);
			EditorGUILayout.PropertyField(NonLocalClones);
			EditorGUILayout.PropertyField(DebugOffsetMirrorClone);
			EditorGUILayout.PropertyField(ViewMirrorReflection);
			EditorGUILayout.PropertyField(ViewBothRealAndMirror);
			EditorGUILayout.PropertyField(avadesc);
		}

		private void DrawVisemeGUI()
		{
			EditorGUILayout.PropertyField(Viseme);
			EditorGUILayout.PropertyField(VisemeIdx);
			EditorGUILayout.PropertyField(Voice);
		}

		private void DrawHandGestureGUI()
		{
			EditorGUILayout.PropertyField(GestureLeft);
			EditorGUILayout.PropertyField(GestureLeftIdx);
			EditorGUILayout.PropertyField(GestureLeftWeight);
			EditorGUILayout.PropertyField(GestureRight);
			EditorGUILayout.PropertyField(GestureRightIdx);
			EditorGUILayout.PropertyField(GestureRightWeight);
		}

		private void DrawLocomotionGUI()
		{
			EditorGUILayout.PropertyField(Velocity);
			EditorGUILayout.PropertyField(AngularY);
			EditorGUILayout.PropertyField(Upright);
			EditorGUILayout.PropertyField(Grounded);
			EditorGUILayout.PropertyField(Jump);
			EditorGUILayout.PropertyField(JumpPower);
			EditorGUILayout.PropertyField(RunSpeed);
			EditorGUILayout.PropertyField(Seated);
			EditorGUILayout.PropertyField(AFK);
			EditorGUILayout.PropertyField(TPoseCalibration);
			EditorGUILayout.PropertyField(IKPoseCalibration);
		}

		private void DrawTrackingSetupAndOtherGUI()
		{
			EditorGUILayout.PropertyField(TrackingType);
			EditorGUILayout.PropertyField(TrackingTypeIdx);
			EditorGUILayout.PropertyField(VRMode);
			EditorGUILayout.PropertyField(MuteSelf);
			EditorGUILayout.PropertyField(Earmuffs);
			EditorGUILayout.PropertyField(InStation);
			EditorGUILayout.PropertyField(AvatarVersion);
			EditorGUILayout.PropertyField(EnableAvatarScaling);
			EditorGUILayout.PropertyField(AvatarHeight);
			EditorGUILayout.PropertyField(VisualOffset);
			EditorGUILayout.PropertyField(IsOnFriendsList);
		}

		private void DrawOutputStateGUI()
		{
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.PropertyField(IsLocal);
				EditorGUILayout.PropertyField(IsMirrorClone);
				EditorGUILayout.PropertyField(IsShadowClone);
				EditorGUILayout.PropertyField(LocomotionIsDisabled);
				EditorGUILayout.PropertyField(IKTrackingOutputData);
			}
		}

		private void DrawCreditsAndLinksGUI()
		{
			EditorGUILayout.PropertyField(VisitOurGithub);
			EditorGUILayout.PropertyField(ViewREADMEManual);
			EditorGUILayout.PropertyField(ViewChangelog);
			EditorGUILayout.PropertyField(ViewMITLicense);
			EditorGUILayout.PropertyField(SendBugsOrFeedback);
		}

		private static void DrawFoldout(string label, ref bool foldout, Action content)
		{
			foldout = EditorGUILayout.Foldout(foldout, label, true, EditorStyles.foldoutHeader);
			if (!foldout) return;

			EditorGUI.indentLevel++;
			content();
			EditorGUI.indentLevel--;
		}

		private static bool ClickableButton(GUIContent content)
		{
			GUIStyle style = GUI.skin.button;
			var r = EditorGUILayout.GetControlRect(false, 20, style);
			EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
			return GUI.Button(r, content, style);
		}

		private static bool ClickableToggle(GUIContent content, bool value)
		{
			GUIStyle style = GUI.skin.toggle;
			var r = EditorGUILayout.GetControlRect(false, 20, style);
			EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
			return GUI.Toggle(r, value, content, style);
		}
		
		private static GUIContent GetContent(SerializedProperty property)
		{ 
			return new GUIContent(property.displayName, property.tooltip);
		}
	}
}