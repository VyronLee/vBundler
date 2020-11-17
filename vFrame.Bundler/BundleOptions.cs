﻿//------------------------------------------------------------
//        File:  BunderSetting.cs
//       Brief:  BunderSetting
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-02-15 20:18
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================

using vFrame.Bundler.Interface;

namespace vFrame.Bundler
{
    public class BundlerOptions
    {
        public ILoaderFactory LoaderFactory = null;
        public int AssetBundleRequestParallelsCount = 5;
        public bool UseAssetDatabaseInsteadOfResources = true;
    }
}