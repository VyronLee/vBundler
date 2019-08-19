//------------------------------------------------------------
//        File:  BundlerDefaultBuildSetting.cs
//       Brief:  BundlerDefaultBuildSetting
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-07-09 10:39
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================
namespace vBundler.Editor
{
    public class BundlerDefaultBuildSettings
    {
        public static string kBundlePath = "Bundles";
        public static string kBuildRuleFilePath = "BundleRules.json";
        public static string kManifestFileName = "Manifest.json";

        public static string kBundleFormatter = "{0}.unity3d";
        public static string kSharedBundleFormatter = "shared/shared_{0}.unity3d";
        public static string kSceneBundleFormatter = "{0}.scene.unity3d";

        public static string kModePreferenceKey = "vBundlerModePreferenceKey";
        public static string kLogLevelPreferenceKey = "vBundlerLogLevelPreferenceKey";
    }
}