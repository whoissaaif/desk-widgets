using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DeskWidgets
{
    static class AppUpdater
    {
        internal const string AppVersion = "1.8.1";
        const string RepoOwner = "whoissaaif";
        const string RepoName = "desk-widgets";
        const string ApiUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";
        const string ExeName = "DeskWidgets.exe";

        static string foundVersion, foundNotes, foundUrl;

        public static void CheckInBackground()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (FindUpdate())
                {
                    string ver = foundVersion, notes = foundNotes, url = foundUrl;
                    App.RunOnUi(delegate { ShowUpdateDialog(ver, notes, url); });
                }
            });
        }

        public static void CheckManual(Action<bool> onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool found = false;
                try { found = FindUpdate(); }
                catch (Exception) { }
                App.RunOnUi(delegate { onComplete(found); });
            });
        }

        public static void PromptInstall()
        {
            if (foundVersion == null || foundUrl == null) return;
            ShowUpdateDialog(foundVersion, foundNotes ?? "", foundUrl);
        }

        static bool FindUpdate()
        {
            string json = Http.Get(ApiUrl);
            var jss = new System.Web.Script.Serialization.JavaScriptSerializer();
            var obj = jss.DeserializeObject(json) as Dictionary<string, object>;
            if (obj == null) return false;

            object tagObj;
            if (!obj.TryGetValue("tag_name", out tagObj)) return false;
            string tag = tagObj as string;
            if (tag == null) return false;
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);

            Version remote;
            Version current;
            if (!Version.TryParse(tag, out remote)) return false;
            if (!Version.TryParse(AppVersion, out current)) return false;
            if (remote <= current) return false;

            string downloadUrl = null;
            object assetsObj;
            if (obj.TryGetValue("assets", out assetsObj))
            {
                var assets = assetsObj as object[];
                if (assets != null)
                {
                    foreach (object a in assets)
                    {
                        var asset = a as Dictionary<string, object>;
                        if (asset == null) continue;
                        object nameObj;
                        if (asset.TryGetValue("name", out nameObj) &&
                            string.Equals(nameObj as string, ExeName, StringComparison.OrdinalIgnoreCase))
                        {
                            object urlObj;
                            if (asset.TryGetValue("browser_download_url", out urlObj))
                                downloadUrl = urlObj as string;
                            break;
                        }
                    }
                }
            }

            if (downloadUrl == null)
                downloadUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/releases/latest/download/" + ExeName;

            string releaseNotes = "";
            object bodyObj;
            if (obj.TryGetValue("body", out bodyObj) && bodyObj is string)
            {
                releaseNotes = ((string)bodyObj).Trim();
                if (releaseNotes.Length > 300)
                    releaseNotes = releaseNotes.Substring(0, 297) + "...";
            }

            foundVersion = tag;
            foundNotes = releaseNotes;
            foundUrl = downloadUrl;
            return true;
        }

        static void ShowUpdateDialog(string version, string notes, string downloadUrl)
        {
            string message = "A newer version of Desk Widgets is ready to install.";
            if (notes.Length > 0) message += "\n\n" + notes;

            var facts = new[]
            {
                new KeyValuePair<string, string>("Installed", AppVersion),
                new KeyValuePair<string, string>("Latest", version)
            };

            bool install = AlertDialog.ConfirmUpdate("An Update Is Available", message, facts);
            if (install) DownloadAndInstall(downloadUrl);
        }

        static void DownloadAndInstall(string url)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string tempExe = Path.Combine(Path.GetTempPath(), "DeskWidgets_update.exe");
                    byte[] data = Http.GetBytes(url);
                    File.WriteAllBytes(tempExe, data);

                    string target = Application.ExecutablePath;
                    string bat = Path.Combine(Path.GetTempPath(), "deskwidgets_update.bat");
                    File.WriteAllText(bat,
                        "@echo off\r\n" +
                        "timeout /t 2 /nobreak >nul\r\n" +
                        "copy /y \"" + tempExe + "\" \"" + target + "\"\r\n" +
                        "start \"\" \"" + target + "\"\r\n" +
                        "del \"" + tempExe + "\"\r\n" +
                        "del \"%~f0\"\r\n",
                        Encoding.ASCII);

                    App.RunOnUi(delegate
                    {
                        Process.Start(new ProcessStartInfo(bat)
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                        Application.Exit();
                    });
                }
                catch (Exception ex)
                {
                    App.RunOnUi(delegate
                    {
                        AlertDialog.Show("Update Failed", "The update couldn't be installed: " + ex.Message +
                            "\n\nYou can download it manually from the website.");
                    });
                }
            });
        }
    }
}
