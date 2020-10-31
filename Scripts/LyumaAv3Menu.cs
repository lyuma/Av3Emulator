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
using UnityEngine;
using System.Collections.Generic;
using VRC.SDK3.Avatars.ScriptableObjects;

[RequireComponent(typeof(Animator))]
public class LyumaAv3Menu : MonoBehaviour
{
    [Serializable]
    public class MenuConditional
    {
        public VRCExpressionsMenu ExpressionsMenu { get; }
        public LyumaAv3Runtime.IntParam MandatedParam { get; }

        public MenuConditional(VRCExpressionsMenu expressionsMenu)
        {
            ExpressionsMenu = expressionsMenu;
        }

        public MenuConditional(VRCExpressionsMenu expressionsMenu, LyumaAv3Runtime.IntParam mandatedParam)
        {
            ExpressionsMenu = expressionsMenu;
            MandatedParam = mandatedParam;
        }

        bool ShouldMenuRemainOpen(List<LyumaAv3Runtime.IntParam> allConditions)
        {
            if (MandatedParam == null) return true;

            var actualParam = allConditions.Find(param => param.name == MandatedParam.name);
            if (actualParam == null) return false;
            return actualParam.value == MandatedParam.value;
        }
    }

    public LyumaAv3Runtime Runtime;
    public VRCExpressionsMenu RootMenu;
    public List<MenuConditional> MenuStack { get; } = new List<MenuConditional>();
    public bool IsMenuOpen { get; private set; }
    private int? _activeControlIndex = null;
    private LyumaAv3Runtime.IntParam _activeControlParameter;

    public delegate void AddRuntime(LyumaAv3Menu runtime);
    public static AddRuntime addRuntimeDelegate;

    private void Awake()
    {
        IsMenuOpen = true;

        if (addRuntimeDelegate != null) {
            addRuntimeDelegate(this);
        }
    }

    public void ToggleMenu()
    {
        if (IsMenuOpen && _activeControlIndex != null)
        {
            UserControlExit();
        }

        IsMenuOpen = !IsMenuOpen;
    }

    public void UserToggle(string paramName, int intValue)
    {
        var currentValue = Runtime.Ints.Find(param => param.name == paramName).value;
        var newValue = intValue == currentValue ? 0 : intValue;
        DoSetRuntimeInt(paramName, newValue);
    }

    public void UserSubMenu(VRCExpressionsMenu subMenu)
    {
        MenuStack.Add(new MenuConditional(subMenu));
    }

    public void UserSubMenu(VRCExpressionsMenu subMenu, string paramName, int intValue)
    {
        MenuStack.Add(new MenuConditional(subMenu, new LyumaAv3Runtime.IntParam {name = paramName, value = intValue}));
        DoSetRuntimeInt(paramName, intValue);
    }

    public void UserBack()
    {
        if (MenuStack.Count == 0) return;
        if (_activeControlIndex != null) return;

        var lastIndex = MenuStack.Count - 1;

        var last = MenuStack[lastIndex];
        if (last.MandatedParam != null)
        {
            DoSetRuntimeInt(last.MandatedParam.name, 0);
        }
        MenuStack.RemoveAt(lastIndex);
    }

    public void UserControlEnter(int controlIndex)
    {
        if (_activeControlIndex != null) return;

        _activeControlIndex = controlIndex;
    }

    public void UserControlEnter(int controlIndex, string paramName, int intValue)
    {
        if (_activeControlIndex != null) return;

        _activeControlIndex = controlIndex;
        _activeControlParameter = new LyumaAv3Runtime.IntParam {name = paramName, value = intValue};
        DoSetRuntimeInt(paramName, intValue);
    }

    public void UserControlExit()
    {
        if (_activeControlIndex == null) return;

        if (_activeControlParameter != null)
        {
            DoSetRuntimeInt(_activeControlParameter.name, 0);
        }
        _activeControlIndex = null;
        _activeControlParameter = null;
    }

    private void DoSetRuntimeInt(string paramName, int newValue)
    {
        var intParam = Runtime.Ints.Find(param => param.name == paramName);
        if (intParam == null) return;

        intParam.value = newValue;
    }

    public bool IsVisualActive(string paramName, int value)
    {
        var intParam = Runtime.Ints.Find(param => param.name == paramName);
        if (intParam == null) return false;

        return intParam.value == value;
    }

    public float FindFloat(string paramName)
    {
        var floatParam = Runtime.Floats.Find(param => param.name == paramName);
        if (floatParam == null) return 0;

        return floatParam.value;
    }

    public void UserFloat(string paramName, float newValue)
    {
        var floatParam = Runtime.Floats.Find(param => param.name == paramName);
        if (floatParam == null) return;

        floatParam.value = newValue;
    }

    public bool HasActiveControl()
    {
        return _activeControlIndex != null;
    }

    public bool IsActiveControl(int controlIndex)
    {
        return _activeControlIndex == controlIndex;
    }
}
