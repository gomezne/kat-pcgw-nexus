using System;

namespace KAT_Helper
{
    public class KATModels
    {
        public string serialNumber { get; set; } = string.Empty;
        public int vid { get; set; }
        public int pid { get; set; }
    }

    public class KATSharedMemory<T> where T : struct
    {
        public KATSharedMemory(string name)
        {
        }

        public T Read() => default;

        public void Write(T value)
        {
        }
    }

    public static class KATSDKInterfaceHelper
    {
        public static KATModels objKATModels { get; } = new KATModels();
        public static KAT_DEVICE_CONNECTION_Model? KAT_DEVICE_CONNECTION { get; set; }
        public static KATSharedMemory<KAT_DRIVER_CONFIG_Model>? KAT_DRIVER_CONFIG_SN { get; set; }
        public static KATSharedMemory<KATInputConfig>? KAT_INPUT_CONFIG { get; set; }
        public static KATSharedMemory<KATInputCalibration>? inputCalibration { get; set; }

        public static bool walk_c2_plus_enhanced_connect { get; set; }
        public static bool walk_c2_connect { get; set; }
        public static bool walk_c2_core_connect { get; set; }
        public static bool walk_c_connect { get; set; }
        public static bool loco_s_connect { get; set; }
        public static bool loco_connect { get; set; }

        public static int ListenCount() => 0;

        public static void GetDeviceConnectionStatus()
        {
        }

        public static KAT_DEVICE_CONNECTION_Model KAT_DEVICE_CONNECTION_Read() => new();

        public static void LEDConst(float intensity)
        {
        }

        public static void VibrateConst(float intensity)
        {
        }

        public static void GetWalkStatus(out TreadMillData data, string serialNumber)
        {
            data = new TreadMillData();
        }

        public struct TreadMillData
        {
            public double lastUpdateTimePoint;
            public DeviceData[]? deviceDatas;
        }

        public struct DeviceData
        {
            public float batteryLevel;
        }

        public struct KAT_DEVICE_CONNECTION_Model
        {
            public float[] sensorStatus;
            public double lastUpdateTime;

            public KAT_DEVICE_CONNECTION_Model()
            {
                sensorStatus = new float[3];
                lastUpdateTime = 0;
            }
        }

        public struct KAT_DRIVER_CONFIG_Model
        {
        }

        public struct KATInputConfig
        {
        }

        public struct KATInputCalibration
        {
        }
    }
}

namespace IBizLibrary
{
    public static class KATSDKInterfaceHelper
    {
        public static KAT_Helper.KATModels objKATModels { get; } = new KAT_Helper.KATModels();

        public static void InitKATSharedMemory()
        {
        }
    }
}

public static class ComUtility
{
    public enum KATDeviceType
    {
        walk_c2_plus_enhanced,
        walk_c2,
        walk_c2_core,
        walk_c,
        loco_s,
        loco
    }

    public static KATDeviceType KATDevice { get; set; }
}

public static class KATCalibrationConfigHelper
{
    public struct CalibrationConfig
    {
    }

    public static void SetCalibrationConfig(string serial, CalibrationConfig config)
    {
    }
}
