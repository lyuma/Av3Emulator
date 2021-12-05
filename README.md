# Avatar 3.0 Emulator

## **[Download the latest version at: https://github.com/lyuma/Av3Emulator/releases](https://github.com/lyuma/Av3Emulator/releases)**

### **New features in v 2.9.0 (3.0 beta):**

**Release note**: Lyuma's Av3 Emulator now comes in two versions: lite/classic version with a basic menu; and the other which includes VRC-Gesture-Manager and the radial menu.

<br clear="all"><img align="right" src="Screenshots/av3_radial_menu.png" width="30%">

* New! Integration with the Avatar 3.0 Menu when [VRC Gesture Manager by BlackStartx](https://github.com/BlackStartx/VRC-Gesture-Manager) is installed.
* Support for MirrorReflection duplicate with only FX playable (can choose which version to show/hide).
* Attempt to emulate the uninitialized state for remote players.
* Add a "update interval" to simulate network delay when sending parameters.

* FIXED: Default layer weights were being ignored. Unity defaults layers to weight 0, and this should make such mistakes easier to catch.
* FIXED: Add ParameterDriver was failing half the time.
* FIXED: AngularY float range to match smooth turn in VR (by NotAKidOnSteam)
* FIXED: GestureWeight is always 0 for neutral; always 1 for most gestures, and only varies from 0 to 1 for Fist.
* FIXED: Local player bounds forced to update when offscreen; and animator culling turned off.

Known issues: Edit Mode in radial menu does not work

As always, watch out for mistakes with Write Defaults. Also, while animating your own avatar might work for you, it may break the avatar for remote players in game: this cannot be perfectly replicated in editor.

### **New features in v 2.2.2:** <br clear="all">
* Fix max value for random int, for example used in ragdoll system (Thanks, ksivl)
* Fix crash when emulator is enabled and exiting play mode (Thanks, ksivl)
* Made a further attempt to mitigate interfering with the upload process if a PipelineSaver component is present.

### **New features in v 2.2.1:**
* Fix off-by-one errors with layer and playable weight changes
* Fix bugs with layer control behaviours
* Fixed saved parameters. They were broken in the last update.
* Added AvatarVersion variable, set to 3 in debug inspector.
* Allow testing IKPose and TPose calibration.
* Force exact path for default controllers from VRCSDK to avoid finding edited duplicates.
* Reduce logspam from parameter drivers.

### **New features in v 2.1.1:**
* Supports new features in VRChat 2021.1.1
* Expression menu support for Bool and Float toggles and submenus, in addition to existing support for Int.
* Removed support for Parameter Drivers from sub-animators, to match ingame. Use a checkbox on the "Avatar 3.0 Emulator" control object to re-enable the legacy behavior for nostalgia sake, I dunno.
* To test saving, there is a checkbox (on by default) which keeps saved parameters when the avatar is reset.
* Supports synced bools and triggers same as ingame. The rules for "Add" and "Set" operations are different for bools and triggers in expression parameters and those not. See below for the rules.
* Fixed issues with 8-bit float quantization. Should now match serialization in-game. Quantization of floats is now off by default except if you check the "Locally 8-bit quantized floats" box or make a non-local clone.
* *What is quantization?* Basically, 0.5 locally is not 0.5 for other users. You should not assume floats are sent precisely over the network. A float is serialized into a value between -127 and 127, and deserialized back to -1.0 to 1.0 range. Only -1.0, 0.0 and 1.0 are sent precisely over the network.

Not implemented: saving and loading saved expression parameters. Parameters are lost every time you enter play mode.

### **New features in v 2.0.0:**
* **Animator To Debug** dropdown has been fixed. View your animator in action in the Unity Animator window, and update parameters in real time.
* The **Lyuma Av3 Menu** component allows using your avatar's expression menu actions directly from the editor. Click + to open two radial menus at once to test combining puppets. (Thanks to @hai-vr for the contribution!)
* Support for testing Visemes.
* Support for the Is VR checkbox, tracking type and more. (Thanks to @hai-vr for the contribution!)
* Basic support for Generic avatars.
* After using **Tools** -> **Enable Avatars 3.0 Emulator**, set default VR tracking type and other settings by selecting the **Avatars 3.0 Emulator Control** object before entering Play Mode.

### **About the Avatar 3.0 Emulator:**

What is Avatars 3.0? Read the VRChat documentation here: https://docs.vrchat.com/v2020.3.2/docs/what-is-avatars-30

This is an emulator for Avatars 3.0 reimplemented in the unity editor on top the the unity [PlayableGraph](https://docs.unity3d.com/Manual/Playables-Graph.html) API, using the [AnimationControllerPlayable](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Animations.AnimatorControllerPlayable.html) and [AnimationLayerMixerPlayable](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Animations.AnimationLayerMixerPlayable.html) APIs.

## Av3 Emulator Overview:
![Avatar 3.0 overview](Screenshots/a3_example.png)
![Avatar 3.0 explanation](Screenshots/avatar3emu_tutorial.png)
[(Open the above full explanation image)](Screenshots/avatar3emu_tutorial.png)

## Features:
* Should emulate most features of Avatar3.
* Test non-local syncing by duplicating or clicking the "Create Non Local Clone" checkbox.
* Supports live viewing and editing within unity's Animator window! Use the "Animator To Debug" dropdown to select which layer is visualized in the Animator window.
* Shows Tracking/Animation in the inspector.
* Gesture left/right weight to test analog Fist gesture strength.
* Custom Expression Menus
* Supports viewing and editing float and int paramters, view the Expression Menu, via Parameters tab of the Animator window, via the blend tree input, via Parameter Driver, or manually, by alt-clicking the ▶Floats and ▶Ints headers at the bottom of the Lyuma Av3 Runtime script.
* Visemes for both parameters and testing builtin blend shapes (note: visemes always set to 0% or 100%, not in between.)

## Not implemented/todo:
* Custom inspector
* visualization of IK Tracking state when a limb is not in Animation mode.
* Eye Tracking / Blinking support is not implemented.
* Set View position not fully implemented.

## How to use the Av3 Emulator:

Go to the **Tools** menu, and select **Avatar 3.0 Emulator**.
This will add an object to your scene: you can always remove it if you don't want it to run. Use this object to set default VR mode, tracking type or Animator to Debug settings. Let me know if other settings would be useful here.

To emulate walking and movement, click the avatar and scroll down the inspector to the bottom section with Lyuma Av3 Runtime component. Here you can change stuff.

It also supports live interacting with the animator controller. To use this, first click your avatar (even if it was already selected), and then open up **Windows** -> **Animation** -> **Animator** ; and pick the controller using "**Animator To Debug**" dropdown. You can also change parameters from inside the controller, for example moving the red dot in the 2D Blend Tree for Standing. Crouch/Prone by changing the Upright slider; or test Sitting or AFK.

If you wish to emulate walking, you can also do this by selecting Base layer, opening up the Locmotion controller with your avatar selected, and going to the Standing blendtree and dragging around the red dot.

## NOTE: about viewing animator state from layers other than Base/locomotion:
The avatar should behave correctly when "Animator to Debug" is set to Base. When you pick another layer, for example FX, the *output* of the animator may differ slightly. For example, Direct BlendTrees with non-zero initial outputs may produce different results. Also, the whole playable weight may be forced to 1 on the debugged animator.

Another useful tool is the "PlayableGraph Visualizer" which can be found in the unity Package Manager (Advanced -> Show preview packages). It is hard to use, but does a good job of visualizing clip, layer, and playable weights.

## Inputing custom stage params:

Use the expression menu under the **Lyuma Av3 Menu** header, or the Parameters tab of the Animator window, after selecting your layer as **Animator To Debug** in the inspector.

For manual control, you can also alt-click the Floats and Ints sections at the bottom of the Lyuma Av3 Runtime script to expand them all, and change the values from there.

## Notes about Set, Add ( blank ) and Random operations for boolean and trigger parameters.

For **Bool and Trigger values not set in expression parameters**, the rules are straightforward:

* Unsynced Bool Set: sets to true if Value is checked
* Unsynced Bool Random: sets to true if RAND() < Chance, false otherwise
* Unsynced Bool Add: sets to true if Value != 0.0 in debug inspector
* Unsynced trigger Set: sets unconditionally
* Unsynced trigger Random: sets if RAND() < Chance
* Unsynced trigger Add: sets unconditionally

(Note that "Add" shows up as a blank dropdown. Unlike the inspector, Add uses the Value instead of the Chance field. You need to check the debug inspector. Also, there is no point in using "Add" in this case, so just fix it if you see a blank dropdown.)

*HOWEVER*, For **Bool values set in expression paramters**, there is a notable difference in the case of the "Add" (blank) operation:

* Synced Bool Set: sets to true if Value is checked
* Synced Bool Random: sets to true if RAND() < Chance, false otherwise
* Synced Bool Add: sets to true if (Value + (currentValue?1.0:0.0)) != 0.0; sets to false otherwise

Using Add (blank dropdown) with a Value of -1.0 (in the debug inspector or using "Set" first), it is possible to make a toggle operation, but only for Bool values in your expression parameters.

Finally, Triggers set in expression parameters act completely unintuitively. **AVOID USING PARAMETER DRIVERS ON TRIGGER PARAMETERS SET IN EXPRESSION PARAMETERS!!!** Still, if you are interested, here are the rules for synced Trigger parameters:

* Synced trigger Set: Uses the Value in the Debug inspector. sets to true if Value != 0.0; sets to false if Value == 0.0; does not set trigger if set to 0.0 by next frame.
* Synced trigger Random: sets if RAND() < Chance, false otherwise
* Synced trigger Add: sets to true if (Value + (currentValue?1.0:0.0)) != 0.0; sets to false otherwise; does not set trigger if set to 0.0 by next frame.

Synced Trigger parameters remember if they were set to true, and will only set again if explicitly set to false and then true again.

## Other known issues:

The `proxy_` animations included in the SDK are incomplete. Unless you override them, do not expect your avatar to have a full walking cycle, and it is normal for backflip (VRCEmote=6) to stop halfway.

If you're having unexplained issues, they might happen in game too. The most common cause is due to Write Defaults being turned on in one or more states, in any layer, in any controller. You must have Write Defaults OFF **everywhere** to ensure proper operation 100% of the time. Please see the guide below.

## Helpful guides

![Lock your inspector to allow investigating other objects](Screenshots/lock_inspector_tutorial.png)![Checklist for turning off Write Defaults.](Screenshots/write_defaults_off.png)
[(View full lock inspector explanation)](Screenshots/lock_inspector_tutorial.png) [(View full write defaults off checklist)](Screenshots/write_defaults_off.png)

