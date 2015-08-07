﻿// Copyright (c) Microsoft. All rights reserved.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Gpio;
using Microsoft.IoT.Devices.Input;
using Microsoft.IoT.Devices;
using Microsoft.IoT.Devices.Adc;
using System.Diagnostics;
using System.Collections.ObjectModel;
using Microsoft.IoT.Devices.Sensors;
using Windows.Devices.Adc;
using System.Threading.Tasks;
using Microsoft.IoT.Devices.Display;
using Windows.UI;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DeviceTester
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        #region Member Variables
        private AdcProviderManager adcManager;
        private IReadOnlyList<AdcController> adcControllers;
        private GpioController gpioController;
        private IGraphicsDisplay display;
        private bool isRunning;
        private string lastOutput;
        private Random rand;
        private Collection<IDevice> devices = new Collection<IDevice>();
        #endregion // Member Variables

        #region Constructors
        public MainPage()
        {
            this.InitializeComponent();
            rand = new Random();
        }
        #endregion // Constructors

        private void AddOutput(string output)
        {
            // Prefix DT
            output = DateTime.Now.ToString("HH:mm:ss") + " " + output;

            // Debug it
            Debug.WriteLine(output);

            // Add to the list
            OutputList.Items.Add(output);

            // If there is no selection or the current selection is the last entry, auto scroll
            if ((OutputList.SelectedItem == null) || ((string)OutputList.SelectedItem == lastOutput))
            {
                // Select the new output
                OutputList.SelectedItem = output;

                // Auto scroll
                OutputList.ScrollIntoView(output, ScrollIntoViewAlignment.Leading);
            }

            // Update last output
            lastOutput = output;
        }

        private async Task StartAsync()
        {
            if (isRunning) { return; }
            isRunning = true;
            StartButton.IsEnabled = false;
            await StartDevicesAsync();
            StopButton.IsEnabled = true;
        }

        private void StartAnalog()
        {
            // Analog sensor is on controller 1 for this sample
            var controller = adcControllers[1];

            // Create analog sensor
            var analogSensor = new AnalogSensor()
            {
                AdcChannel = controller.OpenChannel(0),
                ReportInterval = 500, // This demo doesn't need fast reports and it helps with responsiveness
            };

            // Subscribe to events
            analogSensor.ReadingChanged += AnalogSensor_ReadingChanged;

            // Add to device list
            devices.Add(analogSensor);
        }

        private async Task StartDisplayAsync()
        {
            // Create the display
            var disp = new ST7735()
            {
                ChipSelectLine = 0,
                ControllerName = "SPI0",
                DisplayType = ST7735DisplayType.RBlack,
                ModePin = gpioController.OpenPin(12),
                ResetPin = gpioController.OpenPin(16),
            };

            await disp.InitializeAsync();
            disp.ClearScreen(Colors.Black);

            // Store for future using generic interface
            display = disp;

            // Add to device list
            devices.Add(display);

            // Associate with display panel
            GraphicsPanel.Display = display;

            // Start updates
            GraphicsPanel.AutoUpdate = true;
        }

        private void StartPushButton()
        {
            // Create a pushbutton
            var pushButton = new PushButton()
            {
                Pin = gpioController.OpenPin(5),
            };

            // Click on press
            // pushButton.ClickMode = ButtonClickMode.Press;

            // Subscribe to events
            pushButton.Click += PushButton_Click;
            pushButton.Pressed += PushButton_Pressed;
            pushButton.Released += PushButton_Released;

            // Add to device list
            devices.Add(pushButton);
        }

        private void StartSwitches()
        {
            // Create switches
            var proximity = new Switch()
            {
                Pin = gpioController.OpenPin(6),
            };

            // Subscribe to events
            proximity.Switched += Proximity_Switched;

            // Add to device list
            devices.Add(proximity);
        }

        private void StartThumbstick()
        {
            // Thumbstick is on controller 0 for this sample
            var controller = adcControllers[0];

            // Create
            var thumbstick = new SS944()
            {
                ButtonPin = gpioController.OpenPin(25),
                ReportInterval = 500, // This demo doesn't need fast reports and it helps with responsiveness
                XChannel = controller.OpenChannel(0),
                YChannel = controller.OpenChannel(1),
            };

            // Subscribe to events
            thumbstick.ReadingChanged += Thumbstick_ReadingChanged;

            // Add to device list
            devices.Add(thumbstick);
        }

        private async Task StartDevicesAsync()
        {
            // Start GPIO
            gpioController = GpioController.GetDefault();
            if (gpioController == null)
            {
                AddOutput("GPIO Controller not found!");
                return;
            }

            // ADC
            // Create the manager
            adcManager = new AdcProviderManager();

            // Add providers
            adcManager.Providers.Add(
                new ADC0832()
                {
                    ChipSelectPin = gpioController.OpenPin(18),
                    ClockPin = gpioController.OpenPin(23),
                    DataPin = gpioController.OpenPin(24),
                });

            adcManager.Providers.Add(
                new MCP3208()
                {
                    ChipSelectLine = 0,
                    ControllerName = "SPI1",
                });

            // Get the well-known controller collection back
            adcControllers = await adcManager.GetControllersAsync();

            await StartDisplayAsync();
            //StartPushButton();
            //StartSwitches();
            StartAnalog();
            //StartThumbstick();
        }

        private void Stop()
        {
            if (!isRunning) { return; }
            isRunning = false;
            StopButton.IsEnabled = false;
            StopDevices();
            StartButton.IsEnabled = true;
        }

        private void StopDevices()
        {
            // Stop display rendering
            if (display != null)
            {
                GraphicsPanel.AutoUpdate = false;
                GraphicsPanel.Display = null;
            }

            for (int i = devices.Count -1; i >= 0; i--)
            {
                devices[i].Dispose();
                devices.RemoveAt(i);
            }

            if (adcManager != null)
            {
                adcManager.Dispose();
                adcManager = null;
                adcControllers = null;
            }
        }

        private void AnalogSensor_ReadingChanged(IAnalogSensor sender, AnalogSensorReadingChangedEventArgs args)
        {
            // Get reading
            var r = args.Reading;

            Debug.WriteLine(string.Format("Value: {0}  Ratio: {1}", r.Value, r.Ratio));
        }


        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            OutputList.Items.Clear();
        }

        private void PushButton_Click(IPushButton sender, EmptyEventArgs args)
        {
            Dispatcher.Run(() => AddOutput("Click"));
        }

        private void PushButton_Pressed(IPushButton sender, EmptyEventArgs args)
        {
            Dispatcher.Run(() => AddOutput("Pressed"));
        }

        private void PushButton_Released(IPushButton sender, EmptyEventArgs args)
        {
            Dispatcher.Run(() => AddOutput("Released"));
        }

        private void Proximity_Switched(object sender, bool e)
        {
            Dispatcher.Run(() =>
            {
                if (e)
                {
                    AddOutput("Close");
                }
                else
                {
                    AddOutput("Far");
                }
            });
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await StartAsync();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void Thumbstick_ReadingChanged(IThumbstick sender, ThumbstickReadingChangedEventArgs args)
        {
            // Get reading
            var r = args.Reading;

            Debug.WriteLine(string.Format("X: {0}  Y: {1}  Button: {2}", r.XAxis, r.YAxis, r.IsPressed));
        }
    }
}
