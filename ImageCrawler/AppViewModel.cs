using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows;
using System.Threading;
using System.Windows.Shell;
using System.Windows.Threading;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using System.Net;

namespace ImageCrawler
{
    class AppViewModel : INotifyPropertyChanged
    {
        // Properties
        public uint Downloaded
        {
            get { return m_uDownloaded; }
            private set
            {
                m_uDownloaded = value;
                UpdateTaskBarProgressValue((double)value / NumberOfPage);
                NotifyPropertyChanged(sm_downloadedPropChangedEventArg);
            }
        }
        public uint NumberOfPage
        {
            get { return m_uNumberOfPage; }
            private set
            {
                m_uNumberOfPage = value;
                NotifyPropertyChanged(sm_numPagesPropChangedEventArg);
            }
        }
        public string TargetURL
        {
            get { return m_strTargetURL; }
            set
            {
                m_strTargetURL = value;
                NotifyPropertyChanged(sm_targetURLPropChangedEventArg);
            }
        }
        public string Prefix
        {
            get { return m_strFilePrefix; }
            set
            {
                m_strFilePrefix = value;
                NotifyPropertyChanged(sm_filePrefixPropChangedEventArg);
            }
        }
        public string Postfix
        {
            get { return m_strFilePostfix; }
            set
            {
                m_strFilePostfix = value;
                NotifyPropertyChanged(sm_filePostfixPropChangedEventArg);
            }
        }
        public string Status
        {
            get { return m_strStatusText; }
            set
            {
                m_strStatusText = value;
                NotifyPropertyChanged(sm_statusTextPropChangedEventArg);
            }
        }
        public string Filter
        {
            get { return m_strFilter; }
            set
            {
                m_strFilter = value;
                NotifyPropertyChanged(sm_filterPropChangedEventArg);
            }
        }
        public bool Busy
        {
            get { return m_bBusy; }
            private set
            {
                m_bBusy = value;
                NotifyPropertyChanged(sm_busyPropChangedEventArg);

                Gotcha.RaiseCanExecuteChanged();
                Cancel.RaiseCanExecuteChanged();
            }
        }
        public RelayCommand Gotcha
        {
            get;
            private set;
        }
        public RelayCommand Cancel
        {
            get;
            private set;
        }

        // Events
        public event PropertyChangedEventHandler PropertyChanged;

        // Fields
        uint m_uDownloaded;
        uint m_uNumberOfPage;
        string m_strTargetURL;
        string m_strFilePrefix;
        string m_strFilePostfix;
        string m_strStatusText;
        string m_strFilter;
        bool m_bBusy;
        BackgroundWorker worker;

        public AppViewModel()
        {
            m_uDownloaded = 0;
            m_uNumberOfPage = 0;

            m_strFilePrefix = "";
            m_strFilePostfix = "";
            m_strTargetURL = "";
            m_strStatusText = "";
            m_strFilter = "";

            m_bBusy = false;

            worker = new BackgroundWorker();
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += worker_DoWork;
            worker.RunWorkerCompleted += worker_RunWorkerCompleted;

            Gotcha = new RelayCommand(
                param => this.OnGotcha(),
                param => this.CanGotcha()
                );
            Cancel = new RelayCommand(
                param => this.OnCancel(),
                param => this.CanCancel()
                );

            UpdateTaskBarProgressState(TaskbarItemProgressState.None);
        }

        void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            Busy = true;

            Downloaded = 0;
            NumberOfPage = 0;

            int actionIndex = 0;
            string htmlContents = "";
            HtmlDocument doc = new HtmlDocument();
            int listCursor = 0;
            List<string> imageURLs = new List<string>();
            WebClient client = new WebClient();
            string prefix = (Prefix.Length > 0) ? Prefix : "Image";

