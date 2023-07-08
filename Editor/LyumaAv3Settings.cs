using Lyuma.Av3Emulator.Runtime;
using System;
using UnityEditor;
using UnityEngine;

namespace Lyuma.Av3Emulator.Editor
{
	public class LyumaAv3Settings : EditorWindow
	{
		public const string EDITOR_PREF_KEY = "AV3EmulatorSettings";

		#region properties
		private static GameObject _dataContainer;

		private static GameObject dataContainer
		{
			get
			{
				if (_dataContainer != null) return _dataContainer;

				_dataContainer = new GameObject("AV3Emulator Settings Container");
				_dataContainer.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
				_dataContainer.SetActive(false);
				return _dataContainer;
			}
		}
		public static LyumaAv3Emulator _data;
		public static LyumaAv3Emulator data
		{
			get
			{
				if (_data == null) RefreshData();
				return _data;
			}
		}
		private static UnityEditor.Editor _editor;

		private static UnityEditor.Editor editor
		{
			get
			{
				if (_editor == null) RefreshData();
				return _editor;
			}
		}
		#endregion

		private static bool hasModifiedSettings;
		private static Vector2 scroll;

		[MenuItem("Tools/Avatars 3.0 Emulator/Settings", false, 1001)]
		public static void ShowWindow()
		{
			GetWindow<LyumaAv3Settings>("AV3Emulator Settings");
			RefreshData();
			EditorApplication.quitting -= AskSaveSettings;
			EditorApplication.quitting += AskSaveSettings;
		}

		private static void RefreshData()
		{
			if (_data == null)
			{
				_data = dataContainer.AddComponent<LyumaAv3Emulator>();
				_data.hideFlags = HideFlags.DontSave;
				LoadSettings();
			}
			if (_editor == null || _editor.target != _data) 
				_editor = UnityEditor.Editor.CreateEditor(_data);
			ClearPreviousEditors();
		}

		private static void ClearPreviousEditors()
		{
			var t = _editor.GetType();
			var edits = Resources.FindObjectsOfTypeAll(t);
			foreach (var e in edits)
				if (e != _editor)
					DestroyImmediate(e);

		}

		public void OnGUI()
		{
			scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandWidth(true));
			if (UnityEditor.PlayerSettings.legacyClampBlendShapeWeights != true) {
				EditorGUILayout.HelpBox("Clamp BlendShapes should be enabled in Project Settings/Player to match in-game behavior. Not doing so could cause inconsistencies in emulation of visemes or facial expressions.", MessageType.Warning);
				if (GUILayout.Button("Enable Clamp BlendShapes")) {
					UnityEditor.PlayerSettings.legacyClampBlendShapeWeights = true;
				}
			}

			EditorGUI.BeginChangeCheck();

			float origWidth = EditorGUIUtility.labelWidth;
			EditorGUIUtility.labelWidth = 215.0f;
			try {
				editor.OnInspectorGUI();

			} catch (Exception e) {
				Debug.LogException(e);
			}
			EditorGUIUtility.labelWidth = origWidth;
			EditorGUILayout.EndScrollView();

			hasModifiedSettings |= EditorGUI.EndChangeCheck();

			using (new GUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Restore Defaults"))
				{
					if (EditorUtility.DisplayDialog("Avatar 3.0 Emulator", "Are you sure you want to restore the default settings?", "Yes", "No"))
					{
						DestroyImmediate(_data);
						_data = dataContainer.AddComponent<LyumaAv3Emulator>();
						_data.hideFlags = HideFlags.DontSave;
						RefreshData();
						SaveSettings();
						hasModifiedSettings = false;
					}
				}
				if (!hasModifiedSettings && GUILayout.Button("Apply to Scene"))
				{
					ApplySettingsToAll();
				}
				if (hasModifiedSettings && GUILayout.Button("Save & Apply"))
				{
					SaveSettings();
					hasModifiedSettings = false;
					ApplySettingsToAll();
				}
			}

		}

		public static void ApplySettingsToAll()
		{
			LyumaAv3Emulator[] emulators = FindObjectsOfType<LyumaAv3Emulator>();
			if (emulators.Length == 0)
			{
				emulators = new LyumaAv3Emulator[] {LyumaAv3EditorSupport.EnableAv3Testing()};
			}
			var json = EditorPrefs.GetString(EDITOR_PREF_KEY, string.Empty);
			if (!string.IsNullOrWhiteSpace(json))
			{
				foreach (LyumaAv3Emulator emu in emulators)
				{
					Undo.RecordObject(emu, "Applied Av3Emulator Settings to scene");
					JsonUtility.FromJsonOverwrite(json, emu);
					EditorGUIUtility.PingObject(emu);
				}
			}
		}

		public static void ApplySettings(LyumaAv3Emulator target)
		{
			var json = EditorPrefs.GetString(EDITOR_PREF_KEY, string.Empty);
			if (!string.IsNullOrWhiteSpace(json))
				JsonUtility.FromJsonOverwrite(json, target);
		}

		public static void LoadSettings()
		{
			ApplySettings(data);
			editor.Repaint();
		}

		public static void SaveSettings()
		{
			var json = JsonUtility.ToJson(data);
			EditorPrefs.SetString(EDITOR_PREF_KEY, json);
		}

		private static void AskSaveSettings()
		{
			if (hasModifiedSettings)
			{
				if (EditorUtility.DisplayDialog("Avatar 3.0 Emulator", "There are unsaved changes to AV3Emulator settings!", "Save Settings", "Ignore")) SaveSettings();
				else LoadSettings();
				hasModifiedSettings = false;
			}
		}

		private void OnDisable() => AskSaveSettings();
	}
}
