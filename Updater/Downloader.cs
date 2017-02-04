using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;

namespace Core.Updater
{
    public struct DownloadUnit
    {
        public string srcUrl;
        public string storagePath;
        public string customId;
    }

    public class Downloader
    {
        private readonly Dictionary<string, WebClient> _clients = new Dictionary<string, WebClient>();

        public event Action<string> OnDownloadSuccess;
        public event Action<string, Exception> OnDownloadError;

        public void Download(DownloadUnit unit)
        {
            var client = new WebClient();
            client.DownloadFileCompleted += OnDownloadFileCompleted;
            client.DownloadFileAsync(new Uri(unit.srcUrl), unit.storagePath, unit.customId);

            _clients.Add(unit.customId, client);
        }

        /// <summary>
        /// Not executed on main thread
        /// </summary>
        private void OnDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            ThreadHelper.Instance.QueueOnMainThread(
                            () =>
                            {
                                var customId = (string) e.UserState;

                                if (e.Error != null)
                                {
                                    if (OnDownloadError != null)
                                    {
                                        OnDownloadError(customId, e.Error);
                                    }
                                }
                                else if (e.Cancelled)
                                {
                                    if (OnDownloadError != null)
                                    {
                                        OnDownloadError(customId, new Exception("Download is cancelled."));
                                    }
                                }
                                else
                                {
                                    if (OnDownloadSuccess != null)
                                    {
                                        OnDownloadSuccess(customId);
                                    }
                                }

                                var client = _clients[customId];
                                client.DownloadFileCompleted -= OnDownloadFileCompleted;
                                client.Dispose();
                                _clients.Remove(customId);
                            }
                        );
        }
    }
}