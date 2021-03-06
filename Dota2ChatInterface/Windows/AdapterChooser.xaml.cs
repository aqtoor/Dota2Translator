﻿/*
Copyright (c) 2013 Patrik Sletmo

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Management;
using System.Threading;

namespace Dota2ChatInterface
{
    public partial class AdapterChooser : Window
    {
        #region Device listing

        // The "recommended" adapter. The field Found will be null if there is no recommended adapter.
        private Adapter DefaultAdapter;

        // All adapters found. Used in the advances layout.
        private Adapter[] AllAdapters;

        // Struct which stores the information about the adapter.
        struct Adapter
        {
            // The index used to retrieve the actual device.
            public int Index;
            // The MAC address of the adapter.
            public String MAC;
            // The IP address of the adapter.
            public String IP;
            // The description of the adapter.
            public String Description;
            // Whether or not this adapter is valid.
            public Boolean Found;
        };

        #region DLLImport

        // Returns a list of available devices.
        [DllImport("Dota2ChatDLL.dll")]
        [return: MarshalAs(UnmanagedType.BStr)]
        public static extern string GetDeviceList();

        // Returns a pointer to the specified device.
        [DllImport("Dota2ChatDLL.dll")]
        public static extern IntPtr GetDevice(int num);

        #endregion

        #endregion        

        private Boolean HasDefaultAdapter = false;
        private String DefaultAdapterMAC = "";

        private delegate void VoidDelegate();
        private delegate void LaunchProgramDelegate(int deviceIndex);

        public AdapterChooser()
        {
            InitializeComponent();

            // Register window loaded listener.
            Loaded += AdapterChooser_Loaded;
        }

        // Loads the available adapters in the system.
        private void LoadAdapters()
        {
            String data = null;
            try
            {
                // Try to receive adapter data from WinPcap.
                data = GetDeviceList();
            }
            catch (System.IO.FileNotFoundException)
            {
                // Microsoft Visual C++ 2010 Redistributable is not installed, inform the user.
                MessageBox.Show("It appears that Microsoft Visual C++ 2010 Redistributable is not installed, please run the installer found in the installation directory and then re-run this program.", "Could not detect any device");

                // Close the window.
                Dispatcher.Invoke(Delegate.CreateDelegate(typeof(VoidDelegate), this, typeof(AdapterChooser).GetMethod("CloseWindow")), new object[] { });
                return;
            }
            catch (DllNotFoundException)
            {
                // WinPcap is not installed, inform the user.
                MessageBox.Show("It appears that WinPcap is not installed, please run the WinPcap installer found in the installation directory and then re-run this program.", "Could not detect any device");

                // Close the window.
                Dispatcher.Invoke(Delegate.CreateDelegate(typeof(VoidDelegate), this, typeof(AdapterChooser).GetMethod("CloseWindow")), new object[] { });
                return;
            }

            // Split the received data in lines.
            String[] deviceData = data.Split('\n');

            // The format has 3 fields (lines) per adapter, parse the data accordingly.
            AllAdapters = new Adapter[(deviceData.Length - 1) / 3];
            for (int i = 0; i < AllAdapters.Length; i++)
            {
                // Put all the fields in an Adapter struct.
                Adapter adapter = new Adapter();
                adapter.Index = i;
                adapter.MAC = deviceData[i * 3];
                adapter.IP = deviceData[i * 3 + 1];
                adapter.Description = deviceData[i * 3 + 2];
                adapter.Found = true;

                if (HasDefaultAdapter)
                {
                    // Check if this is our default adapter.
                    if (adapter.MAC.ToLower().Equals(DefaultAdapterMAC.ToLower()))
                    {
                        Dispatcher.Invoke(Delegate.CreateDelegate(typeof(LaunchProgramDelegate), this, typeof(AdapterChooser).GetMethod("LaunchProgram")), new object[] { adapter.Index });
                        return;
                    }
                }

                AllAdapters[i] = adapter;
            }

            // Attempt to find the default adapter.
            DefaultAdapter = GetDefaultAdapter(AllAdapters);

            // Display the results using the main thread.
            Dispatcher.Invoke(Delegate.CreateDelegate(typeof(VoidDelegate), this, typeof(AdapterChooser).GetMethod("DisplayAdapterResults")), new object[] {} );

            if (HasDefaultAdapter)
            {
                // No match for the saved default adapter was found.
                Dispatcher.Invoke(Delegate.CreateDelegate(typeof(VoidDelegate), this, typeof(AdapterChooser).GetMethod("ShowMainLayout")), new object[] { });
            }
        }

        // Closes the window from another thread.
        public void CloseWindow()
        {
            Close();
        }

        // Displays the result after the adapters has been loaded.
        public void DisplayAdapterResults()
        {
            // Enable the buttons.
            DefaultButton.IsEnabled = true;
            AdvancedButton.IsEnabled = true;

            if (DefaultAdapter.Found)
            {
                // Set the default adapter as recommended.
                DefaultIP.Content = DefaultAdapter.IP;
                DefaultDescription.Content = DefaultAdapter.Description;
            }
            else
            {
                // Display all the adapters.
                AdvancedButton_Click(null, null);
            }
        }

        // Tries to identify the default adapter.
        private Adapter GetDefaultAdapter(Adapter[] adapters)
        {
            try
            {
                // Select all adapters in the system.
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("Select MACAddress,PNPDeviceID FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL AND PNPDeviceID IS NOT NULL");
                ManagementObjectCollection mObject = searcher.Get();

                // Iterate over the adapters.
                foreach (ManagementObject obj in mObject)
                {
                    string pnp = obj["PNPDeviceID"].ToString();

                    // Only check against real world adapters (Hamachi, etc will be excluded).
                    if (pnp.Contains("PCI\\"))
                    {
                        // Retrieve the MAC address and check against the adapters from winpcap.
                        string mac = obj["MACAddress"].ToString();
                        mac = mac.Replace(":", string.Empty);

                        foreach (Adapter adapter in adapters)
                        {
                            // Return the adapter if the MAC addresses matches.
                            if (adapter.MAC.ToLower().Equals(mac.ToLower()))
                                return adapter;
                        }
                    }
                }
            }
            catch (COMException)
            {
                // Don't crash the application because of missing service.
                MessageBox.Show("The service 'Windows Management Instrumentation' has to be running in order to determine the default network adapter. All adapters will be shown.", "Unable to find default adapter.");
            }
            catch (Exception)
            {
                // Make sure no other exception is thrown.
            }

            // Return an empty adapter (Found = False).
            return new Adapter();
        }

        // Called when the window has been loaded.
        private void AdapterChooser_Loaded(object sender, EventArgs args)
        {
            // Check if we have a saved default adapter.
            SettingsHandler handler = SettingsHandler.GetInstance();
            if (handler.UseDefaultAdapter)
            {
                // Attempt to load the default adapter.
                HasDefaultAdapter = true;
                DefaultAdapterMAC = handler.DefaultAdapterMAC;
                LoadingDefaultLayout.Visibility = Visibility.Visible;
                MainLayout.Visibility = Visibility.Collapsed;
            }

            // Register button listeners.
            DefaultButton.Click += DefaultButton_Click;
            AdvancedButton.Click += AdvancedButton_Click;
            SelectButton.Click += SelectButton_Click;

            // Load network devices.
            Thread t = new Thread(new ThreadStart(LoadAdapters));
            t.Start();
        }

        // Called when DefaultButton has been clicked.
        private void DefaultButton_Click(object sender, EventArgs args)
        {
            // Check if we want to save this as a default adapter.
            if (AlwaysUseMain.IsChecked.Value)
            {
                // Save the default adapter.
                SettingsHandler handler = SettingsHandler.GetInstance();
                handler.DefaultAdapterMAC = DefaultAdapter.MAC.ToLower();
                handler.UseDefaultAdapter = true;
                handler.SaveSettings();
            }

            // Launch the program using the default adapter.
            LaunchProgram(DefaultAdapter.Index);
        }

        // Called when AdvancedButton has been clicked.
        private void AdvancedButton_Click(object sender, EventArgs args)
        {
            // Create list items for each adapter.
            AvailableNetworkAdapters.Items.Clear();
            foreach (Adapter adapter in AllAdapters)
            {
                NetworkAdapterItem item = new NetworkAdapterItem();
                item.IP = adapter.IP;
                item.MAC = adapter.MAC;
                item.Description = adapter.Description;
                item.AdapterIndex = adapter.Index;

                AvailableNetworkAdapters.Items.Add(item);
            }
            
            // Display the advanced layout.
            AdvancedLayout.Visibility = Visibility.Visible;
            MainLayout.Visibility = Visibility.Collapsed;
            Height += 200;
        }

        // Called when SelectButton has been clicked.
        private void SelectButton_Click(object sender, EventArgs args)
        {
            // Check if an adapter has been selected.
            if (AvailableNetworkAdapters.SelectedIndex == -1)
            {
                MessageBox.Show("No adapter selected!");
                return;
            }

            // Start the program using the selected adapter.
            NetworkAdapterItem adapter = (NetworkAdapterItem)AvailableNetworkAdapters.SelectedItem;

            // Check if we want to save this as a default adapter.
            if (AlwaysUseAdvanced.IsChecked.Value)
            {
                // Save the selected adapter.
                SettingsHandler handler = SettingsHandler.GetInstance();
                handler.DefaultAdapterMAC = adapter.MAC;
                handler.UseDefaultAdapter = true;
                handler.SaveSettings();
            }

            LaunchProgram(adapter.AdapterIndex);
        }

        // Starts the program using the specified adapter.
        public void LaunchProgram(int deviceIndex)
        {
            // Retrieve a pointer to the adapter.
            IntPtr devicePointer = GetDevice(deviceIndex);

            // Create the main window using the pointer and display it.
            new MainWindow(devicePointer).Show();

            // Close this window.
            Close();
        }

        public void ShowMainLayout()
        {
            MessageBox.Show("The default adapter could not be found, please select a new one to start from.\n\nNote that your current default adapter will not be overwritten if you don't explicitly choose to.", "Failed to start using the default adapter");

            MainLayout.Visibility = Visibility.Visible;
            LoadingDefaultLayout.Visibility = Visibility.Collapsed;
        }
    }
}
