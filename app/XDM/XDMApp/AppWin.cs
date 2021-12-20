﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TraceLog;
using XDM.Common.UI;
using XDM.Core.Lib.Common;
using XDM.Core.Lib.UI;
using XDM.Core.Lib.Util;

namespace XDMApp
{
    public class AppWin : IAppUI
    {
        private IAppWinPeer peer;
        private IApp app;
        private delegate void UpdateItemCallBack(string id, string targetFileName, long size);
        private Action<string, int, double, long> updateProgressAction;
        private long lastProgressUpdate = 0;

        public AppWin(IAppWinPeer peer, IApp app)
        {
            this.peer = peer;
            this.app = app;
            this.updateProgressAction = new Action<string, int, double, long>(this.UpdateProgressOnUI);

            AttachedEventHandler();

            this.LoadDownloadList();

            UpdateToolbarButtonState();
        }

        public IApp App { get => app; set => app = value; }

        public void AddItemToTop(
            string id,
            string targetFileName,
            DateTime date,
            long fileSize,
            string type,
            FileNameFetchMode fileNameFetchMode,
            string primaryUrl,
            DownloadStartType startType,
            AuthenticationInfo? authentication,
            ProxyInfo? proxyInfo,
            int maxSpeedLimit)
        {
            RunOnUiThread(() =>
            {
                var downloadEntry = new InProgressDownloadEntry
                {
                    Name = targetFileName,
                    DateAdded = date,
                    DownloadType = type,
                    Id = id,
                    Progress = 0,
                    Size = fileSize,
                    Status = DownloadStatus.Stopped,
                    TargetDir = "",
                    PrimaryUrl = primaryUrl,
                    Authentication = authentication,
                    Proxy = proxyInfo,
                    MaxSpeedLimitInKiB = maxSpeedLimit,
                };

                this.peer.AddToTop(downloadEntry);
                this.peer.SwitchToInProgressView();
                this.peer.ClearInProgressViewSelection();

                this.SaveInProgressList();
                UpdateToolbarButtonState();
            });
        }

        public bool Confirm(object? window, string text)
        {
            return peer.Confirm(window, text);
        }

        public IDownloadCompleteDialog CreateDownloadCompleteDialog()
        {
            return peer.CreateDownloadCompleteDialog(this.app);
        }

        public INewDownloadDialogSkeleton CreateNewDownloadDialog(bool empty)
        {
            return peer.CreateNewDownloadDialog(empty);
        }

        public INewVideoDownloadDialog CreateNewVideoDialog()
        {
            return peer.CreateNewVideoDialog();
        }

        public IProgressWindow CreateProgressWindow(string downloadId)
        {
            return peer.CreateProgressWindow(downloadId, this.app, this);
        }

        public void DownloadCanelled(string id)
        {
            DownloadFailed(id);
        }

        public void DownloadFailed(string id)
        {
            RunOnUiThread(() =>
            {
                CallbackActions.DownloadFailed(id, peer);
                SaveInProgressList();
                UpdateToolbarButtonState();
            });
        }

        public void DownloadFinished(string id, long finalFileSize, string filePath)
        {
            RunOnUiThread(() =>
            {
                CallbackActions.DownloadFinished(id, finalFileSize, filePath, peer, app);
                this.SaveFinishedList();
                this.SaveInProgressList();
                UpdateToolbarButtonState();
                QueueWindowManager.RefreshView();
            });
        }

        public void DownloadStarted(string id)
        {
            RunOnUiThread(() =>
            {
                CallbackActions.DownloadStarted(id, peer);
                UpdateToolbarButtonState();
            });
        }

        public IEnumerable<InProgressDownloadEntry> GetAllInProgressDownloads()
        {
            return peer.InProgressDownloads;
        }

        public InProgressDownloadEntry? GetInProgressDownloadEntry(string downloadId)
        {
            return peer.FindInProgressItem(downloadId)?.DownloadEntry;
        }

        public string GetUrlFromClipboard()
        {
            return peer.GetUrlFromClipboard();
        }

        public AuthenticationInfo? PromtForCredentials(string message)
        {
            return peer.PromtForCredentials(message);
        }

        public void RenameFileOnUI(string id, string folder, string file)
        {
            RunOnUiThread(() =>
            {
                var downloadEntry = this.peer.FindInProgressItem(id);
                if (downloadEntry == null) return;
                if (file != null)
                {
                    downloadEntry.Name = file;
                }
                if (folder != null)
                {
                    downloadEntry.DownloadEntry.TargetDir = folder;
                }
                this.SaveInProgressList();
            });
        }

