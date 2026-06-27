using static LowgiHID.HidApi.HidApi;
using static LowgiHID.HidApi.HidApiWinApi;
using static LowgiHID.HidApi.HidApiHotPlug;
using LowgiHID.HidApi;
using System.Collections.Concurrent;
using LowgiPrimitives;
using LowgiPrimitives.MessageStructs;
using System.Text;

namespace LowgiHID
{
    public sealed class HidppManagerContext
    {
        public static readonly HidppManagerContext _instance = new();
        public static HidppManagerContext Instance => _instance;

        private readonly Dictionary<string, Guid> _containerMap = [];
        private readonly Dictionary<Guid, HidppDevices> _deviceMap = [];
        private readonly object _deviceMapLock = new();
        private readonly BlockingCollection<HidDeviceSnapshot> _deviceQueue = [];
        private readonly Delegate _deviceArrivedCallback;
        private readonly Delegate _deviceLeftCallback;
        private HidHotPlugCallbackHandle _deviceArrivedCallbackHandle;
        private HidHotPlugCallbackHandle _deviceLeftCallbackHandle;

        public delegate void HidppDeviceEventHandler(IPCMessageType messageType, IPCMessage message);

        public event HidppDeviceEventHandler? HidppDeviceEvent;

        private unsafe HidppManagerContext()
        {
            _deviceArrivedCallback = new HidApiHotPlugEventCallbackFn(EnqueueDevice);
            _deviceLeftCallback = new HidApiHotPlugEventCallbackFn(DeviceLeft);
        }

        static HidppManagerContext()
        {
            _ = HidInit();
        }

        public void SignalDeviceEvent(IPCMessageType messageType, IPCMessage message)
        {
            HidppDeviceEvent?.Invoke(messageType, message);
        }

        private unsafe int EnqueueDevice(HidHotPlugCallbackHandle _, HidDeviceInfo* device, HidApiHotPlugEvent hidApiHotPlugEvent, nint __)
        {
            try
            {
                if (hidApiHotPlugEvent == HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED)
                {
                    _deviceQueue.Add(HidDeviceSnapshot.From(*device));
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "HID hotplug arrive callback");
            }

            return 0;
        }

        private async Task<int> InitDevice(HidDeviceSnapshot deviceInfo)
        {
            var messageType = deviceInfo.MessageType;
            LogHidDevice("ARRIVE", deviceInfo, messageType);
            switch (messageType)
            {
                case HidppMessageType.NONE:
                case HidppMessageType.VERY_LONG:
                    return 0;
            }

            string devPath = deviceInfo.Path;

            HidDevicePtr dev = HidOpenPath(devPath);
            if (dev == IntPtr.Zero)
            {
                return 0;
            }

            _ = HidWinApiGetContainerId(dev, out Guid containerId);

#if DEBUG
            Console.WriteLine(devPath);
            Console.WriteLine(containerId.ToString());
            Console.WriteLine("x{0:X04}", (deviceInfo).Usage);
            Console.WriteLine("x{0:X04}", (deviceInfo).UsagePage);
            Console.WriteLine();
#endif

            HidppDevices value;
            lock (_deviceMapLock)
            {
                if (!_deviceMap.TryGetValue(containerId, out value!))
                {
                    value = new();
                    _deviceMap[containerId] = value;
                }

                _containerMap[devPath] = containerId;
            }

            try
            {
                switch (messageType)
                {
                    case HidppMessageType.SHORT:
                        await value.SetDevShort(dev);
                        break;
                    case HidppMessageType.LONG:
                        await value.SetDevLong(dev);
                        break;
                }
            }
            catch
            {
                HidClose(dev);
            }

            return 0;
        }