            Action[] actions =
            {
                // Get HTML Contents
                () =>
                {
                    htmlContents = client.DownloadString(TargetURL);

                    if (htmlContents == null) {
                        UpdateTaskBarProgressState(TaskbarItemProgressState.Error);
                        Status = "Failed to access target URL";
                        actionIndex = -1;
                    }
                    else
                    {
                        actionIndex++;
                    }
                },
                // 2. Parsing HTML Document
                () =>
                {
                    if (htmlContents != null)
                    {
                        doc.LoadHtml(htmlContents);
                        actionIndex++;
                    }
                    else
                    {
                        UpdateTaskBarProgressState(TaskbarItemProgressState.Error);
                        Status = "HTML Contents is empty";
                        actionIndex = -1;
                    }
                },
                // 3. Filter Image Nodes
                () =>
                {
                    foreach(HtmlNode node in doc.DocumentNode.SelectNodes("//img"))
                    {
                        if (Regex.IsMatch(node.Attributes["src"].Value, Filter))
                        {
                            imageURLs.Add(node.Attributes["src"].Value);
                        }
                    }
                    NumberOfPage = (uint)imageURLs.Count;
                    if (NumberOfPage == 0)
                    {
                        Status = "No Image Found. This is caused by Filter";
                        actionIndex = -1;
                    } else
                    {
                        actionIndex++;
                    }
                },
                // 4. Download Images
                () =>
                {
                    Status = "Downloading: " + imageURLs[listCursor];

                    StringBuilder strBuilder = new StringBuilder();
                    strBuilder.Append(prefix);
                    strBuilder.AppendFormat("{0:D3}.", listCursor+1);
                    strBuilder.Append(Postfix);

                    client.DownloadFile(imageURLs[listCursor], strBuilder.ToString());
                    Downloaded++;

                    listCursor++;
                    if (listCursor >= imageURLs.Count)
                    {
                        actionIndex++;
                    }
                },
                // 5. Finish Jobs
                () =>
                {
                    Status = "Done";
                    actionIndex = -1;
                }
            };

            UpdateTaskBarProgressState(TaskbarItemProgressState.Normal);

            while (!worker.CancellationPending && actionIndex >= 0)
            {
                actions[actionIndex]();
            }

            if (worker.CancellationPending)
            {
                UpdateTaskBarProgressState(TaskbarItemProgressState.Paused);
                Status = "Cancelled";
                e.Cancel = true;
            }
        }

        void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Busy = false;
        }

        private void UpdateTaskBarProgressState(TaskbarItemProgressState state)
        {
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    Application.Current.MainWindow.TaskbarItemInfo.ProgressState = state;
                }));
        }

        private void UpdateTaskBarProgressValue(double value)
        {
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    Application.Current.MainWindow.TaskbarItemInfo.ProgressValue = value;
                }));
        }

        void OnGotcha()
        {
            worker.RunWorkerAsync();
        }

        bool CanGotcha()
        {
            return !Busy && (TargetURL.Length > 0);
        }

        void OnCancel()
        {
            Status = "Canceling";
            worker.CancelAsync();
        }

        bool CanCancel()
        {
            return Busy;
        }

        protected void NotifyPropertyChanged(PropertyChangedEventArgs arg)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, arg);
            }
        }

        // Static Codes
        static PropertyChangedEventArgs sm_downloadedPropChangedEventArg;
        static PropertyChangedEventArgs sm_numPagesPropChangedEventArg;

        static PropertyChangedEventArgs sm_targetURLPropChangedEventArg;
        static PropertyChangedEventArgs sm_filePrefixPropChangedEventArg;
        static PropertyChangedEventArgs sm_filePostfixPropChangedEventArg;

        static PropertyChangedEventArgs sm_statusTextPropChangedEventArg;
        static PropertyChangedEventArgs sm_filterPropChangedEventArg;

        static PropertyChangedEventArgs sm_busyPropChangedEventArg;

        static AppViewModel()
        {
            sm_downloadedPropChangedEventArg = new PropertyChangedEventArgs("Downloaded");
            sm_numPagesPropChangedEventArg = new PropertyChangedEventArgs("NumberOfPage");

            sm_targetURLPropChangedEventArg = new PropertyChangedEventArgs("TargetURL");
            sm_filePrefixPropChangedEventArg = new PropertyChangedEventArgs("Prefix");
            sm_filePostfixPropChangedEventArg = new PropertyChangedEventArgs("Postfix");

            sm_statusTextPropChangedEventArg = new PropertyChangedEventArgs("Status");
            sm_filterPropChangedEventArg = new PropertyChangedEventArgs("Filter");

            sm_busyPropChangedEventArg = new PropertyChangedEventArgs("Busy");
        }

    }
}