        public void ResumeDownload(string downloadId)
        {
            var idDict = new Dictionary<string, BaseDownloadEntry>();
            var download = peer.FindInProgressItem(downloadId);
            if (download == null) return;
            idDict[download.DownloadEntry.Id] = download.DownloadEntry;
            App.ResumeDownload(idDict);
        }

        public void RunOnUiThread(Action action)
        {
            peer.RunOnUIThread(action);
        }

        public void SetDownloadStatusWaiting(string id)
        {
            RunOnUiThread(() =>
            {
                var download = this.peer.FindInProgressItem(id);
                if (download == null) return;
                download.Status = DownloadStatus.Stopped;
                UpdateToolbarButtonState();
            });
        }

        public void ShowUpdateAvailableNotification()
        {
            RunOnUiThread(() =>
            {
                peer.ShowUpdateAvailableNotification();
            });
        }

        public void ShowDownloadCompleteDialog(string file, string folder)
        {
            RunOnUiThread(() =>
            {
                DownloadCompleteDialogHelper.ShowDialog(this.App, CreateDownloadCompleteDialog(), file, folder);
            });
        }

        public void ShowMessageBox(object? window, string message)
        {
            peer.ShowMessageBox(window, message);
        }

        public void ShowNewDownloadDialog(Message message)
        {
            var url = message.Url;
            if (NewDownloadPromptTracker.IsPromptAlreadyOpen(url))
            {
                return;
            }
            peer.RunOnUIThread(() =>
            {
                NewDownloadPromptTracker.PromptOpen(url);
                NewDownloadDialogHelper.CreateAndShowDialog(this.App, this, this.CreateNewDownloadDialog(false), message,
                    () => NewDownloadPromptTracker.PromptClosed(url));
            });
        }

        public void ShowVideoDownloadDialog(string videoId, string name, long size)
        {
            RunOnUiThread(() =>
            {
                NewVideoDownloadDialogHelper.ShowVideoDownloadDialog(this.App, this, this.CreateNewVideoDialog(), videoId, name, size);
            });
        }

        public void UpdateItem(string id, string targetFileName, long size)
        {
            RunOnUiThread(() =>
            {
                var download = peer.FindInProgressItem(id);
                if (download == null) return;
                download.Name = targetFileName;
                download.Size = size;
                this.SaveInProgressList();
            });
        }

        private void UpdateProgressOnUI(string id, int progress, double speed, long eta)
        {
            var downloadEntry = peer.FindInProgressItem(id);
            if (downloadEntry != null)
            {
                downloadEntry.Progress = progress;
                downloadEntry.DownloadSpeed = Helpers.FormatSize(speed) + "/s";
                downloadEntry.ETA = Helpers.ToHMS(eta);
                var time = DateTime.Now.Ticks;
                if (time - lastProgressUpdate > 3000)
                {
                    lastProgressUpdate = time;
                    this.SaveInProgressList();
                }
            }
        }

        public void UpdateProgress(string id, int progress, double speed, long eta)
        {
            peer.RunOnUIThread(this.updateProgressAction, id, progress, speed, eta);
        }

        private void LoadDownloadList()
        {
            try
            {
                peer.InProgressDownloads = TransactedIO.ReadInProgressList("inprogress-downloads.db", Config.DataDir);
                peer.FinishedDownloads = TransactedIO.ReadFinishedList("finished-downloads.db", Config.DataDir);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "LoadDownloadList");
            }

        }

        private void SaveInProgressList()
        {
            lock (this)
            {
                TransactedIO.WriteInProgressList(peer.InProgressDownloads, "inprogress-downloads.db", Config.DataDir);
            }
        }

        private void SaveFinishedList()
        {
            lock (this)
            {
                TransactedIO.WriteFinishedList(peer.FinishedDownloads, "finished-downloads.db", Config.DataDir);
            }
        }

        private void DisableButton(IButton button)
        {
            button.Enable = false;
        }

        private void EnableButton(IButton button)
        {
            button.Enable = true;
        }

