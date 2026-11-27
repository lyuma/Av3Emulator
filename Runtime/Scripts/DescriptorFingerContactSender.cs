#if AV3_SDK_NATIVE_FINGER_CONTACTS
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace Lyuma.Av3Emulator.Runtime
{
    internal sealed class DescriptorFingerContactSender : VRCContactSender
    {
        protected override void PerformShapeUpdate()
        {
            base.PerformShapeUpdate();

            var target = GetRootTransform();
            if (!target || !target.parent)
                return;

            // Use the SDK's Finger shape to follow the target and its parent.
            Vector3 offset = target.localPosition;
            float length = offset.magnitude;
            shape.shapeType = CollisionScene.ShapeType.Finger;
            shape.transform0 = target;
            shape.transform1 = target.parent;
            shape.center = offset.normalized * Mathf.Max(0f, height - length);
            shape.maxSize = VRCAvatarDescriptor.COLLIDER_MAX_SIZE;
        }
    }
}
#endif
