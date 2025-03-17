using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Printing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Shapes;
using System.Windows.Xps;
using IBizLibrary;
using KAT_Helper;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace kat_pcgw_nexus
{
    public sealed class NexusService
    {
        private static readonly Lazy<NexusService> instance = new Lazy<NexusService>(() => new NexusService());
        public static NexusService Instance => instance.Value;

        public event Action<string>? BroadcastMessageReceived;

        private string LocalIPAddress = "";
        private IPAddress NetInterface = IPAddress.Any;
        private UdpClient? BroadcastUdpClient;
        private UdpClient? NexusUdpClient;
        private MultimediaTimer.Timer broadcastTimer = new();
        private MultimediaTimer.Timer clientTimer = new();
        private NexusService() {
            System.Environment.SetEnvironmentVariable("PATH", (System.Environment.GetEnvironmentVariable("PATH") ?? "") + ";C:\\Program Files (x86)\\KAT Gateway");

            broadcastTimer.Interval = TimeSpan.FromMilliseconds(1000.0); // Once per second
            broadcastTimer.Resolution = TimeSpan.FromMilliseconds(100); // We don't need high precision there
            broadcastTimer.Elapsed += BroadcastTimer_Elapsed;

            clientTimer.Interval = TimeSpan.FromMilliseconds(1.0); // 1kHz update check timer
            clientTimer.Resolution = TimeSpan.FromMilliseconds(1); // We want this to be reasonably precise
            clientTimer.Elapsed += ClientTimer_Elapsed;
        }

        public static string? DetectLocalIPAddress()
        {
            string hostName = Dns.GetHostName();
            var addresses = Dns.GetHostAddresses(hostName);

            // Filter for an IPv4 address that is not a loopback or link-local address
            var localAddress = addresses.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));

            return localAddress?.ToString();
        }

        public void StartListening(IPAddress network)
        {
            NetInterface = network;
            LocalIPAddress = network.Equals(IPAddress.Any) ? DetectLocalIPAddress() ?? "127.0.0.1" : network.ToString();
            if (BroadcastUdpClient == null)
            {
                BroadcastUdpClient = new UdpClient(new IPEndPoint(NetInterface, 1181));
                BroadcastUdpClient.EnableBroadcast = true;
                BroadcastUdpClient!.BeginReceive(OnBroadcastPacket, null);
                broadcastTimer.Start();
            }
            if (NexusUdpClient == null)
            {
                NexusUdpClient = new UdpClient(new IPEndPoint(NetInterface, 3500));
                NexusUdpClient!.BeginReceive(OnNexusUdpPacket, null);
                // We do not start immediately, we'll start when client knocks on our door
            }
        }

        public void StopListening()
        {
            broadcastTimer.Stop();
            clientTimer.Stop();

            BroadcastUdpClient?.Close();
            BroadcastUdpClient = null;

            NexusUdpClient?.Close();
            NexusUdpClient = null;
        }

        //---------------------------------------------------------
        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 20)]
        public struct KAT_NEXUS_PACKET
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string nexusIPv4;
            [MarshalAs(UnmanagedType.U4)]
            public int devicesCount;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 116)]
        public struct KAT_NEXUS_DEVICE
        {
            public double lastUpdate;
            public ushort pid;
            public ushort vid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
            public string serialNo;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.I1, SizeConst = 3)]
            public bool[] sensorConnected;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] sensorBattery;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] unk2;  // ??
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public int[] sensorPackets;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string clientIPv4;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] unk3;  // ??
            public int nexusPort;
            public byte numClientPorts;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public int[] clientPorts;

            public KAT_NEXUS_DEVICE Initialize()
            {
                sensorConnected = new bool[3];
                sensorBattery = new float[3];
                sensorPackets = new int[3];
                clientPorts = new int[8];
                return this;
            }
        }
        private static T CastArray<T>(byte[] array, int offset) where T : struct
        {
            // return MemoryMarshal.Cast<byte, T>(array.AsSpan(offset, Marshal.SizeOf<T>()))[0];
            GCHandle gch = GCHandle.Alloc(array, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(gch.AddrOfPinnedObject() + offset);
            }
            finally
            {
                gch.Free();
            }
        }

        static T ReadPtrStructAndAdvance<T>(ref IntPtr ptr) where T : struct
        {
            var result = Marshal.PtrToStructure<T>(ptr);
            ptr += Marshal.SizeOf<T>();
            return result;
        }
        static void WriteStructToPtrAndAdvance<T>(T data, ref IntPtr ptr) where T : struct
        {
            Marshal.StructureToPtr<T>(data, ptr, false);
            ptr += Marshal.SizeOf<T>();
        }

        /*
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TREADMILL_SERIAL
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 13)]
            public string serialNumber;
        }

        [DllImport("KATDeviceSDK.dll")]
        public static extern void GetPrimeTreadMill(ref TREADMILL_SERIAL result);
        
        TREADMILL_SERIAL sn = new();
        GetPrimeTreadMill(ref sn);
        */


        private void OnBroadcastPacket(IAsyncResult ar)
        {
            try
            {
                IPEndPoint? remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                if (BroadcastUdpClient == null)
                    return;
                byte[] receivedBytes = BroadcastUdpClient!.EndReceive(ar, ref remoteEndPoint);
                BroadcastUdpClient!.BeginReceive(OnBroadcastPacket, null);

                string message;

                if (receivedBytes.Length < 2 || receivedBytes[0] != 0x84 || receivedBytes[1] != 0x11)
                {
                    message = "ERR: Invalid packet";
                } else
                {
                    GCHandle gch = GCHandle.Alloc(receivedBytes, GCHandleType.Pinned);
                    try
                    {
                        var ptr = gch.AddrOfPinnedObject() + 2;
                        var header = ReadPtrStructAndAdvance<KAT_NEXUS_PACKET>(ref ptr);
                        message = "Found Nexus: " + header.nexusIPv4 + " (" + header.devicesCount + ")";
                        for (int i = 0; i < header.devicesCount; i++)
                        {
                            var dev = ReadPtrStructAndAdvance<KAT_NEXUS_DEVICE>(ref ptr);
                            var problem = "";
                            if (dev.sensorPackets[0] < 50) problem += ":dir";
                            if (dev.sensorPackets[1] < 50) problem += ":left";
                            if (dev.sensorPackets[2] < 50) problem += ":right";
                            problem = (problem=="") ? "OK" : ("no signal" + problem);
                            message += $"[:{dev.nexusPort} {dev.serialNo} @ {dev.lastUpdate} @ ({dev.sensorPackets[0]}:{dev.sensorPackets[1]}:{dev.sensorPackets[2]}:{problem})]";
                        }
                    }
                    finally
                    {
                        gch.Free();
                    }
                }

                BroadcastMessageReceived?.Invoke(message);
            }
            catch (ObjectDisposedException)
            {
                // Handle clean-up when udpClient is disposed
            }
            catch (SocketException)
            {
                // M'kay5
            }
        }

        string TreadmillSn = ""; // The serial No of treadmill we connected to
        IPAddress? ClientAddress;
        List<int> ClientPorts = new(8);
        List<double> LastPings = new(8);
        KATSDKInterfaceHelper.TreadMillData LastUpdateData = new();
        Int64 GoodPackets = 0;
        Int64 BadPackets = 0;
        Int64 UpdatePackets = 0;
        double firstUpdate = 0.0;

        public static double GetCurrentTimeDNative() => (DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10_000_000.0;

        private void BroadcastTimer_Elapsed(object? sender, EventArgs e)
        {
            var nexus = new KAT_NEXUS_PACKET { devicesCount = 0, nexusIPv4 = LocalIPAddress };
            var dev = new KAT_NEXUS_DEVICE { }.Initialize();

            if (!Monitor.TryEnter(this))
            {
                return;
            }
            try {
                if (KATSDKInterfaceHelper.KAT_DEVICE_CONNECTION == null ||
                    IBizLibrary.KATSDKInterfaceHelper.objKATModels.serialNumber == null)
                {
                    if (KATSDKInterfaceHelper.ListenCount() > 0)
                    {
                        KATSDKInterfaceHelper.GetDeviceConnectionStatus();

                        if (KATSDKInterfaceHelper.walk_c2_connect)
                        {
                            ComUtility.KATDevice = ComUtility.KATDeviceType.walk_c2;
                            nexus.devicesCount = 1;
                        }
                        else if (KATSDKInterfaceHelper.walk_c2_core_connect)
                        {
                            ComUtility.KATDevice = ComUtility.KATDeviceType.walk_c2_core;
                            nexus.devicesCount = 1;
                        }
                        else if (KATSDKInterfaceHelper.walk_c_connect)
                        {
                            ComUtility.KATDevice = ComUtility.KATDeviceType.walk_c;
                            nexus.devicesCount = 1;
                        }
                        else if (KATSDKInterfaceHelper.loco_s_connect)
                        {
                            ComUtility.KATDevice = ComUtility.KATDeviceType.loco_s;
                            nexus.devicesCount = 1;
                        }
                        else if (KATSDKInterfaceHelper.loco_connect)
                        {
                            ComUtility.KATDevice = ComUtility.KATDeviceType.loco;
                            nexus.devicesCount = 1;
                        }
                    }
                    if (nexus.devicesCount > 0)
                    {
                        KATSDKInterfaceHelper.GetDeviceConnectionStatus();

                        if (TreadmillSn != IBizLibrary.KATSDKInterfaceHelper.objKATModels.serialNumber
                            || KATSDKInterfaceHelper.KAT_DEVICE_CONNECTION == null)
                        {
                            IBizLibrary.KATSDKInterfaceHelper.InitKATSharedMemory();
                        }
                        TreadmillSn = IBizLibrary.KATSDKInterfaceHelper.objKATModels.serialNumber;
                    }
                }
                else
                {
                    nexus.devicesCount = 1;
                }
            }
            finally
            {
                Monitor.Exit(this);
            }

            if (nexus.devicesCount > 0) {
                dev.serialNo = TreadmillSn;
                dev.numClientPorts = (byte)ClientPorts.Count;
                ClientPorts.CopyTo(dev.clientPorts);
                dev.clientIPv4 = ClientAddress?.ToString() ?? "";
                dev.vid = (ushort)IBizLibrary.KATSDKInterfaceHelper.objKATModels.vid;
                dev.pid = (ushort)IBizLibrary.KATSDKInterfaceHelper.objKATModels.pid;
                dev.nexusPort = 3500;
                KATSDKInterfaceHelper.KAT_DEVICE_CONNECTION_Model deviceConnectionModel = KATSDKInterfaceHelper.KAT_DEVICE_CONNECTION_Read();
                dev.sensorPackets[0] = Convert.ToInt32(deviceConnectionModel.sensorStatus[0]);
                dev.sensorPackets[1] = Convert.ToInt32(deviceConnectionModel.sensorStatus[1]);
                dev.sensorPackets[2] = Convert.ToInt32(deviceConnectionModel.sensorStatus[2]);
                dev.sensorConnected[0] = deviceConnectionModel.sensorStatus[0] > 0;
                dev.sensorConnected[1] = deviceConnectionModel.sensorStatus[1] > 0;
                dev.sensorConnected[2] = deviceConnectionModel.sensorStatus[2] > 0;
                if (LastUpdateData.deviceDatas != null)
                {
                    dev.sensorBattery[0] = LastUpdateData.deviceDatas[0].batteryLevel;
                    dev.sensorBattery[1] = LastUpdateData.deviceDatas[1].batteryLevel;
                    dev.sensorBattery[2] = LastUpdateData.deviceDatas[2].batteryLevel;
                }
                if (firstUpdate == 0 && deviceConnectionModel.lastUpdateTime > 0)
                {
                    firstUpdate = deviceConnectionModel.lastUpdateTime;
                }
                dev.lastUpdate = deviceConnectionModel.lastUpdateTime - firstUpdate;
            }
            else
            {
                firstUpdate = 0;
            }

            var buffer = new byte[512];
            buffer[0] = 0x84;
            buffer[1] = 0x11;
            GCHandle gch = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            int length = 0;
            try
            {
                var ptr = gch.AddrOfPinnedObject() + 2;
                WriteStructToPtrAndAdvance(nexus, ref ptr);
                if (nexus.devicesCount > 0)
                {
                    WriteStructToPtrAndAdvance(dev, ref ptr);
                }
                length = (int)(ptr - gch.AddrOfPinnedObject());

                BroadcastUdpClient?.Send(buffer, length, new IPEndPoint(IPAddress.Broadcast, 1181));
            }
            finally
            {
                gch.Free();
            }
        }

        private void OnNexusUdpPacket(IAsyncResult ar)
        {
            try
            {
                if (NexusUdpClient == null)
                    return;
                IPEndPoint? remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] receivedBytes = NexusUdpClient!.EndReceive(ar, ref remoteEndPoint);
                if (remoteEndPoint == null)
                    return;
                NexusUdpClient!.BeginReceive(OnNexusUdpPacket, null);

                HandleNexusCmd(receivedBytes, remoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                // Handle clean-up when udpClient is disposed
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Disconnected? okay, such is life
            }
        }

        private enum NexusCmdByte
        {
            Ping = 0x00,
            Pong = 0x01,
            SysConfig = 0x0A,
            Connect = 0x14,
            ConnectResult = 0x15,
            Reset = 0x16,
            Disconnect = 0x17,
            SetHaptic = 0x50,
            SetLED = 0x51,
            WalkStatus = 0x63,
        };
        private enum NexusCommands
        {
            Ping = (byte)NexusCmdByte.Ping,
            Pong = (byte)NexusCmdByte.Pong,
            SysConfig = (byte)NexusCmdByte.SysConfig,
            Connect = (byte)NexusCmdByte.Connect,
            Disconnect = (byte)NexusCmdByte.Disconnect,
            SetHaptic = (byte)NexusCmdByte.SetHaptic,
            SetLED = (byte)NexusCmdByte.SetLED,
        };
        private void HandleNexusCmd(byte[] receivedBytes, IPEndPoint remoteEndPoint)
        {
            // Non-broadcast packets uses 84 11 instead of 0x84 0x11
            if (receivedBytes.Length < 3 || receivedBytes[0] != 84 || receivedBytes[1] != 11)
            {
                BadPackets++;
                return;
            }
            // Debug.WriteLine("Nexus CMD: " + receivedBytes.Length + " @ " + ((NexusCommands)(receivedBytes[2])).ToString());
            var handled = (NexusCommands)receivedBytes[2] switch {
                NexusCommands.Ping => Cmd_ClientPing(receivedBytes, remoteEndPoint),
                NexusCommands.Pong => true, // Cmd_ClientPong(receivedBytes, remoteEndPoint),
                NexusCommands.Connect => Cmd_ClientConnect(receivedBytes, remoteEndPoint),
                NexusCommands.Disconnect => Cmd_ClientDisconnect(receivedBytes, remoteEndPoint),
                NexusCommands.SysConfig => Cmd_ClientSysConfig(receivedBytes, remoteEndPoint),
                NexusCommands.SetHaptic => Cmd_SetHaptic(receivedBytes, remoteEndPoint),
                NexusCommands.SetLED => Cmd_SetLED(receivedBytes, remoteEndPoint),
                _ => false
            };
            if (handled) { GoodPackets++; }
            else { BadPackets ++; }
        }

        private bool Cmd_SetLED(byte[] receivedBytes, IPEndPoint remoteEndPoint)
        {
            if (receivedBytes.Length < 11) return false;
            double intensity = BitConverter.ToDouble(receivedBytes, 3);
            KATSDKInterfaceHelper.LEDConst((float)intensity);
            return true;
        }

        private bool Cmd_SetHaptic(byte[] receivedBytes, IPEndPoint remoteEndPoint)
        {
            if (receivedBytes.Length < 11) return false;
            double intensity = BitConverter.ToDouble(receivedBytes, 3);
            KATSDKInterfaceHelper.VibrateConst((float)intensity);
            return true;
        }

        private bool Cmd_ClientSysConfig(byte[] receivedBytes, IPEndPoint remoteEndPoint)
        {
            if (receivedBytes.Length < 260) return false;
            GCHandle gch = GCHandle.Alloc(receivedBytes, GCHandleType.Pinned);
            try
            {
                // offset 0x75 (+117) -- checked for asciiz serialNo to match the connection
                string? configSn = Marshal.PtrToStringAnsi(gch.AddrOfPinnedObject() + 117);
                if (configSn != TreadmillSn) return false;

                InitSharedMemory();

                // offset 0x03 (+  3) -- KAT_DRIVER_CONFIG_ struct
                if (KATSDKInterfaceHelper.KAT_DRIVER_CONFIG_SN != null)
                {
                    var ptr = gch.AddrOfPinnedObject() + 3;
                    var driverConfig = ReadPtrStructAndAdvance<KATSDKInterfaceHelper.KAT_DRIVER_CONFIG_Model>(ref ptr);
                    if (ptr - gch.AddrOfPinnedObject() != 70) return false;
                    KATSDKInterfaceHelper.KAT_DRIVER_CONFIG_SN.Write(driverConfig);
                }

                // offset 0x46 (+ 70) -- KAT_INPUT_CONFIG_ struct
                if (KATSDKInterfaceHelper.KAT_INPUT_CONFIG != null)
                {
                    var ptr = gch.AddrOfPinnedObject() + 70;
                    var inputConfig = ReadPtrStructAndAdvance<KATSDKInterfaceHelper.KATInputConfig>(ref ptr);
                    if (ptr - gch.AddrOfPinnedObject() != 88) return false;
                    KATSDKInterfaceHelper.KAT_INPUT_CONFIG.Write(inputConfig);
                }

                // offset 0x58 (+ 88) -- KAT_DEVICE_CALIBRATION_CONFIG_ struct
                // SKIPPED -- looks like for KatWalkC, not C2/C2Core
                try
                {
                    if (KATSDKInterfaceHelper.walk_c_connect)
                    {
                        var ptr = gch.AddrOfPinnedObject() + 88;
                        var calibrationConfig = ReadPtrStructAndAdvance<KATCalibrationConfigHelper.CalibrationConfig>(ref ptr);
                        if (ptr - gch.AddrOfPinnedObject() != 109) return false;
                        KATCalibrationConfigHelper.SetCalibrationConfig(TreadmillSn, calibrationConfig);
                    }
                }
                catch (Exception e) {
                    // well, i don't know what to do here, so ignore.
                }

                // offset 0x6d (+109) -- KAT_CALIBRATIONDATA_ struct
                if (KATSDKInterfaceHelper.inputCalibration != null)
                {
                    var ptr = gch.AddrOfPinnedObject() + 109;
                    var inputCalibration = ReadPtrStructAndAdvance<KATSDKInterfaceHelper.KATInputCalibration>(ref ptr);
                    if (ptr - gch.AddrOfPinnedObject() != 117) return false;
                    KATSDKInterfaceHelper.inputCalibration.Write(inputCalibration);
                    
                }

                return true;
            }
            finally
            {
                gch.Free();
            }

        }

        private void InitSharedMemory()
        {
            if (KATSDKInterfaceHelper.objKATModels.serialNumber == "")
                return;
            if (KATSDKInterfaceHelper.KAT_DRIVER_CONFIG_SN == null)
                KATSDKInterfaceHelper.KAT_DRIVER_CONFIG_SN = new KATSharedMemory<KATSDKInterfaceHelper.KAT_DRIVER_CONFIG_Model>("KAT_DRIVER_CONFIG_" + KATSDKInterfaceHelper.objKATModels.serialNumber);
        }

        private bool Cmd_ClientDisconnect(byte[] receivedBytes, IPEndPoint remoteEndPoint)
        {
            if (receivedBytes.Length < 7) return false;
            if (ClientAddress?.Equals(remoteEndPoint.Address) != true)
                return false;

            var port = BitConverter.ToInt32(receivedBytes, 3);
            var idx = ClientPorts.IndexOf(port);
            if (idx < 0)
                return false;

            if (ClientPorts.Count == 1)
            {
                ClientPorts.Clear();
                LastPings.Clear();
                ClientAddress = null;
                return true;
            }

            ClientPorts.RemoveAt(idx);
            LastPings.RemoveAt(idx);
            return true;
        }

        private bool Cmd_ClientConnect(byte[] receivedBytes, IPEndPoint remoteEndPoint)
        {
            if (receivedBytes.Length < 8) return false;
            var port = BitConverter.ToInt32(receivedBytes, 3);
            var force = BitConverter.ToBoolean(receivedBytes, 7);
            var connected = false;
            var status = (byte)0;
            var msg = "";

            if (ClientAddress != null && !ClientAddress.Equals(remoteEndPoint.Address))
            {
                if (!force)
                {
                    connected = false;
                    status = 11;
                    msg = "Reject: busy";
                }
                else
                {
                    // Disconnect current client
                    byte[] disconnection = { 84, 11, (byte)NexusCmdByte.Reset, 1, 0 };
                    SendAllClients(disconnection);
                    ClientAddress = null;
                }
            }

            var now = GetCurrentTimeDNative();
            if (ClientAddress == null)
            {
                ClientAddress = remoteEndPoint.Address;
                ClientPorts.Clear();
                ClientPorts.Add(port);
                LastPings.Clear();
                LastPings.Add(now);
                connected = true;
                status = 0;
                msg = TreadmillSn;
                clientTimer.Start();
            }
            else if (ClientAddress.Equals(remoteEndPoint.Address))
            {
                if (ClientPorts.Contains(port))
                {
                    connected = true;
                    status = 3; // already connected
                    msg = TreadmillSn;
                }
                else if (ClientPorts.Count < 8)
                {
                    ClientPorts.Add(port);
                    LastPings.Add(now);
                    connected = true;
                    status = 0;
                    msg = TreadmillSn;
                }
                else
                {
                    connected = false;
                    status = 2;
                    msg = "Reject: limit";
                }
            }

            byte[] msgBytes = Encoding.ASCII.GetBytes(msg);
            byte[] connection = new byte[260];
            connection[0] = 84;
            connection[1] = 11;
            connection[2] = (byte)NexusCmdByte.ConnectResult;
            connection[3] = (byte)(connected ? 0 : 1);
            connection[4] = status;
            BitConverter.GetBytes((int)3500).CopyTo(connection, 5);
            msgBytes.CopyTo(connection, 9);
            connection[9 + msg.Length] = 0; // Well, it's zero already, but won't hurt :D
            Debug.WriteLine($"Connection result: {connected}/{status}/{msg}. Current connection: {ClientAddress.ToString()}:[{string.Join(",", ClientPorts)}]");
            var sent = NexusUdpClient?.Send(connection, new IPEndPoint(ClientAddress, port));
            return connected && (sent == connection.Length);
        }

        private void SendAllClients(byte[] packet)
        {
            if (ClientAddress != null)
            {
                foreach (var port in ClientPorts)
                {
                    NexusUdpClient!.Send(packet, new IPEndPoint(ClientAddress, port));
                }
            }
        }

        private bool Cmd_ClientPing(byte[] receivedBytes, IPEndPoint remoteEndPoint)
        {
            if (receivedBytes.Length < 15) return false;
            if (receivedBytes.Length < 259) Array.Resize(ref receivedBytes, 259);
            var clientPort = BitConverter.ToInt32(receivedBytes, 11);
            var now = GetCurrentTimeDNative();
            BitConverter.GetBytes(now).CopyTo(receivedBytes, 11);

            if (ClientAddress == null) {
                // If there is no active client: autoconnect it
                ClientAddress = remoteEndPoint.Address;
                ClientPorts.Clear();
                ClientPorts.Add(clientPort);
                LastPings.Clear();
                LastPings.Add(now);
                receivedBytes[2] = (Byte)NexusCmdByte.Pong;
            }
            else if (!ClientAddress.Equals(remoteEndPoint.Address))
            {
                // Ping from wrong address: stale client? reset their connection
                receivedBytes[2] = (byte)NexusCmdByte.Reset;
                receivedBytes[3] = 2; // Not connected
            }
            else
            {
                var idx = ClientPorts.IndexOf(clientPort);
                if (idx == -1)
                {
                    if (ClientPorts.Count < 8)
                    {
                        ClientPorts.Add(clientPort);
                        LastPings.Add(now);
                        receivedBytes[2] = (Byte)NexusCmdByte.Pong;
                    }
                    else
                    {
                        // Ping from unknown port and we don't have space: sorry, reset.
                        receivedBytes[2] = (byte)NexusCmdByte.Reset;
                        receivedBytes[3] = 2; // Not connected
                    }
                }
                else
                {
                    LastPings[idx] = now;
                    receivedBytes[2] = (Byte)NexusCmdByte.Pong;
                }
            }
            var sent = NexusUdpClient?.Send(receivedBytes, new IPEndPoint(remoteEndPoint.Address, clientPort));
            return (receivedBytes[2] == (Byte)NexusCmdByte.Pong) && (sent == receivedBytes.Length);
        }

        private void ClientTimer_Elapsed(object? sender, EventArgs e)
        {
            if (ClientAddress == null)
            {
                clientTimer.Stop();
                UpdatePackets = 0;
                return;
            }
            var lastTs = LastUpdateData.lastUpdateTimePoint;
            KATSDKInterfaceHelper.GetWalkStatus(out LastUpdateData, TreadmillSn);
            if (LastUpdateData.lastUpdateTimePoint != lastTs)
            {
                double now = GetCurrentTimeDNative();
                for (int i = ClientPorts.Count - 1; i >= 0; i--)
                {
                    if (now - LastPings[i] > 5.0d)
                    {
                        ClientPorts.RemoveAt(i);
                        LastPings.RemoveAt(i);
                    }
                }
                if (ClientPorts.Count == 0)
                {
                    ClientAddress = null;
                    return;
                }
                byte[] packet = new byte[11 + Marshal.SizeOf<KATSDKInterfaceHelper.TreadMillData>()];
                packet[0] = 84;
                packet[1] = 11;
                packet[2] = 99;
                BitConverter.GetBytes(UpdatePackets).CopyTo(packet, 3);
                GCHandle gch = GCHandle.Alloc(packet, GCHandleType.Pinned);
                try
                {
                    var ptr = gch.AddrOfPinnedObject() + 11;
                    WriteStructToPtrAndAdvance(LastUpdateData, ref ptr);
                }
                finally
                {
                    gch.Free();
                }
                SendAllClients(packet);
                UpdatePackets++;
            }
        }


    }
}
 