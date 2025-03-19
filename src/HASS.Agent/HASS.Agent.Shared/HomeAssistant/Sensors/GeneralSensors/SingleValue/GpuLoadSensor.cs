﻿using System.Globalization;
using System.Linq;
using HASS.Agent.Shared.Managers;
using HASS.Agent.Shared.Models.HomeAssistant;
using LibreHardwareMonitor.Hardware;

namespace HASS.Agent.Shared.HomeAssistant.Sensors.GeneralSensors.SingleValue;

/// <summary>
/// Sensor indicating the current GPU load
/// </summary>
public class GpuLoadSensor : AbstractSingleValueSensor
{
	private const string DefaultName = "gpuload";
	private readonly IHardware _gpu;
    public string Query { get; private set; }

    public GpuLoadSensor(string query, int? updateInterval = null, string entityName = DefaultName, string name = DefaultName, string id = default, string advancedSettings = default) : base(entityName ?? DefaultName, name ?? null, updateInterval ?? 30, id, advancedSettings: advancedSettings)
	{
        Query = query;

        _gpu = HardwareManager.Hardware.FirstOrDefault(
            h => (h.HardwareType == HardwareType.GpuAmd ||
            h.HardwareType == HardwareType.GpuNvidia ||
            h.HardwareType == HardwareType.GpuIntel) && (h.Name == Query)
        );
    }

	public override DiscoveryConfigModel GetAutoDiscoveryConfig()
	{
		if (Variables.MqttManager == null)
			return null;

		var deviceConfig = Variables.MqttManager.GetDeviceConfigModel();
		if (deviceConfig == null)
			return null;

		return AutoDiscoveryConfigModel ?? SetAutoDiscoveryConfigModel(new SensorDiscoveryConfigModel()
		{
			EntityName = EntityName,
			Name = Name,
			Unique_id = Id,
			Device = deviceConfig,
			State_topic = $"{Variables.MqttManager.MqttDiscoveryPrefix()}/{Domain}/{deviceConfig.Name}/{ObjectId}/state",
			Unit_of_measurement = "%",
            State_class = "measurement",
            Availability_topic = $"{Variables.MqttManager.MqttDiscoveryPrefix()}/{Domain}/{deviceConfig.Name}/availability"
		});
	}

	public override string GetState()
	{
		if (_gpu == null)
			return null;

		_gpu.Update();

		var sensor = _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);

		if (sensor?.Value == null)
			return null;

		return sensor.Value.HasValue ? sensor.Value.Value.ToString("#.##", CultureInfo.InvariantCulture) : null;
	}

	public override string GetAttributes() => string.Empty;
}
