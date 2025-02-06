using System;
using Lyuma.Av3Emulator.Runtime;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Lyuma.Av3Emulator.Editor
{
	[CustomEditor(typeof(LyumaAv3Runtime))]
	public class LyumaAv3RuntimeEditor : UnityEditor.Editor
	{
		#region SerializedProperties
		public SerializedProperty OriginalSourceClone;
		public SerializedProperty AvatarSyncSource;
		public SerializedProperty ResetAvatar;
		public SerializedProperty ResetAndHold;
		public SerializedProperty RefreshExpressionParams;
		public SerializedProperty KeepSavedParametersOnReset;
		public SerializedProperty DebugDuplicateAnimator;
		public SerializedProperty ViewAnimatorOnlyNoParams;
		public SerializedProperty EnableAvatarOSC;
		public SerializedProperty LogOSCWarnings;
		public SerializedProperty OSCController;
		public SerializedProperty UseRealPipelineIdJSONFile;
		public SerializedProperty SendRecvAllParamsNotInJSON;
		public SerializedProperty GenerateOSCConfig;
		public SerializedProperty LoadOSCConfig;
		public SerializedProperty SaveOSCConfig;
		public SerializedProperty OSCAvatarID;
		public SerializedProperty OSCFilePath;
		public SerializedProperty OSCConfigId;
		public SerializedProperty OSCConfigName;
		public SerializedProperty OSCConfigParameters;
		public SerializedProperty CreateNonLocalClone;
		public SerializedProperty locally8bitQuantizedFloats;
		public SerializedProperty NonLocalSyncInterval;
		public SerializedProperty IKSyncRadialMenu;
		public SerializedProperty EnableHeadScaling;
		public SerializedProperty DisableMirrorAndShadowClones;
		public SerializedProperty DebugOffsetMirrorClone;
		public SerializedProperty ViewMirrorReflection;
		public SerializedProperty ViewBothRealAndMirror;
		public SerializedProperty Viseme;
		public SerializedProperty VisemeIdx;
		public SerializedProperty Voice;
		public SerializedProperty GestureLeft;
		public SerializedProperty GestureLeftIdx;
		public SerializedProperty GestureLeftWeight;
		public SerializedProperty GestureRight;
		public SerializedProperty GestureRightIdx;
		public SerializedProperty GestureRightWeight;
		public SerializedProperty BlinkRate;
		public SerializedProperty EyeTargetX;
		public SerializedProperty EyeTargetY;
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
		public SerializedProperty IsAnimatorEnabled;
		public SerializedProperty PreviewMode;
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

		public string searchFilter;
		public ReorderableList floatsRL;
		public ReorderableList intsRL;
		public ReorderableList boolsRL;

		//public SerializedProperty emulator;

		public GUIContent warningIcon;
		private void RefreshSerializedProperties()
		{
			OriginalSourceClone = serializedObject.FindProperty("OriginalSourceClone");
			AvatarSyncSource = serializedObject.FindProperty("AvatarSyncSource");
			ResetAvatar = serializedObject.FindProperty("ResetAvatar");
			ResetAndHold = serializedObject.FindProperty("ResetAndHold");
			RefreshExpressionParams = serializedObject.FindProperty("RefreshExpressionParams");
			KeepSavedParametersOnReset = serializedObject.FindProperty("KeepSavedParametersOnReset");
			DebugDuplicateAnimator = serializedObject.FindProperty("DebugDuplicateAnimator");
			ViewAnimatorOnlyNoParams = serializedObject.FindProperty("ViewAnimatorOnlyNoParams");
			EnableAvatarOSC = serializedObject.FindProperty("EnableAvatarOSC");
			LogOSCWarnings = serializedObject.FindProperty("LogOSCWarnings");
			OSCController = serializedObject.FindProperty("OSCController");
			UseRealPipelineIdJSONFile = serializedObject.FindProperty("OSCConfigurationFile.UseRealPipelineIdJSONFile");
			SendRecvAllParamsNotInJSON = serializedObject.FindProperty("OSCConfigurationFile.SendRecvAllParamsNotInJSON");
			GenerateOSCConfig = serializedObject.FindProperty("OSCConfigurationFile.GenerateOSCConfig");
			LoadOSCConfig = serializedObject.FindProperty("OSCConfigurationFile.LoadOSCConfig");
			SaveOSCConfig = serializedObject.FindProperty("OSCConfigurationFile.SaveOSCConfig");
			OSCAvatarID = serializedObject.FindProperty("OSCConfigurationFile.OSCAvatarID");
			OSCFilePath = serializedObject.FindProperty("OSCConfigurationFile.OSCFilePath");
			OSCConfigId = serializedObject.FindProperty("OSCConfigurationFile.OSCJsonConfig.id");
			OSCConfigName = serializedObject.FindProperty("OSCConfigurationFile.OSCJsonConfig.name");
			OSCConfigParameters = serializedObject.FindProperty("OSCConfigurationFile.OSCJsonConfig.parameters");
			CreateNonLocalClone = serializedObject.FindProperty("CreateNonLocalClone");
			locally8bitQuantizedFloats = serializedObject.FindProperty("locally8bitQuantizedFloats");
			NonLocalSyncInterval = serializedObject.FindProperty("NonLocalSyncInterval");
			IKSyncRadialMenu = serializedObject.FindProperty("IKSyncRadialMenu");
			EnableHeadScaling = serializedObject.FindProperty("EnableHeadScaling");
			DisableMirrorAndShadowClones = serializedObject.FindProperty("DisableMirrorAndShadowClones");
			DebugOffsetMirrorClone = serializedObject.FindProperty("DebugOffsetMirrorClone");
			ViewMirrorReflection = serializedObject.FindProperty("ViewMirrorReflection");
			ViewBothRealAndMirror = serializedObject.FindProperty("ViewBothRealAndMirror");
			Viseme = serializedObject.FindProperty("Viseme");
			VisemeIdx = serializedObject.FindProperty("VisemeIdx");
			Voice = serializedObject.FindProperty("Voice");
			GestureLeft = serializedObject.FindProperty("GestureLeft");
			GestureLeftIdx = serializedObject.FindProperty("GestureLeftIdx");
			GestureLeftWeight = serializedObject.FindProperty("GestureLeftWeight");
			GestureRight = serializedObject.FindProperty("GestureRight");
			GestureRightIdx = serializedObject.FindProperty("GestureRightIdx");
			GestureRightWeight = serializedObject.FindProperty("GestureRightWeight");
			BlinkRate = serializedObject.FindProperty("BlinkRate");
			EyeTargetX = serializedObject.FindProperty("EyeTargetX");
			EyeTargetY = serializedObject.FindProperty("EyeTargetY");
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
			IsAnimatorEnabled = serializedObject.FindProperty("IsAnimatorEnabled");
			PreviewMode = serializedObject.FindProperty("PreviewMode");
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
			
			warningIcon = EditorGUIUtility.IconContent("Warning@2x");
		}
		#endregion

		#region Foldout Variables
		private static bool resetAndRefreshFoldout;
		private static bool animatorToDebugFoldout = true;
		private static bool OSCFoldout;
		private static bool OSCConfigFoldout;
		private static bool networkClonesAndSyncFoldout;
		private static bool playerLocalAndMirrorReflectionFoldout;
		private static bool builtInInputsFoldout = true;
		private static bool visemeFoldout;
		private static bool handGestureFoldout = true;
		private static bool locomotionFoldout = true;
		private static bool trackingSetupAndOtherFoldout;
		private static bool userInputsFoldout;
		private static bool outputStateFoldout;
		private static bool creditsAndLinksFoldout = true;
		#endregion
		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			RefreshSerializedProperties();
			if (AvatarSyncSource.objectReferenceValue != target)
			{
				EditorGUILayout.PropertyField(AvatarSyncSource);
			}
			EditorGUILayout.Space();

			DrawFoldout("Reset and Refresh", ref resetAndRefreshFoldout, DrawResetAndRefreshGUI);
			DrawFoldout("Animator to Debug", ref animatorToDebugFoldout, DrawAnimatorToDebugGUI);
			DrawFoldout("OSC", ref OSCFoldout, DrawOSCGUI);
			DrawFoldout("Network Clones and Sync", ref networkClonesAndSyncFoldout, DrawNetworkClonesAndSyncGUI);
			DrawFoldout("Player Local and Mirror Reflection", ref playerLocalAndMirrorReflectionFoldout, DrawPlayerLocalAndMirrorReflectionGUI);
			DrawFoldout("Built-in Inputs", ref builtInInputsFoldout, DrawBuiltInInputsGUI);
			DrawFoldout("User Inputs", ref userInputsFoldout, DrawUserInputsGUI);
			DrawFoldout("Output State", ref outputStateFoldout, DrawOutputStateGUI);
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
			DrawRLWithFoldout(floatsRL);
			DrawRLWithFoldout(intsRL);
			DrawRLWithFoldout(boolsRL);
		}

		private void DrawResetAndRefreshGUI()
		{
			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(ResetAvatar);
				DrawAsClickableToggle(ResetAndHold);
			}

			DrawAsClickableToggle(RefreshExpressionParams);
			DrawAsClickableToggle(KeepSavedParametersOnReset);
			EditorGUILayout.PropertyField(OriginalSourceClone);
		}

		private void DrawAnimatorToDebugGUI()
		{
			if (DebugDuplicateAnimator.enumValueIndex == (int)VRCAvatarDescriptor.AnimLayerType.Base)
			{
				EditorGUILayout.PropertyField(DebugDuplicateAnimator);
				
			}
			else
			{
				EditorGUILayout.PropertyField(DebugDuplicateAnimator);
				using (new EditorGUILayout.HorizontalScope())
				{
					GUILayout.Label(warningIcon, GUILayout.Width(warningIcon.image.width));
					GUILayout.Label("Behaviour can differ from in game when not set to 'Base'");
				}
			}
			EditorGUILayout.PropertyField(ViewAnimatorOnlyNoParams);
		}

		private void DrawOSCGUI()
		{
			DrawAsClickableToggle(EnableAvatarOSC, true);
			DrawAsClickableToggle(LogOSCWarnings);
			EditorGUILayout.PropertyField(OSCController);
			DrawAsClickableToggle(UseRealPipelineIdJSONFile);
			DrawAsClickableToggle(SendRecvAllParamsNotInJSON);
			DrawAsClickableToggle(GenerateOSCConfig);
			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(LoadOSCConfig);
				DrawAsClickableToggle(SaveOSCConfig);	
			}
			EditorGUILayout.PropertyField(OSCAvatarID);
			EditorGUILayout.PropertyField(OSCFilePath);
			DrawFoldout("OSC Json Config", ref OSCConfigFoldout, DrawOSCConfig);
		}

		private void DrawOSCConfig()
		{
			EditorGUILayout.PropertyField(OSCConfigId);
			EditorGUILayout.PropertyField(OSCConfigName);
			EditorGUILayout.PropertyField(OSCConfigParameters);
		}

		private void DrawNetworkClonesAndSyncGUI()
		{
			using (new GUILayout.HorizontalScope())
			{
				EditorGUILayout.PropertyField(locally8bitQuantizedFloats);
				DrawAsClickableToggle(CreateNonLocalClone);
			}
			EditorGUILayout.PropertyField(NonLocalSyncInterval);
			EditorGUILayout.PropertyField(IKSyncRadialMenu);
		}

		private void DrawPlayerLocalAndMirrorReflectionGUI()
		{
			DrawAsClickableToggle(EnableHeadScaling);
			DrawAsClickableToggle(DisableMirrorAndShadowClones);
			
			DrawAsClickableToggle(DebugOffsetMirrorClone);
			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(ViewMirrorReflection);
				DrawAsClickableToggle(ViewBothRealAndMirror);
			}
		}

		private void DrawVisemeGUI()
		{
			DrawAsEnumWithIndex(Viseme, VisemeIdx, 50);
			EditorGUILayout.PropertyField(Voice);
			EditorGUILayout.PropertyField(BlinkRate);
			EditorGUILayout.PropertyField(EyeTargetX);
			EditorGUILayout.PropertyField(EyeTargetY);
		}

		private void DrawHandGestureGUI()
		{
			DrawAsEnumWithIndex(GestureLeft, GestureLeftIdx);
			if (GestureLeftIdx.intValue == 1) {
				EditorGUILayout.PropertyField(GestureLeftWeight);
			}
			
			DrawAsEnumWithIndex(GestureRight, GestureRightIdx);
			if (GestureRightIdx.intValue == 1) {
				EditorGUILayout.PropertyField(GestureRightWeight);
			}
		}

		private void DrawLocomotionGUI()
		{
			EditorGUILayout.PropertyField(Velocity);
			EditorGUILayout.PropertyField(AngularY);
			EditorGUILayout.PropertyField(Upright);
			EditorGUILayout.PropertyField(RunSpeed);
			
			using (new GUILayout.HorizontalScope())
			{
				EditorGUILayout.PropertyField(JumpPower);
				if (ClickableButton("Jump", GUILayout.Width(75)))
					Jump.boolValue = true;
				
				DrawAsClickableToggle(Grounded, true, GUILayout.Width(75));
			}

			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(Seated);
				DrawAsClickableToggle(AFK);
			}
			
			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(TPoseCalibration);
				DrawAsClickableToggle(IKPoseCalibration);
			}
		}

		private void DrawTrackingSetupAndOtherGUI()
		{
			DrawAsEnumWithIndex(TrackingType, TrackingTypeIdx, 150);
			
			using (new GUILayout.HorizontalScope())
			{
				EditorGUILayout.PropertyField(AvatarHeight);
				DrawAsClickableToggle(EnableAvatarScaling, true, GUILayout.Width(150));
			}
			EditorGUILayout.PropertyField(AvatarVersion);
			
			EditorGUILayout.PropertyField(VisualOffset);
			
			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(MuteSelf);
				DrawAsClickableToggle(Earmuffs);
			}
			
			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(PreviewMode);
				DrawAsClickableToggle(IsAnimatorEnabled);
			}

			using (new GUILayout.HorizontalScope())
			{
				DrawAsClickableToggle(VRMode);
				DrawAsClickableToggle(InStation);

			}
			DrawAsClickableToggle(IsOnFriendsList);
		}

		private void DrawOutputStateGUI()
		{
			using (new EditorGUI.DisabledScope(true))
			{
				DrawAsClickableToggle(IsLocal, true);
				using (new GUILayout.HorizontalScope())
				{
					DrawAsClickableToggle(IsMirrorClone, true);
					DrawAsClickableToggle(IsShadowClone, true);
				}

				bool locomotionState = !LocomotionIsDisabled.boolValue;
				using (new ColoredScope(ColoredScope.ColoringType.BG, locomotionState, Color.green, Color.red))
				{
					string label = locomotionState ? "Locomotion Is Enabled" : "Locomotion Is Disabled";
					GUILayout.Button(label);
				}
			}
			EditorGUILayout.PropertyField(IKTrackingOutputData, true);
		}

		private void DrawCreditsAndLinksGUI()
		{
			EditorGUILayout.Space();
			DrawCredits();
			EditorGUILayout.Space();
			
			if (ClickableButton(new GUIContent("Visit Our Github",LyumaAv3Emulator.GIT_REPO)))
				Application.OpenURL(LyumaAv3Emulator.GIT_REPO);

			if (ClickableButton(new GUIContent("Send Bugs Or Feedback")))
				Application.OpenURL(LyumaAv3Emulator.BUG_TRACKER_URL);
			
			using (new GUILayout.HorizontalScope())
			{
				if (ClickableButton("View README Manual"))
					Selection.activeObject = LyumaAv3Emulator.READMEAsset;
				
				if (ClickableButton("View Changelog"))
					Selection.activeObject = LyumaAv3Emulator.CHANGELOGAsset;
				
				if (ClickableButton("View MIT License"))
					Selection.activeObject = LyumaAv3Emulator.LICENSEAsset;
			}
		}

		private void DrawCredits()
		{
			GUILayout.Label("Lyuma's Av3Emulator is open source!", EditorStyles.boldLabel);
			GUILayout.Label(LyumaAv3Emulator.EMULATOR_VERSION_STRING, EditorStyles.boldLabel);
			GUILayout.Label(LyumaAv3Emulator.CREDIT1, EditorStyles.boldLabel);
			GUILayout.Label(LyumaAv3Emulator.CREDIT2, EditorStyles.boldLabel);
			GUILayout.Label(LyumaAv3Emulator.CREDIT3, EditorStyles.boldLabel);
		}

		private static void DrawFoldout(string label, ref bool foldout, Action content)
		{
			foldout = EditorGUILayout.Foldout(foldout, label, true, EditorStyles.foldoutHeader);
			if (!foldout) return;

			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Space(15);
				using (new GUILayout.VerticalScope()) 
					content();
			}
			
		}

		private void OnEnable()
		{
			RefreshSerializedProperties();
			floatsRL = new ReorderableList(serializedObject, Floats, false, false, false, false);
			intsRL = new ReorderableList(serializedObject, Ints, false, false, false, false);
			boolsRL = new ReorderableList(serializedObject, Bools, false, false, false, false);
			
			floatsRL.drawHeaderCallback = intsRL.drawHeaderCallback = boolsRL.drawHeaderCallback = DrawSearchHeader;

			floatsRL.drawElementCallback = DrawFloatsElementCallback;
			intsRL.drawElementCallback = DrawIntsElementCallback;
			boolsRL.drawElementCallback = DrawBoolsElementCallback;

			floatsRL.elementHeightCallback = i => ElementIsSearchableHeightCallback(Floats.GetArrayElementAtIndex(i));
			intsRL.elementHeightCallback = i => ElementIsSearchableHeightCallback(Ints.GetArrayElementAtIndex(i));
			boolsRL.elementHeightCallback = i => ElementIsSearchableHeightCallback(Bools.GetArrayElementAtIndex(i));
		}

		#region Clickables
		private static bool ClickableButton(string content, params GUILayoutOption[] options) => ClickableButton(new GUIContent(content), options);
		private static bool ClickableButton(GUIContent content, params GUILayoutOption[] options)
		{
			GUIStyle style = GUI.skin.button;
			var r = EditorGUILayout.GetControlRect(false, 20, style, options);
			EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
			return GUI.Button(r, content, style);
		}

		private static bool ClickableToggle(GUIContent content, bool value, bool colorOnFalse = false, params GUILayoutOption[] options)
		{
			GUIStyle style = GUI.skin.button;
			var r = EditorGUILayout.GetControlRect(false, 20, style, options);
			if (GUI.enabled) EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
			
			if (colorOnFalse)
			{
				using (new ColoredScope(ColoredScope.ColoringType.BG, value, Color.green, Color.red))
					return GUI.Toggle(r, value, content, style);
			}
			
			using (new ColoredScope(ColoredScope.ColoringType.BG, value, Color.green))
				return GUI.Toggle(r, value, content, style);
			
		}
		private static void DrawAsClickableToggle(SerializedProperty property, bool colorOnFalse = false, params GUILayoutOption[] options) => property.boolValue = ClickableToggle(GetContent(property), property.boolValue, colorOnFalse, options);

		private static void DrawAsEnumWithIndex(SerializedProperty enumProperty, SerializedProperty indexProperty, float width = 100)
		{
			using (new GUILayout.HorizontalScope())
			{
				EditorGUILayout.PropertyField(indexProperty, GetContent(enumProperty));
				EditorGUILayout.PropertyField(enumProperty, GUIContent.none, GUILayout.Width(width));
			}
		}
		#endregion
		
		#region Reorderable List Stuff

		private void DrawRLWithFoldout(ReorderableList list)
		{
			var arrayProp = list.serializedProperty;
			var label = arrayProp.displayName;
			arrayProp.isExpanded = EditorGUILayout.Foldout(arrayProp.isExpanded, label, true, EditorStyles.foldoutHeader);
			if (!arrayProp.isExpanded) return;
			list.DoLayoutList();
		}

		private void DrawSearchHeader(Rect r) => searchFilter = EditorGUI.TextField(r, "Search", searchFilter);
		

		private void DrawFloatsElementCallback(Rect r, int i, bool _, bool __)
		{
			if (!WillDrawElement(Floats, i, out var el)) return;
			var value = el.FindPropertyRelative("expressionValue");
			var dummyValue = value.floatValue;
			EditorGUI.BeginChangeCheck();
			dummyValue = EditorGUI.Slider(r, GetContent(el), dummyValue, -1, 1);
			if (EditorGUI.EndChangeCheck())
				value.floatValue = dummyValue;
		}
		private void DrawBoolsElementCallback(Rect r, int i, bool _, bool __)
		{
			if (!WillDrawElement(Bools, i, out var el)) return;
			var value = el.FindPropertyRelative("value");
			value.boolValue = EditorGUI.Toggle(r, GetContent(el), value.boolValue);
		}
		
		private void DrawIntsElementCallback(Rect r, int i, bool _, bool __)
		{
			if (!WillDrawElement(Ints, i, out var el)) return;
			var value = el.FindPropertyRelative("value");
			value.intValue = EditorGUI.IntField(r, GetContent(el), value.intValue);
		}

		private bool WillDrawElement(SerializedProperty array, int index, out SerializedProperty element)
		{
			element = null;
			if (index >= array.arraySize || index < 0) return false;
			element = array.GetArrayElementAtIndex(index);
			return IsElementSearchable(element);
		}
		
		private const float SEARCHABLE_ELEMENT_HEIGHT = 18;
		private float ElementIsSearchableHeightCallback(SerializedProperty element) => IsElementSearchable(element) ? SEARCHABLE_ELEMENT_HEIGHT : 0;

		private bool IsElementSearchable(SerializedProperty element)
		{
			if (string.IsNullOrEmpty(searchFilter)) return true;
			var s = element.FindPropertyRelative("name").stringValue;
			return !string.IsNullOrEmpty(s) && s.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		#endregion
		
		private static GUIContent GetContent(SerializedProperty property)
		{ 
			return new GUIContent(property.displayName, property.tooltip);
		}

		internal sealed class ColoredScope : System.IDisposable
		{
			internal enum ColoringType
			{
				BG = 1 << 0,
				FG = 1 << 1,
				General = 1 << 2,
				All = BG | FG | General
			}

			private readonly Color[] ogColors = new Color[3];
			private readonly ColoringType coloringType;
			private bool changedAnyColor;

			private void MemorizeColor()
			{
				changedAnyColor = true;
				ogColors[0] = GUI.backgroundColor;
				ogColors[1] = GUI.contentColor;
				ogColors[2] = GUI.color;
			}

			private void SetColors(Color color)
			{
				MemorizeColor();

				if (coloringType.HasFlag(ColoringType.BG))
					GUI.backgroundColor = color;

				if (coloringType.HasFlag(ColoringType.FG))
					GUI.contentColor = color;

				if (coloringType.HasFlag(ColoringType.General))
					GUI.color = color;
			}

			internal ColoredScope(ColoringType type, Color color)
			{
				coloringType = type;
				SetColors(color);
			}

			internal ColoredScope(ColoringType type, bool isActive, Color color)
			{
				coloringType = type;
				if (isActive) SetColors(color);

			}

			internal ColoredScope(ColoringType type, bool isActive, Color active, Color inactive)
			{
				coloringType = type;
				SetColors(isActive ? active : inactive);
			}

			internal ColoredScope(ColoringType type, int selectedIndex, params Color[] colors)
			{
				coloringType = type;
				if (selectedIndex >= 0)
				{
					MemorizeColor();
					SetColors(colors[selectedIndex]);
				}
			}

			public void Dispose()
			{
				if (!changedAnyColor) return;

				if (coloringType.HasFlag(ColoringType.BG))
					GUI.backgroundColor = ogColors[0];
				if (coloringType.HasFlag(ColoringType.FG))
					GUI.contentColor = ogColors[1];
				if (coloringType.HasFlag(ColoringType.General))
					GUI.color = ogColors[2];


			}
		}
	}
}
