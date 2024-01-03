// ------------------------------------------------------------
//         File: AssetBundleLoader.cs
//        Brief: AssetBundleLoader.cs
//
//       Author: VyronLee, lwz_jz@hotmail.com
//
//      Created: 2024-1-3 22:49
//    Copyright: Copyright (c) 2024, VyronLee
// ============================================================

using UnityEngine;

namespace vFrame.Bundler
{
    internal abstract class AssetBundleLoader : Loader
    {
        protected AssetBundleLoader(BundlerContexts bundlerContexts, LoaderContexts loaderContexts, string bundlePath)
            : base(bundlerContexts, loaderContexts) {

            BundlePath = bundlePath;
            Adapter = bundlerContexts.Options.AssetBundleCreateRequestAdapter ??
                       new InternalAssetBundleCreateRequestAdapter(bundlerContexts);
        }

        protected string BundlePath { get; }
        protected IAssetBundleCreateRequestAdapter Adapter { get; }

        public abstract AssetBundle AssetBundle { get; }
    }
}