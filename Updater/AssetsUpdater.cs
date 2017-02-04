using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Core.Updater
{
    public class AssetsUpdater
    {
        /// <summary>
        /// Update State
        /// </summary>
        private enum UpdateState
        {
            UNCHECKED,
            PREDOWNLOAD_VERSION,
            DOWNLOADING_VERSION,
            VERSION_LOADED,
            PREDOWNLOAD_MANIFEST,
            DOWNLOADING_MANIFEST,
            MANIFEST_LOADED,
            NEED_UPDATE,
            UPDATING,
            // UNZIPPING,
            UP_TO_DATE,
            FAIL_TO_UPDATE
        }

        private const string VERSION_FILENAME = "version.manifest";
        private const string TEMP_MANIFEST_FILENAME = "project.manifest.temp";
        private const string MANIFEST_FILENAME = "project.manifest";
        private const string VERSION_ID = "@version";
        private const string MANIFEST_ID = "@manifest";

        /// <summary>
        /// The local path of cached manifest file
        /// </summary>
        private readonly string _cacheManifestPath;

        /// <summary>
        /// The local path of cached version file
        /// </summary>
        private readonly string _cacheVersionPath;

        /// <summary>
        /// The path to store downloaded resources
        /// </summary>
        private readonly string _storagePath;

        /// <summary>
        /// The local path of cached temporary manifest file
        /// </summary>
        private readonly string _tempManifestPath;

        private readonly Downloader _downloader = new Downloader();

        /// <summary>
        /// All assets unit to download
        /// </summary>
        private Dictionary<string, DownloadUnit> _downloadUnits = new Dictionary<string, DownloadUnit>();

        /// <summary>
        /// All failed units
        /// </summary>
        private Dictionary<string, DownloadUnit> _failedUnits = new Dictionary<string, DownloadUnit>();

        /// <summary>
        /// Local manifest
        /// </summary>
        private Manifest _localManifest = new Manifest();

        /// <summary>
        /// Remote manifest
        /// </summary>
        private Manifest _remoteManifest = new Manifest();

        /// <summary>
        /// Local temporary manifest for download resuming
        /// </summary>
        private readonly Manifest _tempManifest = new Manifest();

        /// <summary>
        /// Download percent by file
        /// </summary>
        private int _percentByFile;

        /// <summary>
        /// Total number of assets to download
        /// </summary>
        private int _totalToDownload;

        /// <summary>
        /// Total number of assets still waiting to be downloaded
        /// </summary>
        private int _totalWaitToDownload;

        private UpdateState _updateState = UpdateState.UNCHECKED;

        public event Action<UpdateEvent> OnUpdateEvent;

        public AssetsUpdater(string manifestUrl, string storagePath)
        {
            if (storagePath.EndsWith("/"))
            {
                _storagePath = storagePath;
            }
            else
            {
                _storagePath = storagePath + "/";
            }

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }

            _cacheVersionPath = _storagePath + VERSION_FILENAME;
            _cacheManifestPath = _storagePath + MANIFEST_FILENAME;
            _tempManifestPath = _storagePath + TEMP_MANIFEST_FILENAME;

            _downloader.OnDownloadError += OnDownloadError;
            _downloader.OnDownloadSuccess += OnDownloadSuccess;

            InitManifest(manifestUrl);
        }

        /// <summary>
        /// Check for update
        /// </summary>
        public void CheckUpdate()
        {
            if (!_localManifest.Loaded)
            {
                Debug.LogError("AssetsUpdater : No local manifest file found.");
                DispatchUpdateEvent(UpdateEvent.EventCode.ERROR_NO_LOCAL_MANIFEST);
                return;
            }

            switch (_updateState)
            {
                case UpdateState.UNCHECKED:
                case UpdateState.PREDOWNLOAD_VERSION:
                    DownloadVersion();
                    break;
                case UpdateState.UP_TO_DATE:
                    DispatchUpdateEvent(UpdateEvent.EventCode.ALREADY_UP_TO_DATE);
                    break;
                case UpdateState.NEED_UPDATE:
                case UpdateState.FAIL_TO_UPDATE:
                    DispatchUpdateEvent(UpdateEvent.EventCode.NEW_VERSION_FOUND);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Start update from remote, must call CheckUpdate first
        /// </summary>
        public void StartUpdate()
        {
            if (!_localManifest.Loaded)
            {
                Debug.LogError("AssetsUpdater : No local manifest file found.");
                DispatchUpdateEvent(UpdateEvent.EventCode.ERROR_NO_LOCAL_MANIFEST);
                return;
            }

            switch (_updateState)
            {
                case UpdateState.NEED_UPDATE:
                case UpdateState.FAIL_TO_UPDATE:
                    if (!_remoteManifest.Loaded)
                    {
                        _updateState = UpdateState.PREDOWNLOAD_MANIFEST;
                        DownloadManifest();
                    }
                    else
                    {
                        DoUpdate();
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Download failed assets
        /// </summary>
        public void DownloadFailedAssets()
        {
            if (_updateState != UpdateState.FAIL_TO_UPDATE)
            {
                return;
            }

            if (_failedUnits.Count == 0)
            {
                return;
            }

            _updateState = UpdateState.UPDATING;
            _downloadUnits.Clear();
            _downloadUnits = _failedUnits;
            _failedUnits = new Dictionary<string, DownloadUnit>();
            _totalToDownload = _totalWaitToDownload = _downloadUnits.Count;
            BatchDownload();
        }

        #region private methods

        private void InitManifest(string manifestUrl)
        {
            // local
            LoadLocalManifest(manifestUrl);

            // temp
            if (File.Exists(_tempManifestPath))
            {
                _tempManifest.Parse(_tempManifestPath);
                if (!_tempManifest.Loaded)
                {
                    File.Delete(_tempManifestPath);
                }
            }
        }

        private void LoadLocalManifest(string manifestUrl)
        {
            // Find the cached manifest file
            Manifest cachedManifest = null;
            if (File.Exists(_cacheManifestPath))
            {
                cachedManifest = new Manifest();
                cachedManifest.Parse(manifestUrl);
                if (!cachedManifest.Loaded)
                {
                    File.Delete(_cacheManifestPath);
                    cachedManifest = null;
                }
            }

            // Load local manifest in app package
            _localManifest.Parse(manifestUrl);
            if (_localManifest.Loaded)
            {
                // Compare with cached manifest to determine which one to use
                if (cachedManifest != null)
                {
                    if (string.Compare(_localManifest.Version, cachedManifest.Version, StringComparison.Ordinal) > 0)
                    {
                        Directory.Delete(_storagePath, true);
                        Directory.CreateDirectory(_storagePath);
                    }
                    else
                    {
                        _localManifest = cachedManifest;
                    }
                }
            }

            if (!_localManifest.Loaded)
            {
                Debug.LogError("AssetsUpdater : No local manifest found.");
                DispatchUpdateEvent(UpdateEvent.EventCode.ERROR_NO_LOCAL_MANIFEST);
            }
        }

        private void DownloadVersion()
        {
            if (_updateState > UpdateState.PREDOWNLOAD_VERSION)
            {
                return;
            }

            var versionUrl = _localManifest.VersionUrl;
            if (!string.IsNullOrEmpty(versionUrl))
            {
                _updateState = UpdateState.DOWNLOADING_VERSION;
                var unit = new DownloadUnit
                {
                    customId = VERSION_ID,
                    srcUrl = versionUrl,
                    storagePath = _cacheVersionPath
                };
                _downloader.Download(unit);
            }
            else
            {
                Debug.LogError("AssetsUpdater : No version file found, step skipped");
                _updateState = UpdateState.PREDOWNLOAD_MANIFEST;
                DownloadManifest();
            }
        }

        private void ParseVersion()
        {
            if (_updateState != UpdateState.VERSION_LOADED)
            {
                return;
            }

            _remoteManifest.ParseVersion(_cacheVersionPath);
            if (!_remoteManifest.VersionLoaded)
            {
                Debug.LogError("AssetsUpdater : failed to parse version file, step skipped");
                _updateState = UpdateState.PREDOWNLOAD_MANIFEST;
                DownloadManifest();
            }
            else
            {
                if (_localManifest.VersionEquals(_remoteManifest))
                {
                    _updateState = UpdateState.UP_TO_DATE;
                    DispatchUpdateEvent(UpdateEvent.EventCode.ALREADY_UP_TO_DATE);
                }
                else
                {
                    _updateState = UpdateState.NEED_UPDATE;
                    DispatchUpdateEvent(UpdateEvent.EventCode.NEW_VERSION_FOUND);
                }
            }
        }

        private void DownloadManifest()
        {
            if (_updateState != UpdateState.PREDOWNLOAD_MANIFEST)
            {
                return;
            }

            var manifestUrl = _remoteManifest.VersionLoaded ? _remoteManifest.ManifestUrl : _localManifest.ManifestUrl;

            if (!string.IsNullOrEmpty(manifestUrl))
            {
                _updateState = UpdateState.DOWNLOADING_MANIFEST;
                var unit = new DownloadUnit
                {
                    customId = MANIFEST_ID,
                    srcUrl = manifestUrl,
                    storagePath = _tempManifestPath
                };
                _downloader.Download(unit);
            }
            else
            {
                Debug.LogError("AssetsUpdater : No manifest file found, check update failed");
                DispatchUpdateEvent(UpdateEvent.EventCode.ERROR_DOWNLOAD_MANIFEST);
                _updateState = UpdateState.UNCHECKED;
            }
        }

        private void ParseManifest()
        {
            if (_updateState != UpdateState.MANIFEST_LOADED)
            {
                return;
            }

            var gotVersionBefore = _remoteManifest.VersionLoaded;

            _remoteManifest.Parse(_tempManifestPath);

            if (!_remoteManifest.Loaded)
            {
                Debug.LogError("AssetsUpdater : Error parsing manifest file");
                DispatchUpdateEvent(UpdateEvent.EventCode.ERROR_PARSE_MANIFEST);
                _updateState = UpdateState.UNCHECKED;
            }
            else
            {
                if (_localManifest.VersionEquals(_remoteManifest))
                {
                    _updateState = UpdateState.UP_TO_DATE;
                    if (!gotVersionBefore)
                    {
                        DispatchUpdateEvent(UpdateEvent.EventCode.ALREADY_UP_TO_DATE);
                    }
                }
                else
                {
                    _updateState = UpdateState.NEED_UPDATE;

                    if (!gotVersionBefore)
                    {
                        DispatchUpdateEvent(UpdateEvent.EventCode.NEW_VERSION_FOUND);
                    }
                    else
                    {
                        DoUpdate();
                    }
                }
            }
        }

        private void DispatchUpdateEvent(UpdateEvent.EventCode code, string assetId = "", string message = "")
        {
            if (OnUpdateEvent != null)
            {
                var evt = new UpdateEvent(this, code, assetId, _percentByFile, message);
                OnUpdateEvent(evt);
            }
        }

        private void DoUpdate()
        {
            if (_updateState != UpdateState.NEED_UPDATE)
            {
                return;
            }

            _updateState = UpdateState.UPDATING;

            _downloadUnits.Clear();
            _failedUnits.Clear();

            _totalToDownload = 0;
            _totalWaitToDownload = 0;
            _percentByFile = 0;

            // Temporary manifest exists, resuming previous download
            if (_tempManifest.Loaded && _tempManifest.VersionEquals(_remoteManifest))
            {
                _remoteManifest = _tempManifest;
                _remoteManifest.GenResumeDownloadUnits(_storagePath, ref _downloadUnits);
                _totalWaitToDownload = _totalToDownload = _downloadUnits.Count;
                BatchDownload();
                var msg =
                    string.Format(
                        "AssetsUpdater : Resuming from previous unfinished update, {0} files remains to be finished.",
                        _totalToDownload);
                DispatchUpdateEvent(UpdateEvent.EventCode.UPDATE_PROGRESSION, "", msg);
            }
            else
            {
                // Temporary manifest not exists or out of date,
                var diffDic = _localManifest.GenDiff(_remoteManifest);
                if (diffDic.Count == 0)
                {
                    UpdateSucceed();
                }
                else
                {
                    var packageUrl = _remoteManifest.PackageUrl;
                    foreach (var diffKV in diffDic)
                    {
                        var diff = diffKV.Value;
                        if (diff.diffType == Manifest.DiffType.DELETED)
                        {
                            File.Delete(_storagePath + diff.asset.fileName);
                        }
                        else
                        {
                            var dir = Path.GetDirectoryName(_storagePath + diff.asset.fileName);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            var unit = new DownloadUnit
                            {
                                customId = diff.asset.fileName,
                                srcUrl = packageUrl + diff.asset.fileName,
                                storagePath = _storagePath + diff.asset.fileName
                            };
                            _downloadUnits.Add(unit.customId, unit);
                        }
                    }

                    var assets = _remoteManifest.GetAssets();
                    foreach (var assetKV in assets)
                    {
                        var key = assetKV.Key;
                        if (!diffDic.ContainsKey(key))
                        {
                            _remoteManifest.SetAssetDownloadState(key, Manifest.DownloadState.SUCCESSED);
                        }
                    }
                    _totalWaitToDownload = _totalToDownload = _downloadUnits.Count;
                    BatchDownload();

                    var msg = string.Format("Start to update {0} files from remote package.", _totalToDownload);
                    DispatchUpdateEvent(UpdateEvent.EventCode.UPDATE_PROGRESSION, "", msg);
                }
            }
        }

        private void BatchDownload()
        {
            foreach (var unitKV in _downloadUnits)
            {
                _downloader.Download(unitKV.Value);
            }
        }

        private void OnDownloadUnitsFinished()
        {
            if (_failedUnits.Count > 0)
            {
                _remoteManifest.SaveToFile(_tempManifestPath);
                _updateState = UpdateState.FAIL_TO_UPDATE;
                DispatchUpdateEvent(UpdateEvent.EventCode.UPDATE_FAILED);
            }
            else
            {
                UpdateSucceed();
            }
        }

        private void UpdateSucceed()
        {
            // rename temporary manifest to valid manifest
            if (File.Exists(_cacheManifestPath))
            {
                File.Delete(_cacheManifestPath);
            }
            File.Move(_tempManifestPath, _cacheManifestPath);

            _updateState = UpdateState.UP_TO_DATE;
            DispatchUpdateEvent(UpdateEvent.EventCode.UPDATE_FINISHED);
        }

        private void OnDownloadSuccess(string customId)
        {
            if (customId.Equals(VERSION_ID))
            {
                _updateState = UpdateState.VERSION_LOADED;
                ParseVersion();
                return;
            }

            if (customId.Equals(MANIFEST_ID))
            {
                _updateState = UpdateState.MANIFEST_LOADED;
                ParseManifest();
                return;
            }

            var assets = _remoteManifest.GetAssets();
            if (assets.ContainsKey(customId))
            {
                _remoteManifest.SetAssetDownloadState(customId, Manifest.DownloadState.SUCCESSED);
            }

            if (_downloadUnits.ContainsKey(customId))
            {
                // Reduce count only when unit found in _downloadUnits
                _totalWaitToDownload--;
                _percentByFile = 100 * (_totalToDownload - _totalWaitToDownload) / _totalToDownload;
                DispatchUpdateEvent(UpdateEvent.EventCode.UPDATE_PROGRESSION);
            }

            DispatchUpdateEvent(UpdateEvent.EventCode.ASSET_UPDATED, customId);

            if (_totalWaitToDownload <= 0)
            {
                OnDownloadUnitsFinished();
            }
        }

        private void OnDownloadError(string customId, Exception error)
        {
            Debug.Log(error.Message);

            if (customId.Equals(VERSION_ID))
            {
                Debug.Log("AssetsUpdater : Fail to download version file, step skipped");
                _updateState = UpdateState.PREDOWNLOAD_MANIFEST;
                DownloadManifest();
                return;
            }

            if (customId.Equals(MANIFEST_ID))
            {
                DispatchUpdateEvent(UpdateEvent.EventCode.ERROR_DOWNLOAD_MANIFEST, customId, error.Message);
                return;
            }

            if (_downloadUnits.ContainsKey(customId))
            {
                _totalWaitToDownload--;
                _failedUnits.Add(customId, _downloadUnits[customId]);
            }
            DispatchUpdateEvent(UpdateEvent.EventCode.ERROR_UPDATING, customId, error.Message);

            if (_totalWaitToDownload <= 0)
            {
                OnDownloadUnitsFinished();
            }
        }

        #endregion
    }
}