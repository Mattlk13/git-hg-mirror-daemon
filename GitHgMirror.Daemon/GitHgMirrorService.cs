﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GitHgMirror.Runner;

namespace GitHgMirror.Daemon
{
    public partial class GitHgMirrorService : ServiceBase
    {
        private MirroringSettings _settings;
        private MirrorRunner _runner;
        private UntouchedRepositoriesCleaner _cleaner;
        private ManualResetEvent _waitHandle = new ManualResetEvent(false);


        public GitHgMirrorService()
        {
            InitializeComponent();
        }


        protected override void OnStart(string[] args)
        {
            serviceEventLog.MaximumKilobytes = 65536;
            serviceEventLog.WriteEntry("GitHgMirrorDaemon started.");

            _settings = new MirroringSettings
            {
                ApiEndpointUrl = new Uri("http://githgmirror.com/api/GitHgMirror.Common/Mirrorings"),
                ApiPassword = ConfigurationManager.ConnectionStrings[Constants.ApiPasswordKey]?.ConnectionString ?? string.Empty,
                RepositoriesDirectoryPath = @"C:\GitHgMirror\Repositories",
                MaxDegreeOfParallelism = 10,
                // This way no sync waits for another one to finish in a batch but they run independently of each other,
                // the throughput only being limited by MaxDegreeOfParallelism.
                BatchSize = 1
            };

            var startTimer = new System.Timers.Timer(10000);
            startTimer.Elapsed += timer_Elapsed;
            startTimer.Enabled = true;

            _cleaner = new UntouchedRepositoriesCleaner(_settings, serviceEventLog);
            var cleanerTimer = new System.Timers.Timer(3600000 * 2); // Two hours
            cleanerTimer.Elapsed += (sender, e) =>
                {
                    _cleaner.Clean();
                };
            cleanerTimer.Enabled = true;
        }

        protected override void OnStop()
        {
            serviceEventLog.WriteEntry("GitHgMirrorDaemon stopped. Stopping mirroring.");

            _runner.Stop();
            _waitHandle.Set();

            serviceEventLog.WriteEntry("Mirroring stopped.");
        }


        void timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ((System.Timers.Timer)sender).Enabled = false;

            serviceEventLog.WriteEntry("Starting mirroring.");

            _runner = new MirrorRunner(_settings, serviceEventLog);

            var started = false;
            while (!started)
            {
                try
                {
                    _runner.Start();

                    serviceEventLog.WriteEntry("Mirroring started.");

                    _waitHandle.WaitOne();
                    started = true;
                }
                catch (Exception ex)
                {
                    serviceEventLog.WriteEntry(
                        "Starting mirroring failed with the following exception: " + ex.ToString() +
                        Environment.NewLine +
                        "A new start will be attempted in 30s.",
                        EventLogEntryType.Error);

                    Thread.Sleep(30000);
                }
            }
        }
    }
}
