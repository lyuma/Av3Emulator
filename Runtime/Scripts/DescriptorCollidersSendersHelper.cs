/* Copyright (c) 2023 Dreadrith, Lyuma and Av3Emulator authors

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
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Avatars.Components;
using System.Reflection;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Lyuma.Av3Emulator.Runtime
{
	public class DescriptorCollidersSendersHelper {

		#region Descriptor Colliders & Senders emulation

		public enum DescriptorExtractionType
		{
			None = 0,
			CollidersOnly = 1 << 0,
			SendersOnly = 1 << 1,
			CollidersAndSenders = ~0,
		}
		public static void ExtractDescriptorColliders(VRCAvatarDescriptor descriptor, DescriptorExtractionType extractionType)
		{
			var animator = descriptor.GetComponent<Animator>();
			if (!animator)
			{
				Debug.LogError($"No animator found on {descriptor.gameObject.name}; Can't extract descriptor colliders.");
				return;
			}

			FieldInfo allowCollisionField = typeof(VRCPhysBoneBase).GetField("allowCollision");
			FieldInfo collisionFilterField = typeof(VRCPhysBoneBase).GetField("collisionFilter");
			FieldInfo filterAllowSelfField = collisionFilterField != null ? collisionFilterField.FieldType.GetField("allowSelf") : null;

			bool AllowsSelfCollision(VRCPhysBone pb)
			{
				if (collisionFilterField == null)
					return (bool) allowCollisionField.GetValue(pb);

				switch ((int) allowCollisionField.GetValue(pb))
				{
					default: return false;
					case 1: return true;
					case 2: return (bool) filterAllowSelfField.GetValue(collisionFilterField.GetValue(pb));

				}
			}

			List<VRCPhysBoneCollider> createdColliders = new List<VRCPhysBoneCollider>();
			var collidingBones = descriptor.GetComponentsInChildren<VRCPhysBone>().Where(AllowsSelfCollision);

			void ExtractCollider(VRCAvatarDescriptor.ColliderConfig config, HumanBodyBones matchedBone, string collisionName, bool isSenderOnly = false)
			{
				if (config.state != VRCAvatarDescriptor.ColliderConfig.State.Disabled)
				{
					var target = config.transform;

					if (target)
					{
						//For some reason the visuals followed the parent of finger bones but has the local position of the finger as offset
						//Haven't yet confirmed if this is translated in-game but it should be
						Vector3 localOffset = Vector3.zero;
						if (IsFingerBone(matchedBone))
						{
							//Sometimes users switch the transform to be something that isn't a finger
							//Would have to go through the animator's hierarchy and double check whether this is still a non proximal finger bone
							localOffset = target.transform.localPosition;
							target = target.parent;
						}

						var collider = target.gameObject.AddComponent<VRCPhysBoneCollider>();
						var sender = target.gameObject.AddComponent<VRCContactSender>();

						collider.radius = sender.radius = config.radius;
						collider.height = sender.height = config.height;
						collider.position = sender.position = config.position + localOffset;
						collider.rotation = sender.rotation = config.rotation;
						collider.rootTransform = sender.rootTransform = target;

						collider.shapeType = VRCPhysBoneColliderBase.ShapeType.Capsule;
						sender.shapeType = ContactBase.ShapeType.Capsule;

						sender.collisionTags.Add(collisionName);
						if (HasMirrorBone(matchedBone))
							sender.collisionTags.Add(collisionName + (IsLeftBone(matchedBone) ? "L" : "R"));

						if (isSenderOnly || !extractionType.HasFlag(DescriptorExtractionType.CollidersOnly)) Object.DestroyImmediate(collider);
						else createdColliders.Add(collider);

						if (!extractionType.HasFlag(DescriptorExtractionType.SendersOnly)) 
							Object.DestroyImmediate(sender);
					}
				}
			}

			ExtractCollider(descriptor.collider_head, HumanBodyBones.Head, "Head", true);
			ExtractCollider(descriptor.collider_torso, HumanBodyBones.Chest, "Torso", true);
			ExtractCollider(descriptor.collider_footR, HumanBodyBones.RightToes, "Foot", true);
			ExtractCollider(descriptor.collider_footL, HumanBodyBones.LeftToes, "Foot", true);
			ExtractCollider(descriptor.collider_handR, HumanBodyBones.RightHand, "Hand");
			ExtractCollider(descriptor.collider_fingerLittleR, HumanBodyBones.RightLittleIntermediate, "FingerLittle");
			ExtractCollider(descriptor.collider_fingerIndexR, HumanBodyBones.RightIndexIntermediate, "FingerIndex");
			ExtractCollider(descriptor.collider_fingerMiddleR, HumanBodyBones.RightMiddleIntermediate, "FingerMiddle");
			ExtractCollider(descriptor.collider_fingerRingR, HumanBodyBones.RightRingIntermediate, "FingerRing");
			ExtractCollider(descriptor.collider_handL, HumanBodyBones.LeftHand, "Hand");
			ExtractCollider(descriptor.collider_fingerLittleL, HumanBodyBones.LeftLittleIntermediate, "FingerLittle");
			ExtractCollider(descriptor.collider_fingerIndexL, HumanBodyBones.LeftIndexIntermediate, "FingerIndex");
			ExtractCollider(descriptor.collider_fingerMiddleL, HumanBodyBones.LeftMiddleIntermediate, "FingerMiddle");
			ExtractCollider(descriptor.collider_fingerRingL, HumanBodyBones.LeftRingIntermediate, "FingerRing");

			if (extractionType.HasFlag( DescriptorExtractionType.CollidersOnly))
				foreach (var p in collidingBones)
					p.colliders.AddRange(createdColliders);
		}

		internal static Transform SmartGetBone(Animator animator, HumanBodyBones humanBone)
		{
			int boneIndex = (int)humanBone;
			Transform b = animator.GetBoneTransform((HumanBodyBones)boneIndex);
			if (b) return b;

			if (IsFingerBone(humanBone) && boneIndex%3 != 0)
				b = SmartGetBone(animator, (HumanBodyBones)(--boneIndex));
			else if (IsToeBone(humanBone))
			{
				boneIndex -= 14;
				b = animator.GetBoneTransform((HumanBodyBones)boneIndex);
			}

			return b;
		}

		internal static bool IsFingerBone(HumanBodyBones bone) => (int)bone >= 24 && (int)bone <= 53;
		internal static bool IsToeBone(HumanBodyBones bone) => (int) bone == 19 || (int) bone == 20;
		internal static bool IsLeftBone(HumanBodyBones bone) => bone.ToString().Contains("Left");

		internal static bool HasMirrorBone(HumanBodyBones bone)
		{
			string name = bone.ToString();
			return name.Contains("Left") || name.Contains("Right");
		}
		#endregion
	}
}
