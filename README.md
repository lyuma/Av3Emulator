# Avatar 3.0 Emulator
**NEW IN v0.5.0**: UPDATED _July 24, 2020_ for the Open Beta with support for VRCExpressionParameters!

Documentation here: https://docs.vrchat.com/v2020.3.2/docs/what-is-avatars-30

This is an emulator for Avatars 3.0 reimplemented in the unity editor on top the the unity [PlayableGraph](https://docs.unity3d.com/Manual/Playables-Graph.html) API, using the [AnimationControllerPlayable](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Animations.AnimatorControllerPlayable.html) and [AnimationLayerMixerPlayable](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Animations.AnimationLayerMixerPlayable.html) APIs.

## Features:
* Should emulate most features of Avatar3.
* Test non-local syncing by duplicating or clicking the "Create Non Local Clone" checkbox.
* Supports viewing and editing float and int paramters. Alt-click the ▶Floats and ▶Ints headers at the bottom.
* Supports live viewing of animator controller state. To use, click the avatar in the scene, then in project view, click the correct animator controller. This will be your own if overriding; or it will be one of the defaults in VRCSDK/Examples3/Animation/Controllers. If you did this right, the Animator window should "Auto Live Link" the controller state and allow you to observe what is happening.
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

It also supports live interacting with the animator controller. To use this, first click your avatar (even if it was already selected), and then open up Windows -> Animation -> Animator ; and find the locomotion or base controller from project. If you customize it, pick your customized version... otherwise, go to VRCSDK3/Examples3/Animation/Controllers and click `vrc_AvatarV3LocomotionLayer` (or whichever controller you want to debug). You can also change parameters from inside the controller, for example moving the red dot in the 2D Blend Tree for Standing. Crouch/Prone by changing the Upright slider; or test Sitting or AFK.

If you wish to emulate walking, you can also do this by opening up the Locmotion controller with your avatar selected, and going to the Standing blendtree and dragging around the red dot.

## NOTE: about viewing animator state from layers other than Base/locomotion:
Only the Base layer will show parameters and layer weights in the unity Animator window. Other layers will show 0's for everything and every layer will have weight 0.

You can still edit parameter values, just they show 0 when you finish editing. Additionally, while it is ok to open Blend Trees in the *inspector*, opening a BlendTree in the animation editor (such as double-clicking on it) will force the input values to 0. I believe this to be a Unity bug.

A workaround is don't double-click blendtrees while playing, or if you want to test the state machine, you can put your FX layer into the base slot temporarily to test it, and tick reset Avatar in the emulator component. Another tool is the "PlayableGraph Visualizer" which can be found in the unity Package Manager (Advanced -> Show preview packages). It is hard to use, but does a good job of visualizing clip, layer, and playable weights.

## Inputing custom stage params:

For testing your own controls, alt-click the Floats and Ints sections at the bottom of the Lyuma Av3 Runtime script to expand them all, and change the values from there. Unfortunately the expressions menu is not emulated yet: you must change the values directly.

## Other known issues:

As mentioned above, a Unity bug prevents you from double-clicking blendtrees in the Animator window, or from *observing* parameter values or layer weights in the Animator window.

The `proxy_` animations included in the SDK are incomplete. Unless you override them, do not expect your avatar to have a full walking cycle, and it is normal for backflip (VRCEmote=6) to stop halfway.

Avoid changing parameter values too quickly (for example click-dragging on the inspector row): it can cause the avatar to glitch out.