        private void UpdateToolbarButtonState()
        {
            DisableButton(peer.OpenFileButton);
            DisableButton(peer.OpenFolderButton);
            DisableButton(peer.PauseButton);
            DisableButton(peer.ResumeButton);
            DisableButton(peer.DeleteButton);

            if (peer.IsInProgressViewSelected)
            {
                peer.OpenFileButton.Visible = peer.OpenFolderButton.Visible = false;
                peer.PauseButton.Visible = peer.ResumeButton.Visible = true;
                var selectedRows = peer.SelectedInProgressRows;
                if (selectedRows.Count > 0)
                {
                    EnableButton(peer.DeleteButton);
                }
                if (selectedRows.Count > 1)
                {
                    EnableButton(peer.ResumeButton);
                    EnableButton(peer.PauseButton);
                }
                else if (selectedRows.Count == 1)
                {
                    var ent = selectedRows[0];
                    var isActive = App.IsDownloadActive(ent.DownloadEntry.Id);
                    if (isActive)
                    {
                        EnableButton(peer.PauseButton);
                    }
                    else
                    {
                        EnableButton(peer.ResumeButton);
                    }
                }
            }
            else
            {
                peer.OpenFileButton.Visible = peer.OpenFolderButton.Visible = true;
                peer.PauseButton.Visible = peer.ResumeButton.Visible = false;
                if (peer.SelectedFinishedRows.Count > 0)
                {
                    EnableButton(peer.DeleteButton);
                }

                if (peer.SelectedFinishedRows.Count == 1)
                {
                    EnableButton(peer.OpenFileButton);
                    EnableButton(peer.OpenFolderButton);
                }
            }
        }

        private void DeleteDownloads()
        {
            UIActions.DeleteDownloads(peer.IsInProgressViewSelected,
                peer, App, inProgress =>
                {
                    if (inProgress)
                    {
                        SaveInProgressList();
                    }
                    else
                    {
                        SaveFinishedList();
                    }
                });
        }

        private void AttachedEventHandler()
        {
            peer.NewDownloadClicked += (s, e) =>
            {
                peer.RunOnUIThread(() =>
                {
                    NewDownloadDialogHelper.CreateAndShowDialog(this.App, this, CreateNewDownloadDialog(true));
                });
            };

            peer.YoutubeDLDownloadClicked += (s, e) =>
            {
                peer.ShowYoutubeDLDialog(this, app);
            };

            peer.BatchDownloadClicked += (s, e) =>
            {
                peer.ShowBatchDownloadWindow(app, this);
            };

            peer.SelectionChanged += (s, e) =>
            {
                UpdateToolbarButtonState();
            };

            peer.CategoryChanged += (s, e) =>
            {
                UpdateToolbarButtonState();
            };

            peer.NewButton.Clicked += (s, e) =>
            {
                peer.OpenNewDownloadMenu();
            };

            peer.DeleteButton.Clicked += (a, b) =>
            {
                DeleteDownloads();
            };

            peer.OpenFolderButton.Clicked += (a, b) =>
            {
                UIActions.OpenSelectedFolder(peer);
            };

            peer.OpenFileButton.Clicked += (a, b) =>
            {
                UIActions.OpenSelectedFile(peer);
            };

            peer.PauseButton.Clicked += (a, b) =>
            {
                if (peer.IsInProgressViewSelected)
                {
                    UIActions.StopSelectedDownloads(peer, App);
                }
            };

            peer.ResumeButton.Clicked += (a, b) =>
            {
                if (peer.IsInProgressViewSelected)
                {
                    UIActions.ResumeDownloads(peer, App);
                }
            };

            peer.SettingsClicked += (s, e) =>
            {
                peer.ShowSettingsDialog(app, 1);
                peer.UpdateParallalismLabel();
            };

            peer.BrowserMonitoringSettingsClicked += (s, e) =>
            {
                peer.ShowBrowserMonitoringDialog(app);
                peer.UpdateParallalismLabel();
            };

            peer.ClearAllFinishedClicked += (s, e) =>
            {
                peer.DeleteAllFinishedDownloads();
                SaveFinishedList();
            };

            peer.ImportClicked += (s, e) =>
            {
                peer.ImportDownloads(app);
                LoadDownloadList();
            };

            peer.ExportClicked += (s, e) =>
            {
                peer.ExportDownloads(app);
            };

            peer.HelpClicked += (s, e) =>
            {
                Helpers.OpenBrowser(app.HelpPage);
            };

            peer.UpdateClicked += (s, e) =>
            {
                if (App.IsAppUpdateAvailable)
                {
                    Helpers.OpenBrowser(App.UpdatePage);
                }
                else
                {
                    if (peer.Confirm(peer, App.ComponentUpdateText))
                    {
                        LaunchUpdater(UpdateMode.FFmpegUpdateOnly | UpdateMode.YoutubeDLUpdateOnly);
                    }
                }
            };

            peer.BrowserMonitoringButtonClicked += (s, e) =>
            {
                if (Config.Instance.IsBrowserMonitoringEnabled)
                {
                    Config.Instance.IsBrowserMonitoringEnabled = false;
                }
                else
                {
                    Config.Instance.IsBrowserMonitoringEnabled = true;
                }
                Config.SaveConfig();
                app.ApplyConfig();
                peer.UpdateBrowserMonitorButton();
            };

            peer.SupportPageClicked += (s, e) =>
            {
                Helpers.OpenBrowser("https://subhra74.github.io/xdm/redirect-support.html");
            };

            peer.BugReportClicked += (s, e) =>
            {
                Helpers.OpenBrowser("https://subhra74.github.io/xdm/redirect-issue.html");
            };

            peer.CheckForUpdateClicked += (s, e) =>
            {
                Helpers.OpenBrowser(app.UpdatePage);
            };

            peer.SchedulerClicked += (s, e) =>
            {
                ShowQueueWindow(peer);
            };

            peer.MoveToQueueClicked += (s, e) =>
            {
                var selectedIds = peer.SelectedInProgressRows?.Select(x => x.DownloadEntry.Id)?.ToArray() ?? new string[0];
                MoveToQueue(selectedIds);
                //var queueSelectionDialog = peer.CreateQueueSelectionDialog();
                //queueSelectionDialog.SetData(QueueManager.Queues.Select(q => q.Name), selectedIds);
                //queueSelectionDialog.ManageQueuesClicked += (_, _) =>
                //{
                //    ShowQueueWindow();
                //};
                //queueSelectionDialog.QueueSelected += (s, e) =>
                //{
                //    var index = e.SelectedQueueIndex;
                //    var queueId = QueueManager.Queues[index].ID;
                //    var downloadIds = e.DownloadIds;
                //    QueueManager.AddDownloadsToQueue(queueId, downloadIds);
                //};
                //queueSelectionDialog.ShowWindow(peer);
            };

            AttachContextMenuEvents();

            peer.InProgressContextMenuOpening += (_, _) => InProgressContextMenuOpening();
            peer.FinishedContextMenuOpening += (_, _) => FinishedContextMenuOpening();
        }

