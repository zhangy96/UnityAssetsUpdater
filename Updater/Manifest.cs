using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SimpleJson;
using UnityEngine;

namespace Core.Updater
{
    public class Manifest
    {
        /// <summary>
        /// The type of difference
        /// </summary>
        public enum DiffType
        {
            ADDED,
            DELETED,
            MODIFIED
        }

        /// <summary>
        /// Download status
        /// </summary>
        public enum DownloadState
        {
            UNSTARTED,
            DOWNLOADING,
            SUCCESSED
        }

        /// <summary>
        /// Asset object info
        /// </summary>
        public class AssetInfo
        {
            public string md5;
            public string fileName;
            public DownloadState downloadState;
        }

        /// <summary>
        /// difference between 2 assets
        /// </summary>
        public class AssetDiff
        {
            public AssetInfo asset;
            public DiffType diffType;
        }

        private const string KEY_VERSION = "version";
        private const string KEY_PACKAGE_URL = "packageUrl";
        private const string KEY_MANIFEST_URL = "remoteManifestUrl";
        private const string KEY_VERSION_URL = "remoteVersionUrl";
        private const string KEY_ENGINE_VERSION = "engineVersion";
        private const string KEY_ASSETS = "assets";
        private const string KEY_MD5 = "md5";
        private const string KEY_DOWNLOAD_STATE = "downloadState";

        private JsonObject _json = null;

        /// <summary>
        /// The version of the manifest
        /// </summary>
        public string Version { get; private set; }

        /// <summary>
        /// The remote package url
        /// </summary>
        public string PackageUrl { get; private set; }

        /// <summary>
        /// The remote url of version file
        /// </summary>
        public string VersionUrl { get; private set; }

        /// <summary>
        /// The remote url of manifest file
        /// </summary>
        public string ManifestUrl { get; private set; }

        /// <summary>
        /// The engine version
        /// </summary>
        public string EngineVersion { get; private set; }

        /// <summary>
        /// Whether the manifest have been fully loaded
        /// </summary>
        public bool Loaded { get; private set; }

        /// <summary>
        /// Whether the version informations have been fully loaded
        /// </summary>
        public bool VersionLoaded { get; private set; }

        /// <summary>
        /// Full assets list
        /// </summary>
        private readonly Dictionary<string, AssetInfo> _assets = new Dictionary<string, AssetInfo>();

        public Dictionary<string, AssetInfo> GetAssets()
        {
            return _assets;
        }

        /// <summary>
        /// Parse the whole file, caller should check where the file exist
        /// </summary>
        public void Parse(string manifestUrl)
        {
            LoadJson(manifestUrl);

            if (_json != null)
            {
                LoadManifest();
            }
        }

        /// <summary>
        /// Parse the version part, caller should check where the file exist
        /// </summary>
        public void ParseVersion(string versionUrl)
        {
            LoadJson(versionUrl);

            if (_json != null)
            {
                LoadVersion();
            }
        }

        public bool VersionEquals(Manifest other)
        {
            return Version.Equals(other.Version);
        }

