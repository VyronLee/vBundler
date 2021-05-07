﻿//------------------------------------------------------------
//        File:  BundlerGenerator.cs
//       Brief:  BundlerGenerator
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-07-09 10:40
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using vFrame.Bundler.Exception;
using vFrame.Bundler.Utils;
using Debug = UnityEngine.Debug;

namespace vFrame.Bundler.Editor
{
    public static class BundlerGenerator
    {
        private static string DependenciesCacheFilePath =>
            PathUtility.RelativeProjectPathToAbsolutePath("Library/AssetDependenciesCache.json");

        private static bool IsUnmanagedResources(string name)
        {
            if (string.IsNullOrEmpty(name))
                return true;

            var unmanaged = false;
            unmanaged |= Path.GetExtension(name) == ".meta";
            unmanaged |= Path.GetExtension(name) == ".cs";
            //unmanaged |= Path.GetExtension(name) == ".asset";
            unmanaged |= Path.GetExtension(name) == ".dll";
            unmanaged |= name.EndsWith("unity_builtin_extra");
            unmanaged |= name.EndsWith("unity default resources");
            return unmanaged;
        }

        private static bool IsShader(string name) {
            return Path.GetExtension(name) == ".shader";
        }

        private static bool IsScriptableObject(string name) {
            return Path.GetExtension(name) == ".asset";
        }

        private static bool IsScript(string name) {
            return Path.GetExtension(name) == ".cs";
        }

        private static bool IsAssembly(string name) {
            return Path.GetExtension(name) == ".dll";
        }

        private static bool IsBuiltinResource(string name) {
            return name.EndsWith("unity_builtin_extra")
                || name.EndsWith("unity default resources");
        }

        private static bool IsExclude(string path, string pattern)
        {
            path = PathUtility.NormalizePath(path);
            return !string.IsNullOrEmpty(pattern) && Regex.IsMatch(path, pattern);
        }

        public static void GenerateManifestToFile(BundlerBuildRule buildRule, string outputPath)
        {
            var manifest = GenerateManifest(buildRule);
            SaveManifest(ref manifest, outputPath);
        }

        public static BundlerManifest GenerateManifest(BundlerBuildRule buildRule) {
            var cache = LoadAssetDependenciesCache();
            var reservedSharedBundle = new ReservedSharedBundleInfo();
            var depsInfo = ParseBundleDependenciesFromRules(buildRule, ref reservedSharedBundle, ref cache);
            var bundlesInfo = GenerateBundlesInfo(ref depsInfo, ref reservedSharedBundle, ref cache);
            ResolveBundleDependencies(ref bundlesInfo, ref cache);
            //FilterRedundantDependencies(ref bundlesInfo);
            var manifest = GenerateBundlerManifest(ref bundlesInfo);
            Resources.UnloadUnusedAssets();
            SaveAssetDependenciesCache(cache);
            return manifest;
        }

        public static void StripUnmanagedFiles(BundlerBuildRule buildRule, BundlerManifest manifest) {
            var managedFiles = GetManagedFilesByRules(buildRule);
            var toRemoved = new List<string>();
            foreach (var kv in manifest.assets) {
                if (!managedFiles.Contains(kv.Key)) {
                    toRemoved.Add(kv.Key);
                }
            }
            toRemoved.ForEach(v => manifest.assets.Remove(v));
            Debug.Log(string.Format("{0} unmanaged files removed from manifest.", toRemoved.Count));
        }

        public static void GenerateAssetBundles(BundlerManifest manifest, BuildTarget platform, string outputPath) {
            var builds = GenerateAssetBundleBuilds(manifest);
            Debug.Log(string.Format("Generate asset bundles to path: {0}, build count: {1}", outputPath, builds.Length));
            BuildPipeline.BuildAssetBundles(outputPath, builds, BundlerBuildSettings.kAssetBundleBuildOptions, platform);
        }

