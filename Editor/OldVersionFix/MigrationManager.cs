using System;
using System.Collections.Generic;
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

			AutoMigratePrefabs();
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

		private static bool ScanAndReplace(Scene scene)
		{
			GameObject[] objects = scene.GetRootGameObjects();
			return ReplaceEmulatorOnObjects(objects);
		}

		private static bool ReplaceEmulatorOnObjects(GameObject[] objects)
		{
			bool modified = false;
			foreach (var root in objects)
			{
				// little hack: every GameObject always have Transform as their components so I can iterate
				//    all GameObjects using GetComponentsInChildren<Transform>(true).
				foreach (var childTransform in root.GetComponentsInChildren<Transform>(true))
				{
					modified |= ScriptGuidMigrator.DoMigration(childTransform.gameObject);
				}
			}

			return modified;
		}

		[MenuItem("Tools/Avatars 3.0 Emulator Migrations/Migrate Scenes")]
		private static void ManualMigrateScenes()
		{
			try
			{
				var scenePaths = AssetDatabase.FindAssets("t:scene").Select(AssetDatabase.GUIDToAssetPath).ToList();

				// load each scene and migrate scene
				for (var i = 0; i < scenePaths.Count; i++)
				{
					var scenePath = scenePaths[i];

					EditorUtility.DisplayProgressBar("Migrating Scenes",
						$"{scenePath.Substring(scenePath.LastIndexOf('/'))} ({i} / {scenePaths.Count})",
						i / (float)scenePaths.Count);

					// opening scene will perform migration with 'sceneOpened' callback.
					var scene = EditorSceneManager.OpenScene(scenePath);
					if (scene.isDirty)
						EditorSceneManager.SaveScene(scene);
				}
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
			}
			catch
			{
				EditorUtility.DisplayDialog("Error!", "Error in migration process!", "OK");
				throw;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		[MenuItem("Tools/Avatars 3.0 Emulator Migrations/Migrate Prefabs")]
		private static void ManualMigratePrefabs()
		{
			try
			{
				var prefabs = GetPrefabs();

				// there's nothing to migrate
				if (prefabs.Count == 0) return;

				MigratePrefabs(prefabs, (name, i) => EditorUtility.DisplayProgressBar(
					"Migrating Prefabs",
					$"{name ?? "Migration Finished"} ({i} / {prefabs.Count})",
					i / (float)prefabs.Count));
			}
			catch
			{
				EditorUtility.DisplayDialog("Av3Emulator", "Error in prefab migration process!", "OK");
				throw;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		private static void AutoMigratePrefabs()
		{
			try
			{
				var prefabs = GetPrefabs();

				// there's nothing to migrate
				if (prefabs.Count == 0) return;

				if (!EditorUtility.DisplayDialog("Av3Emulator",
					    "We found prefabs might have old Av3Emulator.\n" +
					    "We need to upgrade Av3Emulator to make it work well.\n" +
					    "Do you want to migrate prefabs?",
					    "Yes", "Cancel"))
					return;

				MigratePrefabs(prefabs, (name, i) => EditorUtility.DisplayProgressBar(
					"Migrating Prefabs",
					$"{name ?? "Migration Finished"} ({i} / {prefabs.Count})",
					i / (float)prefabs.Count));
			}
			catch
			{
				EditorUtility.DisplayDialog("Av3Emulator", "Error in prefab migration process!", "OK");
				throw;
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		private static void MigratePrefabs(List<GameObject> prefabAssets, Action<string, int> progressCallback)
		{
			for (var i = 0; i < prefabAssets.Count; i++)
			{
				var prefabAsset = prefabAssets[i];
				progressCallback(prefabAsset.name, i);

				var modified = false;

				try
				{
					foreach (var childTransform in prefabAsset.GetComponentsInChildren<Transform>(true))
						modified |= ScriptGuidMigrator.DoMigration(childTransform.gameObject);
				}
				catch (Exception e)
				{
					throw new Exception($"Migrating Prefab {prefabAsset.name}: {e.Message}", e);
				}

				if (modified)
					PrefabUtility.SavePrefabAsset(prefabAsset);
			}
			progressCallback(null, prefabAssets.Count);
		}

		// based on https://github.com/anatawa12/AvatarOptimizer/blob/bfec145ec6b71055a274cbf72accf0e8f95cffdf/Editor/Migration/Migration.cs#L452-L564
		// Copyright (c) anatawa12 2023 originally published under MIT License

		private class PrefabInfo
		{
			public readonly GameObject Prefab;
			public readonly List<PrefabInfo> Children = new List<PrefabInfo>();
			public readonly List<PrefabInfo> Parents = new List<PrefabInfo>();

			public PrefabInfo(GameObject prefab)
			{
				Prefab = prefab;
			}
		}

		/// <returns>List of prefab assets. parent prefab -> child prefab</returns>
		private static List<GameObject> GetPrefabs()
		{
			bool CheckPrefabType(PrefabAssetType type) =>
				type != PrefabAssetType.MissingAsset && type != PrefabAssetType.Model &&
				type != PrefabAssetType.NotAPrefab;

			var allPrefabRoots = AssetDatabase.FindAssets("t:prefab")
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(AssetDatabase.LoadAssetAtPath<GameObject>)
				.Where(x => x)
				.Where(x => CheckPrefabType(PrefabUtility.GetPrefabAssetType(x)))
				.Where(x => x.GetComponentsInChildren<Component>().Any(y => y == null))
				// ensure missing script os old Av3Emulator because many avatars still have DynamicBone version of
				// prefabs and it's likely to be missing
				.Where(x => x.GetComponentsInChildren<Transform>()
					.Any(y => ScriptGuidMigrator.NeedsMigration(y.gameObject)))
				.ToArray();

			var sortedVertices = new List<GameObject>();

			var vertices = new LinkedList<PrefabInfo>(allPrefabRoots.Select(prefabRoot => new PrefabInfo(prefabRoot)));

			// assign Parents and Children here.
			{
				var vertexLookup = vertices.ToDictionary(x => x.Prefab, x => x);
				foreach (var vertex in vertices)
				{
					foreach (var parentPrefab in vertex.Prefab
						         .GetComponentsInChildren<Transform>(true)
						         .Select(x => x.gameObject)
						         .Where(PrefabUtility.IsAnyPrefabInstanceRoot)
						         .Select(PrefabUtility.GetCorrespondingObjectFromSource)
						         .Select(x => x.transform.root.gameObject))
					{
						if (vertexLookup.TryGetValue(parentPrefab, out var parent))
						{
							vertex.Parents.Add(parent);
							parent.Children.Add(vertex);
						}
					}
				}
			}

			// Orphaned nodes with no parents or children go first
			{
				var it = vertices.First;
				while (it != null)
				{
					var cur = it;
					it = it.Next;
					if (cur.Value.Children.Count != 0 || cur.Value.Parents.Count != 0) continue;
					sortedVertices.Add(cur.Value.Prefab);
					vertices.Remove(cur);
				}
			}

			var openSet = new Queue<PrefabInfo>();

			// Find root nodes with no parents
			foreach (var vertex in vertices.Where(vertex => vertex.Parents.Count == 0))
				openSet.Enqueue(vertex);

			var visitedVertices = new HashSet<PrefabInfo>();
			while (openSet.Count > 0)
			{
				var vertex = openSet.Dequeue();

				if (visitedVertices.Contains(vertex))
				{
					continue;
				}

				if (vertex.Parents.Count > 0)
				{
					var neededParentVisit = false;

					foreach (var vertexParent in vertex.Parents.Where(vertexParent =>
						         !visitedVertices.Contains(vertexParent)))
					{
						neededParentVisit = true;
						openSet.Enqueue(vertexParent);
					}

					if (neededParentVisit)
					{
						// Re-queue to visit after we have traversed the node's parents
						openSet.Enqueue(vertex);
						continue;
					}
				}

				visitedVertices.Add(vertex);
				sortedVertices.Add(vertex.Prefab);

				foreach (var vertexChild in vertex.Children)
					openSet.Enqueue(vertexChild);
			}

			// Sanity check
			foreach (var vertex in vertices.Where(vertex => !visitedVertices.Contains(vertex)))
				throw new Exception($"Invalid DAG state: node '{vertex.Prefab}' was not visited.");

			return sortedVertices;
		}
	}
}