        public void ShowQueueWindow(object window)
        {
            QueueWindowManager.ShowWindow(window, peer.CreateQueuesAndSchedulerWindow(this), this.app);
        }

        private void LaunchUpdater(UpdateMode updateMode)
        {
            var updateDlg = peer.CreateUpdateUIDialog(this);
            var updates = App.Updates?.Where(u => u.IsExternal)?.ToList() ?? new List<UpdateInfo>(0);
            if (updates.Count == 0) return;
            var commonUpdateUi = new ComponentUpdaterUI(updateDlg, app, updateMode);
            updateDlg.Load += (_, _) => commonUpdateUi.StartUpdate();
            updateDlg.Finished += (_, _) =>
            {
                RunOnUiThread(() =>
                {
                    peer.ClearUpdateInformation();
                });
            };
            updateDlg.Show();
        }

        private void AttachContextMenuEvents()
        {
            peer.MenuItemMap["pause"].Clicked += (_, _) => UIActions.StopSelectedDownloads(peer, App);
            peer.MenuItemMap["resume"].Clicked += (_, _) => UIActions.ResumeDownloads(peer, App);
            peer.MenuItemMap["delete"].Clicked += (_, _) => DeleteDownloads();
            peer.MenuItemMap["saveAs"].Clicked += (_, _) => UIActions.SaveAs(peer, App);
            peer.MenuItemMap["refresh"].Clicked += (_, _) => UIActions.RefreshLink(peer, App);
            peer.MenuItemMap["showProgress"].Clicked += (_, _) => UIActions.ShowProgressWindow(peer, App);
            peer.MenuItemMap["copyURL"].Clicked += (_, _) => UIActions.CopyURL1(peer, App);
            peer.MenuItemMap["copyURL1"].Clicked += (_, _) => UIActions.CopyURL2(peer, App);
            peer.MenuItemMap["properties"].Clicked += (_, _) => UIActions.ShowSeletectedItemProperties(peer, App);
            peer.MenuItemMap["open"].Clicked += (_, _) => UIActions.OpenSelectedFile(peer);
            peer.MenuItemMap["openFolder"].Clicked += (_, _) => UIActions.OpenSelectedFolder(peer);
            peer.MenuItemMap["deleteDownloads"].Clicked += (_, _) => DeleteDownloads();
            peer.MenuItemMap["copyFile"].Clicked += (_, _) => UIActions.CopyFile(peer);
            peer.MenuItemMap["properties1"].Clicked += (_, _) => UIActions.ShowSeletectedItemProperties(peer, App);
            peer.MenuItemMap["downloadAgain"].Clicked += (_, _) => UIActions.RestartDownload(peer, App);
            peer.MenuItemMap["restart"].Clicked += (_, _) => UIActions.RestartDownload(peer, App);
            //peer.MenuItemMap["schedule"].Clicked += (_, _) => UIActions.ScheduleDownload(peer, App);
        }

