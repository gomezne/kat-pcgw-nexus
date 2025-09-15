using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Threading;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Net.NetworkInformation;
using System.Windows.Controls;
using System.Reflection;
using System.Runtime.InteropServices;

namespace kat_pcgw_nexus
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [FlagsAttribute]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
            // Legacy flag, should not be used.
            // ES_USER_PRESENT = 0x00000004
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
        private void DisableSleepState()
        {
            SetThreadExecutionState(EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
            OnMessageReceived("Sleep disabled while application is running.");
        }

        private static readonly string JsonUrl = "https://gatewayservice.katvr.com/api/v1/nexus/lists";

        private void PopulateIpAddressComboBox(string CurrentIp)
        {
            var ipAddresses = GetIpAddressList();
            IpAddressComboBox.ItemsSource = ipAddresses;
            foreach (var ipAddressInfo in ipAddresses)
            {
                if (ipAddressInfo.AddressStr == CurrentIp)
                {
                    IpAddressComboBox.SelectedItem = ipAddressInfo;
                    break;
                }
            }
        }

        private List<IPAddressInfo> GetIpAddressList()
        {
            var list = new List<IPAddressInfo>();
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            list.Add(new IPAddressInfo {
                                AddressStr = unicastAddress.Address.ToString(),
                                AddressObj = unicastAddress.Address,
                                AdapterName = networkInterface.Name,
                            });
                        }
                    }
                }
            }
            return list;
        }

        // Define a simple class to hold IP address information
        public class IPAddressInfo
        {
            public required string AddressStr { get; set; }
            public required IPAddress AddressObj { get; set; }
            public required string AdapterName { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            IpAddressComboBox.SelectionChanged += IpAddressComboBox_SelectionChanged;
            PopulateIpAddressComboBox(NexusService.DetectLocalIPAddress()??"127.0.0.1");
            Loaded += MainWindow_Loaded; // Attach the event handler
            DisableSleepState();
            NexusService.Instance.BroadcastMessageReceived += OnMessageReceived;
            // Retrieve version information
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
            this.Title = $"kat_pcgw_nexus - Version {version}";
        }

        private void IpAddressComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                var selectedAddress = (IPAddressInfo)e.AddedItems[0]!;
                // Call your desired function here, passing the selected IP address
                NexusService.Instance.StopListening();
                NexusService.Instance.StartListening(selectedAddress.AddressObj);
            }
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
                var fieldValue = parsedJson.RootElement.GetProperty("data")[0].GetProperty("nexusVersion").GetString() ?? "{Error}";

                var isVerOkay = "";
                if (fieldValue == "2.1.6" || fieldValue == "2.1.5")
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
