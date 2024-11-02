using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Threading;
using System.IO;


namespace kat_pcgw_nexus
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            Directory.SetCurrentDirectory("C:\\Program Files (x86)\\KAT Gateway\\");
            InitializeComponent();
            NexusService.Instance.BroadcastMessageReceived += OnMessageReceived;
            NexusService.Instance.StartListening();
        }

        private void OnMessageReceived(string message)
        {
            // Update the UI on the main thread
            Dispatcher.Invoke(() =>
            {
                ReceivedDataTextBox.AppendText(message + Environment.NewLine);
                ReceivedDataTextBox.ScrollToEnd();
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            NexusService.Instance.StopListening();
            base.OnClosing(e);
        }

    }
}