        private void InProgressContextMenuOpening()
        {
            foreach (var menu in peer.MenuItems)
            {
                menu.Enabled = false;
            }
            peer.MenuItemMap["delete"].Enabled = true;
            peer.MenuItemMap["schedule"].Enabled = true;
            peer.MenuItemMap["moveToQueue"].Enabled = true;
            var selectedRows = peer.SelectedInProgressRows;
            if (selectedRows.Count > 1)
            {
                peer.MenuItemMap["pause"].Enabled = true;
                peer.MenuItemMap["resume"].Enabled = true;
                peer.MenuItemMap["showProgress"].Enabled = true;
            }
            else if (selectedRows.Count == 1)
            {
                peer.MenuItemMap["showProgress"].Enabled = true;
                peer.MenuItemMap["copyURL"].Enabled = true;
                peer.MenuItemMap["saveAs"].Enabled = true;
                peer.MenuItemMap["refresh"].Enabled = true;
                peer.MenuItemMap["properties"].Enabled = true;
                peer.MenuItemMap["saveAs"].Enabled = true;
                peer.MenuItemMap["saveAs"].Enabled = true;
                peer.MenuItemMap["copyURL"].Enabled = true;

                var ent = selectedRows[0].DownloadEntry;//selectedRows[0].Cells[1].Value as InProgressDownloadEntry;
                if (ent == null) return;
                var isActive = App.IsDownloadActive(ent.Id);
                Log.Debug("Selected item active: " + isActive);
                if (isActive)
                {
                    peer.MenuItemMap["pause"].Enabled = true;
                }
                else
                {
                    peer.MenuItemMap["resume"].Enabled = true;
                    peer.MenuItemMap["restart"].Enabled = true;
                }
            }
        }

        private void FinishedContextMenuOpening()
        {
            foreach (var menu in peer.MenuItems)
            {
                menu.Enabled = false;
            }

            peer.MenuItemMap["deleteDownloads"].Enabled = true;

            var selectedRows = peer.SelectedFinishedRows;
            if (selectedRows.Count == 1)
            {
                foreach (var menu in peer.MenuItems)
                {
                    menu.Enabled = true;
                }
            }
        }

        public void InstallLatestFFmpeg()
        {
            LaunchUpdater(UpdateMode.FFmpegUpdateOnly);
        }

        public void InstallLatestYoutubeDL()
        {
            LaunchUpdater(UpdateMode.YoutubeDLUpdateOnly);
        }

        public void MoveToQueue(string[] selectedIds, bool prompt = false, Action? callback = null)
        {
            if (prompt && !peer.Confirm(peer, "Add to queue?"))
            {
                return;
            }
            var queueSelectionDialog = peer.CreateQueueSelectionDialog();
            queueSelectionDialog.SetData(QueueManager.Queues.Select(q => q.Name), selectedIds);
            queueSelectionDialog.ManageQueuesClicked += (_, _) =>
            {
                ShowQueueWindow(peer);
            };
            queueSelectionDialog.QueueSelected += (s, e) =>
            {
                var index = e.SelectedQueueIndex;
                var queueId = QueueManager.Queues[index].ID;
                var downloadIds = e.DownloadIds;
                QueueManager.AddDownloadsToQueue(queueId, downloadIds);
            };
            queueSelectionDialog.ShowWindow(peer);
        }
    }
}