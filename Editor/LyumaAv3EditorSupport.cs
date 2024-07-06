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

using System.Collections.Generic;
using System.Reflection;
using Lyuma.Av3Emulator.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Compilation;
using UnityEngine;
using static Lyuma.Av3Emulator.Runtime.LyumaAv3Emulator;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Lyuma.Av3Emulator.Editor
{
	[InitializeOnLoadAttribute]
	public static class LyumaAv3EditorSupport
	{
		static Dictionary<VRCAvatarDescriptor.AnimLayerType, string> animLayerToDefaultFile = new Dictionary<VRCAvatarDescriptor.AnimLayerType, string> {
			{VRCAvatarDescriptor.AnimLayerType.TPose, "vrc_AvatarV3UtilityTPose"},
			{VRCAvatarDescriptor.AnimLayerType.IKPose, "vrc_AvatarV3UtilityIKPose"},
			{VRCAvatarDescriptor.AnimLayerType.Base, "vrc_AvatarV3LocomotionLayer"},
			{VRCAvatarDescriptor.AnimLayerType.Sitting, "vrc_AvatarV3SittingLayer"},
			{VRCAvatarDescriptor.AnimLayerType.Additive, "vrc_AvatarV3IdleLayer"},
			{VRCAvatarDescriptor.AnimLayerType.FX, "vrc_AvatarV3FaceLayer"},
			{VRCAvatarDescriptor.AnimLayerType.Action, "vrc_AvatarV3ActionLayer"},
			{VRCAvatarDescriptor.AnimLayerType.Gesture, "vrc_AvatarV3HandsLayer"},
		};
		static Dictionary<VRCAvatarDescriptor.AnimLayerType, string> animLayerToDefaultGUID = new Dictionary<VRCAvatarDescriptor.AnimLayerType, string> {
			{VRCAvatarDescriptor.AnimLayerType.TPose, "00121b5812372b74f9012473856d8acf"},
			{VRCAvatarDescriptor.AnimLayerType.IKPose, "a9b90a833b3486e4b82834c9d1f7c4ee"},
			{VRCAvatarDescriptor.AnimLayerType.Base, "4e4e1a372a526074884b7311d6fc686b"},
			{VRCAvatarDescriptor.AnimLayerType.Sitting, "1268460c14f873240981bf15aa88b21a"},
			{VRCAvatarDescriptor.AnimLayerType.Additive, "573a1373059632b4d820876efe2d277f"},
			{VRCAvatarDescriptor.AnimLayerType.FX, "d40be620cf6c698439a2f0a5144919fe"},
		{VRCAvatarDescriptor.AnimLayerType.Action, "3e479eeb9db24704a828bffb15406520"},
			{VRCAvatarDescriptor.AnimLayerType.Gesture, "404d228aeae421f4590305bc4cdaba16"},
		};
	
		static void InitDefaults() {
			foreach (var kv in animLayerToDefaultFile) {
				if (kv.Value == null) {
					LyumaAv3Runtime.animLayerToDefaultController[kv.Key] = null;
				} else
				{
					string SDKPath = AssetDatabase.GUIDToAssetPath(animLayerToDefaultGUID[kv.Key]);
					AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(SDKPath);
					if (ac == null)
					{
						Debug.LogWarning("Failed to resolve animator controller " + kv.Value + " for " + kv.Key);
						foreach (var guid in AssetDatabase.FindAssets(kv.Value))
						{
							string path = AssetDatabase.GUIDToAssetPath(guid);
							if (path.EndsWith("/" + kv.Value + ".controller")) {
								ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
								break;
							}
						}
					}
					LyumaAv3Runtime.animLayerToDefaultController[kv.Key] = ac;
				}
			}

			LyumaAv3Emulator.READMEAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath("d417c05c9fa0406189ff5923ecfc745f"));
			LyumaAv3Emulator.LICENSEAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath("6eadcec0dbb74827b1adc0aedbb43fb9"));
			LyumaAv3Emulator.CHANGELOGAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath("f60b10c20752a814a9df2299ce2a55a5"));
		
			LyumaAv3Runtime.updateSelectionDelegate = (obj, mode) => {
				if (obj == null && LyumaAv3Emulator.emulatorInstance != null) {
					// Debug.Log("Resetting selected object: " + LyumaAv3Emulator.emulatorInstance);
					obj = LyumaAv3Emulator.emulatorInstance.gameObject;
				}
				GameObject go = obj as GameObject;
				if (go == null) {
					Component comp = obj as Component;
					if (comp != null) {
						go = comp.gameObject;
					}
				}
				if (mode == 1) {
					if (go != null && go == Selection.activeGameObject) {
						return;
					}
				}
				if (mode == 2) {
					if (go != null && go.GetComponent<LyumaAv3Runtime>() != null && Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<LyumaAv3Runtime>() != null) {
						return;
					}
				}
				// Debug.Log("Setting selected object: " + go);
				Selection.SetActiveObjectWithContext(obj, obj);
				// Highlighter.Highlight("Inspector", "Animator To Debug");
			};

			LyumaAv3Runtime.updateAnimatorWindowDelegate = (rac) =>
			{
				if (rac == null)
				{
					LyumaAv3Runtime.updateSelectionDelegate(LyumaAv3Emulator.emulatorInstance, 1);
					return;
				}
				if (LyumaAv3Emulator.emulatorInstance.SelectAssetOnChangeAnimatorToDebug)
				{
					LyumaAv3Runtime.updateSelectionDelegate(rac, 0);
					return;
				}

				var type = System.Type.GetType("UnityEditor.Graphs.AnimatorControllerTool, UnityEditor.Graphs");
				var prop = type?.GetProperty("animatorController");
				if (type != null && prop != null && Resources.FindObjectsOfTypeAll(type)?.Length > 0)
				{
					prop.SetValue(EditorWindow.GetWindow(type, false, "Animator", false), rac);
				}
			};

			LyumaAv3Runtime.updateSceneLayersDelegate = (layers) => {
				if (Tools.visibleLayers == layers) {
					return;
				}
				// Debug.Log("Setting selected layers: " + layers);
				Tools.visibleLayers = layers;
				Camera[] cameras = new Camera[Camera.allCamerasCount];
				Camera.GetAllCameras(cameras);
				foreach (Camera c in cameras) {
					if (c != null && c.targetTexture == null && c.GetComponentInParent<LyumaAv3Runtime>() == null && c.gameObject.activeInHierarchy && c.isActiveAndEnabled) {
						c.cullingMask = layers;
					}
				}
				// Highlighter.Highlight("Inspector", "Animator To Debug");
			};

			LyumaAv3Runtime.addRuntimeDelegate = (runtime) => {
				MoveComponentToTop(runtime);
			};

			// Currently PhysBone and ContactManager cause exceptions if scripts reload during Play mode.
			// This applies a workaround: disable the objects before compile; call RuntimeInit to recreate them after.
			LyumaAv3Runtime.ApplyOnEnableWorkaroundDelegate = () => {
				CompilationPipeline.compilationStarted -= WorkaroundDestroyManagersBeforeCompile;
				CompilationPipeline.compilationStarted += WorkaroundDestroyManagersBeforeCompile;
				GameObject gotmp = GameObject.Find("/TempReloadDontDestroy");
				if (gotmp != null) {
					GameObject.DestroyImmediate(gotmp);
					var avatarDynamicsSetup = typeof(VRCExpressionsMenuEditor).Assembly.GetType("VRC.SDK3.Avatars.AvatarDynamicsSetup");
					if (avatarDynamicsSetup != null) {
						var RuntimeInit = avatarDynamicsSetup.GetMethod("RuntimeInit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
						if (RuntimeInit != null) {
							Debug.Log("Caling avatarDynamicsSetup.RuntimeInit(): " + RuntimeInit);
							RuntimeInit.Invoke(null, new object[0]);
						}
					}
					Debug.Log("DONE workaround");
				}
			};

			LyumaAv3Osc.GetEditorViewportDelegate = () => {
				try {
					Rect ret = UnityEditor.SceneView.currentDrawingSceneView.position;
					// Gizmos are relative to the active window in terms of x and y.
					ret.x = 1.0f;
					ret.y = 1.0f;
					ret.height -= 7.0f;
					return ret;
				} catch {
					Vector2 gvsize = Handles.GetMainGameViewSize();
					return new Rect(0, -18, gvsize.x, gvsize.y);
				}
			};
			LyumaAv3Osc.DrawDebugRectDelegate = (Rect pos, Color col, Color outlineCol) => {
				// Debug.Log("Debug raw rect " + pos);
				Color origColor = GUI.color;
				GUI.color = col;
				UnityEditor.Handles.BeginGUI();
				UnityEditor.Handles.DrawSolidRectangleWithOutline(pos, col, outlineCol);
				UnityEditor.Handles.EndGUI();
				GUI.color = origColor;
			};
			LyumaAv3Osc.DrawDebugTextDelegate = (Rect pos, Color backgroundCol, Color outlineCol, Color textCol, string str, TextAnchor alignment) => {
				// Debug.Log("Debug raw text " + str + " at " + pos);
				Color origColor = GUI.color;
				GUI.color = backgroundCol;
				var view = UnityEditor.SceneView.currentDrawingSceneView;
				// Vector2 size = GUI.skin.label.CalcSize(new GUIContent(str));
				// Rect pos = new Rect(location.x, location.y, size.x, size.y);
				UnityEditor.Handles.BeginGUI();
				UnityEditor.Handles.DrawSolidRectangleWithOutline(pos, backgroundCol, outlineCol);
				GUI.color = textCol.r + textCol.b + textCol.g > 0.5f ? new Color(0,0,0,textCol.a * 0.5f) : new Color(1,1,1,textCol.a * 0.5f);//new Color(1.0f, 1.0f, 1.0f, textCol.a * 0.25f);
				var style = new GUIStyle();
				style.fontStyle = FontStyle.Bold;
				style.alignment = alignment;
				style.normal.textColor = GUI.color;
				pos.y += 1;
				GUI.Label(pos, str, style);
				pos.x += 1;
				GUI.Label(pos, str, style);
				pos.y -= 1;
				GUI.Label(pos, str, style);
				pos.x -= 1;
				GUI.Label(pos, str, style);
				pos.x += 0.5f;
				pos.y += 0.5f;
				GUI.color = textCol;
				style.normal.textColor = GUI.color;
				GUI.Label(pos, str, style);
				UnityEditor.Handles.EndGUI();
				GUI.color = origColor;
			};
			LyumaAv3Runtime.forceUpdateDescriptorColliders = (VRCAvatarDescriptor descriptor) => {
				UnityEditor.Editor tempEditor = null;
				try
				{
					var descriptorEditor = System.Type.GetType("AvatarDescriptorEditor3, Assembly-CSharp-Editor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
					if (descriptorEditor == null) descriptorEditor = System.Type.GetType("AvatarDescriptorEditor3, VRC.SDK3A.Editor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
					var editorAvatarField = descriptorEditor.GetField("avatarDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
					var updateMethod = descriptorEditor.GetMethod("UpdateAutoColliders", BindingFlags.NonPublic | BindingFlags.Instance);

					tempEditor = UnityEditor.Editor.CreateEditor(descriptor, descriptorEditor);
					editorAvatarField.SetValue(tempEditor, descriptor);
					updateMethod.Invoke(tempEditor, null);
					Object.DestroyImmediate(tempEditor);
				}
				catch
				{
					Debug.LogError("Failed to force update Descriptor Colliders through reflection.");
					if (tempEditor != null) Object.DestroyImmediate(tempEditor);
				}
			};
			LyumaAv3Runtime.convertDynamicBones = (GameObject avatarObj) => {
					var avatarDynamicsSetup = typeof(VRCExpressionsMenuEditor).Assembly.GetType("VRC.SDK3.Avatars.AvatarDynamicsSetup");
					var TypeDynamicBone = System.Type.GetType("DynamicBone, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
					var TypeDynamicBoneCollider = System.Type.GetType("DynamicBoneCollider, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
					if (TypeDynamicBone != null && TypeDynamicBoneCollider != null && avatarDynamicsSetup != null) {
						var ConvertToPhysBones = avatarDynamicsSetup.GetMethod("ConvertToPhysBones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
						if (ConvertToPhysBones != null) {
							Debug.Log("Convert dyn bone to phys");
							ConvertToPhysBones.Invoke(null, new object[1]{avatarObj});
						}
					}
			};
		}

		public static void OnPlayModeStateChange(UnityEditor.PlayModeStateChange pmsc) {
			// We don't want any of our callbacks causing trouble outside of play mode.
			if (pmsc != UnityEditor.PlayModeStateChange.EnteredPlayMode) {
				CompilationPipeline.compilationStarted -= WorkaroundDestroyManagersBeforeCompile;
			}
		}

		private static void WorkaroundDestroyManagersBeforeCompile(object obj) {
			Debug.Log("Compile Started");
			if (!GameObject.Find("/TempReloadDontDestroy")) {
				GameObject gotmp = new GameObject("TempReloadDontDestroy");
				Object.DontDestroyOnLoad(gotmp);
			}
			GameObject go;
			go = GameObject.Find("/TriggerManager");
			if (go != null) {
				Object.DestroyImmediate(go);
			}
			go = GameObject.Find("/PhysBoneManager");
			if (go != null) {
				Object.DestroyImmediate(go);
			}
		}

		static void MoveComponentToTop(Component c) {
			GameObject go = c.gameObject;
			Component[] components = go.GetComponents<Component>();

			foreach (Component comp in components) {
				if (comp.GetType().Name == "PipelineSaver") {
					return;
				}
			}
			
			try {
				if (PrefabUtility.IsPartOfAnyPrefab(go)) {
					PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
				}
			} catch (System.Exception) {}
			int moveUpCalls = components.Length - 2;
			if (!PrefabUtility.IsPartOfAnyPrefab(go.GetComponents<Component>()[1])) {
				for (int i = 0; i < moveUpCalls; i++) {
					UnityEditorInternal.ComponentUtility.MoveComponentUp(c);
				}
			}
		}

		// register an event handler when the class is initialized
		static LyumaAv3EditorSupport()
		{
			InitDefaults();
			EditorApplication.playModeStateChanged += OnPlayModeStateChange;
			LyumaAv3Runtime.InvokeOnPreProcessAvatar = (obj) =>
			{
				IVRCSDKPreprocessAvatarCallback lockMaterials = null;
				List<IVRCSDKPreprocessAvatarCallback> _preprocessAvatarCallbacks = new List<IVRCSDKPreprocessAvatarCallback>();
				
				FieldInfo ProcessorField = typeof(VRCBuildPipelineCallbacks).GetField("_preprocessAvatarCallbacks", BindingFlags.Static | BindingFlags.NonPublic);
				if (ProcessorField != null)
				{
					object value = ProcessorField.GetValue(null);
					if (value is List<IVRCSDKPreprocessAvatarCallback> callbacks)
					{
						_preprocessAvatarCallbacks = callbacks;
						foreach (var pac in _preprocessAvatarCallbacks) {
							if (pac.GetType().Name == "LockMaterialsOnUpload") {
								lockMaterials = pac;
							}
						}
					}
				}

				if (lockMaterials != null)
				{
					_preprocessAvatarCallbacks.Remove(lockMaterials);
				}
				Debug.Log("Invoking OnPreprocessAvatar for " + obj, obj);
				try
				{
					VRCBuildPipelineCallbacks.OnPreprocessAvatar(obj);
				}
				finally
				{
					if (lockMaterials != null)
					{
						_preprocessAvatarCallbacks.Add(lockMaterials);
					}	
				}
			};
		}

		[MenuItem("Tools/Avatars 3.0 Emulator/Enable", false, 1000)]
		public static LyumaAv3Emulator EnableAv3Testing() {
			if (GameObject.Find("/GestureManager"))
			{
				GameObject.Find("/GestureManager").SetActive(false);
			}
			GameObject go = GameObject.Find("/Avatars 3.0 Emulator Control");
			if (go != null) {
				go.SetActive(true);
			} else {
				go = new GameObject("Avatars 3.0 Emulator Control");
			}
			Selection.SetActiveObjectWithContext(go, go);
			GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
			var emulator = GetOrAddComponent<LyumaAv3Emulator>(go);
			LyumaAv3Settings.ApplySettings(emulator);
			LyumaAv3OSCSettings.ApplySettings(GetOrAddComponent<LyumaAv3Osc>(go));
			EditorGUIUtility.PingObject(go);
			return emulator;
		}
	}
}
