using System.Collections;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using System.Data;

namespace DedicatedServer.Demo.JumpingGame
{
    public static class ResourceManager
    {
        private static Dictionary<string, AssetBundle> _loadedAssetBundles = new Dictionary<string, AssetBundle>();

        public static T LoadResource<T>(string assetBundlePath, string resourceName) where T : UnityEngine.Object
        {
            AssetBundle assetbundle = GetAssetBundle(assetBundlePath);
            T result = assetbundle.LoadAsset<T>(resourceName);
            /*if (HelperMethods.IsNull(result))
            {
                DebugHelper.LogWarning("resource is null");
            }*/
            return result;
        }

        private static AssetBundle GetAssetBundle(string path)
        {
            AssetBundle result = null;
            if(!_loadedAssetBundles.TryGetValue(path, out result))
            {
                result = AssetBundle.LoadFromFile(Application.streamingAssetsPath + path);
                _loadedAssetBundles.Add(path, result);
            }
            return result;
        }
    }
}