        /// <summary>
        ///     Re-generate target asset bundle. Dependencies WON'T be re-generated.
        /// </summary>
        /// <param name="manifest"></param>
        /// <param name="path"></param>
        /// <param name="platform"></param>
        /// <param name="outputPath"></param>
        public static void RegenerateAssetBundle(BundlerManifest manifest, string path, BuildTarget platform,
            string outputPath)
        {
            var bundleName = PathUtility.NormalizeAssetBundlePath(path);

            var assets = manifest.assets.Where(v => v.Value.bundle == bundleName);

            var build = new AssetBundleBuild
            {
                assetNames = assets.Select(v => v.Key).ToArray(),
                assetBundleName = bundleName
            };
            BuildPipeline.BuildAssetBundles(outputPath, new[] {build}, BundlerBuildSettings.kAssetBundleBuildOptions, platform);
        }

        /// <summary>
        /// Validate bundle dependencies
        /// </summary>
        /// <param name="manifest"></param>
        /// <param name="outputPath"></param>
        /// <exception cref="BundleException"></exception>
        public static void ValidateBundleDependencies(BundlerManifest manifest, string outputPath) {
            var abManifestPath = string.Format("{0}/{1}", outputPath, new DirectoryInfo(outputPath).Name);
            var abManifestRelativePath = PathUtility.AbsolutePathToRelativeProjectPath(abManifestPath);
            var ab = AssetBundle.LoadFromFile(abManifestRelativePath);
            if (!ab) {
                throw new BundleException("Load asset bundle failed: " + abManifestPath);
            }

            var abManifest = ab.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            if (!abManifest) {
                ab.Unload(true);
                throw new BundleException("Load asset bundle manifest failed: " + abManifestPath);
            }

            void ValidateBundle(string assetBundle, List<string> validated) {
                if (validated.Contains(assetBundle)) {
                    throw new BundleException("Circular dependency detected: " + assetBundle);
                }
                validated.Add(assetBundle);

                if (!manifest.bundles.ContainsKey(assetBundle)) {
                    throw new BundleException("Bundle does not contain in manifest: " + assetBundle);
                }

                var dependencies = abManifest.GetDirectDependencies(assetBundle);
                foreach (var dependency in dependencies) {
                    if (!manifest.bundles[assetBundle].dependencies.Contains(dependency)) {
                        throw new BundleException(string.Format(
                            "Bundle dependencies lost, bundle name: {0}, dependency: {1}", assetBundle, dependency));
                    }
                    //Debug.Log("Validating dependency: " + dependency);
                    ValidateBundle(dependency, validated);
                }
                validated.Remove(assetBundle);
            }

            var index = 0f;
            var abs = abManifest.GetAllAssetBundles();
            try {
                foreach (var assetBundle in abs) {
                    EditorUtility.DisplayProgressBar("Validating asset bundle", assetBundle, index++/abs.Length);
                    //Debug.Log("Validating asset bundle: " + assetBundle);
                    ValidateBundle(assetBundle, new List<string>());
                }
            }
            finally {
                EditorUtility.ClearProgressBar();
                ab.Unload(true);
                AssetBundle.UnloadAllAssetBundles(true);
            }
        }

        private static DependencyCache LoadAssetDependenciesCache() {
            try {
                var jsonData = File.ReadAllText(DependenciesCacheFilePath);
                return JsonUtility.FromJson<DependencyCache>(jsonData);
            }
            catch {
                return new DependencyCache();
            }
        }

        private static void SaveAssetDependenciesCache(DependencyCache cache) {
            var jsonData = JsonUtility.ToJson(cache);
            File.WriteAllText(DependenciesCacheFilePath, jsonData);
        }

