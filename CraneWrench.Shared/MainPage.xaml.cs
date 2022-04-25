using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.Exceptions;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CraneWrench
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        IBluetoothLE BLE = CrossBluetoothLE.Current;
        IAdapter Adapter = CrossBluetoothLE.Current.Adapter;
        IDevice ConnectedDevice = null;
        ICharacteristic StreamCharactertistic = null;

        ObservableCollection<IDevice> DeviceList = new ObservableCollection<IDevice>();

        public MainPage()
        {
            this.InitializeComponent();

            Loaded += OnLoaded;

            _scanButton.Click += OnScanButton_Click;
            _connectButton.Click += OnConnectButton_Click;
            _sendButton.Click += OnSendButton_Click;
            _comCheckButton.Click += OnComCheckButton_Click;
            Adapter.DeviceDiscovered += OnAdapter_DeviceDiscovered;
            Adapter.ScanTimeout = 5000;
            _deviceComboBox.ItemsSource = DeviceList;
            _deviceComboBox.SelectionChanged += OnDeviceComboBox_SelectionChanged;
            BLE.StateChanged += BLE_StateChanged;
        }

        async void OnComCheckButton_Click(object sender, RoutedEventArgs e)
            => await SendTextAsync("$9100*");

        void OnStreamCharactertistic_ValueUpdated(object sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
        {
            try
            {
                var bytes = e.Characteristic.Value;
                if (bytes != null && bytes.Length > 0)
                {
                    var text = Encoding.ASCII.GetString(bytes);
                    P42.Utils.Uno.MainThread.BeginInvokeOnMainThread(() =>
                    _terminalTextBlock.Text += text + "\n");
                }
                else
                {
                    P42.Utils.Uno.MainThread.BeginInvokeOnMainThread(() =>
                    _terminalTextBlock.Text += "{EMPTY RESPONSE}" + "\n");
                }
            }
            catch (Exception ex)
            {
                P42.Utils.Uno.MainThread.BeginInvokeOnMainThread(() =>
                    _bleState.Text = "Could not READ");
                System.Diagnostics.Debug.WriteLine($"MainPage.OnConnectButton_Click: COULD NOT SEND [{ex.Message}]");

            }
        }




        async void OnSendButton_Click(object sender, RoutedEventArgs e)
            => await SendTextAsync(_inputTextBox.Text);

        async Task SendTextAsync(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                var bytes = Encoding.ASCII.GetBytes(text);
                var checkSum = bytes.Aggregate(0, (p, v) => p ^ v);
                var checkSumText = checkSum.ToString("X2");

                text = $"{text}{checkSumText}";
                var payload = Encoding.ASCII.GetBytes(text + "\r");
                //var payloadAsHexText = new System.Runtime.Remoting.Metadata.W3cXsd2001.SoapHexBinary(payload).ToString();
                var payloadAsHexText = string.Join(' ', payload.Select(x => x.ToString("X2")));

                _terminalTextBlock.Text += $"> {text} [{payloadAsHexText}]\n";

                try
                {
                    await StreamCharactertistic.WriteAsync(payload);
                    System.Diagnostics.Debug.WriteLine($"MainPage.OnSendButtonClicked: message [{text}\\r] [{payloadAsHexText}] Sent");
                }
                catch (Exception ex)
                {
                    _bleState.Text = "Could not SEND";
                    System.Diagnostics.Debug.WriteLine($"MainPage.OnConnectButton_Click: COULD NOT SEND [{ex.Message}]");
                }

            }
        }



        string ParseResponse(byte[] bytes)
        {
            var resultHex = new StringBuilder();
            var resultText = new StringBuilder();
            foreach (var b in bytes)
            {
                resultText.Append((char)b);
                resultHex.Append(b.ToString("X2"));
            }

            return $"[{resultHex}] => \"{resultText}\"";
        }

        private void OnAdapter_DeviceDiscovered(object sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Device.Name))
                P42.Utils.Uno.MainThread.BeginInvokeOnMainThread(() => DeviceList.Add(e.Device));
        }

        async void OnDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _connectButton.IsEnabled = _deviceComboBox.SelectedItem is IDevice && !_connecting;
            await Task.Delay(50);
        }


        bool _connecting;
        async void OnConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_connecting || !(_deviceComboBox.SelectedItem is IDevice))
                return;

            _connecting = true;
            _sendButton.IsEnabled = _comCheckButton.IsEnabled = _connectButton.IsEnabled = _deviceComboBox.SelectedItem is IDevice && !_connecting;

            var popup = await P42.Uno.Controls.BusyPopup.CreateAsync("CONNECTING ...");
            try
            {
                if (_deviceComboBox.SelectedItem is IDevice device)
                {
                    await Adapter.ConnectToDeviceAsync(device);
                    _bleState.Text = "CONNECTED";
                    ConnectedDevice = device;
                    await DisplayConnectdDeviceServicesAndCharacteristics();

                    if (StreamCharactertistic != null)
                    {
                        StreamCharactertistic.ValueUpdated += OnStreamCharactertistic_ValueUpdated;
                        await StreamCharactertistic.StartUpdatesAsync();
                        System.Diagnostics.Debug.WriteLine($"MainPage.OnConnectButton_Click: SUBSCRIBED TO SERVICE:CHARACTERISTIC: [{StreamCharactertistic.Service.Id}] : [{StreamCharactertistic.Id}]");
                    }

                    _connectButton.IsEnabled = false;
                    _scanButton.IsEnabled = false;
                }
                else
                    _bleState.Text = BLE.State.ToString();
            }
            catch (DeviceConnectionException ex)
            {
                // ... could not connect to device
                _bleState.Text = "Could not connect to device";
                System.Diagnostics.Debug.WriteLine($"MainPage.OnConnectButton_Click: COULD NOT CONNECT [{ex.Message}]");
            }
            catch (Exception ex1)
            {
                _bleState.Text = "Could not connect to device";
                System.Diagnostics.Debug.WriteLine($"MainPage.OnConnectButton_Click: COULD NOT CONNECT [{ex1.Message}]");
            }
            await popup.PopAsync();

            _connecting = false;
            _sendButton.IsEnabled = _connectButton.IsEnabled = _comCheckButton.IsEnabled = _deviceComboBox.SelectedItem is IDevice && !_connecting;
        }

        async void OnScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_scanButton.IsEnabled)
                return;

            _scanButton.IsEnabled = false;
            DeviceList.Clear();

            var popup = await P42.Uno.Controls.BusyPopup.CreateAsync("Scanning ... ");

            await Task.Delay(50);

            try
            {
                Adapter.ScanMode = ScanMode.LowLatency;
                await Adapter.StartScanningForDevicesAsync();
            }
            catch (Exception ex)
            {
                _bleState.Text = "Could not scan for devices";
                System.Diagnostics.Debug.WriteLine($"MainPage.OnConnectButton_Click: COULD NOT SCAN [{ex.Message}]");
            }

            _scanButton.IsEnabled = true;

            await popup.PopAsync();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _bleState.Text = BLE.State.ToString();
            _scanButton.IsEnabled = BLE.State == BluetoothState.On;
        }

        bool alive;
        private void BLE_StateChanged(object sender, Plugin.BLE.Abstractions.EventArgs.BluetoothStateChangedArgs e)
        {
            _bleState.Text = BLE.State.ToString();

            if (!alive && BLE.State == BluetoothState.On)
            {
                OnScanButton_Click(null, null);
            }

            alive = true;

            _scanButton.IsEnabled = BLE.State == BluetoothState.On;
        }

        async Task DisplayConnectdDeviceServicesAndCharacteristics()
        {
            try
            {
                var services = await ConnectedDevice.GetServicesAsync();

                var result = string.Empty;
                foreach (var service in services)
                {
                    var serviceText = $"{service.Id}:{service.Name}";
                    result += serviceText + "\n";
                    System.Diagnostics.Debug.Write($"\nSERVICE: {serviceText}");
                    foreach (var characteristic in await service.GetCharacteristicsAsync())
                    {
                        var value = string.Empty;
                        if (characteristic.CanRead)
                        {
                            var bytes = await characteristic.ReadAsync();
                            if (characteristic.Name.Contains("String"))
                                value = System.Text.Encoding.ASCII.GetString(bytes);
                            else
                            {
                                foreach (var b in bytes)
                                    value += b.ToString("X2") + " ";
                            }
                        }
                        var accessText = $"{characteristic.Id}:[{(characteristic.CanRead ? 'R' : '-')}{(characteristic.CanWrite ? 'W' : '-')}{(characteristic.CanUpdate ? 'U' : '-')}]";
                        var props = characteristic.Properties;
                        var propertyText = $"[{((props & CharacteristicPropertyType.Broadcast) > 0 ? "B" : "-")}" +
                            $"{((props & CharacteristicPropertyType.Read) > 0 ? "R" : "-")}" +
                            $"{((props & CharacteristicPropertyType.WriteWithoutResponse) > 0 ? "w" : "-")}" +
                            $"{((props & CharacteristicPropertyType.Write) > 0 ? "W" : "-")}" +
                            $"{((props & CharacteristicPropertyType.Notify) > 0 ? "N" : "-")}" +
                            $"{((props & CharacteristicPropertyType.Indicate) > 0 ? "I" : "-")}" +
                            $"{((props & CharacteristicPropertyType.AuthenticatedSignedWrites) > 0 ? "A" : "-")}" +
                            $"{((props & CharacteristicPropertyType.ExtendedProperties) > 0 ? "E" : "-")}" +
                            $"{((props & CharacteristicPropertyType.NotifyEncryptionRequired) > 0 ? "n" : "-")}" +
                            $"{((props & CharacteristicPropertyType.IndicateEncryptionRequired) > 0 ? "i" : "-")}]";
                        var charText = $"{accessText}:{propertyText}:{characteristic.Name}:{(characteristic.CanRead ? " \"" + value + "\" " : "")}";
                        result += "\t" + charText + "\n";
                        System.Diagnostics.Debug.Write($"\n\tCHARACTERISTIC: {charText}");

                        if (characteristic.CanRead && characteristic.CanWrite && characteristic.CanUpdate)
                            StreamCharactertistic = characteristic;

                        foreach (var descriptor in await characteristic.GetDescriptorsAsync())
                        {
                            var dvalue = string.Empty;
                            var bytes = await descriptor.ReadAsync();
                            if (bytes != null && bytes.Length > 0)
                            {
                                if (descriptor.Name.Contains("String"))
                                    dvalue = System.Text.Encoding.ASCII.GetString(bytes);
                                else
                                {
                                    foreach (var b in bytes)
                                        dvalue += b.ToString("X2") + " ";
                                }
                            }
                            var decrText = $"{descriptor.Id}:{descriptor.Name}:\"{dvalue}\" ";
                            result += "\t\t" + decrText + "\n";
                            System.Diagnostics.Debug.Write($"\n\t\tDESCRIPTOR: {decrText}");
                        }
                    }
                    System.Diagnostics.Debug.Write("\n");
                }

                result += "DONE\n";
                System.Diagnostics.Debug.WriteLine("DONE");

                _dataTextBlock.Text = result;
            }
            catch (Exception ex)
            {
                _bleState.Text = "Could not query for services";
                System.Diagnostics.Debug.WriteLine($"MainPage.OnConnectButton_Click: COULD NOT QUERY FOR SERVICES [{ex.Message}]");
            }

        }
    }
}
