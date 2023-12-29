// ------------------------------------------------------------
//         File: BuildBundleManifestTask.cs
//        Brief: BuildBundleManifestTask.cs
//
//       Author: VyronLee, lwz_jz@hotmail.com
//
//      Created: 2023-12-25 22:42
//    Copyright: Copyright (c) 2023, VyronLee
// ============================================================

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using vFrame.Bundler.Utils;

namespace vFrame.Bundler.Editor.Task
{
    internal class BuildBundlerManifestTask : BuildTaskBase
    {
        public override void Run(BuildContext context) {
            var manifest = new BundlerManifest();
            GrantAssetInfos(context, manifest);
            GrantAssetBundleInfos(context, manifest);
            WriteToDisk(context, manifest);
            context.BundlerManifest = manifest;
        }

        private void GrantAssetInfos(BuildContext context, BundlerManifest manifest) {
            var index = 0f;
            var total = context.MainAssetInfos.Count;
            try {
                foreach (var kv in context.MainAssetInfos) {
                    var assetInfo = kv.Value;
                    EditorUtility.DisplayProgressBar("Building Bundler Manifest",
                        $"Granting asset info: {assetInfo.AssetPath}", ++index / total );
                    manifest.assets[assetInfo.AssetPath] = new AssetData {
                        bundle = assetInfo.BundlePath
                    };
                }
            }
            finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private void GrantAssetBundleInfos(BuildContext context, BundlerManifest manifest) {
            var index = 0f;
            var total = context.MainAssetInfos.Count;
            var abs = context.BundleInfos.Keys;

            try {
                foreach (var ab in abs) {
                    EditorUtility.DisplayProgressBar("Building Bundler Manifest",
                        $"Granting assetBundle: {ab}", ++index / total );

                    var assets = context.BundleInfos[ab].AssetPaths;
                    var dependencies = context.AssetBundleManifest.GetDirectDependencies(ab) ?? Array.Empty<string>();

                    manifest.bundles[ab] = new BundleData {
                        assets = assets.ToList(),
                        dependencies = dependencies.ToList()
                    };
                }
            }
            finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private void WriteToDisk(BuildContext context, BundlerManifest manifest) {
            var jsonData = JsonUtility.ToJson(manifest);
            var savePath = PathUtility.Combine(
                context.BuildSettings.BundlePath,
                context.BuildSettings.ManifestFileName);

            if (!Directory.Exists(context.BuildSettings.BundlePath)) {
                Directory.CreateDirectory(context.BuildSettings.BundlePath);
            }
            File.WriteAllText(savePath, jsonData);
        }

    }
}