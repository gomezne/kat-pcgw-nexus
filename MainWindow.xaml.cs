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
        private LogWindow? logWindow;
        private bool isConnectedState;

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
        }

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
            DisableSleepState();
            NexusService.Instance.IsConnected += Instance_IsConnected;
            // Retrieve version information
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
            this.Title = $"kat_pcgw_nexus - Version {version}";
        }

        private void Instance_IsConnected(bool isConnected)
        {
            if (isConnected != isConnectedState)
            {
                Dispatcher.Invoke(() =>
                {
                    statusBarMsg.Text = isConnected ? "Connected to devices" : "No connected devices";
                });
                isConnectedState = isConnected;
            }
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

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            NexusService.Instance.StopListening();
            base.OnClosing(e);
        }

        private void ViewLogBtn_Click(object sender, RoutedEventArgs e)
        {
            if (logWindow == null)
            {
                logWindow = new LogWindow
                {
                    Owner = this
                };
            }

            if (logWindow.Visibility != Visibility.Visible)
            {
                logWindow.Show();
            }
            else
            {
                logWindow.Activate();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