        private unsafe int DeviceLeft(HidHotPlugCallbackHandle callbackHandle, HidDeviceInfo* deviceInfo, HidApiHotPlugEvent hidApiHotPlugEvent, nint userData)
        {
            try
            {
                HidDeviceSnapshot snapshot = HidDeviceSnapshot.From(*deviceInfo);
                LogHidDevice("LEFT", snapshot, snapshot.MessageType);
                string devPath = (*deviceInfo).GetPath();

                lock (_deviceMapLock)
                {
                    if (_containerMap.TryGetValue(devPath, out var containerId))
                    {
                        if (_deviceMap.TryGetValue(containerId, out var device))
                        {
                            device.Dispose();
                            _deviceMap.Remove(containerId);
                        }

                        foreach (var mappedPath in _containerMap.Where(x => x.Value == containerId).Select(x => x.Key).ToArray())
                        {
                            _containerMap.Remove(mappedPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "HID hotplug left callback");
            }

            return 0;
        }

        public void Start(CancellationToken cancellationToken)
        {
            new Thread(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    HidDeviceSnapshot dev;
                    try
                    {
                        dev = _deviceQueue.Take(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    try
                    {
                        await InitDevice(dev);
                    }
                    catch (Exception ex)
                    {
                        CrashLog.Write(ex, "HID device queue");
                    }
                }
            }).Start();

            unsafe
            {
                HidHotPlugCallbackHandle arrivedHandle = 0;
                HidHotPlugCallbackHandle leftHandle = 0;

                HidHotplugRegisterCallback(
                    0x046D,
                    0x00,
                    HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_ARRIVED,
                    HidApiHotPlugFlag.HID_API_HOTPLUG_ENUMERATE,
                    (HidApiHotPlugEventCallbackFn)_deviceArrivedCallback,
                    IntPtr.Zero,
                    &arrivedHandle);

                HidHotplugRegisterCallback(
                    0x046D,
                    0x00,
                    HidApiHotPlugEvent.HID_API_HOTPLUG_EVENT_DEVICE_LEFT,
                    HidApiHotPlugFlag.NONE,
                    (HidApiHotPlugEventCallbackFn)_deviceLeftCallback,
                    IntPtr.Zero,
                    &leftHandle);

                _deviceArrivedCallbackHandle = arrivedHandle;
                _deviceLeftCallbackHandle = leftHandle;
                CrashLog.WriteRunEvent($"hid hotplug callbacks registered arrive={_deviceArrivedCallbackHandle} left={_deviceLeftCallbackHandle}");
            }
        }

        public unsafe void RediscoverDevices()
        {
            lock (_deviceMapLock)
            {
                foreach (var device in _deviceMap.Values)
                {
                    device.Dispose();
                }

                _deviceMap.Clear();
                _containerMap.Clear();
            }

            HidDeviceInfo* devices = HidEnumerate(0x046D, 0x00);
            try
            {
                for (HidDeviceInfo* device = devices; device != null; device = device->Next)
                {
                    HidppMessageType messageType = (*device).GetHidppMessageType();
                    if (messageType is HidppMessageType.SHORT or HidppMessageType.LONG)
                    {
                        _deviceQueue.Add(HidDeviceSnapshot.From(*device, messageType));
                    }
                }
            }
            finally
            {
                HidFreeEnumeration(devices);
            }
        }
    
        public async Task ForceBatteryUpdates()
        {
            HidppDevices[] devices;
            lock (_deviceMapLock)
            {
                devices = _deviceMap.Values.ToArray();
            }

            foreach (var hidppDevice in devices)
            {
                var tasks = hidppDevice.DeviceCollection
                    .Select(x => x.Value)
                    .Select(x => x.UpdateBattery(true));

                await Task.WhenAll(tasks);
            }
        }

        private static void LogHidDevice(string action, HidDeviceSnapshot deviceInfo, HidppMessageType messageType)
        {
            if (!CrashLog.Enabled)
            {
                return;
            }

            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "hid_devices.log");
                string line = string.Join('\t',
                    DateTimeOffset.Now.ToString("O"),
                    action,
                    $"vid=0x{deviceInfo.VendorId:X4}",
                    $"pid=0x{deviceInfo.ProductId:X4}",
                    $"usagePage=0x{deviceInfo.UsagePage:X4}",
                    $"usage=0x{deviceInfo.Usage:X4}",
                    $"interface={deviceInfo.InterfaceNumber}",
                    $"bus={deviceInfo.BusType}",
                    $"type={messageType}");

                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private readonly record struct HidDeviceSnapshot(
            string Path,
            ushort VendorId,
            ushort ProductId,
            ushort UsagePage,
            ushort Usage,
            int InterfaceNumber,
            HidBusType BusType,
            HidppMessageType MessageType)
        {
            public static HidDeviceSnapshot From(HidDeviceInfo deviceInfo)
            {
                return From(deviceInfo, deviceInfo.GetHidppMessageType());
            }

            public static HidDeviceSnapshot From(HidDeviceInfo deviceInfo, HidppMessageType messageType)
            {
                return new HidDeviceSnapshot(
                    deviceInfo.GetPath(),
                    deviceInfo.VendorId,
                    deviceInfo.ProductId,
                    deviceInfo.UsagePage,
                    deviceInfo.Usage,
                    deviceInfo.InterfaceNumber,
                    deviceInfo.BusType,
                    messageType);
            }
        }

        public async Task ApplyLedMode(LogiLedMode ledMode, int lowBatteryThreshold, IEnumerable<string> selectedDeviceIds, bool refreshBattery = true, bool refreshLed = false)
        {
            var selectedDevices = selectedDeviceIds.ToHashSet(StringComparer.Ordinal);
            HidppDevices[] devices;
            lock (_deviceMapLock)
            {
                devices = _deviceMap.Values.ToArray();
            }

            foreach (var hidppDevice in devices)
            {
                var hidppDevices = hidppDevice.DeviceCollection.Select(x => x.Value).ToArray();
                bool hasSelectedMatch = hidppDevices.Any(x => selectedDevices.Contains(x.Identifier));
                var tasks = hidppDevice.DeviceCollection
                    .Select(x => x.Value)
                    .Select(x => x.ApplyLedMode(
                        ledMode,
                        lowBatteryThreshold,
                        !hasSelectedMatch || selectedDevices.Contains(x.Identifier),
                        refreshBattery,
                        refreshLed));

                await Task.WhenAll(tasks);
            }
        }
    }
}
