﻿using System.Diagnostics.CodeAnalysis;
using HASS.Agent.API;
using HASS.Agent.Commands;
using HASS.Agent.Enums;
using HASS.Agent.Forms.Commands;
using HASS.Agent.Forms.Sensors;
using HASS.Agent.Forms.Service;
using HASS.Agent.Functions;
using HASS.Agent.HomeAssistant;
using HASS.Agent.Managers;
using HASS.Agent.Managers.DeviceSensors;
using HASS.Agent.Media;
using HASS.Agent.Models.Internal;
using HASS.Agent.Resources.Localization;
using HASS.Agent.Sensors;
using HASS.Agent.Service;
using HASS.Agent.Settings;
using HASS.Agent.Shared;
using HASS.Agent.Shared.Enums;
using HASS.Agent.Shared.Extensions;
using HASS.Agent.Shared.Functions;
using HASS.Agent.Shared.Managers;
using HASS.Agent.Shared.Managers.Audio;
using Serilog;
using Syncfusion.Windows.Forms;
using WindowsDesktop;
using WK.Libraries.HotkeyListenerNS;
using NativeMethods = HASS.Agent.Functions.NativeMethods;
using QuickActionsConfig = HASS.Agent.Forms.QuickActions.QuickActionsConfig;
using Task = System.Threading.Tasks.Task;

namespace HASS.Agent.Forms
{
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Local")]
    public partial class Main : MetroForm
    {
        private bool _isClosing = false;

        public Main()
        {
            InitializeComponent();
        }

