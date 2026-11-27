#if UNITY_EDITOR
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[assembly: InternalsVisibleTo("lyuma.av3emulator.Editor")]

namespace Lyuma.Av3Emulator.Runtime
{
    public partial class LyumaAv3Runtime
    {
        private struct ManualPoseBinding
        {
            public TransformSceneHandle Scene;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Captured;
        }

        private struct ManualPoseJob : IAnimationJob
        {
            public NativeArray<ManualPoseBinding> Bones;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                for (int index = 0; index < Bones.Length; index++)
                {
                    var bone = Bones[index];
                    if (!bone.Scene.IsValid(stream))
                        continue;

                    // The scene still contains the user's edits when the animation stream is evaluated.
                    bone.Position = bone.Scene.GetLocalPosition(stream);
                    bone.Rotation = bone.Scene.GetLocalRotation(stream);
                    bone.Captured = true;
                    Bones[index] = bone;
                }
            }
        }

        private AnimationScriptPlayable manualPose;
        private NativeArray<ManualPoseBinding> manualPoseBindings;
        private Transform[] manualPoseBones;

        internal bool IsManualPoseActive => manualPose.IsValid();

        internal void StartManualAvatarPose()
        {
            if (IsManualPoseActive || !playableGraph.IsValid() || AvatarSyncSource != this ||
                !IsLocal || !animator || !animator.isHuman)
                return;

            var bones = new List<Transform>();
            for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
            {
                var transform = animator.GetBoneTransform((HumanBodyBones)index);
                if (transform)
                    bones.Add(transform);
            }

            manualPoseBones = bones.ToArray();
            manualPoseBindings = new NativeArray<ManualPoseBinding>(manualPoseBones.Length, Allocator.Persistent);
            for (int index = 0; index < manualPoseBones.Length; index++)
                manualPoseBindings[index] = new ManualPoseBinding {Scene = animator.BindSceneTransform(manualPoseBones[index])};
            manualPose = AnimationScriptPlayable.Create(playableGraph, new ManualPoseJob {Bones = manualPoseBindings}, 1);
            playableGraph.Connect(playableMixer, 0, manualPose, 0);
            manualPose.SetInputWeight(0, 1);
            ((AnimationPlayableOutput)playableGraph.GetOutput(0)).SetSourcePlayable(manualPose);
        }

        internal void StopManualAvatarPose()
        {
            if (IsManualPoseActive)
            {
                ((AnimationPlayableOutput)playableGraph.GetOutput(0)).SetSourcePlayable(playableMixer);
                playableGraph.DestroyPlayable(manualPose);
                manualPose = default;
            }
            if (manualPoseBindings.IsCreated)
                manualPoseBindings.Dispose();
            manualPoseBones = null;
        }

        private void DestroyAvatarPlayableGraph()
        {
            StopManualAvatarPose();
            if (playableGraph.IsValid())
                playableGraph.Destroy();
        }

        private void ApplyManualAvatarPose()
        {
            if (!IsManualPoseActive)
                return;

            for (int index = 0; index < manualPoseBones.Length; index++)
            {
                var bone = manualPoseBones[index];
                var pose = manualPoseBindings[index];
                if (bone && pose.Captured)
                {
                    bone.localPosition = pose.Position;
                    bone.localRotation = pose.Rotation;
                }
            }
        }
    }
}
#endif
