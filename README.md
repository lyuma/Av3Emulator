# Avatar 3.0 Emulator
**NEW IN v0.5.0**: UPDATED _July 24, 2020_ for the Open Beta with support for VRCExpressionParameters!

Documentation here: https://docs.vrchat.com/v2020.3.2/docs/what-is-avatars-30

This is an emulator for Avatars 3.0 reimplemented in the unity editor on top the the unity [PlayableGraph](https://docs.unity3d.com/Manual/Playables-Graph.html) API, using the [AnimationControllerPlayable](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Animations.AnimatorControllerPlayable.html) and [AnimationLayerMixerPlayable](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Animations.AnimationLayerMixerPlayable.html) APIs.

## Features:
* Should emulate most features of Avatar3.
* Test non-local syncing by duplicating or clicking the "Create Non Local Clone" checkbox.
* Supports viewing and editing float and int paramters. Alt-click the ▶Floats and ▶Ints headers at the bottom.
* Supports live viewing and editing within unity's Animator window! Use the "Animator To Debug" dropdown to select which layer is visualized in the Animator window.
* Shows Tracking/Animation in the inspector.

## Not implemented/todo:
* Custom inspector
* Custom Expression Menus
* visualization of IK Tracking state when a limb is not in Animation mode.
* Eye Tracking / Blinking support
* Set View position is wrong.
* Gesture left/right weight seems wrong

## To use:
Go to the **Tools** menu, and select **Avatar 3.0 Emulator**.
This will add an object to your scene: you can always remove it if you don't want it to run.

To emulate walking and movement, click the avatar and scroll down the inspector to the bottom section with Lyuma Av3 Runtime component. Here you can change stuff.

It also supports live interacting with the animator controller. To use this, first click your avatar (even if it was already selected), and then open up Windows -> Animation -> Animator ; and pick the controller using "Animator To Debug" dropdown. You can also change parameters from inside the controller, for example moving the red dot in the 2D Blend Tree for Standing. Crouch/Prone by changing the Upright slider; or test Sitting or AFK.

If you wish to emulate walking, you can also do this by selecting Base layer, opening up the Locmotion controller with your avatar selected, and going to the Standing blendtree and dragging around the red dot.

## NOTE: about viewing animator state from layers other than Base/locomotion:
The avatar should behave correctly when "Animator to Debug" is set to Base. When you pick another layer, for example FX, the *output* of the animator may differ slightly. For example, Direct BlendTrees with non-zero initial outputs may produce different results. Also, the whole playable weight may be forced to 1 on the debugged animator.

Another useful tool is the "PlayableGraph Visualizer" which can be found in the unity Package Manager (Advanced -> Show preview packages). It is hard to use, but does a good job of visualizing clip, layer, and playable weights.

## Inputing custom stage params:

For testing your own controls, alt-click the Floats and Ints sections at the bottom of the Lyuma Av3 Runtime script to expand them all, and change the values from there. Unfortunately the expressions menu is not emulated yet: you must change the values directly.

## Other known issues:

The `proxy_` animations included in the SDK are incomplete. Unless you override them, do not expect your avatar to have a full walking cycle, and it is normal for backflip (VRCEmote=6) to stop halfway.
