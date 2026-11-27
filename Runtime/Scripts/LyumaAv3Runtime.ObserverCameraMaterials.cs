using System.Collections.Generic;
using UnityEngine;

namespace Lyuma.Av3Emulator.Runtime
{
	public partial class LyumaAv3Runtime
	{
		private Dictionary<RenderTexture, RenderTexture> observerCameraTargets;
		private Dictionary<Material, Material> observerCameraMaterials;
		private List<Object> observerCameraResources;
		private List<Material> observerCameraMaterialSlots;
		private Renderer[] observerCameraRenderers;

		private void InitializeObserverCameras()
		{
			if (observerCameraTargets != null)
				return;

			foreach (var camera in avadesc.GetComponentsInChildren<Camera>(true)) {
				var source = camera.targetTexture;
				if (!source)
					continue;
				camera.enabled = false;
				if (observerCameraTargets == null) {
					observerCameraTargets = new Dictionary<RenderTexture, RenderTexture>();
					observerCameraResources = new List<Object>();
				}
				if (!observerCameraTargets.TryGetValue(source, out var target)) {
					target = new RenderTexture(source) {name = source.name, hideFlags = HideFlags.HideAndDontSave};
					observerCameraTargets.Add(source, target);
					observerCameraResources.Add(target);
				}
				camera.targetTexture = target;
			}
			if (observerCameraTargets == null)
				return;

			// The hidden source clone already remaps camera targets. Animated material keys still refer to the authored ones.
			var template = AvatarSyncSource.OriginalSourceClone;
			if (template && template != this && template.observerCameraTargets != null) {
				foreach (var templateTarget in template.observerCameraTargets) {
					if (observerCameraTargets.TryGetValue(templateTarget.Value, out var target)
						&& !observerCameraTargets.ContainsKey(templateTarget.Key))
						observerCameraTargets.Add(templateTarget.Key, target);
				}
			}

			// A renderer can animate to a camera material even if its initial slot uses another texture.
			observerCameraRenderers = avadesc.GetComponentsInChildren<Renderer>(true);
			observerCameraMaterials = new Dictionary<Material, Material>();
			observerCameraMaterialSlots = new List<Material>();
			RebindObserverCameraMaterials();
		}

		private void RebindObserverCameraMaterials()
		{
			if (observerCameraRenderers == null)
				return;

			// Animator object-reference curves can restore authored material slots on every evaluation.
			foreach (var renderer in observerCameraRenderers) {
				if (!renderer)
					continue;
				observerCameraMaterialSlots.Clear();
				renderer.GetSharedMaterials(observerCameraMaterialSlots);
				bool changed = false;
				for (int slot = 0; slot < observerCameraMaterialSlots.Count; slot++) {
					var source = observerCameraMaterialSlots[slot];
					if (!source)
						continue;
					if (!observerCameraMaterials.TryGetValue(source, out var material)) {
						material = RemapObserverCameraMaterial(source);
						observerCameraMaterials.Add(source, material);
					}
					if (material != source) {
						observerCameraMaterialSlots[slot] = material;
						changed = true;
					}
				}
				if (!changed)
					continue;
#if UNITY_2022_3_OR_NEWER
				renderer.SetSharedMaterials(observerCameraMaterialSlots);
#else
				renderer.sharedMaterials = observerCameraMaterialSlots.ToArray();
#endif
			}
		}

		private Material RemapObserverCameraMaterial(Material source)
		{
			Material copy = null;
			foreach (var property in source.GetTexturePropertyNames()) {
				if (!(source.GetTexture(property) is RenderTexture texture) || !observerCameraTargets.TryGetValue(texture, out var target))
					continue;
				if (!copy) {
					copy = new Material(source) {name = source.name, hideFlags = HideFlags.HideAndDontSave};
					observerCameraResources.Add(copy);
				}
				copy.SetTexture(property, target);
			}
			return copy ? copy : source;
		}

		private void ReleaseObserverCameraResources()
		{
			if (observerCameraResources == null)
				return;
			foreach (var resource in observerCameraResources) {
				if (Application.isPlaying)
					Destroy(resource);
				else
					DestroyImmediate(resource);
			}
			observerCameraResources = null;
			observerCameraTargets = null;
			observerCameraMaterials = null;
			observerCameraMaterialSlots = null;
			observerCameraRenderers = null;
		}
	}
}
