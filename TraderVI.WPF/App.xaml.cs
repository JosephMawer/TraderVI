using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace TraderVI.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0 && e.Args[0] == "--practice")
            {
                // This entry point constructs only the recorded-file viewer, not the operational dashboard.
                base.OnStartup(e);
                if (e.Args.Length != 2) { Shutdown(2); return; }
                var window = new PracticeWindow(e.Args[1]);
                MainWindow = window;
                window.Show();
                return;
            }
            StartupUri = new Uri("PaperDashboardWindow.xaml", UriKind.Relative);
            base.OnStartup(e);
        }
    }
}
