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
				return foundByGuid;

			if (Directory.Exists("Assets/Lyuma/Av3Emulator"))
				return "Assets/Lyuma/Av3Emulator";

			Type emulatorType = Type.GetType("LyumaAv3Emulator, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
			if (emulatorType != null)
			{
				string[] guids = AssetDatabase.FindAssets("LyumaAv3Emulator", new[] { "Assets" });
				string[] assetPaths = guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
				foreach (var assetPath in assetPaths)
				{
					string absolutePath = Application.dataPath + "/" + assetPath.Substring(7);
					string absoluteParentPath = new System.IO.DirectoryInfo(absolutePath).Parent.Parent.FullName;
					string parentPath = "Assets" + Path.DirectorySeparatorChar +
					                    absoluteParentPath.Substring(Application.dataPath.Length + 1);

					return parentPath;
				}
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

