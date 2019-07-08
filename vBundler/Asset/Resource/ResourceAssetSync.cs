﻿//------------------------------------------------------------
//        File:  ResourceAssetSync.cs
//       Brief:  Sync load assets from resources.
//
//      Author:  VyronLee, lwz_jz@hotmail.co
//
//    Modified:  2019-02-15 20:00
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================

using System;
using System.IO;
using UnityEngine;
using vBundler.Exception;
using vBundler.Utils;
using Logger = vBundler.Log.Logger;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace vBundler.Asset.Resource
{
    public class ResourceAssetSync : AssetBase
    {
        public ResourceAssetSync(string assetName, Type type)
            : base(assetName, type, null)
        {
        }

        protected override void LoadAssetInternal()
        {
            Logger.LogInfo("Start synchronously loading asset: {0}", _path);

#if UNITY_EDITOR
            if (BundlerSetting.kUseAssetDatabaseInsteadOfResources)
            {
                _asset = AssetDatabase.LoadAssetAtPath(_path, _type);
                if (!_asset)
                    throw new BundleAssetLoadFailedException("Could not load asset from AssetDatabase: " + _path);
            }
            else
#endif
            {
                var resPath = PathUtility.RelativeProjectPathToRelativeResourcesPath(_path);

                var sb = FixedStringBuilderPool.Get();
                sb.Append(Path.GetDirectoryName(resPath));
                sb.Append("/");
                sb.Append(Path.GetFileNameWithoutExtension(resPath));

                _asset = Resources.Load(sb.ToString(), _type);
                if (!_asset)
                    throw new BundleAssetLoadFailedException("Could not load asset from resources: " + _path);

                FixedStringBuilderPool.Return(sb);
            }

            IsDone = true;

            Logger.LogInfo("End synchronously loading asset: " + _path);
        }
    }
}