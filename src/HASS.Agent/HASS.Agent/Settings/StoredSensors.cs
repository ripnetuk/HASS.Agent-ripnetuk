﻿using System.IO;
using HASS.Agent.Enums;
using HASS.Agent.Extensions;
using HASS.Agent.HomeAssistant.Sensors.GeneralSensors.MultiValue;
using HASS.Agent.HomeAssistant.Sensors.GeneralSensors.SingleValue;
using HASS.Agent.Managers.DeviceSensors;
using HASS.Agent.Resources.Localization;
using HASS.Agent.Shared.Enums;
using HASS.Agent.Shared.Extensions;
using HASS.Agent.Shared.HomeAssistant.Sensors;
using HASS.Agent.Shared.HomeAssistant.Sensors.GeneralSensors.MultiValue;
using HASS.Agent.Shared.HomeAssistant.Sensors.GeneralSensors.SingleValue;
using HASS.Agent.Shared.HomeAssistant.Sensors.PerfCounterSensors.SingleValue;
using HASS.Agent.Shared.HomeAssistant.Sensors.WmiSensors.SingleValue;
using HASS.Agent.Shared.Models.Config;
using HASS.Agent.Shared.Models.HomeAssistant;
using LibreHardwareMonitor.Hardware;
using Newtonsoft.Json;
using Serilog;
using SensorType = HASS.Agent.Shared.Enums.SensorType;

namespace HASS.Agent.Settings
{
    /// <summary>
    /// Handles loading and storing sensors
    /// </summary>
    internal static class StoredSensors
    {
        /// <summary>
        /// Load all stored sensors
        /// </summary>
        /// <returns></returns>
        internal static async Task<bool> LoadAsync()
        {
            try
            {
                // set empty lists
                Variables.SingleValueSensors = new List<AbstractSingleValueSensor>();
                Variables.MultiValueSensors = new List<AbstractMultiValueSensor>();

                // check for existing file
                if (!File.Exists(Variables.SensorsFile))
                {
                    // none yet
                    Log.Information("[SETTINGS_SENSORS] Config not found, no entities loaded");
                    Variables.MainForm?.SetSensorsStatus(ComponentStatus.Stopped);
                    return true;
                }

                // read the content
                var sensorsRaw = await File.ReadAllTextAsync(Variables.SensorsFile);
                if (string.IsNullOrWhiteSpace(sensorsRaw))
                {
                    Log.Information("[SETTINGS_SENSORS] Config is empty, no entities loaded");
                    Variables.MainForm?.SetSensorsStatus(ComponentStatus.Stopped);
                    return true;
                }

                // deserialize
                var configuredSensors = JsonConvert.DeserializeObject<List<ConfiguredSensor>>(sensorsRaw);

                // null-check
                if (configuredSensors == null)
                {
                    Log.Error("[SETTINGS_SENSORS] Error loading entities: returned null object");
                    Variables.MainForm?.SetSensorsStatus(ComponentStatus.Failed);
                    return false;
                }

                // convert to abstract sensors
                await Task.Run(delegate
                {
                    foreach (var sensor in configuredSensors)
                    {
                        if (sensor.IsSingleValue()) Variables.SingleValueSensors.Add(ConvertConfiguredToAbstractSingleValue(sensor));
                        else Variables.MultiValueSensors.Add(ConvertConfiguredToAbstractMultiValue(sensor));
                    }
                });

                // all good
                Log.Information("[SETTINGS_SENSORS] Loaded {count} entities", (Variables.SingleValueSensors.Count + Variables.MultiValueSensors.Count));
                Variables.MainForm?.SetSensorsStatus(ComponentStatus.Ok);
                return true;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[SETTINGS_SENSORS] Error loading entities: {err}", ex.Message);
                Variables.MainForm?.ShowMessageBox(string.Format(Languages.StoredSensors_Load_MessageBox1, ex.Message), true);

                Variables.MainForm?.SetSensorsStatus(ComponentStatus.Failed);
                return false;
            }
        }

