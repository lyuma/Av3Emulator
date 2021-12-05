/* Copyright (c) 2020 Lyuma <xn.lyuma@gmail.com>

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

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

[CustomEditor(typeof(LyumaAv3Menu), true)]
public class LyumaAv3MenuEditor : Editor
{
    private readonly Dictionary<Texture2D, Texture2D> _resizedIcons = new Dictionary<Texture2D, Texture2D>();
    private VRCExpressionsMenu _currentMenu;

    public override void OnInspectorGUI()
    {
        var menu = (LyumaAv3Menu)target;
        if (menu.Runtime == null) return;
        if (menu.RootMenu == null)
        {
            menu.RootMenu = (VRCExpressionsMenu)EditorGUILayout.ObjectField(new GUIContent("Expressions Menu"), null, typeof(VRCExpressionsMenu), false);
            return;
        }

        var isInRootMenu = menu.MenuStack.Count == 0;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(menu.IsMenuOpen ? "Close menu" : "Open menu"))
        {
            menu.ToggleMenu();
        }

        if (menu.gameObject.GetComponents<LyumaAv3Menu>().Length == 1)
        {
            if (GUILayout.Button("+", GUILayout.Width(20)))
            {
                OpenMenuForTwoHandedSupport(menu);
            }
        }

        GUILayout.EndHorizontal();

        GUILayout.Label(
            (isInRootMenu ? "Expressions" : LabelizeMenu()) +
            (menu.IsMenuOpen ? "" : " [Menu is closed]"),
            EditorStyles.boldLabel);

        if (!menu.IsMenuOpen) {
            return;
        }

        _currentMenu = menu.MenuStack.Count == 0 ? menu.RootMenu : menu.MenuStack.Last().ExpressionsMenu;

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField(_currentMenu, typeof(VRCExpressionsMenu), false);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(isInRootMenu || menu.HasActiveControl());
        if (GUILayout.Button("Back"))
        {
            menu.UserBack();
        }
        EditorGUI.EndDisabledGroup();
        for (var controlIndex = 0; controlIndex < _currentMenu.controls.Count; controlIndex++)
        {
            var control = _currentMenu.controls[controlIndex];
            switch (control.type)
            {
                case VRCExpressionsMenu.Control.ControlType.Button:
                    FromToggle(control, "Button");
                    break;
                case VRCExpressionsMenu.Control.ControlType.Toggle:
                    FromToggle(control, "Toggle");
                    break;
                case VRCExpressionsMenu.Control.ControlType.SubMenu:
                    FromSubMenu(control);
                    break;
                case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet:
                    FromTwoAxis(control, controlIndex);
                    break;
                case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet:
                    FromFourAxis(control, controlIndex);
                    break;
                case VRCExpressionsMenu.Control.ControlType.RadialPuppet:
                    FromRadial(control, controlIndex);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        if (_currentMenu.controls.Count == 0)
        {
            EditorGUILayout.LabelField("(This menu has no controls)");
        }
    }

    private static void OpenMenuForTwoHandedSupport(LyumaAv3Menu menu)
    {
        var mainMenu = menu.Runtime.gameObject.AddComponent<LyumaAv3Menu>();
        mainMenu.Runtime = menu.Runtime;
        mainMenu.RootMenu = menu.RootMenu;
    }

    private string LabelizeMenu()
    {
        var menu = (LyumaAv3Menu)target;

        var lastMenu = menu.MenuStack.Last();
        if (lastMenu.MandatedParam == null)
        {
            return lastMenu.ExpressionsMenu.name;
        }

        return lastMenu.ExpressionsMenu.name + " (" + lastMenu.MandatedParam.name + " = " + lastMenu.MandatedParam.value + ")";
    }

    private void FromToggle(VRCExpressionsMenu.Control control, string labelType)
    {
        var menu = (LyumaAv3Menu)target;

        var parameterName = control.parameter.name;
        var controlValue = control.value;

        var isActive = menu.IsVisualActive(parameterName, controlValue);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(menu.HasActiveControl());
        if (GreenBackground(isActive, () => ParameterizedButton(control, parameterName, controlValue)))
        {
            menu.UserToggle(parameterName, controlValue);
        }
        EditorGUI.EndDisabledGroup();
        LabelType(labelType);
        EditorGUILayout.EndHorizontal();
    }

    private void FromSubMenu(VRCExpressionsMenu.Control control)
    {
        var menu = (LyumaAv3Menu)target;

        var parameterName = control.parameter.name;
        var wantedValue = control.value;

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(menu.HasActiveControl());
        if (ParameterizedButton(control, parameterName, wantedValue))
        {
            if (IsValidParameterName(parameterName))
            {
                menu.UserSubMenu(control.subMenu, parameterName, wantedValue);
            }
            else
            {
                menu.UserSubMenu(control.subMenu);
            }
        }
        EditorGUI.EndDisabledGroup();
        LabelType("SubMenu");
        EditorGUILayout.EndHorizontal();
    }

    private void FromRadial(VRCExpressionsMenu.Control control, int controlIndex)
    {
        var menu = (LyumaAv3Menu)target;

        SubControl(control, controlIndex, menu, "Radial");

        if (menu.IsActiveControl(controlIndex))
        {
            if (control.subParameters.Length > 0)
            {
                SliderFloat(menu, control.subParameters[0], "Rotation", 0f, 1f);
            }
        }
    }

    private void FromTwoAxis(VRCExpressionsMenu.Control control, int controlIndex)
    {
        var menu = (LyumaAv3Menu)target;

        SubControl(control, controlIndex, menu, "TwoAxis");

        if (menu.IsActiveControl(controlIndex))
        {
            var sanitySubParamLength = control.subParameters.Length;
            if (sanitySubParamLength > 0) SliderFloat(menu, control.subParameters[0], "Horizontal", -1f, 1f);
            if (sanitySubParamLength > 1) SliderFloat(menu, control.subParameters[1], "Vertical", -1f, 1f);

            var oldColor = Color.HSVToRGB(
                0,
                sanitySubParamLength > 0 ? menu.FindFloat(control.subParameters[0].name) * 0.5f + 0.5f : 0,
                sanitySubParamLength > 1 ? menu.FindFloat(control.subParameters[1].name) * 0.5f + 0.5f : 0);
            var newColor = EditorGUILayout.ColorField(oldColor);
            if (oldColor.r != newColor.r || oldColor.g != newColor.g || oldColor.b != newColor.b)
            {
                Color.RGBToHSV(newColor, out _, out var s, out var v);
                if (sanitySubParamLength > 0) menu.UserFloat(control.subParameters[0].name, s  * 2 - 1);
                if (sanitySubParamLength > 1) menu.UserFloat(control.subParameters[1].name, v * 2 - 1);
            }
        }
    }

    private void FromFourAxis(VRCExpressionsMenu.Control control, int controlIndex)
    {
        var menu = (LyumaAv3Menu)target;

        SubControl(control, controlIndex, menu, "FourAxis");

        if (menu.IsActiveControl(controlIndex))
        {
            var sanitySubParamLength = control.subParameters.Length;
            if (sanitySubParamLength > 0) SliderFloat(menu, control.subParameters[0], "Up", 0f, 1f);
            if (sanitySubParamLength > 1) SliderFloat(menu, control.subParameters[1], "Right", 0f, 1f);
            if (sanitySubParamLength > 2) SliderFloat(menu, control.subParameters[2], "Down", 0f, 1f);
            if (sanitySubParamLength > 3) SliderFloat(menu, control.subParameters[3], "Left", 0f, 1f);

            var oldColor = Color.HSVToRGB(
                0,
                (sanitySubParamLength > 0 ? menu.FindFloat(control.subParameters[0].name) : 0) * 0.5f + 0.5f
                -(sanitySubParamLength > 2 ? menu.FindFloat(control.subParameters[2].name) : 0) * 0.5f + 0.5f,
                (sanitySubParamLength > 1 ? menu.FindFloat(control.subParameters[1].name) : 0) * 0.5f + 0.5f
                -(sanitySubParamLength > 3 ? menu.FindFloat(control.subParameters[3].name) : 0) * 0.5f + 0.5f);
            var newColor = EditorGUILayout.ColorField(oldColor);
            if (oldColor.r != newColor.r || oldColor.g != newColor.g || oldColor.b != newColor.b)
            {
                Color.RGBToHSV(newColor, out _, out var s, out var v);
                if (sanitySubParamLength > 0) menu.UserFloat(control.subParameters[0].name, Mathf.Clamp(v  * 2 - 1, 0f, 1f));
                if (sanitySubParamLength > 1) menu.UserFloat(control.subParameters[1].name, Mathf.Clamp(s  * 2 - 1, 0f, 1f));
                if (sanitySubParamLength > 2) menu.UserFloat(control.subParameters[2].name, -Mathf.Clamp(v  * 2 - 1, -1f, 0f));
                if (sanitySubParamLength > 3) menu.UserFloat(control.subParameters[3].name, -Mathf.Clamp(s  * 2 - 1, -1f, 0f));
            }
        }
    }

    private void SubControl(VRCExpressionsMenu.Control control, int controlIndex, LyumaAv3Menu menu, string labelType)
    {
        var parameterName = control.parameter.name;
        var intValue = (int) control.value;

        var isActive = menu.IsVisualActive(parameterName, intValue);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginDisabledGroup(menu.HasActiveControl() && !menu.IsActiveControl(controlIndex));
        if (GreenBackground(isActive || menu.IsActiveControl(controlIndex), () => ParameterizedButton(control, parameterName, intValue)))
        {
            if (!menu.IsActiveControl(controlIndex))
            {
                if (IsValidParameterName(parameterName))
                {
                    menu.UserControlEnter(controlIndex, parameterName, intValue);
                }
                else
                {
                    menu.UserControlEnter(controlIndex);
                }
            }
            else
            {
                menu.UserControlExit();
            }
        }

        EditorGUI.EndDisabledGroup();
        LabelType(labelType);
        EditorGUILayout.EndHorizontal();
    }

    private static void SliderFloat(LyumaAv3Menu menu, VRCExpressionsMenu.Control.Parameter subParam, string intent, float left, float right)
    {
        if (subParam == null || subParam.name == "")
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.Slider(intent, 0, left, right);
            EditorGUI.EndDisabledGroup();
            return;
        }

        menu.UserFloat(subParam.name, EditorGUILayout.Slider(intent + " (" + subParam.name + ")", menu.FindFloat(subParam.name), left, right));
    }

    private bool ParameterizedButton(VRCExpressionsMenu.Control control, string parameterName, float wantedValue)
    {
        var hasParameter = IsValidParameterName(parameterName);
        return GUILayout.Button(new GUIContent(control.name + (hasParameter ? " (" + parameterName + " = " + wantedValue + ")" : ""), ResizedIcon(control.icon)));
    }

    private Texture2D ResizedIcon(Texture2D originalIcon)
    {
        if (_resizedIcons.ContainsKey(originalIcon))
        {
            return _resizedIcons[originalIcon];
        }

        var resizedIcon = GenerateResizedIcon(originalIcon, 32);
        _resizedIcons[originalIcon] = resizedIcon;
        return resizedIcon;
    }

    private static Texture2D GenerateResizedIcon(Texture2D originalIcon, int width)
    {
        var render = new RenderTexture(width, width, 24);
        RenderTexture.active = render;
        Graphics.Blit(originalIcon, render);

        var resizedIcon = new Texture2D(width, width);
        resizedIcon.ReadPixels(new Rect(0, 0, width, width), 0, 0);
        resizedIcon.Apply();

        return resizedIcon;
    }

    private static T GreenBackground<T>(bool isActive, Func<T> inside)
    {
        var col = GUI.color;
        try
        {
            if (isActive) GUI.color = Color.green;
            return inside();
        }
        finally
        {
            GUI.color = col;
        }
    }

    private static void LabelType(string toggle)
    {
        EditorGUILayout.LabelField(toggle, GUILayout.Width(70), GUILayout.ExpandHeight(true));
    }

    private static bool IsValidParameterName(string parameterName)
    {
        return !string.IsNullOrEmpty(parameterName);
    }
}
