using nanoFramework.Hardware.Esp32;
using nanoFramework.Runtime.Native;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LINKIT.NBLTE
{
    public class Program
    {
        static SerialPort _serialPort;
        static string _apn = "sam.iot-provider.com";
        static int _preferedNetworkMode = 9;

        static bool _success = false;
        static int _retry = 0;
        static int _maximumRetry = 3;

        //static string _deviceId = "<YOUR-DEVICE-NAME>";
        //static string _hubName = "<YOUR-IOT-HUB-NAME>";
        static string _deviceId = "itnext01";
        static string _hubName = "itnext-weu-iot";

        //INFO https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-dev-guide-sas?tabs=node#authenticating-a-device-to-iot-hub
        static string _username = $"{_hubName}.azure-devices.net/{_deviceId}/?api-version=2021-04-12";

        //REMARK Generate SAS token for IoT Hub with VS Code
        //static string _password = $"<YOUR-SAS-TOKEN>";
        static string _password = $"SharedAccessSignature sr=itnext-weu-iot.azure-devices.net%2Fdevices%2Fitnext01&sig=eWnQOAIjOIivKijAGtSao3rCo2RuFyItl6vSoiy3Z2M%3D&se=1710956708";


        //INFO https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-devguide-messages-c2d
        static string _subTopic = $"devices/{_deviceId}/messages/devicebound/#";

        //INFO https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-devguide-messages-d2c
        static string _pubTopic = $"devices/{_deviceId}/messages/events/";

        static string _location = "unknown";

        //REMARK Send messages in minutes interval
        static int _telemetryFrequency = 5;

        public static void Main()
        {
            //REMARK Display available serial ports
            AvailableSerialPorts();

            //REMARK Open serial port
            do
            {
                _retry++;

                Notify("SerialPort", $"Attempt {_retry}", true);

                _success = OpenSerialPort();

            } while (!_success && _retry < _maximumRetry);

            CheckStatus();

            //REMARK Setup an event handler that will fire when a char is received in the serial device input stream
            _serialPort.DataReceived += SerialDevice_DataReceived;

            //REMARK Switch to prefered network mode
            SetNetworkSystemMode(false, _preferedNetworkMode);

            //REMARK Connect to narrow band network
            do
            {
                _retry++;

                Notify("APN", $"Attempt {_retry}", true);

                ConnectAccessPoint();

            } while (!_success && _retry < _maximumRetry);

            CheckStatus();

            //REMARK Connect to IoT Hub
            do
            {
                _retry++;

                Notify("MQTT", $"Attempt {_retry}", true);

                if (_retry > 1)
                {
                    //REMARK Disconnect on previous attempt error
                    DisconnectMQTT(true);
                }

                ConnectMQTT(5000);

            } while (!_success && _retry < _maximumRetry);

            CheckStatus();

            //REMARK Simcom module MQTT subscribe to C2D topic
            do
            {
                _retry++;

                Notify("Subscribe", $"Attempt {_retry}", true);

                ExecuteCommand($"AT+SMSUB=\"{_subTopic}\",1");

            } while (!_success && _retry < _maximumRetry);

            CheckStatus();

            //REMARK Get location
            do
            {
                _retry++;

                Notify("Location", $"Attempt {_retry}", true);

                GetLocation();

            } while (!_success && _retry < _maximumRetry);

            CheckStatus();

            //SendMessage(_location);

            //REMARK For debugging purposes, check MQTT connection status
            SendTestMessage(2);

            //REMARK Enable for Direct Method test
            //Thread.Sleep(Timeout.Infinite);

            DisconnectMQTT(false);

            DisconnectAccessPoint();

            CloseSerialPort();

            Sleep.EnableWakeupByTimer(new TimeSpan(0, 0, _telemetryFrequency, 0));
            Sleep.StartDeepSleep();

            Thread.Sleep(Timeout.Infinite);
        }

        /// <summary>
        /// Write Console Notification
        /// </summary>
        /// <param name="category"></param>
        /// <param name="message"></param>
        private static void Notify(string category, string message, bool isDebug)
        {
            var notification = $"[{category.PadRight(15, '.')}] {message}";

            if (isDebug)
            {
                //REMARK Development only
                Debug.WriteLine(notification);
            }
            else
            {
                //REMARK Production and development
                Console.WriteLine(notification);
            }
        }

        /// <summary>
        /// Get list of available serial ports
        /// </summary>
        private static void AvailableSerialPorts()
        {
            //REMARK  get available ports
            var ports = SerialPort.GetPortNames();

            Notify("Port", "Scan available ports", true);

            foreach (string port in ports)
            {
                Notify("Port", $"\t{port}", true);
            }
        }

        /// <summary>
        /// Configure and open the serial port for communication
        /// </summary>
        /// <param name="port"></param>
        /// <param name="baudRate"></param>
        /// <param name="parity"></param>
        /// <param name="stopBits"></param>
        /// <param name="handshake"></param>
        /// <param name="dataBits"></param>
        /// <param name="readBufferSize"></param>
        /// <param name="readTimeout"></param>
        /// <param name="writeTimeout"></param>
        /// <param name="watchChar"></param>
        private static bool OpenSerialPort(
            string port = "COM3",
            int baudRate = 115200,
            Parity parity = Parity.None,
            StopBits stopBits = StopBits.One,
            Handshake handshake = Handshake.XOnXOff,
            int dataBits = 8,
            int readBufferSize = 2048,
            int readTimeout = 1000,
            int writeTimeout = 1000,
            char watchChar = '\r')
        {
            //REMARK Configure GPIOs 16 and 17 to be used in UART2 (that's refered as COM3)
            Configuration.SetPinFunction(16, DeviceFunction.COM3_RX);
            Configuration.SetPinFunction(17, DeviceFunction.COM3_TX);

            if (_serialPort == null || !_serialPort.IsOpen)
            {
                _serialPort = new SerialPort(port);

                //REMARK Set parameters
                _serialPort.BaudRate = baudRate;
                _serialPort.Parity = parity;
                _serialPort.StopBits = stopBits;
                _serialPort.Handshake = handshake;
                _serialPort.DataBits = dataBits;

                //REMARK If dealing with massive data input, increase the buffer size
                _serialPort.ReadBufferSize = readBufferSize;
                _serialPort.ReadTimeout = readTimeout;
                _serialPort.WriteTimeout = writeTimeout;

                try
                {
                    //REMARK Open the serial port
                    _serialPort.Open();

                    Notify("SerialPort", $"Port {_serialPort.PortName} opened", false);
                }
                catch (Exception exception)
                {
                    Notify("SerialPort", $"{exception.Message}", true);
                }

                //REMARK Set a watch char to be notified when it's available in the input stream
                _serialPort.WatchChar = watchChar;
            }

            return _serialPort.IsOpen;
        }

        /// <summary>
        /// Execute AT (attention) Command on modem
        /// </summary>
        /// <param name="command"></param>
        /// <param name="wait"></param>
        private static void ExecuteCommand(string command, int wait = 1000)
        {
            _serialPort.WriteLine($"{command}\r");
            Thread.Sleep(wait);
        }

        /// <summary>
        /// Set system connectivity mode
        /// </summary>
        /// <paramref name="enableReporting">Enable auto reporting of the network system mode information</paramref>
        /// <paramref name="mode">1 GSM, 3 EGPRS, 7 LTE M1,9 LTE NB<paramref name="mode"/>
        internal static void SetNetworkSystemMode(bool enableReporting, int mode)
        {
            string systemMode = "LTE M1";

            switch (mode)
            {
                case 1:
                    systemMode = "GSM";
                    break;
                case 3:
                    systemMode = "EGPRS";
                    break;
                case 9:
                    systemMode = "LTE NB";
                    break;
                default:
                    mode = 7;
                    break;
            }

            //REMARK Toggle reporting
            var reportingEnabled = (enableReporting) ? 1 : 0;

            //REMARK Set mode
            ExecuteCommand($"AT+CNSMOD={reportingEnabled},{mode}");

            Notify("NetworkMode", $"Switching to {systemMode}", false);

            Thread.Sleep(5000);
        }

        /// <summary>
        /// Reset <see cref="_retry"/> and <see cref="_success"/> after successfull finish
        /// Reboot on failure
        /// </summary>
        private static void CheckStatus()
        {
            Notify("STATUS", $"\r\nSuccess: {_success}\r\n", true);

            if (_retry >= _maximumRetry && !_success) Power.RebootDevice();

            _success = false;
            _retry = 0;
        }

        /// <summary>
        /// Connect to the provider access point
        /// </summary>
        private static void ConnectAccessPoint()
        {
            try
            {
                //REMARK Indicates if password is required
                //ExecuteCommand("AT+CGPSIN");

                //REMARK Read Signal Quality
                ExecuteCommand("AT+CSQ");

                //REMARK Return current Operator
                ExecuteCommand("AT+COPS?");

                //REMARK Get Network APN in CAT-M or NB-IoT
                ExecuteCommand("AT+CGNAPN");

                //REMARK Define PDP Context, saves APN
                ExecuteCommand($"AT+CGDCONT=1,\"IP\",\"{_apn}\"");

                if (_retry > 2)
                {
                    //REMARK Deactive App Network on error
                    ExecuteCommand("AT+CNACT=0,0");
                }

                //REMARK App Network Active, assign IP
                ExecuteCommand("AT+CNACT=0,2");

                //REMARK Read IP
                ExecuteCommand("AT+CNACT?");

                Notify("APN", $"Connected to Access Point {_apn}", false);
            }
            catch (Exception exception)
            {
                Notify("APN", $"{exception.Message}", true);
            }
        }

        /// <summary>
        /// Event raised when message is received from the serial port
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void SerialDevice_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialDevice = (SerialPort)sender;
            ReadMessage(serialDevice);
        }

        /// <summary>
        /// Read message from the serial port
        /// </summary>
        /// <param name="serialDevice"></param>
        private static void ReadMessage(SerialPort serialDevice)
        {
            if (serialDevice.BytesToRead > 0)
            {
                Notify("Read", $"{serialDevice.BytesToRead} Bytes to read from {serialDevice.PortName}", true);

                byte[] buffer = new byte[serialDevice.BytesToRead];

                var bytesRead = serialDevice.Read(buffer, 0, buffer.Length);

                Notify("Read", $"Completed: {bytesRead} bytes were read from {serialDevice.PortName}", true);

                try
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    _success = true;

                    switch (message)
                    {
                        //REMARK Get Location from message
                        case string m when m.Contains("+CLBS:"):
                            SetLocation(message);
                            break;

                        //REMARK On error
                        case string m when m.Contains("ERROR"):
                            _success = false;
                            break;
                    }

                    Notify("Read", $"Acknowledgement:\r\n{message}\r\n", true);
                }
                catch (Exception exception)
                {
                    Notify("Read", $"Acknowledgement:\r\n{exception.Message}\r\n", true);

                    _success = false;
                }
            }
            else
            {
                //Notify("Read", "Noting to read", true);
            }
        }

        /// <summary>
        /// Disconnect to the provider access point
        /// </summary>
        private static void DisconnectAccessPoint()
        {
            //REMARK Simcom module MQTT open the disconnect from APN
            ExecuteCommand("AT+CNACT=0,0");

            Notify("APN", $"Disconnect", false);
        }

        /// <summary>
        /// Close the serial port
        /// </summary>
        private static void CloseSerialPort()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();

                Notify("SerialPort", $"Port closed", false);
            }
        }

        /// <summary>
        /// Connect to the Azure IoT Hub
        /// </summary>
        private static void ConnectMQTT(int wait)
        {
            try
            {
                //REMARK Simcom module MQTT parameter that sets the device/client id
                ExecuteCommand($"AT+SMCONF=\"CLIENTID\",\"{_deviceId}\"");

                //REMARK Set MQTT time to connect server
                ExecuteCommand("AT+SMCONF=\"KEEPTIME\",60");

                //REMARK Simcom module MQTT parameter that sets the server URL and port
                ExecuteCommand($"AT+SMCONF=\"URL\",\"{_hubName}.azure-devices.net\",\"8883\"");

                //REMARK Delete messages after they have been successfully sent
                ExecuteCommand("AT+SMCONF=\"CLEANSS\",1");

                //REMARK 
                ExecuteCommand("AT+SMCONF=\"QOS\",1");

                //REMARK Simcom module MQTT parameter that sets the api endpoint for the specific device
                ExecuteCommand($"AT+SMCONF=\"USERNAME\",\"{_username}\"");

                //REMARK Simcom module MQTT parameter that sets the secure access token
                ExecuteCommand($"AT+SMCONF=\"PASSWORD\",\"{_password}\"");

                //REMARK Simcom module MQTT open the connection
                ExecuteCommand("AT+SMCONN", wait);

                if (_success)
                {
                    Notify("MQTT", "IoT Hub connected", false);
                }
            }
            catch (Exception exception)
            {
                Notify("MQTT", $"{exception.Message}", true);

                ExecuteCommand("+CEDUMP=1");

                _success = false;
            }
        }

        /// <summary>
        /// Disconnect from the Azure IoT Hub
        /// </summary>
        /// <param name="skipSubscription">Only disconnect, skip unsunscribe</param>
        private static void DisconnectMQTT(bool skipSubscription)
        {
            if (!skipSubscription)
            {
                //REMARK Simcom module MQTT unsubscribe to D2C topic
                //DEBUG Raises Error, allready disconnected
                ExecuteCommand("AT+SMSTATE?");
                ExecuteCommand($"AT+SMUNSUB=\"{_subTopic}\"");
            }

            //REMARK Simcom module MQTT open the disconnect from hub
            ExecuteCommand("AT+SMDISC");

            Notify("MQTT", $"Disconnect", false);
        }

        /// <summary>
        /// Send message to the serial port
        /// </summary>
        /// <param name="message"></param>
        private static void SendMessage(string message)
        {
            try
            {
                Notify("Sending", $"Length : {message.Length}", true);

                //REMARK Simcom module MQTT subscribe to D2C topic
                ExecuteCommand($"AT+SMPUB=\"{_pubTopic}\",{message.Length},1,1");

                //REMARK Simcom module MQTT sends the message
                ExecuteCommand(message);

                Notify("Sending", $"{message} : {_success}", true);
            }
            catch (Exception exception)
            {
                Notify("Send", exception.Message.ToString(), true);
            }
        }

        /// <summary>
        /// Send X messages for testing purposes
        /// </summary>
        private static void SendTestMessage(int numberOfMessages)
        {
            for (int i = 0; i < numberOfMessages;)
            {
                var message = (!string.IsNullOrEmpty(_location)) ? $"{i}:{_location}" : $"Message number {i}";
                i++;

                SendMessage(message);
            }
        }

        /// <summary>
        /// Get current location using GSM network
        /// </summary>
        private static void GetLocation()
        {
            //REMARK Get list supported location commands
            //ExecuteCommand("AT+CLBS=?");

            //REMARK Query the LBS server's address
            ExecuteCommand("AT+CLBSCFG=0,3");

            //REMARK Get longitude latitude
            ExecuteCommand("AT+CLBS=1,0");
        }

        /// <summary>
        /// Set location (latitude, longitude)
        /// </summary>
        /// <param name="message">Modem acknowledgment message</param>
        internal static void SetLocation(string message)
        {
            _location = "unknown";
            _success = false;

            //REMARK  Acknowledgment pattern
            var m = Regex.Match(message, @"([^,][\d.]+)");

            var longitude = m.NextMatch().Value;
            var latitude = m.NextMatch().NextMatch().Value;

            if (string.IsNullOrEmpty(longitude))
            {
                //REMARK Get error code when empty
                m = Regex.Match(message, @"CLBS: (\d)");

                _location = $"{_location}: {m.Groups[1].Value}";
            }
            else
            {
                _location = $"{latitude},{longitude}";
            }

            _success = true;

            Notify("Location", $"{_location}", false);
        }
    }
}