        /// <summary>
        /// Convert a single-value 'ConfiguredSensor' (local storage, UI) to an 'AbstractSensor' (MQTT)
        /// </summary>
        /// <param name="sensor"></param>
        /// <returns></returns>
        internal static AbstractSingleValueSensor ConvertConfiguredToAbstractSingleValue(ConfiguredSensor sensor)
        {
            AbstractSingleValueSensor abstractSensor = null;

            switch (sensor.Type)
            {
                case SensorType.UserNotificationStateSensor:
                    abstractSensor = new UserNotificationStateSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.DummySensor:
                    abstractSensor = new DummySensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.CurrentClockSpeedSensor:
                    abstractSensor = new CurrentClockSpeedSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.ApplyRounding, sensor.Round, sensor.AdvancedSettings);
                    break;
                case SensorType.CpuLoadSensor:
                    abstractSensor = new CpuLoadSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.ApplyRounding, sensor.Round, sensor.AdvancedSettings);
                    break;
                case SensorType.MemoryUsageSensor:
                    abstractSensor = new MemoryUsageSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.ApplyRounding, sensor.Round, sensor.AdvancedSettings);
                    break;
                case SensorType.ActiveWindowSensor:
                    abstractSensor = new ActiveWindowSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.ActiveDesktopSensor:
                    abstractSensor = new ActiveDesktopSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.NamedWindowSensor:
                    abstractSensor = new NamedWindowSensor(sensor.WindowName, sensor.EntityName, sensor.Name, sensor.UpdateInterval, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.LastActiveSensor:
                    abstractSensor = new LastActiveSensor(sensor.ApplyRounding, sensor.Round, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.LastSystemStateChangeSensor:
                    abstractSensor = new LastSystemStateChangeSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.LastBootSensor:
                    abstractSensor = new LastBootSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.WebcamActiveSensor:
                    abstractSensor = new WebcamActiveSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.MicrophoneActiveSensor:
                    abstractSensor = new MicrophoneActiveSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.SessionStateSensor:
                    abstractSensor = new SessionStateSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.CurrentVolumeSensor:
                    abstractSensor = new CurrentVolumeSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.GpuLoadSensor:
                    abstractSensor = new GpuLoadSensor(sensor.Query, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.GpuTemperatureSensor:
                    abstractSensor = new GpuTemperatureSensor(sensor.Query, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.WmiQuerySensor:
                    abstractSensor = new WmiQuerySensor(sensor.Query, sensor.Scope, sensor.ApplyRounding, sensor.Round, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.PerformanceCounterSensor:
                    abstractSensor = new PerformanceCounterSensor(sensor.Category, sensor.Counter, sensor.Instance, sensor.ApplyRounding, sensor.Round, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.ProcessActiveSensor:
                    abstractSensor = new ProcessActiveSensor(sensor.Query, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.ServiceStateSensor:
                    abstractSensor = new ServiceStateSensor(sensor.Query, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.LoggedUsersSensor:
                    abstractSensor = new LoggedUsersSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.LoggedUserSensor:
                    abstractSensor = new LoggedUserSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.GeoLocationSensor:
                    abstractSensor = new GeoLocationSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.MonitorPowerStateSensor:
                    abstractSensor = new MonitorPowerStateSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.PowershellSensor:
                    abstractSensor = new PowershellSensor(sensor.Query, sensor.ApplyRounding, sensor.Round, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.WindowStateSensor:
                    abstractSensor = new WindowStateSensor(sensor.Query, sensor.EntityName, sensor.Name, sensor.UpdateInterval, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.MicrophoneProcessSensor:
                    abstractSensor = new MicrophoneProcessSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.WebcamProcessSensor:
                    abstractSensor = new WebcamProcessSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.BluetoothDevicesSensor:
                    abstractSensor = new BluetoothDevicesSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.BluetoothLeDevicesSensor:
                    abstractSensor = new BluetoothLeDevicesSensor(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.InternalDeviceSensor:
                    abstractSensor = new HomeAssistant.Sensors.GeneralSensors.SingleValue.InternalDeviceSensor(sensor.Query, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                case SensorType.ScreenshotSensor:
                    abstractSensor = new ScreenshotSensor(sensor.Query, sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString(), sensor.AdvancedSettings);
                    break;
                default:
                    Log.Error("[SETTINGS_SENSORS] [{name}] Unknown configured single-value sensor type: {type}", sensor.EntityName, sensor.Type.ToString());
                    break;
            }

            if (abstractSensor != null)
                abstractSensor.IgnoreAvailability = sensor.IgnoreAvailability;

            return abstractSensor;
        }

        /// <summary>
        /// Convert a multi-value 'ConfiguredSensor' (local storage, UI) to an 'AbstractSensor' (MQTT)
        /// </summary>
        /// <param name="sensor"></param>
        /// <returns></returns>
        internal static AbstractMultiValueSensor ConvertConfiguredToAbstractMultiValue(ConfiguredSensor sensor)
        {
            AbstractMultiValueSensor abstractSensor = null;

            switch (sensor.Type)
            {
                case SensorType.StorageSensors:
                    abstractSensor = new StorageSensors(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString());
                    break;
                case SensorType.NetworkSensors:
                    abstractSensor = new NetworkSensors(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Query, sensor.Id.ToString());
                    break;
                case SensorType.WindowsUpdatesSensors:
                    abstractSensor = new WindowsUpdatesSensors(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString());
                    break;
                case SensorType.BatterySensors:
                    abstractSensor = new BatterySensors(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString());
                    break;
                case SensorType.DisplaySensors:
                    abstractSensor = new DisplaySensors(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString());
                    break;
                case SensorType.AudioSensors:
                    abstractSensor = new AudioSensors(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString());
                    break;
                case SensorType.PrintersSensors:
                    abstractSensor = new PrintersSensors(sensor.UpdateInterval, sensor.EntityName, sensor.Name, sensor.Id.ToString());
                    break;
                default:
                    Log.Error("[SETTINGS_SENSORS] [{name}] Unknown configured multi-value sensor type: {type}", sensor.EntityName, sensor.Type.ToString());
                    break;
            }

            abstractSensor.IgnoreAvailability = sensor.IgnoreAvailability;

            return abstractSensor;
        }

        /// <summary>
        /// Convert a single-value 'AbstractSensor' (MQTT) to an 'ConfiguredSensor' (local storage, UI)
        /// </summary>
        /// <param name="sensor"></param>
        /// <returns></returns>
        internal static ConfiguredSensor ConvertAbstractSingleValueToConfigured(AbstractSingleValueSensor sensor)
        {
            switch (sensor)
            {
                case WmiQuerySensor wmiSensor:
                    {
                        _ = Enum.TryParse<SensorType>(wmiSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(wmiSensor.Id),
                            EntityName = wmiSensor.EntityName,
                            Name = wmiSensor.Name,
                            Type = type,
                            UpdateInterval = wmiSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = wmiSensor.IgnoreAvailability,
                            Scope = wmiSensor.Scope,
                            Query = wmiSensor.Query,
                            ApplyRounding = wmiSensor.ApplyRounding,
                            Round = wmiSensor.Round,
                            AdvancedSettings = wmiSensor.AdvancedSettings
                        };
                    }

                case NamedWindowSensor namedWindowSensor:
                    {
                        _ = Enum.TryParse<SensorType>(namedWindowSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(namedWindowSensor.Id),
                            EntityName = namedWindowSensor.EntityName,
                            Name = namedWindowSensor.Name,
                            Type = type,
                            UpdateInterval = namedWindowSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = namedWindowSensor.IgnoreAvailability,
                            WindowName = namedWindowSensor.WindowName,
                            AdvancedSettings = namedWindowSensor.AdvancedSettings
                        };
                    }

                case PerformanceCounterSensor performanceCounterSensor:
                    {
                        _ = Enum.TryParse<SensorType>(performanceCounterSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(performanceCounterSensor.Id),
                            EntityName = performanceCounterSensor.EntityName,
                            Name = performanceCounterSensor.Name,
                            Type = type,
                            UpdateInterval = performanceCounterSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = performanceCounterSensor.IgnoreAvailability,
                            Category = performanceCounterSensor.CategoryName,
                            Counter = performanceCounterSensor.CounterName,
                            Instance = performanceCounterSensor.InstanceName,
                            ApplyRounding = performanceCounterSensor.ApplyRounding,
                            Round = performanceCounterSensor.Round,
                            AdvancedSettings = performanceCounterSensor.AdvancedSettings
                        };
                    }

                case ProcessActiveSensor processActiveSensor:
                    {
                        _ = Enum.TryParse<SensorType>(processActiveSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(processActiveSensor.Id),
                            EntityName = processActiveSensor.EntityName,
                            Name = processActiveSensor.Name,
                            Type = type,
                            UpdateInterval = processActiveSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = processActiveSensor.IgnoreAvailability,
                            Query = processActiveSensor.ProcessName,
                            AdvancedSettings = processActiveSensor.AdvancedSettings
                        };
                    }

                case ServiceStateSensor serviceStateSensor:
                    {
                        _ = Enum.TryParse<SensorType>(serviceStateSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(serviceStateSensor.Id),
                            EntityName = serviceStateSensor.EntityName,
                            Name = serviceStateSensor.Name,
                            Type = type,
                            UpdateInterval = serviceStateSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = serviceStateSensor.IgnoreAvailability,
                            Query = serviceStateSensor.ServiceName,
                            AdvancedSettings = serviceStateSensor.AdvancedSettings
                        };
                    }

                case PowershellSensor powershellSensor:
                    {
                        _ = Enum.TryParse<SensorType>(powershellSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(powershellSensor.Id),
                            EntityName = powershellSensor.EntityName,
                            Name = powershellSensor.Name,
                            Type = type,
                            UpdateInterval = powershellSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = powershellSensor.IgnoreAvailability,
                            Query = powershellSensor.Command,
                            ApplyRounding = powershellSensor.ApplyRounding,
                            Round = powershellSensor.Round,
                            AdvancedSettings = powershellSensor.AdvancedSettings
                        };
                    }

                case LastActiveSensor lastActiveSensor:
                    {
                        _ = Enum.TryParse<SensorType>(lastActiveSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(lastActiveSensor.Id),
                            EntityName = lastActiveSensor.EntityName,
                            Name = lastActiveSensor.Name,
                            Type = type,
                            UpdateInterval = lastActiveSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = lastActiveSensor.IgnoreAvailability,
                            ApplyRounding = lastActiveSensor.ApplyRounding,
                            Round = lastActiveSensor.Round,
                            AdvancedSettings = lastActiveSensor.AdvancedSettings
                        };
                    }

                case WindowStateSensor windowStateSensor:
                    {
                        _ = Enum.TryParse<SensorType>(windowStateSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(windowStateSensor.Id),
                            EntityName = windowStateSensor.EntityName,
                            Name = windowStateSensor.Name,
                            Type = type,
                            UpdateInterval = windowStateSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = windowStateSensor.IgnoreAvailability,
                            Query = windowStateSensor.ProcessName,
                            AdvancedSettings = windowStateSensor.AdvancedSettings
                        };
                    }

                case HomeAssistant.Sensors.GeneralSensors.SingleValue.InternalDeviceSensor internalDeviceSensor:
                    {
                        _ = Enum.TryParse<SensorType>(internalDeviceSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(internalDeviceSensor.Id),
                            EntityName = internalDeviceSensor.EntityName,
                            Name = internalDeviceSensor.Name,
                            Type = type,
                            UpdateInterval = internalDeviceSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = internalDeviceSensor.IgnoreAvailability,
                            Query = internalDeviceSensor.SensorType.ToString(),
                            AdvancedSettings = internalDeviceSensor.AdvancedSettings
                        };
                    }


                case HASS.Agent.Shared.HomeAssistant.Sensors.GeneralSensors.SingleValue.GpuTemperatureSensor gpuTemperatureSensor:
                    {
                        _ = Enum.TryParse<SensorType>(gpuTemperatureSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(gpuTemperatureSensor.Id),
                            EntityName = gpuTemperatureSensor.EntityName,
                            Name = gpuTemperatureSensor.Name,
                            Type = type,
                            UpdateInterval = gpuTemperatureSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = gpuTemperatureSensor.IgnoreAvailability,
                            Query = gpuTemperatureSensor.Query,
                            AdvancedSettings = gpuTemperatureSensor.AdvancedSettings
                        };
                    }


                case HASS.Agent.Shared.HomeAssistant.Sensors.GeneralSensors.SingleValue.GpuLoadSensor gpuloadSensor:
                    {
                        _ = Enum.TryParse<SensorType>(gpuloadSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(gpuloadSensor.Id),
                            EntityName = gpuloadSensor.EntityName,
                            Name = gpuloadSensor.Name,
                            Type = type,
                            UpdateInterval = gpuloadSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = gpuloadSensor.IgnoreAvailability,
                            Query = gpuloadSensor.Query,
                            AdvancedSettings = gpuloadSensor.AdvancedSettings
                        };
                    }

                case ScreenshotSensor screenshotSensor:
                    {
                        _ = Enum.TryParse<SensorType>(screenshotSensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(screenshotSensor.Id),
                            EntityName = screenshotSensor.EntityName,
                            Name = screenshotSensor.Name,
                            Type = type,
                            UpdateInterval = screenshotSensor.UpdateIntervalSeconds,
                            IgnoreAvailability = screenshotSensor.IgnoreAvailability,
                            Query = screenshotSensor.ScreenIndex.ToString(),
                            AdvancedSettings = screenshotSensor.AdvancedSettings
                        };
                    }

                default:
                    {
                        _ = Enum.TryParse<SensorType>(sensor.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(sensor.Id),
                            EntityName = sensor.EntityName,
                            Name = sensor.Name,
                            Type = type,
                            UpdateInterval = sensor.UpdateIntervalSeconds,
                            IgnoreAvailability = sensor.IgnoreAvailability,
                            AdvancedSettings = sensor.AdvancedSettings
                        };
                    }
            }
        }

        /// <summary>
        /// Convert a multi-value 'AbstractSensor' (MQTT) to an 'ConfiguredSensor' (local storage, UI)
        /// </summary>
        /// <param name="sensor"></param>
        /// <returns></returns>
        internal static ConfiguredSensor ConvertAbstractMultiValueToConfigured(AbstractMultiValueSensor sensor)
        {
            switch (sensor)
            {
                case StorageSensors storageSensors:
                    {
                        _ = Enum.TryParse<SensorType>(storageSensors.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(storageSensors.Id),
                            EntityName = storageSensors.EntityName,
                            Name = storageSensors.Name,
                            Type = type,
                            UpdateInterval = storageSensors.UpdateIntervalSeconds,
                            IgnoreAvailability = storageSensors.IgnoreAvailability
                        };
                    }

                case NetworkSensors networkSensors:
                    {
                        _ = Enum.TryParse<SensorType>(networkSensors.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(networkSensors.Id),
                            EntityName = networkSensors.EntityName,
                            Name = networkSensors.Name,
                            Query = networkSensors.NetworkCard,
                            Type = type,
                            UpdateInterval = networkSensors.UpdateIntervalSeconds,
                            IgnoreAvailability = networkSensors.IgnoreAvailability
                        };
                    }

                case WindowsUpdatesSensors windowsUpdatesSensors:
                    {
                        _ = Enum.TryParse<SensorType>(windowsUpdatesSensors.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(windowsUpdatesSensors.Id),
                            EntityName = windowsUpdatesSensors.EntityName,
                            Name = windowsUpdatesSensors.Name,
                            Type = type,
                            UpdateInterval = windowsUpdatesSensors.UpdateIntervalSeconds,
                            IgnoreAvailability = windowsUpdatesSensors.IgnoreAvailability
                        };
                    }

                case BatterySensors batterySensors:
                    {
                        _ = Enum.TryParse<SensorType>(batterySensors.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(batterySensors.Id),
                            EntityName = batterySensors.EntityName,
                            Name = batterySensors.Name,
                            Type = type,
                            UpdateInterval = batterySensors.UpdateIntervalSeconds,
                            IgnoreAvailability = batterySensors.IgnoreAvailability
                        };
                    }

                case DisplaySensors displaySensors:
                    {
                        _ = Enum.TryParse<SensorType>(displaySensors.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(displaySensors.Id),
                            EntityName = displaySensors.EntityName,
                            Name = displaySensors.Name,
                            Type = type,
                            UpdateInterval = displaySensors.UpdateIntervalSeconds,
                            IgnoreAvailability = displaySensors.IgnoreAvailability
                        };
                    }

                case AudioSensors audioSensors:
                    {
                        _ = Enum.TryParse<SensorType>(audioSensors.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(audioSensors.Id),
                            EntityName = audioSensors.EntityName,
                            Name = audioSensors.Name,
                            Type = type,
                            UpdateInterval = audioSensors.UpdateIntervalSeconds,
                            IgnoreAvailability = audioSensors.IgnoreAvailability
                        };
                    }

                case PrintersSensors printersSensors:
                    {
                        _ = Enum.TryParse<SensorType>(printersSensors.GetType().Name, out var type);
                        return new ConfiguredSensor
                        {
                            Id = Guid.Parse(printersSensors.Id),
                            EntityName = printersSensors.EntityName,
                            Name = printersSensors.Name,
                            Type = type,
                            UpdateInterval = printersSensors.UpdateIntervalSeconds,
                            IgnoreAvailability = printersSensors.IgnoreAvailability
                        };
                    }
            }

            return null;
        }

        /// <summary>
        /// Store all current sensors
        /// </summary>
        /// <returns></returns>
        internal static bool Store()
        {
            try
            {
                // check config dir
                if (!Directory.Exists(Variables.ConfigPath))
                {
                    // create
                    Directory.CreateDirectory(Variables.ConfigPath);
                }

                // convert single-value sensors
                var configuredSensors = Variables.SingleValueSensors.Select(ConvertAbstractSingleValueToConfigured).Where(configuredSensor => configuredSensor != null).ToList();

                // convert multi-value sensors
                var configuredMultiValueSensors = Variables.MultiValueSensors.Select(ConvertAbstractMultiValueToConfigured).Where(configuredSensor => configuredSensor != null).ToList();
                configuredSensors = configuredSensors.Concat(configuredMultiValueSensors).ToList();

                // serialize to file
                var sensors = JsonConvert.SerializeObject(configuredSensors, Formatting.Indented);
                File.WriteAllText(Variables.SensorsFile, sensors);

                // done
                Log.Information("[SETTINGS_SENSORS] Stored {count} entities", (Variables.SingleValueSensors.Count + Variables.MultiValueSensors.Count));
                Variables.MainForm?.SetSensorsStatus(ComponentStatus.Ok);
                return true;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[SETTINGS_SENSORS] Error storing entities: {err}", ex.Message);
                Variables.MainForm?.ShowMessageBox(string.Format(Languages.StoredSensors_Store_MessageBox1, ex.Message), true);

                Variables.MainForm?.SetSensorsStatus(ComponentStatus.Failed);
                return false;
            }
        }
    }
}
