﻿//------------------------------------------------------------
//        File:  BundleLoaderAsync.cs
//       Brief:  BundleLoaderAsync
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-02-15 20:08
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================

using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using vFrame.Bundler.Exception;
using vFrame.Bundler.Interface;
using vFrame.Bundler.Utils;
using Logger = vFrame.Bundler.Logs.Logger;

namespace vFrame.Bundler.Loaders
{
    public class BundleLoaderAsync : BundleLoaderBase, IAsync
    {
        private AssetBundleCreateRequest _bundleLoadRequest;

        private static int _assetbundleRequestParallelsCount = 0;

        public override AssetBundle AssetBundle
        {
            get
            {
                if (!_assetBundle)
                    _assetBundle = _bundleLoadRequest.assetBundle;
                return _assetBundle;
            }
        }

        public override bool IsDone
        {
            get { return !MoveNext(); }
        }

        public float Progress
        {
            get
            {
                if (_bundleLoadRequest == null)
                    return 0f;

                if (!_bundleLoadRequest.isDone)
                    return _bundleLoadRequest.progress;

                return 1f;
            }
        }

        public bool MoveNext()
        {
            if (_assetBundle)
                return false;

            if (_bundleLoadRequest == null) {
                Profiler.BeginSample("BundleLoaderAsync:MoveNext");
                if (_assetbundleRequestParallelsCount >= Options.AssetBundleRequestParallelsCount) {
                    Profiler.EndSample();
                    return true;
                }
                _assetbundleRequestParallelsCount++;

                _bundleLoadRequest = CreateBundleLoadRequest();
            }

            if (!_bundleLoadRequest.isDone || _bundleLoadRequest.progress < 1f)
            {
                Logger.LogInfo("Bundle load request does not finished: {0}, progress: {1:0.00}",
                    _path, _bundleLoadRequest.progress);
                Profiler.EndSample();
                return true;
            }

            Logger.LogInfo("Bundle load request finished: {0}", _path);

            _assetBundle = _bundleLoadRequest.assetBundle;
            _assetbundleRequestParallelsCount--;

            Logger.LogInfo("Add assetbundle to cache: {0}", _path);

            if (AssetBundleCache.ContainsKey(_path))
                throw new System.Exception("Assetbundle already in cache: " + _path);
            AssetBundleCache.Add(_path, _assetBundle);

            Logger.LogInfo("AssetBundle asynchronously loading finished, path: {0}", _path);

            IsLoading = false;
            Profiler.EndSample();
            return false;
        }

        public void Reset()
        {
        }

        public object Current { get; private set; }

        protected override bool LoadProcess()
        {
            Logger.LogInfo("Start asynchronously loading process: {0}", _path);

            IsLoading = true;

            return false;
        }

        private AssetBundleCreateRequest CreateBundleLoadRequest()
        {
            Logger.LogInfo("Bundle load request does not exist, create it from file: {0}", _path);

            foreach (var basePath in _searchPaths)
            {
                var path = Path.Combine(basePath, _path);
                path = PathUtility.NormalizePath(path);

                try
                {
                    // Avoid throwing error messages.
                    if (PathUtility.IsFileInPersistentDataPath(path) && !File.Exists(path))
                    {
                        Logger.LogInfo("AssetBundle cannot load at path: {0}, searching next ... ", path);
                        continue;
                    }

                    Profiler.BeginSample("BundleLoaderAsync:CreateBuiltinBundleLoadRequest - AssetBundle.LoadFromFileAsync");
                    var bundleLoadRequest = LoadAssetBundleAsync(path);
                    Profiler.EndSample();
                    if (bundleLoadRequest != null)
                        return bundleLoadRequest;

                    Logger.LogInfo("AssetBundle cannot load at path: {0}, searching next ... ", path);
                }
                catch
                {
                    Logger.LogInfo("AssetBundle cannot load at path: {0}, searching next ... ", path);
                }
            }

            throw new BundleLoadFailedException("Cannot load assetbundle: " + _path);
        }

        protected virtual AssetBundleCreateRequest LoadAssetBundleAsync(string path) {
            return AssetBundle.LoadFromFileAsync(path);
        }
    }
}