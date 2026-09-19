using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace DedicatedServer.UnityFramework.Editor
{
    public class AssetBundleBuilder
    {
        [MenuItem("Asset Bundles/Build Asset Bundles")]
        private static void BuildAssetBundles()
        {
            BuildPipeline.BuildAssetBundles(Application.streamingAssetsPath, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        }
    }
}
