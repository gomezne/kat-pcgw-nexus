using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace kat_pcgw_nexus
{
    /// <summary>
    /// Interaction logic for LogWindow.xaml
    /// </summary>
    public partial class LogWindow : Window
    {
        private static readonly string JsonUrl = "https://gatewayservice.katvr.com/api/v1/nexus/lists";

        public LogWindow()
        {
            InitializeComponent();
            Loaded += LogWindow_Loaded; // Attach the event handler
            IsVisibleChanged += LogWindow_IsVisibleChanged;
            Closing += LogWindow_Closing;
        }

        private void LogWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Hide();
            e.Cancel = true;
        }

        private void LogWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
                NexusService.Instance.BroadcastMessageReceived += OnMessageReceived;
            else
                NexusService.Instance.BroadcastMessageReceived -= OnMessageReceived;
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

        private async void LogWindow_Loaded(object sender, RoutedEventArgs e)
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
                var fieldValue = parsedJson.RootElement.GetProperty("data")[0].GetProperty("nexusVersion").GetString() ?? "{Error}";

                var isVerOkay = "";
                if (fieldValue == "2.1.7")
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

        private void LogWindow_Closed(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ReceivedDataTextBox.Text = string.Empty;
            });
        }
    }
}