        [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
        private async void Main_Load(object sender, EventArgs e)
        {
            try
            {
#if DEBUG
                // show the form while we're debugging
                _ = Task.Run(async delegate
                {
                    await Task.Delay(500);
                    Invoke(new MethodInvoker(Show));
                    await Task.Delay(150);
                    Invoke(new MethodInvoker(BringToFront));
                });
#endif

                // check if we're enabling extended logging
                if (Variables.ExtendedLogging)
                {
                    // exception handlers
                    Application.ThreadException += Application_ThreadException;
                    AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                }

                // catch all key presses
                KeyPreview = true;

                // hide donate button?
                if (SettingsManager.GetHideDonateButton())
                    PbDonate.Visible = false;

                // set all statuses to loading
                SetLocalApiStatus(ComponentStatus.Loading);
                SetHassApiStatus(ComponentStatus.Loading);
                SetQuickActionsStatus(ComponentStatus.Loading);
                SetServiceStatus(ComponentStatus.Loading);
                SetSensorsStatus(ComponentStatus.Loading);
                SetCommandsStatus(ComponentStatus.Loading);
                SetMqttStatus(ComponentStatus.Loading);

                // create a hotkey listener
                Variables.HotKeyListener = new HotkeyListener();

                // check for dpi scaling
                CheckDpiScalingFactor();

                // core components initialization - required for loading the entities
                await RadioManager.Initialize();
                await InternalDeviceSensorsManager.Initialize();
                InitializeHardwareManager();
                InitializeVirtualDesktopManager();
                await Task.Run(InitializeAudioManager);

                // load entities
                var loaded = await SettingsManager.LoadEntitiesAsync();
                if (!loaded)
                {
                    MessageBoxAdv.Show(this, Languages.Main_Load_MessageBox1, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

                    // abort
                    Variables.ShuttingDown = true;
                    await Log.CloseAndFlushAsync();
                    Close();

                    return;
                }

                // initialize hass.agent's shared library
                AgentSharedBase.Initialize(Variables.AppSettings.DeviceName, Variables.MqttManager, Variables.AppSettings.CustomExecutorBinary);

                ProcessOnboarding();

                // if we're shutting down, no point continuing
                if (Variables.ShuttingDown)
                    return;

                ProcessTrayIcon();
                InitializeHotkeys();

                var initTask = Task.Run(async () =>
                {
                    _ = Task.Run(ApiManager.Initialize);
                    var hassApiTask = Task.Run(HassApiManager.InitializeAsync);
                    await Task.Run(Variables.MqttManager.Initialize);
                    var sensorTask = Task.Run(SensorsManager.Initialize);
                    var commandsTask = Task.Run(CommandsManager.Initialize);
                    var serviceTask = Task.Run(ServiceManager.Initialize);
                    var upateTask = Task.Run(UpdateManager.Initialize);
                    var systemStateTask = Task.Run(SystemStateManager.Initialize);
                    var cacheTask = Task.Run(CacheManager.Initialize);
                    var notificationTask = Task.Run(NotificationManager.Initialize);
                    var mediaTask = Task.Run(MediaManager.InitializeAsync);

                    await Task.WhenAll(hassApiTask, sensorTask, commandsTask, serviceTask,
                            upateTask, systemStateTask, cacheTask, notificationTask, mediaTask);
                });

                // handle activation from a previously generated notification
                if (Environment.GetCommandLineArgs().Contains(NotificationManager.NotificationLaunchArgument))
                {
                    _ = Task.Run(async () =>
                    {
                        await initTask;
                        while (!Variables.MqttManager.IsReady())
                            await Task.Delay(TimeSpan.FromSeconds(5));

                        NotificationManager.HandleNotificationLaunch();
                    });
                }

                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MAIN] Main_Load: {err}", ex.Message);
                MessageBoxAdv.Show(this, Languages.Main_Load_MessageBox2, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

                // we're done
                Application.Exit();
            }
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            AudioManager.Shutdown();
            HardwareManager.Shutdown();
            NotificationManager.Exit();
        }

        /// <summary>
        /// Listen to Windows messages
        /// </summary>
        /// <param name="m"></param>
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WM_QUERYENDSESSION:
                    SystemStateManager.ProcessSessionEnd();
                    break;

                case NativeMethods.WM_POWERBROADCAST:
                    SystemStateManager.ProcessMonitorPowerChange(m);
                    break;
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// Register for events when we have an handle
        /// </summary>
        /// <param name="e"></param>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            var settingGuid = new NativeMethods.PowerSettingGuid();
            var powerGuid = HelperFunctions.IsWindows8Plus()
                ? settingGuid.ConsoleDisplayState
                : settingGuid.MonitorPowerGuid;

            SystemStateManager.UnRegPowerNotify = NativeMethods.RegisterPowerSettingNotification(Handle, powerGuid, NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
        }

        /// <summary>
        /// Checks to see if we need to launch onboarding, and if so, does that
        /// </summary>
        private void ProcessOnboarding()
        {
            if (Variables.AppSettings.OnboardingStatus is OnboardingStatus.Completed or OnboardingStatus.Aborted)
                return;

            // hide ourselves
            ShowInTaskbar = false;
            Opacity = 0;

            // show onboarding
            using var onboarding = new Onboarding();
            onboarding.ShowDialog();

            // show ourselves
            ShowInTaskbar = true;
            Opacity = 100;
        }

        /// <summary>
        /// Checks whether the user has a non-default DPI scaling
        /// </summary>
        [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
        private void CheckDpiScalingFactor()
        {
            var (scalingFactor, dpiScalingFactor) = HelperFunctions.GetScalingFactors();
            if (scalingFactor == 1 && dpiScalingFactor == 1)
                return;

            // already shown warning?
            if (SettingsManager.GetDpiWarningShown())
                return;

            // nope, flag it as shown
            SettingsManager.SetDpiWarningShown(true);

            // and show it
            MessageBoxAdv.Show(this, Languages.Main_CheckDpiScalingFactor_MessageBox1, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }

        private void ProcessTrayIcon()
        {
            if (Variables.AppSettings.TrayIconUseModern)
            {
                var icon = (Icon)new System.Resources.ResourceManager(typeof(Main)).GetObject("ModernNotifyIcon");
                if (icon != null)
                    NotifyIcon.Icon = icon;
            }

            // are we set to show the webview and keep it loaded?
            if (!Variables.AppSettings.TrayIconShowWebView)
                return;
            if (!Variables.AppSettings.TrayIconWebViewBackgroundLoading)
                return;

            // yep, check the url
            if (string.IsNullOrEmpty(Variables.AppSettings.TrayIconWebViewUrl))
            {
                Log.Warning("[MAIN] Unable to prepare tray icon webview for background loading: no URL set");

                return;
            }

            // prepare the tray icon's webview
            HelperFunctions.PrepareTrayIconWebView();
        }

        /// <summary>
        /// Log ThreadExceptions (if enabled)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Fatal(e.Exception, "[MAIN] ThreadException: {err}", e.Exception.Message);
        }

        /// <summary>
        /// Log unhandled exceptions (if enabled)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                Log.Fatal(ex, "[MAIN] ThreadException: {err}", ex.Message);
        }

        /// <summary>
        /// Initialize the hotkeys (if any)
        /// </summary>
        private void InitializeHotkeys()
        {
            // prepare listener
            Variables.HotKeyListener.HotkeyPressed += HotkeyListener_HotkeyPressed;

            // bind quick actions hotkey (if configured)
            Variables.HotKeyManager.InitializeQuickActionsHotKeys();
        }

        /// <summary>
        /// Fires when a registered hotkey is pressed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HotkeyListener_HotkeyPressed(object sender, HotkeyEventArgs e)
        {
            if (e.Hotkey == Variables.QuickActionsHotKey)
                ShowQuickActions();
            else
                HotKeyManager.ProcessQuickActionHotKey(e.Hotkey.ToString());
        }

        /// <summary>
        /// Initializes the Virtual Desktop Manager
        /// </summary>
        private void InitializeVirtualDesktopManager()
        {
            VirtualDesktopManager.Initialize();
        }

        /// <summary>
        /// Initialized the Hardware Manager
        /// </summary>
        private void InitializeHardwareManager()
        {
            HardwareManager.Initialize();
        }

        /// <summary>
        /// Initialized the Audio Manager
        /// </summary>
        private void InitializeAudioManager()
        {
            AudioManager.Initialize();
        }

        /// <summary>
        /// Hide if not shutting down, close otherwise
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isClosing)
                return;

            Invoke(() =>
            {
                HelperFunctions.GetForm("QuickActions")?.Close();
                HelperFunctions.GetForm("Configuration")?.Close();
                HelperFunctions.GetForm("QuickActionsConfig")?.Close();
                HelperFunctions.GetForm("SensorsConfig")?.Close();
                HelperFunctions.GetForm("ServiceConfig")?.Close();
                HelperFunctions.GetForm("CommandsConfig")?.Close();
                HelperFunctions.GetForm("Help")?.Close();
                HelperFunctions.GetForm("Donate")?.Close();

                new MethodInvoker(Hide).Invoke();
            });

            if (!Variables.ShuttingDown)
            {
                e.Cancel = true;

                return;
            }

            // we're calling the shutdown function, but async won't hold so ignore that
            Task.Run(HelperFunctions.ShutdownAsync);

            // cancel and let the shutdown function handle it
            _isClosing = true;
            e.Cancel = true;
        }

