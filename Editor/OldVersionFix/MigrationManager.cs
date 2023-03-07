using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Directory = UnityEngine.Windows.Directory;

namespace Lyuma.Av3Emulator.Editor.OldVersionFix
{
	public static class MigrationManager
	{
		static MigrationManager()
		{
			EditorSceneManager.sceneOpened += (scene, _) => ScanAndReplace(scene);
		}

		[InitializeOnLoadMethod]
		private static void ReplaceObjectsInCurrentScene()
		{
			var oldAv3EmulatorPath = FindOldAv3Emulator();
			if (oldAv3EmulatorPath != null)
			{
				if (EditorUtility.DisplayDialog("Av3Emulator",
					    "You still have an old version of the Av3Emulator Installed.\n" +
					    "This could cause unwanted behaviour. Remove it?\n" +
					    "This would delete the following folder:\n" +
					    oldAv3EmulatorPath,
					    "Yes", "No"))
				{
					Directory.Delete(oldAv3EmulatorPath);
					AssetDatabase.Refresh();
				}
			}

			for (var i = 0; i < SceneManager.sceneCount; i++)
			{
				ReplaceEmulatorOnObjects(SceneManager.GetSceneAt(i).GetRootGameObjects());
			}
		}

		private static string FindOldAv3Emulator()
		{
			var foundByGuid = AssetDatabase.GUIDToAssetPath("e42c5d0b3e2b3f64e8a88c225b3cef62");

			if (!string.IsNullOrEmpty(foundByGuid))
				return Directory.Exists(foundByGuid) ? foundByGuid : null;

			if (Directory.Exists("Assets/Lyuma/Av3Emulator"))
				return "Assets/Lyuma/Av3Emulator";

			foreach (var assetPath in AssetDatabase.FindAssets("LyumaAv3Emulator t:MonoScript", new[] { "Assets" })
				         .Select(AssetDatabase.GUIDToAssetPath)
				         .Where(x => AssetDatabase.LoadAssetAtPath<MonoScript>(x)?.GetClass() != null))
			{
				// '/' is the only path separator in unity.
				var pathComponents = assetPath.Split('/');
				var parentPath = string.Join("/", pathComponents.Take(pathComponents.Length - 2));
				return parentPath;
			}

			return null;
		}

		private static void ScanAndReplace(Scene scene)
		{
			GameObject[] objects = scene.GetRootGameObjects();
			ReplaceEmulatorOnObjects(objects);
		}

		private static void ReplaceEmulatorOnObjects(GameObject[] objects)
		{
			foreach (var root in objects)
			{
				// little hack: every GameObject always have Transform as their components so I can iterate
				//    all GameObjects using GetComponentsInChildren<Transform>(true).
				foreach (var childTransform in root.GetComponentsInChildren<Transform>(true))
				{
					ScriptGuidMigrator.DoMigration(childTransform.gameObject);
				}
			}
		}
	}
}

