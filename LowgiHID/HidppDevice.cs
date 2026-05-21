using LowgiPrimitives;
using LowgiPrimitives.MessageStructs;
using LowgiHID.Features;
using System.Text;

using static LowgiHID.HidppDevices;

#if DEBUG
using Log = System.Console;
#else
using Log = System.Diagnostics.Debug;
#endif

namespace LowgiHID
{
    public class HidppDevice
    {
        private readonly SemaphoreSlim _initSemaphore = new(1, 1);
        private Func<HidppDevice, Task<BatteryUpdateReturn?>>? _getBatteryAsync;
        private byte? _ledFeatureIndex;
        private ushort? _ledFeatureId;
        private int _ledZoneCount;
        private readonly List<LedZone> _ledZones = [];

        private enum LedFeatureKind
        {
            ColorLedEffects,
            RgbEffects,
        }

        private sealed record LedEffect(byte Index, ushort Id);
        private sealed record LedZone(byte Index, IReadOnlyList<LedEffect> Effects);

        public string DeviceName { get; private set; } = string.Empty;
        public int DeviceType { get; private set; } = 3;
        public string Identifier { get; private set; } = string.Empty;

        private BatteryUpdateReturn lastBatteryReturn;
        private bool hasBatteryReturn;
        private DateTimeOffset lastUpdate = DateTimeOffset.MinValue;

        private readonly HidppDevices _parent;
        public HidppDevices Parent => _parent;

        private readonly byte _deviceIdx;
        public byte DeviceIdx => _deviceIdx;

        private readonly Dictionary<ushort, byte> _featureMap = [];
        public Dictionary<ushort, byte> FeatureMap => _featureMap;

        public HidppDevice(HidppDevices parent, byte deviceIdx)
        {
            _parent = parent;
            _deviceIdx = deviceIdx;
        }

        public async Task InitAsync()
        {
            await _initSemaphore.WaitAsync();
            try
            {
                Hidpp20 ret;

                // Sync Ping
                int successCount = 0;
                int successThresh = 3;
                for (int i = 0; i < 10; i++)
                {
                    var ping = await _parent.Ping20(_deviceIdx, 100);
                    if (ping)
                    {
                        successCount++;
                    }
                    else
                    {
                        successCount = 0;
                    }

                    if (successCount >= successThresh) { break; }
                }

                if (successCount < successThresh) { return; }

                // Find 0x0001 IFeatureSet
                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, 0x00, 0x00 | SW_ID, 0x00, 0x01, 0x00 });
                _featureMap[0x0001] = ret.GetParam(0);

                // Get Feature Count
                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, _featureMap[0x0001], 0x00 | SW_ID, 0x00, 0x00, 0x00 });
                int featureCount = ret.GetParam(0);

                // Enumerate Features
                for (byte i = 0; i <= featureCount; i++)
                {
                    ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, _featureMap[0x0001], 0x10 | SW_ID, i, 0x00, 0x00 });
                    ushort featureId = (ushort)((ret.GetParam(0) << 8) + ret.GetParam(1));

                    _featureMap[featureId] = i;
                }

                await InitPopulateAsync();
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0018:Inline variable declaration")]
        private async Task InitPopulateAsync()
        {
            Hidpp20 ret;
            byte featureId;

            // Device name
            if (_featureMap.TryGetValue(0x0005, out featureId))
            {
                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x00 | SW_ID, 0x00, 0x00, 0x00 });
                int nameLength = ret.GetParam(0);

                string name = "";

                while (name.Length < nameLength)
                {
                    ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x10 | SW_ID, (byte)name.Length, 0x00, 0x00 });
                    name += Encoding.UTF8.GetString(ret.GetParams());
                }

                DeviceName = name.TrimEnd('\0');

                foreach (var tag in GlobalSettings.settings.DisabledDevices)
                {
                    if (DeviceName.Contains(tag))
                    {
                        Log.WriteLine($"{DeviceName} is marked as disabled");
                        return;
                    }
                };

                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x20 | SW_ID, 0x00, 0x00, 0x00 });
                DeviceType = ret.GetParam(0);
            }
            else
            {
                // Device does not have a name/Hidpp error ignore it
                return;
            }

            if (_featureMap.TryGetValue(0x0003, out featureId))
            {
                ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x00 | SW_ID, 0x00, 0x00, 0x00 });

                string unitId = BitConverter.ToString(ret.GetParams().ToArray(), 1, 4).Replace("-", string.Empty);
                string modelId = BitConverter.ToString(ret.GetParams().ToArray(), 7, 5).Replace("-", string.Empty);

                bool serialNumberSupported = (ret.GetParam(14) & 0x1) == 0x1;
                string? serialNumber = null;
                if (serialNumberSupported)
                {
                    ret = await _parent.WriteRead20(_parent.DevShort, new byte[7] { 0x10, _deviceIdx, featureId, 0x20 | SW_ID, 0x00, 0x00, 0x00 });
                    serialNumber = BitConverter.ToString(ret.GetParams().ToArray(), 0, 11).Replace("-", string.Empty);
                }

                Identifier = serialNumber ?? $"{unitId}-{modelId}";
            }
            else
            {
                // Device does not have a serial identifier the device name as a hash identifier
                Identifier = $"{DeviceName.GetHashCode():X04}";
            }