        public void SetAssetDownloadState(string fileName, DownloadState state)
        {
            AssetInfo asset;
            if (_assets.TryGetValue(fileName, out asset))
            {
                asset.downloadState = state;
            }

            if (_json.ContainsKey(KEY_ASSETS))
            {
                foreach (var kv in (JsonObject) _json[KEY_ASSETS])
                {
                    if (kv.Key.Equals(fileName))
                    {
                        var obj = (JsonObject) kv.Value;
                        if (obj.ContainsKey(KEY_DOWNLOAD_STATE))
                        {
                            obj[KEY_DOWNLOAD_STATE] = state;
                        }
                        else
                        {
                            obj.Add(KEY_DOWNLOAD_STATE, state);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Generate resuming download assets list
        /// </summary>
        public void GenResumeDownloadUnits(string storageRoot, ref Dictionary<string, DownloadUnit> units)
        {
            foreach (var assetKV in _assets)
            {
                var asset = assetKV.Value;
                if (asset.downloadState != DownloadState.SUCCESSED)
                {
                    var unit = new DownloadUnit
                    {
                        customId = asset.fileName,
                        srcUrl = PackageUrl + asset.fileName,
                        storagePath = storageRoot + asset.fileName
                    };
                    if (units.ContainsKey(unit.customId))
                    {
                        units[unit.customId] = unit;
                    }
                    else
                    {
                        units.Add(unit.customId, unit);
                    }
                }
            }
        }

        /// <summary>
        /// Generate difference between this Manifest and another
        /// </summary>
        public Dictionary<string, AssetDiff> GenDiff(Manifest other)
        {
            var diffDic = new Dictionary<string, AssetDiff>();

            var otherAssets = other.GetAssets();
            foreach (var assetKV in _assets)
            {
                var key = assetKV.Key;
                var valueA = assetKV.Value;

                // Deleted
                if (!otherAssets.ContainsKey(key))
                {
                    var diff = new AssetDiff
                    {
                        asset = valueA,
                        diffType = DiffType.DELETED
                    };
                    diffDic.Add(key, diff);
                    continue;
                }

                // Modified
                var valueB = otherAssets[key];
                if (valueA.md5 != valueB.md5)
                {
                    var diff = new AssetDiff
                    {
                        asset = valueB,
                        diffType = DiffType.MODIFIED
                    };
                    diffDic.Add(key, diff);
                }
            }

            foreach (var otherKV in otherAssets)
            {
                var key = otherKV.Key;
                var valueB = otherKV.Value;

                // Added
                if (!_assets.ContainsKey(key))
                {
                    var diff = new AssetDiff
                    {
                        asset = valueB,
                        diffType = DiffType.ADDED
                    };
                    diffDic.Add(key, diff);
                }
            }

            return diffDic;
        }

        public void SaveToFile(string path)
        {
            File.WriteAllText(path, _json.ToString(), Encoding.UTF8);
        }

        #region private methods

        private void LoadJson(string url)
        {
            Clear();

            // from android apk
            if (url.Contains("://") && Application.platform == RuntimePlatform.Android)
            {
                WWW www = new WWW(url);
                while (!www.isDone && string.IsNullOrEmpty(www.error))
                {
                }
                if (!string.IsNullOrEmpty(www.error))
                {
                    Debug.Log("Manifest.LoadJson - load failed, url = " + url);
                    return;
                }
                _json = SimpleJson.SimpleJson.DeserializeObject<JsonObject>(www.text);
            }
            else
            {
                try
                {
                    var text = File.ReadAllText(url);
                    _json = SimpleJson.SimpleJson.DeserializeObject<JsonObject>(text);
                }
                catch (Exception e)
                {
                    Debug.Log(e.Message);
                }
            }
        }

        /// <summary>
        /// Load the version part
        /// </summary>
        private void LoadVersion()
        {
            if (_json.ContainsKey(KEY_MANIFEST_URL))
            {
                ManifestUrl = (string) _json[KEY_MANIFEST_URL];
            }

            if (_json.ContainsKey(KEY_VERSION_URL))
            {
                VersionUrl = (string) _json[KEY_VERSION_URL];
            }

            if (_json.ContainsKey(KEY_VERSION))
            {
                Version = (string) _json[KEY_VERSION];
            }

            if (_json.ContainsKey(KEY_ENGINE_VERSION))
            {
                EngineVersion = (string) _json[KEY_ENGINE_VERSION];
            }

            VersionLoaded = true;
        }

        /// <summary>
        /// Load all
        /// </summary>
        private void LoadManifest()
        {
            LoadVersion();

            if (_json.ContainsKey(KEY_PACKAGE_URL))
            {
                PackageUrl = (string) _json[KEY_PACKAGE_URL];
                if ((PackageUrl.Length > 0) && !PackageUrl.EndsWith("/"))
                {
                    PackageUrl += "/";
                }
            }

            if (_json.ContainsKey(KEY_ASSETS))
            {
                foreach (var kv in (JsonObject) _json[KEY_ASSETS])
                {
                    var asset = ParseAsset(kv.Key, (JsonObject) kv.Value);
                    _assets.Add(kv.Key, asset);
                }
            }

            Loaded = true;
        }

        /// <summary>
        /// Parse asset info
        /// </summary>
        private AssetInfo ParseAsset(string fileName, JsonObject json)
        {
            var asset = new AssetInfo
            {
                fileName = fileName,
                downloadState = DownloadState.UNSTARTED,
                md5 = ""
            };

            object md5str;
            if (json.TryGetValue(KEY_MD5, out md5str))
            {
                asset.md5 = (string) md5str;
            }

            object state;
            if (json.TryGetValue(KEY_DOWNLOAD_STATE, out state))
            {
                asset.downloadState = (DownloadState) (long) state;
            }

            return asset;
        }

        private void Clear()
        {
            _assets.Clear();
            _json = null;

            Loaded = false;
            VersionLoaded = false;

            VersionUrl = "";
            ManifestUrl = "";
            Version = "";
            EngineVersion = "";
            PackageUrl = "";
        }

        #endregion
    }
}