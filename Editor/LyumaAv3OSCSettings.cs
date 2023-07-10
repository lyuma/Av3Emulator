using Lyuma.Av3Emulator.Runtime;
using System;
using UnityEditor;
using UnityEngine;

namespace Lyuma.Av3Emulator.Editor
{
	public class LyumaAv3OSCSettings : EditorWindow
	{
		public const string EDITOR_PREF_KEY = "AV3EmulatorOSC";

		#region properties
		public static LyumaAv3Osc _data;
		public static LyumaAv3Osc data
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

		[MenuItem("Tools/Avatars 3.0 Emulator/OSC Control Panel", false, 1001)]
		public static void ShowWindow()
		{
			GetWindow<LyumaAv3OSCSettings>("AV3Emulator OSC Control");
			RefreshData();
			EditorApplication.quitting -= AskSaveSettings;
			EditorApplication.quitting += AskSaveSettings;
		}

		private static void RefreshData()
		{
			if (_data == null)
			{
				LyumaAv3Osc[] emulators = FindObjectsOfType<LyumaAv3Osc>();
				if (emulators.Length == 0)
				{
					emulators = new LyumaAv3Osc[] {LyumaAv3EditorSupport.EnableAv3Testing().GetComponent<LyumaAv3Osc>()};
				}
				_data = emulators[0];
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
			if (_data == null) {
				EditorGUILayout.HelpBox("The Avatar 3.0 Emulator object was destroyed. Nothing to see here.", MessageType.Warning);
				if (GUILayout.Button("Refresh / Re-add Emulator to Scene")) {
					RefreshData();
				}
				return;
			}
			scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandWidth(true));
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
						LyumaAv3Osc oldData = _data;
						_data = _data.gameObject.AddComponent<LyumaAv3Osc>();
						_data.hideFlags = HideFlags.DontSave;
						SaveSettings();
						DestroyImmediate(_data);
						_data = oldData;
						Undo.RecordObject(_data, "Applied Av3Osc Settings to scene");
						ApplySettings(_data);
						hasModifiedSettings = false;
						editor.Repaint();
					}
				}
				if (GUILayout.Button("Reload Last Saved"))
				{
					Undo.RecordObject(_data, "Applied Av3Osc Settings to scene");
					ApplySettings(_data);
					hasModifiedSettings = false;
					editor.Repaint();
				}
				using (new EditorGUI.DisabledScope(!hasModifiedSettings)) {
					if (GUILayout.Button("Save Settings"))
					{
						SaveSettings();
						hasModifiedSettings = false;
					}
				}
			}

		}

		public static void ApplySettings(LyumaAv3Osc target)
		{
			var json = EditorPrefs.GetString(EDITOR_PREF_KEY, string.Empty);
			if (!string.IsNullOrWhiteSpace(json))
				JsonUtility.FromJsonOverwrite(json, target);
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
				if (EditorUtility.DisplayDialog("Avatar 3.0 Emulator", "There are unsaved changes to AV3Emulator OSC Control Settings!", "Save OSC Settings", "Ignore")) SaveSettings();
				//else LoadSettings();
				hasModifiedSettings = false;
			}
		}

		private void OnDisable() => AskSaveSettings();
	}
}
