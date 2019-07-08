﻿//------------------------------------------------------------
//        File:  IAsset.cs
//       Brief:  Asset interface.
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-02-15 20:04
//   Copyright:  Copyright (c) 2018, VyronLee
//============================================================

using UnityEngine;

namespace vBundler.Interface
{
    public interface IAsset
    {
        bool IsDone { get; set; }

        Object GetAsset();
        T GetAsset<T>() where T : Object;

        GameObject Instantiate();
        void SetTo(Component target, string propName);

        void Retain();
        void Release();
    }
}