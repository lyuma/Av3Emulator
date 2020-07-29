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
using UnityEngine;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;

[RequireComponent(typeof(Animator))]
public class LyumaAv3Emulator : MonoBehaviour
{
    public bool RestartEmulator;
    private bool RestartingEmulator;
    public bool CreateNonLocalClone;

    static public LyumaAv3Emulator emulatorInstance;
    static public RuntimeAnimatorController EmptyController;

    public List<LyumaAv3Runtime> runtimes = new List<LyumaAv3Runtime>();

    private void Awake()
    {
        Animator animator = gameObject.GetOrAddComponent<Animator>();
        animator.enabled = false;
        animator.runtimeAnimatorController = EmptyController;
        emulatorInstance = this;
    }

    private void Start()
    {
        VRCAvatarDescriptor[] avatars = FindObjectsOfType<VRCAvatarDescriptor>();
        Debug.Log("drv len "+avatars.Length);
        foreach (var avadesc in avatars)
        {
            // Creates the playable director, and initializes animator.
            runtimes.Add(avadesc.gameObject.GetOrAddComponent<LyumaAv3Runtime>());
        }
    }
    private void OnDisable() {
        foreach (var runtime in runtimes) {
            runtime.enabled = false;
        }
    }
    private void OnEnable() {
        foreach (var runtime in runtimes) {
            runtime.enabled = true;
        }
    }
    private void OnDestroy() {
        foreach (var runtime in runtimes) {
            Destroy(runtime);
        }
        runtimes.Clear();
    }

    private void Update() {
        if (RestartingEmulator) {
            RestartingEmulator = false;
            Start();
        } else if (RestartEmulator) {
            RestartEmulator = false;
            OnDestroy();
            RestartingEmulator = true;
        }
        if (CreateNonLocalClone) {
            CreateNonLocalClone = false;
            foreach (var runtime in runtimes)
            {
                if (runtime.AvatarSyncSource == runtime)
                {
                    runtime.CreateNonLocalClone = true;
                }
            }
        }
    }

}