        private static DependenciesInfo ParseBundleDependenciesFromRules(BundlerBuildRule buildRule,
            ref ReservedSharedBundleInfo reserved, ref DependencyCache cache)
        {
            buildRule.rules.Sort((a, b) => -a.shared.CompareTo(b.shared));

            var depsInfo = new DependenciesInfo();
            foreach (var rule in buildRule.rules)
                switch (rule.packType)
                {
                    case PackType.PackByFile:
                        ParseBundleDependenciesByRuleOfPackByFile(rule, ref depsInfo, ref reserved, ref cache);
                        break;
                    case PackType.PackByDirectory:
                        ParseBundleDependenciesByRuleOfPackByDirectory(rule, ref depsInfo, ref reserved, ref cache);
                        break;
                    case PackType.PackBySubDirectory:
                        ParseBundleDependenciesByRuleOfPackBySubDirectory(rule, ref depsInfo, ref reserved, ref cache);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

            return depsInfo;
        }

        private static void ParseBundleDependenciesByRuleOfPackByFile(BundleRule rule, ref DependenciesInfo depsInfo,
            ref ReservedSharedBundleInfo reserved, ref DependencyCache cache)
        {
            var excludePattern = rule.excludePattern;
            var searchPath = PathUtility.RelativeProjectPathToAbsolutePath(rule.path);
            var files = Directory.GetFiles(searchPath, rule.searchPattern, SearchOption.AllDirectories)
                .Where(v => !IsUnmanagedResources(v))
                .Where(v => !IsExclude(v, excludePattern))
                .ToArray();

            try {
                var index = 0;
                foreach (var file in files)
                {
                    EditorUtility.DisplayProgressBar("Parsing Rule of Pack By File Name", file,
                        (float) index++ / files.Length);

                    var bundleName = PathUtility.NormalizeAssetBundlePath(file);
                    bundleName = string.Format(BundlerBuildSettings.kBundleFormatter, bundleName);
                    bundleName = BundlerBuildSettings.kHashAssetBundlePath
                        ? PathUtility.HashPath(bundleName)
                        : bundleName;

                    var relativePath = PathUtility.AbsolutePathToRelativeProjectPath(file);
                    if (rule.shared)
                        TryAddToForceSharedBundle(relativePath, bundleName, ref reserved);

                    AddDependenciesInfo(bundleName, relativePath, ref depsInfo, ref reserved, ref cache);
                }
            }
            finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ParseBundleDependenciesByRuleOfPackByDirectory(BundleRule rule, ref DependenciesInfo depsInfo,
            ref ReservedSharedBundleInfo reserved, ref DependencyCache cache) {
            var bundleName = PathUtility.NormalizeAssetBundlePath(rule.path);
            bundleName = string.Format(BundlerBuildSettings.kBundleFormatter, bundleName);
            bundleName = BundlerBuildSettings.kHashAssetBundlePath
                ? PathUtility.HashPath(bundleName)
                : bundleName;

            var searchPath = PathUtility.RelativeProjectPathToAbsolutePath(rule.path);

            var excludePattern = rule.excludePattern;
            var files = Directory.GetFiles(searchPath, rule.searchPattern, SearchOption.AllDirectories)
                .Where(v => !IsUnmanagedResources(v))
                .Where(v => !IsExclude(v, excludePattern))
                .ToArray();

            try {
                var index = 0;
                foreach (var file in files)
                {
                    EditorUtility.DisplayProgressBar("Parsing Rule of Pack By Directory Name", file,
                        (float) index++ / files.Length);

                    var relativePath = PathUtility.AbsolutePathToRelativeProjectPath(file);
                    if (rule.shared)
                        TryAddToForceSharedBundle(relativePath, bundleName, ref reserved);

                    AddDependenciesInfo(bundleName, relativePath, ref depsInfo, ref reserved, ref cache);
                }
            }
            finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ParseBundleDependenciesByRuleOfPackBySubDirectory(BundleRule rule, ref DependenciesInfo depsInfo,
            ref ReservedSharedBundleInfo reserved, ref DependencyCache cache) {
            var excludePattern = rule.excludePattern;

            var searchPath = PathUtility.RelativeProjectPathToAbsolutePath(rule.path);
            var subDirectories = Directory.GetDirectories(searchPath, "*.*", (SearchOption) rule.depth)
                .Where(v => !IsExclude(v, excludePattern))
                .ToArray();

            foreach (var subDirectory in subDirectories)
            {
                var files = Directory
                    .GetFiles(subDirectory, rule.searchPattern, SearchOption.AllDirectories)
                    .Where(v => !IsUnmanagedResources(v))
                    .Where(v => !IsExclude(v, excludePattern))
                    .ToArray();

                var bundleName = PathUtility.NormalizeAssetBundlePath(subDirectory);
                bundleName = string.Format(BundlerBuildSettings.kBundleFormatter, bundleName);
                bundleName = BundlerBuildSettings.kHashAssetBundlePath
                    ? PathUtility.HashPath(bundleName)
                    : bundleName;

                try {
                    var index = 0;
                    foreach (var file in files)
                    {
                        EditorUtility.DisplayProgressBar("Parsing Rule of Pack By Sub Directory Name", file,
                            (float) index++ / files.Length);

                        var relativePath = PathUtility.AbsolutePathToRelativeProjectPath(file);
                        if (rule.shared)
                            TryAddToForceSharedBundle(relativePath, bundleName, ref reserved);

                        AddDependenciesInfo(bundleName, relativePath, ref depsInfo, ref reserved, ref cache);
                    }
                }
                finally {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        private static string[] GetManagedFilesByRules(BundlerBuildRule rules) {
            var files = new List<string>();
            rules.rules.ForEach(v => files.AddRange(GetFileListByRule(v)));
            return files.ToArray();
        }

        private static string[] GetFileListByRule(BundleRule rule) {
            var excludePattern = rule.excludePattern;
            var searchPath = PathUtility.RelativeProjectPathToAbsolutePath(rule.path);
            switch (rule.packType) {
                case PackType.PackByFile:
                case PackType.PackByDirectory: {
                    return Directory.GetFiles(searchPath, rule.searchPattern, SearchOption.AllDirectories)
                        .Where(v => !IsUnmanagedResources(v))
                        .Where(v => !IsExclude(v, excludePattern))
                        .Select(v => PathUtility.AbsolutePathToRelativeProjectPath(v))
                        .ToArray();
                }
                case PackType.PackBySubDirectory: {
                    var subDirectories = Directory.GetDirectories(searchPath, "*.*", (SearchOption) rule.depth)
                        .Where(v => !IsExclude(v, excludePattern))
                        .ToArray();

                    var files = new List<string>();
                    foreach (var subDirectory in subDirectories) {
                        var filesInDir = Directory
                            .GetFiles(subDirectory, rule.searchPattern, SearchOption.AllDirectories)
                            .Where(v => !IsUnmanagedResources(v))
                            .Where(v => !IsExclude(v, excludePattern))
                            .Select(v => PathUtility.AbsolutePathToRelativeProjectPath(v));
                        files.AddRange(filesInDir);
                    }
                    return files.ToArray();
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void TryAddToForceSharedBundle(string assetName, string bundleName, ref ReservedSharedBundleInfo reserved) {
            if (reserved.ContainsKey(assetName)) {
                throw new BundleRuleConflictException(
                    string.Format("Asset({0} has already contain in bundle: {1}, but trying add to new bundle: {2})",
                        assetName, reserved[assetName], bundleName));
            }
            reserved.Add(assetName, bundleName);
        }

        private static void AddDependenciesInfo(string bundleName, string relativePath, ref DependenciesInfo info,
            ref ReservedSharedBundleInfo reserved, ref DependencyCache cache)
        {
            var dependencies = CollectDependencies(relativePath, ref cache);
            var deps = dependencies.ToList();
            deps.Add(relativePath);

            foreach (var dependency in deps)
            {
                if (!info.ContainsKey(dependency))
                    info[dependency] = new DependencyInfo();

                if (IsShader(dependency)) {
                    if (BundlerBuildSettings.kSeparateShaderBundle) {
                        info[dependency].referenceInBundles.Add(BundlerBuildSettings.kSeparatedShaderBundleName);
                        reserved[dependency] = BundlerBuildSettings.kSeparatedShaderBundleName;
                    }
                }
                info[dependency].referenceInBundles.Add(bundleName);
            }
        }

        private static BundlesInfo GenerateBundlesInfo(ref DependenciesInfo depsInfo,
            ref ReservedSharedBundleInfo reserved, ref DependencyCache cache)
        {
            var sharedBundles = ParseSharedBundleInfo(ref depsInfo, ref reserved, ref cache);
            var noneSharedBundles = ParseNoneSharedBundleInfo(ref depsInfo, ref sharedBundles);

            var bundles = new BundlesInfo();
            sharedBundles.Concat(noneSharedBundles).ToList().ForEach(kv => {
                if (kv.Value.assets.Count > 0) {
                    bundles.Add(kv.Key, kv.Value);
                }
            });
            return bundles;
        }

        private static IEnumerable<string> CollectDependencies(string asset, ref DependencyCache cache) {
            // Try get dependencies from cache
            if (cache.cacheData.TryGetValue(asset, out var cacheData)) {
                if (cacheData.validated) {
                    return cacheData.dependencies;
                }

                var hash = AssetDatabase.GetAssetDependencyHash(asset);
                if (hash == cacheData.assetHash) {
                    cacheData.validated = true;
                    return cacheData.dependencies;
                }
            }

            var dep1 = AssetDatabase.GetDependencies(asset, true); // Must recursive here.

            // On Unity 2018 or newer, LightingDataAsset depends on SceneAsset directly,
            // which will lead to circular dependency
            var lightingDataAsset = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(asset);
            if (lightingDataAsset) {
                dep1 = GetLightingDataValidDependencies(lightingDataAsset);
                Resources.UnloadAsset(lightingDataAsset);
            }

            var dep2 = dep1.Where(v => v != asset).ToArray();
            var dep3 = dep2.Where(v => !IsUnmanagedResources(v)).ToArray();
            var dep4 = dep3.Distinct().ToArray();

            cache.cacheData[asset] = new DependencyCacheData {
                assetHash = AssetDatabase.GetAssetDependencyHash(asset),
                dependencies = dep4.ToList(),
                validated = true,
            };
            return dep4;
        }

        private static string[] GetLightingDataValidDependencies(LightingDataAsset lightingDataAsset) {
            var path = AssetDatabase.GetAssetPath(lightingDataAsset);
            var dep1 = AssetDatabase.GetDependencies(path, false);
            var dep2 = dep1.Where(v => {
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(v);
                var isSceneAsset = null != asset;
                if (asset) {
                    Resources.UnloadAsset(asset);
                }
                return !isSceneAsset;
            }).ToArray();
            return dep2;
        }

        private static void ResolveBundleDependencies(ref BundlesInfo bundlesInfo, ref DependencyCache cache) {
            var info = bundlesInfo;
            var cacheData = cache;
            foreach (var kv in bundlesInfo) {
                //Debug.Log(string.Format("Resolving bundle dependency: {0}", kv.Key));

                var bundleInfo = kv.Value;
                bundleInfo.dependencies.Clear();

                void ResolveAssetDependency(string asset) {
                    var dependencies = CollectDependencies(asset, ref cacheData);
                    foreach (var dependencyAsset in dependencies) {
                        // Which bundle contains this asset?
                        var depBundle = info.FirstOrDefault(
                            v => v.Value.assets.Contains(dependencyAsset)).Key;

                        if (string.IsNullOrEmpty(depBundle)) {
                            continue;
                        }

                        if (depBundle == kv.Key) {
                            continue;
                        }

                        if (bundleInfo.dependencies.Contains(depBundle)) {
                            continue;
                        }
                        bundleInfo.dependencies.Add(depBundle);
                    }
                }

                foreach (var asset in bundleInfo.assets) {
                    ResolveAssetDependency(asset);
                }
            }
        }

        private static void FilterRedundantDependencies(ref BundlesInfo bundlesInfo) {
            // Filter out redundant dependencies.
            var bsInfo = bundlesInfo;
            foreach (var kv in bundlesInfo) {
                var bundleInfo = kv.Value;

                bool IsDependencyInBundle(string bundleName, BundleInfo bInfo) {
                    foreach (var dependency in bInfo.dependencies) {
                        if (dependency == bundleName) {
                            return true;
                        }

                        if (!bsInfo.ContainsKey(dependency))
                            continue;

                        if (IsDependencyInBundle(bundleName, bsInfo[dependency])) {
                            return true;
                        }
                    }
                    return false;
                }

                bool IsReferenceInDependencies(string toCheck, BundleInfo bInfo) {
                    //Debug.Log(string.Format("Checking dependency: {0}", toCheck));
                    foreach (var dependency in bInfo.dependencies) {
                        if (dependency == toCheck) {
                            continue;
                        }

                        if (!bsInfo.ContainsKey(dependency))
                            continue;

                        if (IsDependencyInBundle(toCheck, bsInfo[dependency])) {
                            return true;
                        }
                    }
                    return false;
                }

                //Debug.Log(string.Format("Checking redundant dependency: {0}", kv.Key));

                var toRemove = new List<string>();
                foreach (var dependency in bundleInfo.dependencies) {
                    if (IsReferenceInDependencies(dependency, bundleInfo)) {
                        toRemove.Add(dependency);
                    }
                }
                toRemove.ForEach(v => {
                    //Debug.Log(string.Format("Dependency redundant, removed: {0}, in bundle: {1}", v, kv.Key));
                    bundleInfo.dependencies.Remove(v);
                });
            }
        }

        private static BundlesInfo ParseSharedBundleInfo(ref DependenciesInfo depsInfo,
            ref ReservedSharedBundleInfo reserved, ref DependencyCache cache)
        {
            var sharedDict = new Dictionary<string, string>();

            var index = 0;
            // Determine shared bundles
            foreach (var kv in depsInfo)
            {
                var asset = kv.Key;

                // Ignore this asset when forced build in shared bundle.
                if (reserved.ContainsKey(asset))
                    continue;

                var depSet = kv.Value;

                // The count of dependencies no greater than 1 means that there are no other bundles
                // sharing this asset
                if (depSet.referenceInBundles.Count <= 1)
                    continue;

                // Otherwise, assets which depended by the same bundles will be separated to shared bundle.
                if (!sharedDict.ContainsKey(asset)) {
                    sharedDict[asset] = string.Format(BundlerBuildSettings.kSharedBundleFormatter,
                        (++index).ToString());

                    // Sub-assets dependencies.
                    var deps = CollectDependencies(asset, ref cache);
                    foreach (var dep in deps) {
                        if (reserved.ContainsKey(dep))
                            continue;
                        sharedDict[dep] = string.Format(BundlerBuildSettings.kSharedBundleFormatter,
                            (++index).ToString());
                    }
                }
            }

            // Collect shared bundles info
            var bundlesInfo = new BundlesInfo();
            foreach (var kv in sharedDict) {
                var name = sharedDict[kv.Key];
                if (bundlesInfo.ContainsKey(name))
                    throw new BundleException("Shared bundle duplicated: " + name);

                bundlesInfo[name] = new BundleInfo();
                bundlesInfo[name].assets.Add(kv.Key);
            }

            // Generate unique bundle name according to assets list.
            var sharedBundle = new BundlesInfo();
            foreach (var kv in bundlesInfo)
            {
                var assets = kv.Value.assets.ToList();
                assets.Sort((a, b) => string.Compare(a, b, StringComparison.Ordinal));

                var bundleName = string.Join("-", assets.ToArray());
                if (BundlerBuildSettings.kHashSharedBundle) {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(bundleName)))
                    {
                        var nameHash = CalculateMd5(stream);
                        bundleName = string.Format(BundlerBuildSettings.kSharedBundleFormatter, nameHash);
                    }
                }
                else {
                    bundleName = string.Format(BundlerBuildSettings.kSharedBundleFormatter, bundleName.ToLower());
                }

                var bundleInfo = new BundleInfo();
                foreach (var asset in kv.Value.assets)
                    bundleInfo.assets.Add(asset);
                sharedBundle.Add(bundleName, bundleInfo);
            }

            // Add reserved shared bundles info
            foreach (var kv in reserved)
            {
                var assetName = kv.Key;
                var bundle = kv.Value;
                if (!sharedBundle.ContainsKey(bundle))
                    sharedBundle.Add(bundle, new BundleInfo());
                sharedBundle[bundle].assets.Add(assetName);
            }

            return sharedBundle;
        }

        private static BundlesInfo ParseNoneSharedBundleInfo(ref DependenciesInfo depsInfo, ref BundlesInfo sharedBundles)
        {
            // Parse none shared bundle info
            var noneSharedBundles = new BundlesInfo();
            foreach (var kv in depsInfo)
            {
                var assetName = kv.Key;
                var bundles = kv.Value.referenceInBundles;

                var sharedBundle = sharedBundles.FirstOrDefault(skv => skv.Value.assets.Contains(assetName)).Key ?? "";

                foreach (var bundle in bundles)
                {
                    if (!string.IsNullOrEmpty(sharedBundles.FirstOrDefault(skv => skv.Key == bundle).Key))
                        continue; // Equals to shared bundle, pass.

                    if (!noneSharedBundles.ContainsKey(bundle))
                        noneSharedBundles.Add(bundle, new BundleInfo());

                    if (string.IsNullOrEmpty(sharedBundle))
                        noneSharedBundles[bundle].assets.Add(assetName);
                }
            }

            // Move *.unity files to independent bundles, because 'Cannot mark assets and scenes in one AssetBundle'
            var sceneBundles = new BundlesInfo();
            foreach (var kv in noneSharedBundles)
            {
                var noneSharedAssets = kv.Value.assets;
                var removed = new List<string>();
                foreach (var asset in noneSharedAssets)
                {
                    if (!asset.EndsWith(".unity"))
                        continue;

                    var sceneBundleName = string.Format(BundlerBuildSettings.kSceneBundleFormatter,
                        PathUtility.RelativeProjectPathToAbsolutePath(asset));
                    sceneBundleName = PathUtility.NormalizeAssetBundlePath(sceneBundleName);

                    if (sceneBundles.ContainsKey(sceneBundleName))
                        throw new BundleException("Scene asset bundle duplicated: " + sceneBundleName);

                    var bundle = new BundleInfo();
                    bundle.assets.Add(asset);

                    sceneBundles.Add(sceneBundleName, bundle);

                    removed.Add(asset);
                }

                removed.ForEach(v => noneSharedAssets.Remove(v));
            }

            // Merge to none shared bundles
            foreach (var kv in sceneBundles)
                noneSharedBundles.Add(kv.Key, kv.Value);

            return noneSharedBundles;
        }

        private static BundlerManifest GenerateBundlerManifest(ref BundlesInfo bundlesInfo)
        {
            var manifest = new BundlerManifest();

            foreach (var kv in bundlesInfo)
            {
                var bundleName = kv.Key;
                var assets = kv.Value.assets;
                var dependencies = kv.Value.dependencies;

                // No assets in this bundle, skips.
                if (assets.Count <= 0)
                    continue;

                // Generate assets data
                foreach (var asset in assets)
                {
                    if (manifest.assets.ContainsKey(asset))
                        throw new BundleException(string.Format(
                            "Asset duplicated: {0}, already reference in bundle: {1}, conflict with: {2}",
                            asset, manifest.assets[asset].bundle, bundleName));

                    var assetData = new AssetData
                    {
                        //name = asset, // Memory optimized
                        bundle = bundleName
                    };
                    manifest.assets.Add(asset, assetData);
                }

                // Generate bundles data
                if (manifest.bundles.ContainsKey(bundleName))
                    throw new BundleException(string.Format("Bundle duplicated: {0}", bundleName));

                //manifest.bundles.Add(bundleName, new BundleData {name = bundleName}); // Memory optimized
                manifest.bundles.Add(bundleName, new BundleData { assets = assets.ToList()});
                foreach (var depName in dependencies)
                    manifest.bundles[bundleName].dependencies.Add(depName);
            }

            return manifest;
        }

        private static void SaveManifest(ref BundlerManifest manifest, string outputPath)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var jsonData = JsonUtility.ToJson(manifest);
            File.WriteAllText(outputPath, jsonData);
        }

        private static AssetBundleBuild[] GenerateAssetBundleBuilds(BundlerManifest manifest)
        {
            var bundles = new Dictionary<string, HashSet<string>>();

            foreach (var kv in manifest.assets)
            {
                if (!bundles.ContainsKey(kv.Value.bundle))
                    bundles.Add(kv.Value.bundle, new HashSet<string>());

                // Editor only objects cannot be included in AssetBundles
                if (kv.Key.EndsWith("LightingData.asset")) {
                    continue;
                }
                bundles[kv.Value.bundle].Add(kv.Key);
            }

            var builds = new List<AssetBundleBuild>();
            foreach (var kv in bundles)
            {
                if (kv.Value.Count <= 0)
                    continue;

                var build = new AssetBundleBuild
                {
                    assetBundleName = kv.Key,
                    assetNames = kv.Value.ToArray()
                };
                builds.Add(build);
            }

            return builds.ToArray();
        }

        private static string CalculateMd5(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // HashSet<T> EqualityComparer does not implement in mono 2.0
        private class StringHashSetComparer : IEqualityComparer<HashSet<string>>
        {
            public bool Equals(HashSet<string> x, HashSet<string> y)
            {
                return !x.Any(v => !y.Contains(v)) && !y.Any(v => !x.Contains(v));
            }

            public int GetHashCode(HashSet<string> obj)
            {
                var hashcode = 0x7FFFFFFF;
                foreach (var s in obj) hashcode |= s.GetHashCode();

                return hashcode;
            }
        }

        #region Data Structure Alias

        // Asset name -> bundles name contains this asset
        private class DependenciesInfo : Dictionary<string, DependencyInfo>
        {
        }

        private class DependencyInfo
        {
            public readonly HashSet<string> referenceInBundles = new HashSet<string>();
        }

        // Bundle name -> assets in this bundle + bundle dependencies
        private class BundlesInfo : Dictionary<string, BundleInfo>
        {
        }

        private class BundleInfo
        {
            public readonly HashSet<string> assets = new HashSet<string>();
            public readonly HashSet<string> dependencies = new HashSet<string>();
        }

        // Asset name -> bundle name
        private class ReservedSharedBundleInfo : Dictionary<string, string>
        {
        }

        [Serializable]
        private class DependencyCache : ISerializationCallbackReceiver
        {
            [NonSerialized]
            public Dictionary<string, DependencyCacheData> cacheData = new Dictionary<string, DependencyCacheData>();

            [SerializeField]
            private List<string> _assetIndexes = new List<string>();

            [SerializeField]
            private List<DependencyCacheData> _cacheData = new List<DependencyCacheData>();

            public void OnBeforeSerialize() {
                var stopWatch = Stopwatch.StartNew();

                _assetIndexes.Clear();
                _cacheData.Clear();

                var indexesMap = new Dictionary<string, int>(cacheData.Count);
                var index = 0;
                foreach (var kv in cacheData) {
                    _assetIndexes.Add(kv.Key);
                    _cacheData.Add(kv.Value);
                    indexesMap.Add(kv.Key, index++);
                }

                foreach (var data in cacheData.Select(kv => kv.Value)) {
                    data.assetHashStr = data.assetHash.ToString();
                    data.dependencyIndexes.Clear();
                    foreach (var dependency in data.dependencies) {
                        if (!indexesMap.TryGetValue(dependency, out var idx)) {
                            _assetIndexes.Add(dependency);
                            idx = _assetIndexes.Count - 1;
                            indexesMap.Add(dependency, idx);
                        }
                        data.dependencyIndexes.Add(idx);
                    }
                }
                stopWatch.Stop();

                Debug.LogFormat("Serialize DependencyCache finished, cost: {0}s", stopWatch.Elapsed.TotalSeconds);
            }

            public void OnAfterDeserialize() {
                var stopWatch = Stopwatch.StartNew();

                cacheData.Clear();

                for (var idx = 0; idx < _cacheData.Count; idx++) {
                    _cacheData[idx].dependencies = _cacheData[idx].dependencyIndexes.ConvertAll(v => _assetIndexes[v]);
                    _cacheData[idx].assetHash = Hash128.Parse(_cacheData[idx].assetHashStr);
                    cacheData.Add(_assetIndexes[idx], _cacheData[idx]);
                }

                stopWatch.Stop();

                Debug.LogFormat("Deserialize DependencyCache finished, cost: {0}s", stopWatch.Elapsed.TotalSeconds);
            }
        }

        [Serializable]
        private class DependencyCacheData
        {
            [NonSerialized]
            public Hash128 assetHash;

            [NonSerialized]
            public List<string> dependencies = new List<string>();

            [NonSerialized]
            public bool validated;

            [SerializeField]
            public List<int> dependencyIndexes = new List<int>();

            [SerializeField]
            public string assetHashStr;
        }

        #endregion
    }
}