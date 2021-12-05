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

public class LyumaAv3Menu : MonoBehaviour
{
    [Serializable]
    public class MenuConditional
    {
        public VRCExpressionsMenu ExpressionsMenu { get; }
        public LyumaAv3Runtime.FloatParam MandatedParam { get; }

        public MenuConditional(VRCExpressionsMenu expressionsMenu)
        {
            ExpressionsMenu = expressionsMenu;
        }

        public MenuConditional(VRCExpressionsMenu expressionsMenu, LyumaAv3Runtime.FloatParam mandatedParam)
        {
            ExpressionsMenu = expressionsMenu;
            MandatedParam = mandatedParam;
        }

        bool ShouldMenuRemainOpen(List<LyumaAv3Runtime.FloatParam> allConditions)
        {
            if (MandatedParam.name == null) return true;

            var actualParam = allConditions.Find(param => param.name == MandatedParam.name);
            if (actualParam == null) return false;
            return actualParam.value == MandatedParam.value;
        }
    }

    public LyumaAv3Runtime Runtime;
    public VRCExpressionsMenu RootMenu;
    public List<MenuConditional> MenuStack { get; } = new List<MenuConditional>();
    public bool IsMenuOpen { get; protected set; }
    private int? _activeControlIndex = null;
    private string _activeControlParameterName;

    private void Awake()
    {
        IsMenuOpen = true;

        if (LyumaAv3Runtime.addRuntimeDelegate != null) {
            LyumaAv3Runtime.addRuntimeDelegate(this);
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

    public void UserToggle(string paramName, float wantedValue) {
        var intx = Runtime.Ints.Find(param => param.name == paramName);
        if (intx != null) {
            var currentValue = intx.value;
            var newValue = (int)wantedValue == currentValue ? 0 : wantedValue;
            DoSetRuntimeX(paramName, newValue);
        }
        var floatx = Runtime.Floats.Find(param => param.name == paramName);
        if (floatx != null) {
            var currentValue = floatx.value;
            var newValue = wantedValue == currentValue ? 0.0f : wantedValue;
            DoSetRuntimeX(paramName, newValue);
        }
        var boolx = Runtime.Bools.Find(param => param.name == paramName);
        if (boolx != null) {
            var currentValue = boolx.value;
            var newValue = !currentValue;
            DoSetRuntimeX(paramName, newValue ? 1.0f : 0.0f);
        }
    }

    public void UserSubMenu(VRCExpressionsMenu subMenu)
    {
        MenuStack.Add(new MenuConditional(subMenu));
    }

    public void UserSubMenu(VRCExpressionsMenu subMenu, string paramName, float wantedValue)
    {
        MenuStack.Add(new MenuConditional(subMenu, new LyumaAv3Runtime.FloatParam {name = paramName, value = wantedValue}));
        DoSetRuntimeX(paramName, wantedValue);
    }

    public void UserBack()
    {
        if (MenuStack.Count == 0) return;
        if (_activeControlIndex != null) return;

        var lastIndex = MenuStack.Count - 1;

        var last = MenuStack[lastIndex];
        if (last.MandatedParam != null)
        {
            DoSetRuntimeX(last.MandatedParam.name, 0.0f);
        }
        MenuStack.RemoveAt(lastIndex);
    }

    public void UserControlEnter(int controlIndex)
    {
        if (_activeControlIndex != null) return;

        _activeControlIndex = controlIndex;
    }

    public void UserControlEnter(int controlIndex, string paramName, float enterValue)
    {
        if (_activeControlIndex != null) return;

        _activeControlIndex = controlIndex;
        _activeControlParameterName = paramName;
        DoSetRuntimeX(paramName, enterValue);
    }

    public void UserControlExit()
    {
        if (_activeControlIndex == null) return;

        if (_activeControlParameterName != null)
        {
            DoSetRuntimeX(_activeControlParameterName, 0.0f);
        }
        _activeControlIndex = null;
        _activeControlParameterName = null;
    }

    private void DoSetRuntimeX(string paramName, float newValue)
    {
        var intParam = Runtime.Ints.Find(param => param.name == paramName);
        if (intParam != null) {
            intParam.value = (int)newValue;
        }
        var floatParam = Runtime.Floats.Find(param => param.name == paramName);
        if (floatParam != null) {
            floatParam.value = newValue;
        }
        var boolParam = Runtime.Bools.Find(param => param.name == paramName);
        if (boolParam != null) {
            boolParam.value = newValue != 0.0;
        }
    }

    public bool IsVisualActive(string paramName, float value)
    {
        var intParam = Runtime.Ints.Find(param => param.name == paramName);
        if (intParam != null) {
            return intParam.value == (int)value;
        }

        var floatParam = Runtime.Floats.Find(param => param.name == paramName);
        if (floatParam != null) {
            return floatParam.value == value;
        }

        var boolParam = Runtime.Bools.Find(param => param.name == paramName);
        if (boolParam != null) {
            return boolParam.value == (value != 0.0);
        }
        return false;
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