#if DEBUG
            Log.WriteLine("---");
            Log.WriteLine(DeviceName + " Ready");
            Log.WriteLine(Identifier);
            foreach ((ushort featureIdItr, string featureDesc) in new (ushort, string)[]
            {
                (0x1000, "Battery Unified Level"),
                (0x1001, "Battery Voltage"),
                (0x1004, "Unified Battery"),
                (0x8070, "Color LED Effects"),
                (0x8071, "Color LED Effects"),
            })
            {
                if (_featureMap.ContainsKey(featureIdItr))
                {
                    Log.WriteLine($"0x{featureIdItr:X} - {featureDesc} Found");
                }
            }
            Log.WriteLine("---");
#endif

            _getBatteryAsync = FeatureMap switch
            {
                { } when FeatureMap.ContainsKey(0x1000) => Battery1000.GetBatteryAsync,
                { } when FeatureMap.ContainsKey(0x1001) => Battery1001.GetBatteryAsync,
                { } when FeatureMap.ContainsKey(0x1004) => Battery1004.GetBatteryAsync,
                _ => null
            };

            await InitLedAsync();
            string ledFeatureIndex = _ledFeatureIndex is { } value ? value.ToString("X2") : "none";
            string features = string.Join(",", _featureMap.Keys.Select(x => $"0x{x:X4}"));
            LogLed($"INIT device='{DeviceName}' id='{Identifier}' features={features} ledFeature={ledFeatureIndex} zones={_ledZoneCount}");

            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.INIT,
                new InitMessage(Identifier, DeviceName, _getBatteryAsync != null, (DeviceType)DeviceType)
            );

            await UpdateBattery(true);

            _ = Task.Run(async () =>
            {
                if (_getBatteryAsync == null) { return; }

                while (true)
                {
                    var now = DateTimeOffset.Now;
#if DEBUG
                    var expectedUpdateTime = lastUpdate.AddSeconds(1);
#else
                    var expectedUpdateTime = lastUpdate.AddSeconds(GlobalSettings.settings.PollPeriod);
#endif
                    if (now < expectedUpdateTime)
                    {
                        await Task.Delay((int)(expectedUpdateTime - now).TotalMilliseconds);
                    }

                    await UpdateBattery();
                    await Task.Delay(GlobalSettings.settings.RetryTime * 1000);
                }
            });
        }

        public async Task UpdateBattery(bool forceIpcUpdate = false)
        {
            if (Parent.Disposed) { return; }
            if (_getBatteryAsync == null) { return; }

            var ret = await _getBatteryAsync.Invoke(this);

            if (ret == null) { return; }

            var batStatus = ret.Value;
            lastUpdate = DateTimeOffset.Now;
            hasBatteryReturn = true;

            if (!forceIpcUpdate && (batStatus == lastBatteryReturn))
            {
                // Don't report if no change
                return;
            }

            lastBatteryReturn = batStatus;
            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.UPDATE,
                new UpdateMessage(Identifier, batStatus.batteryPercentage, batStatus.status, batStatus.batteryMVolt, lastUpdate)
            );
        }

        private async Task InitLedAsync()
        {
            _ledFeatureIndex = null;
            _ledFeatureId = null;
            _ledZoneCount = 0;
            _ledZones.Clear();

            foreach (var ledFeature in new ushort[] { 0x8071, 0x8070 })
            {
                if (!FeatureMap.TryGetValue(ledFeature, out var featureIndex))
                {
                    continue;
                }

                try
                {
                    LedFeatureKind kind = ledFeature == 0x8071 ? LedFeatureKind.RgbEffects : LedFeatureKind.ColorLedEffects;
                    Hidpp20 ret = await ReadLedInfo(featureIndex, kind);

                    if (ret.Length == 0)
                    {
                        LogLed($"LED getInfo no response feature=0x{ledFeature:X4} index=0x{featureIndex:X2}");
                        continue;
                    }

                    if (ret.GetFeatureIndex() == 0xFF)
                    {
                        LogLed($"LED getInfo error feature=0x{ledFeature:X4} index=0x{featureIndex:X2} error=0x{ret.GetParam(2):X2}");
                        continue;
                    }

                    LogLed($"LED getInfo feature=0x{ledFeature:X4} index=0x{featureIndex:X2} raw={ToHex(ret)}");

                    int zoneCount = kind == LedFeatureKind.RgbEffects ? ret.GetParam(2) : ret.GetParam(0);
                    if (zoneCount <= 0)
                    {
                        continue;
                    }

                    List<LedZone> zones = await ReadLedZones(ledFeature, featureIndex, kind, zoneCount);
                    if (zones.Count == 0)
                    {
                        continue;
                    }

                    _ledFeatureIndex = featureIndex;
                    _ledFeatureId = ledFeature;
                    _ledZoneCount = zones.Count;
                    _ledZones.AddRange(zones);
                    LogLed($"LED ready feature=0x{ledFeature:X4} index=0x{featureIndex:X2} zones={_ledZoneCount}");
                    return;
                }
                catch (Exception ex)
                {
                    LogLed($"LED getInfo failed feature=0x{ledFeature:X4}: {ex.GetType().Name}");
                }
            }
        }

        private async Task<Hidpp20> ReadLedInfo(byte featureIndex, LedFeatureKind kind)
        {
            byte[] request = kind == LedFeatureKind.RgbEffects
                ? new byte[7] { 0x10, _deviceIdx, featureIndex, 0x00 | SW_ID, 0xFF, 0xFF, 0x00 }
                : new byte[7] { 0x10, _deviceIdx, featureIndex, 0x00 | SW_ID, 0x00, 0x00, 0x00 };

            Hidpp20 ret = await _parent.WriteRead20(_parent.DevShort, request, 500);

            if (ret.Length != 0)
            {
                return ret;
            }

            byte[] buffer = new byte[20];
            buffer[0] = 0x11;
            buffer[1] = _deviceIdx;
            buffer[2] = featureIndex;
            buffer[3] = 0x00 | SW_ID;
            if (kind == LedFeatureKind.RgbEffects)
            {
                buffer[4] = 0xFF;
                buffer[5] = 0xFF;
            }

            return await _parent.WriteRead20(_parent.DevLong, buffer, 500);
        }

        private async Task<List<LedZone>> ReadLedZones(ushort ledFeature, byte featureIndex, LedFeatureKind kind, int zoneCount)
        {
            List<LedZone> zones = [];

            for (byte zone = 0; zone < zoneCount; zone++)
            {
                Hidpp20 zoneInfo = await ReadLedZoneInfo(featureIndex, kind, zone);
                if (zoneInfo.Length == 0 || zoneInfo.GetFeatureIndex() == 0xFF)
                {
                    LogLed($"LED zoneInfo failed feature=0x{ledFeature:X4} zone={zone} raw={ToHex(zoneInfo)}");
                    continue;
                }

                int effectCount = kind == LedFeatureKind.RgbEffects ? zoneInfo.GetParam(4) : zoneInfo.GetParam(3);
                LogLed($"LED zoneInfo feature=0x{ledFeature:X4} zone={zone} effects={effectCount} raw={ToHex(zoneInfo)}");
                if (effectCount <= 0)
                {
                    continue;
                }

                List<LedEffect> effects = [];
                for (byte effect = 0; effect < effectCount; effect++)
                {
                    Hidpp20 effectInfo = await ReadLedEffectInfo(featureIndex, kind, zone, effect);
                    if (effectInfo.Length == 0 || effectInfo.GetFeatureIndex() == 0xFF)
                    {
                        LogLed($"LED effectInfo failed feature=0x{ledFeature:X4} zone={zone} effect={effect} raw={ToHex(effectInfo)}");
                        continue;
                    }

                    ushort effectId = (ushort)((effectInfo.GetParam(2) << 8) | effectInfo.GetParam(3));
                    effects.Add(new LedEffect(effect, effectId));
                    LogLed($"LED effectInfo feature=0x{ledFeature:X4} zone={zone} effect={effect} id=0x{effectId:X4} raw={ToHex(effectInfo)}");
                }

                if (effects.Count > 0)
                {
                    zones.Add(new LedZone(zone, effects));
                }
            }

            return zones;
        }

        private async Task<Hidpp20> ReadLedZoneInfo(byte featureIndex, LedFeatureKind kind, byte zone)
        {
            byte fn = kind == LedFeatureKind.RgbEffects ? (byte)0x00 : (byte)0x10;
            return await _parent.WriteRead20(
                _parent.DevShort,
                new byte[7] { 0x10, _deviceIdx, featureIndex, (byte)(fn | SW_ID), zone, 0xFF, 0x00 },
                500);
        }

        private async Task<Hidpp20> ReadLedEffectInfo(byte featureIndex, LedFeatureKind kind, byte zone, byte effect)
        {
            byte fn = kind == LedFeatureKind.RgbEffects ? (byte)0x00 : (byte)0x20;
            return await _parent.WriteRead20(
                _parent.DevShort,
                new byte[7] { 0x10, _deviceIdx, featureIndex, (byte)(fn | SW_ID), zone, effect, 0x00 },
                500);
        }

        public async Task ApplyLedMode(LogiLedMode ledMode, int lowBatteryThreshold, bool isSelected)
        {
            if (!isSelected || _ledFeatureIndex == null || _ledFeatureId == null || _ledZones.Count == 0)
            {
                string ledFeatureIndex = _ledFeatureIndex is { } value ? value.ToString("X2") : "none";
                LogLed($"LED skip selected={isSelected} feature={ledFeatureIndex} zones={_ledZones.Count} mode={ledMode}");
                return;
            }

            bool enabled = ledMode != LogiLedMode.Off;
            byte red = 0;
            byte green = 0;
            byte blue = 0;

            switch (ledMode)
            {
                case LogiLedMode.White:
                    red = green = blue = 255;
                    break;
                case LogiLedMode.Grey:
                    red = green = blue = 128;
                    break;
                case LogiLedMode.LowBattery:
                    if (hasBatteryReturn && lastBatteryReturn.batteryPercentage <= lowBatteryThreshold)
                    {
                        red = 255;
                    }
                    else
                    {
                        red = green = blue = 128;
                    }
                    break;
            }

            if (_ledFeatureId == 0x8071)
            {
                await ClaimRgbControl();
            }

            foreach (var zone in _ledZones)
            {
                await SetLedZoneState(zone, enabled, red, green, blue);
            }
        }

        private async Task ClaimRgbControl()
        {
            if (_ledFeatureIndex == null)
            {
                return;
            }

            try
            {
                Hidpp20 ret = await _parent.WriteRead20(
                    _parent.DevShort,
                    new byte[7] { 0x10, _deviceIdx, _ledFeatureIndex.Value, 0x50 | SW_ID, 0x01, 0x03, 0x04 },
                    500);
                LogLed($"LED rgbControl result={FormatResult(ret)} raw={ToHex(ret)}");
            }
            catch (Exception ex)
            {
                LogLed($"LED rgbControl failed: {ex.GetType().Name}");
            }
        }

        private async Task SetLedZoneState(LedZone zone, bool enabled, byte red, byte green, byte blue)
        {
            if (_ledFeatureIndex == null || _ledFeatureId == null)
            {
                return;
            }

            try
            {
                byte? effectIndex = FindEffectIndex(zone, enabled ? (ushort)0x0001 : (ushort)0x0000);
                if (effectIndex == null)
                {
                    effectIndex = FindEffectIndex(zone, 0x0001);
                    red = green = blue = 0;
                }

                if (effectIndex == null)
                {
                    LogLed($"LED set skipped zone={zone.Index} missing static/disabled effect");
                    return;
                }

                byte[] buffer = new byte[20];
                buffer[0] = 0x11;
                buffer[1] = _deviceIdx;
                buffer[2] = _ledFeatureIndex.Value;
                buffer[3] = (byte)((_ledFeatureId == 0x8071 ? 0x10 : 0x30) | SW_ID);
                buffer[4] = zone.Index;
                buffer[5] = effectIndex.Value;
                buffer[6] = red;
                buffer[7] = green;
                buffer[8] = blue;
                if (_ledFeatureId == 0x8070 && enabled)
                {
                    buffer[9] = 0x01;
                }
                if (_ledFeatureId == 0x8071)
                {
                    buffer[16] = 0x01;
                }

                var ret = await _parent.WriteRead20(_parent.DevLong, buffer, 500);
                LogLed($"LED set zone={zone.Index} effect=0x{(enabled ? 1 : 0):X4}/{effectIndex.Value} enabled={enabled} rgb={red},{green},{blue} result={FormatResult(ret)} raw={ToHex(ret)}");
            }
            catch (Exception ex)
            {
                LogLed($"LED set failed zone={zone.Index}: {ex.GetType().Name}");
            }
        }

        private static byte? FindEffectIndex(LedZone zone, ushort effectId)
        {
            foreach (var effect in zone.Effects)
            {
                if (effect.Id == effectId)
                {
                    return effect.Index;
                }
            }

            return null;
        }

        private static string FormatResult(Hidpp20 ret)
        {
            return ret.Length == 0
                ? "none"
                : ret.GetFeatureIndex() == 0xFF
                    ? $"error=0x{ret.GetParam(2):X2}"
                    : $"ok feature=0x{ret.GetFeatureIndex():X2}";
        }

        private static string ToHex(Hidpp20 ret)
        {
            return ret.Length == 0 ? string.Empty : Convert.ToHexString((byte[])ret);
        }

        private static void LogLed(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "led_debug.log"),
                    $"{DateTimeOffset.Now:O}\t{message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
