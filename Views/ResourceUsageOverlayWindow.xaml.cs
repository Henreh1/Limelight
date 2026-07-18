using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Limelight.Views
{
    public partial class ResourceUsageOverlayWindow : Window
    {
        private readonly Process _limelightProcess;
        private readonly DispatcherTimer _refreshTimer;

        public ResourceUsageOverlayWindow()
        {
            InitializeComponent();

            _limelightProcess =
                Process.GetCurrentProcess();

            _refreshTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromSeconds(1)
                };

            _refreshTimer.Tick +=
                RefreshTimer_Tick;

            Loaded +=
                ResourceUsageOverlayWindow_Loaded;

            Closed +=
                ResourceUsageOverlayWindow_Closed;
        }

        private void ResourceUsageOverlayWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            // I keep the monitor tucked into the working area so it
            // does not sit underneath the Windows taskbar.
            Rect workArea =
                SystemParameters.WorkArea;

            Left =
    workArea.Left +
    18;

            Top =
                workArea.Bottom -
                ActualHeight -
                18;

            RefreshUsage();
            _refreshTimer.Start();
        }

        private void RefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            RefreshUsage();
        }

        private void RefreshUsage()
        {
            _limelightProcess.Refresh();

            double workingSetMb =
                _limelightProcess.WorkingSet64 /
                1024d /
                1024d;

            double managedMemoryMb =
                GC.GetTotalMemory(
                    forceFullCollection: false) /
                1024d /
                1024d;

            MemoryUsageText.Text =
                $"{workingSetMb:F0} MB RAM";

            ResourceDetailText.Text =
                $"MANAGED {managedMemoryMb:F0} MB | CURRENT WORKING SET";
        }

        private void ResourceUsageOverlayWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _refreshTimer.Stop();
            _limelightProcess.Dispose();
        }
    }
}