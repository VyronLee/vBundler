//------------------------------------------------------------
//        File:  BundleMode.cs
//       Brief:  BundleMode
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-02-15 20:12
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using vBundler.Asset.Bundle;
using vBundler.Exception;
using vBundler.Interface;
using vBundler.Loader;
using vBundler.LoadRequests;
using vBundler.Messenger;
using vBundler.Utils;
using Logger = vBundler.Log.Logger;

namespace vBundler.Mode
{
    public class BundleMode : ModeBase
    {
        private static readonly Dictionary<string, BundleLoaderBase> LoaderCache
            = new Dictionary<string, BundleLoaderBase>();

        public BundleMode(BundlerManifest manifest, List<string> searchPaths) : base(manifest, searchPaths)
        {
        }

        public override ILoadRequest Load(string path)
        {
            var loader = CreateLoaderByAssetPath<BundleLoaderSync>(path);
            return new LoadRequestSync(this, path, loader);
        }

        public override ILoadRequestAsync LoadAsync(string path)
        {
            var loader = CreateLoaderByAssetPath<BundleLoaderAsync>(path);
            return new LoadRequestAsync(this, path, loader);
        }

        private BundleLoaderBase CreateLoaderByAssetPath<TLoader>(string assetPath)
            where TLoader : BundleLoaderBase, new()
        {
            assetPath = PathUtility.NormalizePath(assetPath);

            if (!_manifest.assets.ContainsKey(assetPath))
                throw new BundleNoneConfigurationException("Asset path not specified: " + assetPath);

            var assetData = _manifest.assets[assetPath];
            return CreateLoader<TLoader>(assetData.bundle);
        }

        private BundleLoaderBase CreateLoader<TLoader>(string bundlePath) where TLoader : BundleLoaderBase, new()
        {
            BundleLoaderBase bundleLoader;
            if (!LoaderCache.TryGetValue(bundlePath, out bundleLoader))
            {
                var bundleData = _manifest.bundles[bundlePath];
                var dependencies = new List<BundleLoaderBase>();
                bundleData.dependencies.ForEach(v => dependencies.Add(CreateLoader<TLoader>(v)));

                bundleLoader = new TLoader();
                bundleLoader.Initialize(bundlePath, _searchPaths);
                bundleLoader.Dependencies = dependencies;

                LoaderCache.Add(bundlePath, bundleLoader);
            }

            bundleLoader.Retain();
            return bundleLoader;
        }

        public override void Collect()
        {
            var unused = new LinkedList<string>();
            foreach (var kv in LoaderCache)
            {
                var loader = kv.Value;
                if (loader.GetReferences() <= 0)
                    unused.AddLast(kv.Key);
            }

            foreach (var name in unused)
            {
                var loader = LoaderCache[name];
                if (loader.IsLoading)
                    continue;

                loader.Unload();
                LoaderCache.Remove(name);
            }
        }

        public override void DeepCollect()
        {
            var messengers = DestroyedMessenger.Messengers;
            var deadMessengers = new LinkedList<DestroyedMessenger>();

            // "OnDestroy will only be called on game objects that have previously been active."
            // In this case we can only use "NullReference" to check whether game object is alive or not
            foreach (var messenger in messengers)
            {
                if (messenger == null)
                    deadMessengers.AddLast(messenger);
            }

            foreach (var messenger in deadMessengers)
                messenger.ReleaseRef();
            deadMessengers.Clear();

            Collect();
        }

        public override IAsset GetAsset(LoadRequest request, Type type)
        {
            return new BundleAssetSync(request.AssetPath, type, request.Loader);
        }

        public override IAssetAsync GetAssetAsync(LoadRequest request, Type type)
        {
            return new BundleAssetAsync(request.AssetPath, type, request.Loader);
        }
    }
}