        /// <summary>
        /// Hides the tray icon
        /// </summary>
        internal void HideTrayIcon()
        {
            if (!IsHandleCreated)
                return;
            if (IsDisposed)
                return;

            Invoke(new MethodInvoker(delegate
            {
                NotifyIcon.Visible = false;
            }));
        }

        /// <summary>
        /// Show a messagebox on the UI thread
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="error"></param>
        internal void ShowMessageBox(string msg, bool error = false)
        {
            if (!IsHandleCreated)
                return;
            if (IsDisposed)
                return;

            Invoke(new MethodInvoker(delegate
            {
                var icon = error ? MessageBoxIcon.Error : MessageBoxIcon.Information;
                MessageBoxAdv.Show(this, msg, Variables.MessageBoxTitle, MessageBoxButtons.OK, icon);
            }));
        }

        /// <summary>
        /// Show/hide ourself
        /// </summary>
        private async void ShowMain()
        {
            if (!IsHandleCreated)
                return;
            if (IsDisposed)
                return;

            if (Visible)
            {
                Hide();
            }
            else
            {
                Show();

                await Task.Delay(150);

                BringToFront();
                Activate();
            }
        }

        /// <summary>
        /// Show the QuickActions form
        /// </summary>
        private async void ShowQuickActions()
        {

            // check if quickactions aren't already open, and if there are any actions
            if (HelperFunctions.CheckIfFormIsOpen("QuickActions"))
            {
                var quickActionsForm = HelperFunctions.GetForm("QuickActions") as QuickActions.QuickActions;
                var result = await HelperFunctions.TryBringToFront(quickActionsForm);
                if (result)
                    quickActionsForm.SelectNextQuickActionItem();

                return;
            }

            if (!Variables.QuickActions.Any())
                return;

            // show a new window
            var form = new QuickActions.QuickActions(Variables.QuickActions);
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        /// <summary>
        /// Ask the user what to do and process the choice
        /// </summary>
        [SuppressMessage("ReSharper", "InvertIf")]
        private void Exit()
        {
            using var exitDialog = new ExitDialog();
            var result = exitDialog.ShowDialog();
            if (result != DialogResult.OK)
                return;

            if (exitDialog.HideToTray)
            {
                Hide();

                return;
            }

            if (exitDialog.Restart)
            {
                HelperFunctions.Restart();

                return;
            }

            if (exitDialog.Exit)
            {
                Variables.ShuttingDown = true;
                Close();
            }
        }

        /// <summary>
        /// Show the config form
        /// </summary>
        private async void ShowConfiguration()
        {
            if (await HelperFunctions.TryBringToFront("Configuration"))
                return;

            var form = new Configuration();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        /// <summary>
        /// Show the quickactions manager form
        /// </summary>
        private async void ShowQuickActionsManager()
        {
            if (await HelperFunctions.TryBringToFront("QuickActionsConfig"))
                return;

            var form = new QuickActionsConfig();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        /// <summary>
        /// Show the sensors manager form
        /// </summary>
        private async void ShowSensorsManager()
        {
            if (await HelperFunctions.TryBringToFront("SensorsConfig"))
                return;

            var form = new SensorsConfig();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        /// <summary>
        /// Show the service manager form
        /// </summary>
        private async void ShowServiceManager()
        {
            if (await HelperFunctions.TryBringToFront("ServiceConfig"))
                return;

            var form = new ServiceConfig();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        /// <summary>
        /// /Show the commands manager form
        /// </summary>
        private async void ShowCommandsManager()
        {
            if (await HelperFunctions.TryBringToFront("CommandsConfig"))
                return;

            var form = new CommandsConfig();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        /// <summary>
        /// Change the status of the local API
        /// </summary>
        /// <param name="status"></param>
        internal void SetLocalApiStatus(ComponentStatus status) => SetComponentStatus(new ComponentStatusUpdate(Component.LocalApi, status));

        /// <summary>
        /// Change the status of the HASS API manager
        /// </summary>
        /// <param name="status"></param>
        internal void SetHassApiStatus(ComponentStatus status) => SetComponentStatus(new ComponentStatusUpdate(Component.HassApi, status));

        /// <summary>
        /// Change the status of the QuickActions
        /// </summary>
        /// <param name="status"></param>
        internal void SetQuickActionsStatus(ComponentStatus status) => SetComponentStatus(new ComponentStatusUpdate(Component.QuickActions, status));

        /// <summary>
        /// Change the status of the sensors manager
        /// </summary>
        /// <param name="status"></param>
        internal void SetSensorsStatus(ComponentStatus status) => SetComponentStatus(new ComponentStatusUpdate(Component.Sensors, status));

        /// <summary>
        /// Change the status of the service manager
        /// </summary>
        /// <param name="status"></param>
        internal void SetServiceStatus(ComponentStatus status) => SetComponentStatus(new ComponentStatusUpdate(Component.Service, status));

        /// <summary>
        /// Change the status of the commands manager status
        /// </summary>
        /// <param name="status"></param>
        internal void SetCommandsStatus(ComponentStatus status) => SetComponentStatus(new ComponentStatusUpdate(Component.Commands, status));

        /// <summary>
        /// Change the status of the MQTT manager status
        /// </summary>
        /// <param name="status"></param>
        internal void SetMqttStatus(ComponentStatus status) => SetComponentStatus(new ComponentStatusUpdate(Component.Mqtt, status));

        /// <summary>
        /// Set the status of the component in our UI
        /// </summary>
        /// <param name="update"></param>
        internal void SetComponentStatus(ComponentStatusUpdate update)
        {
            if (!IsHandleCreated)
                return;
            if (IsDisposed)
                return;

            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    try
                    {
                        var status = update.Status;

                        switch (update.Component)
                        {
                            case Component.LocalApi:
                                LblStatusLocalApi.Text = status.GetLocalizedDescription().ToLower();
                                LblStatusLocalApi.ForeColor = status.GetColor();
                                break;

                            case Component.HassApi:
                                LblStatusHassApi.Text = status.GetLocalizedDescription().ToLower();
                                LblStatusHassApi.ForeColor = status.GetColor();
                                break;

                            case Component.Mqtt:
                                LblStatusMqtt.Text = status.GetLocalizedDescription().ToLower();
                                LblStatusMqtt.ForeColor = status.GetColor();
                                break;

                            case Component.QuickActions:
                                LblStatusQuickActions.Text = status.GetLocalizedDescription().ToLower();
                                LblStatusQuickActions.ForeColor = status.GetColor();
                                break;

                            case Component.Sensors:
                                if (status != ComponentStatus.Loading)
                                {
                                    BtnSensorsManager.Text = Languages.Main_BtnSensorsManage_Ready;
                                    BtnSensorsManager.Enabled = true;
                                }
                                LblStatusSensors.Text = status.GetLocalizedDescription().ToLower();
                                LblStatusSensors.ForeColor = status.GetColor();
                                break;

                            case Component.Service:
                                LblStatusService.Text = status.GetLocalizedDescription().ToLower();
                                LblStatusService.ForeColor = status.GetColor();
                                break;

                            case Component.Commands:
                                if (status != ComponentStatus.Loading)
                                {
                                    BtnCommandsManager.Text = Languages.Main_BtnCommandsManager_Ready;
                                    BtnCommandsManager.Enabled = true;
                                }
                                LblStatusCommands.Text = status.GetLocalizedDescription().ToLower();
                                LblStatusCommands.ForeColor = status.GetColor();
                                break;
                        }

                        GpStatus.Refresh();
                    }
                    catch
                    {
                        // best effort
                    }
                }));
            }
            catch
            {
                // best effort
            }
        }

        /// <summary>
        /// Shows a tray-icon tooltip containing the message, and optionally and error-icon
        /// </summary>
        /// <param name="message"></param>
        /// <param name="error"></param>
        internal void ShowToolTip(string message, bool error = false)
        {
            if (!IsHandleCreated)
                return;
            if (IsDisposed)
                return;

            try
            {
                // check if the user wants to see our notifications
                if (!Variables.AppSettings.EnableStateNotifications)
                    return;

                Invoke(new MethodInvoker(delegate
                {
                    try
                    {
                        NotifyIcon.BalloonTipIcon = error ? ToolTipIcon.Error : ToolTipIcon.Info;
                        NotifyIcon.BalloonTipText = message;
                        NotifyIcon.ShowBalloonTip((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
                    }
                    catch
                    {
                        // best effort
                    }
                }));
            }
            catch
            {
                // best effort
            }
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e) => ShowMain();

        private void BtnExit_Click(object sender, EventArgs e) => Exit();

        private void BtnHide_Click(object sender, EventArgs e) => Hide();

        private void BtnAppSettings_Click(object sender, EventArgs e) => ShowConfiguration();

        private void BtnActionsManager_Click(object sender, EventArgs e) => ShowQuickActionsManager();

        private void BtnSensorsManager_Click(object sender, EventArgs e) => ShowSensorsManager();

        private void BtnServiceManager_Click(object sender, EventArgs e) => ShowServiceManager();

        private void BtnCommandsManager_Click(object sender, EventArgs e) => ShowCommandsManager();

        private void Main_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape)
                return;

            // Escape Pressed, but make sure it's not escape while alt-tabbing
            if (e.Alt) return;

            Hide();
        }

        private void TsShow_Click(object sender, EventArgs e) => ShowMain();

        private void TsShowQuickActions_Click(object sender, EventArgs e) => ShowQuickActions();

        private void TsConfig_Click(object sender, EventArgs e) => ShowConfiguration();

        private void TsQuickItemsConfig_Click(object sender, EventArgs e) => ShowQuickActionsManager();

        private void TsLocalSensors_Click(object sender, EventArgs e) => ShowSensorsManager();

        private void TsCommands_Click(object sender, EventArgs e) => ShowCommandsManager();

        private void TsSatelliteService_Click(object sender, EventArgs e) => ShowServiceManager();

        private void TsExit_Click(object sender, EventArgs e) => Exit();

        private async void TsAbout_Click(object sender, EventArgs e)
        {
            if (await HelperFunctions.TryBringToFront("About"))
                return;

            var form = new About();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        private async void BtnHelp_Click(object sender, EventArgs e)
        {
            if (await HelperFunctions.TryBringToFront("Help"))
                return;

            var form = new Help();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        private void BtnCheckForUpdate_Click(object sender, EventArgs e) => CheckForUpdate();

        private void TsCheckForUpdates_Click(object sender, EventArgs e) => CheckForUpdate();

        /// <summary>
        /// Checks GitHub API to see if there's a newer release available
        /// </summary>
        private async void CheckForUpdate()
        {
            try
            {
                TsCheckForUpdates.Enabled = false;
                BtnCheckForUpdate.Enabled = false;

                TsCheckForUpdates.Text = Languages.Main_Checking;
                BtnCheckForUpdate.Text = Languages.Main_Checking;

                var (isAvailable, version) = await UpdateManager.CheckIsUpdateAvailableAsync();
                if (!isAvailable)
                {
                    var beta = Variables.Beta ? " [BETA]" : string.Empty;
                    MessageBoxAdv.Show(this, string.Format(Languages.Main_CheckForUpdate_MessageBox1, Variables.Version, beta), Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

                    return;
                }

                // new update, hide
                Hide();

                // show info
                ShowUpdateInfo(version);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MAIN] Error while checking for updates: {msg}", ex.Message);
            }
            finally
            {
                TsCheckForUpdates.Enabled = true;
                BtnCheckForUpdate.Enabled = true;

                TsCheckForUpdates.Text = Languages.Main_CheckForUpdates;
                BtnCheckForUpdate.Text = Languages.Main_CheckForUpdates;
            }
        }

        /// <summary>
        /// Shows a window, informing the user of a pending update
        /// </summary>
        /// <param name="pendingUpdate"></param>
        internal void ShowUpdateInfo(PendingUpdate pendingUpdate)
        {
            if (!IsHandleCreated)
                return;
            if (IsDisposed)
                return;

            try
            {
                // ReSharper disable once AsyncVoidLambda
                BeginInvoke(new MethodInvoker(async delegate
                {
                    if (await HelperFunctions.TryBringToFront("UpdatePending"))
                        return;

                    var form = new UpdatePending(pendingUpdate);
                    form.FormClosed += delegate
                    {
                        form.Dispose();
                    };
                    form.Show(this);
                }));
            }
            catch
            {
                // best effort
            }
        }

        private void Main_ResizeEnd(object sender, EventArgs e)
        {
            if (Variables.ShuttingDown)
                return;
            if (!IsHandleCreated)
                return;
            if (IsDisposed)
                return;

            try
            {
                Refresh();
            }
            catch
            {
                // best effort
            }
        }

        private async void TsHelp_Click(object sender, EventArgs e)
        {
            if (await HelperFunctions.TryBringToFront("Help"))
                return;

            var form = new Help();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        private void TsDonate_Click(object sender, EventArgs e) => HelperFunctions.LaunchUrl("https://www.buymeacoffee.com/lab02research");

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            // we're only interested if the webview's enabled
            if (!Variables.AppSettings.TrayIconShowWebView)
                return;

            // ignore doubleclicks
            if (e.Clicks > 1)
            {
                CmTrayIcon.Close();

                return;
            }

            // if it's a leftclick, show the regular menu if enabled
            if (e.Button == MouseButtons.Left && Variables.AppSettings.TrayIconWebViewShowMenuOnLeftClick)
            {
                CmTrayIcon.Show(MousePosition);

                return;
            }

            // if it's anything but rightclick, do nothing
            if (e.Button != MouseButtons.Right)
                return;

            // close the menu if it loaded
            CmTrayIcon.Close();

            // check the url
            if (string.IsNullOrEmpty(Variables.AppSettings.TrayIconWebViewUrl))
            {
                MessageBoxAdv.Show(this, Languages.Main_NotifyIcon_MouseClick_MessageBox1, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

                return;
            }

            HelperFunctions.LaunchTrayIconWebView();
        }

        private async void PbDonate_Click(object sender, EventArgs e)
        {
            if (await HelperFunctions.TryBringToFront("Donate"))
                return;

            var form = new Donate();
            form.FormClosed += delegate
            {
                form.Dispose();
            };
            form.Show(this);
        }

        /// <summary>
        /// Hides the 'Donate' button from the UI
        /// </summary>
        internal void HideDonateButton()
        {
            Invoke(new MethodInvoker(delegate
            {
                PbDonate.Visible = false;
            }));
        }
    }
}
