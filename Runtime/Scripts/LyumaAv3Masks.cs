using System;
using UnityEngine;

namespace Lyuma.Av3Emulator.Runtime
{

	public class LyumaAv3Masks
	{
		private static AvatarMask _emptyMask = null;
		public static AvatarMask emptyMask {
			get {
				if (_emptyMask != null) {
					return _emptyMask;
				}
				InitializeMasks();
				return _emptyMask;
			}
		}

		public static AvatarMask _fullMask;
		public static AvatarMask fullMask {
			get {
				if (_fullMask != null) {
					return _fullMask;
				}
				InitializeMasks();
				return _fullMask;
			}
		}
		public static AvatarMask _handsOnly;
		public static AvatarMask handsOnly {
			get {
				if (_handsOnly != null) {
					return _handsOnly;
				}
				InitializeMasks();
				return _handsOnly;
			}
		}
		public static AvatarMask _musclesOnly;
		public static AvatarMask musclesOnly {
			get {
				if (_musclesOnly != null) {
					return _musclesOnly;
				}
				InitializeMasks();
				return _musclesOnly;
			}
		}

		static AvatarMask InitializeMasks() // always returns null to workaround stupid c# syntax issue
		{
			_emptyMask = new AvatarMask();
			_fullMask = new AvatarMask();
			_handsOnly = new AvatarMask();
			_musclesOnly = new AvatarMask();
			foreach (AvatarMaskBodyPart value in Enum.GetValues(typeof(AvatarMaskBodyPart)))
			{
				if (value == AvatarMaskBodyPart.LastBodyPart)
				{
					continue;
				}

				_emptyMask.SetHumanoidBodyPartActive(value, false);
				_fullMask.SetHumanoidBodyPartActive(value, true);
				_handsOnly.SetHumanoidBodyPartActive(value, false);
				_musclesOnly.SetHumanoidBodyPartActive(value, true);
			}

			_handsOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
			_handsOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
			_musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
			_musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
			_musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
			_musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);
			return null; // allow ?? operator because c# stupid and doesn't support , operator
		}
	}

}