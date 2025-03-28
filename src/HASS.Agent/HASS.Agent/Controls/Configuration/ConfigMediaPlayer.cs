﻿using HASS.Agent.API;
using HASS.Agent.Functions;
using HASS.Agent.Managers;
using HASS.Agent.Models.HomeAssistant;
using HASS.Agent.Resources.Localization;
using Serilog;
using Syncfusion.Windows.Forms;

namespace HASS.Agent.Controls.Configuration
{
    public partial class ConfigMediaPlayer : UserControl
    {
        public ConfigMediaPlayer()
        {
            InitializeComponent();
        }

        private void BtnNotificationsReadme_Click(object sender, EventArgs e) => HelperFunctions.LaunchUrl("https://www.hass-agent.io/latest/getting-started/media-player/");
        
        private void ConfigMediaPlayer_Load(object sender, EventArgs e)
        {
            LblConnectivityDisabled.Visible = !Variables.AppSettings.LocalApiEnabled && !Variables.AppSettings.MqttEnabled;
        }
    }
}
