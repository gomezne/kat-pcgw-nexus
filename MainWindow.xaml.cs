using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Threading;
using System.IO;
using System.Net.Http;
using System.Text.Json;


namespace kat_pcgw_nexus
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly string JsonUrl = "https://gatewayservice.katvr.com/api/v1/nexus/lists";

        public MainWindow()
        {
            Directory.SetCurrentDirectory("C:\\Program Files (x86)\\KAT Gateway\\");
            InitializeComponent();
            Loaded += MainWindow_Loaded; // Attach the event handler
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

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await FetchAndDisplayJsonAsync();
        }

        private async Task FetchAndDisplayJsonAsync()
        {
            try
            {
                using var client = new HttpClient();
                string json = await client.GetStringAsync(JsonUrl);

                // Optionally parse JSON if you're looking for specific fields
                var parsedJson = JsonDocument.Parse(json);
                var fieldValue = parsedJson.RootElement.GetProperty("data")[0].GetProperty("nexusVersion").GetString();

                var isVerOkay = "";
                if (fieldValue == "2.1.5")
                {
                    isVerOkay = "[All good] ";
                }
                else if (fieldValue.StartsWith("2.1."))
                {
                    isVerOkay = "[Should be OK] ";
                }
                else
                {
                    isVerOkay = "[WARNING] ";
                }

                // Update TextBox
                JsonTextBox.Text = $"{isVerOkay}Upstream Nexus version: {fieldValue}. This application should be good for 2.1.x versions.";
            }
            catch (Exception ex)
            {
                JsonTextBox.Text = $"Error fetching upstream Nexus: {ex.Message}";
            }
        }

    }
}
