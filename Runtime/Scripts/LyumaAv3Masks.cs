using System;
using UnityEngine;

namespace Lyuma.Av3Emulator.Runtime
{

	public class LyumaAv3Masks
	{
		public static AvatarMask emptyMask;
		public static AvatarMask fullMask;
		public static AvatarMask handsOnly;
		public static AvatarMask musclesOnly;

		static LyumaAv3Masks()
		{
			emptyMask = new AvatarMask();
			fullMask = new AvatarMask();
			handsOnly = new AvatarMask();
			musclesOnly = new AvatarMask();
			foreach (AvatarMaskBodyPart value in Enum.GetValues(typeof(AvatarMaskBodyPart)))
			{
				if (value == AvatarMaskBodyPart.LastBodyPart)
				{
					continue;
				}

				emptyMask.SetHumanoidBodyPartActive(value, false);
				fullMask.SetHumanoidBodyPartActive(value, true);
				handsOnly.SetHumanoidBodyPartActive(value, false);
				musclesOnly.SetHumanoidBodyPartActive(value, true);
			}

			handsOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
			handsOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
			musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
			musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
			musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
			musclesOnly.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);
		}
	}

}