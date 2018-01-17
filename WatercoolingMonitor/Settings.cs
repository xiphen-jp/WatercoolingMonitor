using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace WatercoolingMonitor
{
    public class Settings
    {
        private string _comPort;
        private int _checkInterval;
        private int _recvTimeout;
        private int _dataCount;
        private string _nvsmiPath; 
        private int _waterTempLimit;
        private int _avgSeconds;
        private bool _testMode;
        private List<Device> _devices;

        // 接続するシリアルポート
        public string ComPort
        {
            get { return _comPort; }
            set { _comPort = value; }
        }
        // 切断チェック間隔
        public int CheckInterval
        {
            get { return _checkInterval; }
            set { _checkInterval = value; }
        }
        // データ受信タイムアウト
        public int ReceiveTimeout
        {
            get { return _dataCount; }
            set { _dataCount = value; }
        }
        // データ受信タイムアウト
        public int DataCount
        {
            get { return _recvTimeout; }
            set { _recvTimeout = value; }
        }
        // nvidia-smi.exeのパス
        public string NvSmiPath
        {
            get { return _nvsmiPath; }
            set { _nvsmiPath = value; }
        }
        // 水温警告温度
        public int WaterTempLimit
        {
            get { return _waterTempLimit; }
            set { _waterTempLimit = value; }
        }
        // 平均使用率計測秒数
        public int AvgSeconds
        {
            get { return _avgSeconds; }
            set { _avgSeconds = value; }
        }
        // テストモードフラグ
        public bool TestMode
        {
            get { return _testMode; }
            set { _testMode = value; }
        }
        // デバイスリスト
        public List<Device> Devices
        {
            get { return _devices; }
            set { _devices = value; }
        }

        public Settings()
        {
            _comPort = null;
            _checkInterval = 5000;
            _recvTimeout = 10000;
            _dataCount = 14;
            _nvsmiPath = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
            _waterTempLimit = 50;
            _avgSeconds = 5;
            _testMode = false;
            _devices = new List<Device>();
        }
    }

    public class Device
    {
        private double _value;
        private string _valueString;
        private int _order;
        private string _label;
        private string _format;
        private string _unit;
        private int _alertLevel;
        private int? _alertThreshold;
        private string _alertComparison;
        private string _alertMessage;

        public void SetValue(double value)
        {
            _value = value;
            // 画面出力用文字列
            switch (_format)
            {
                case "int":
                    _valueString = Math.Round(_value, MidpointRounding.AwayFromZero).ToString() + " " + _unit;
                    break;
                case "1f":
                    _valueString = String.Format("{0:N1}", _value) + " " + _unit;
                    break;
                case "2f":
                    _valueString = String.Format("{0:N2}", _value) + " " + _unit;
                    break;
                default:
                    _valueString = _value.ToString();
                    break;
            }
            // 警告処理
            if (_alertLevel == 0)
            {
                return;
            }
            switch (_alertComparison)
            {
                case "more":
                    if (_value >= _alertThreshold)
                    {
                        break;
                    }
                    return;
                case "more than":
                    if (_value > _alertThreshold)
                    {
                        break;
                    }
                    return;
                case "less":
                    if (_value <= _alertThreshold)
                    {
                        break;
                    }
                    return;
                case "less than":
                    if (_value < _alertThreshold)
                    {
                        break;
                    }
                    return;
                case "equal":
                    if (_value == _alertThreshold)
                    {
                        break;
                    }
                    return;
            }
            switch (_alertLevel)
            {
                case 1:
                    // 通知を一定時間表示
                    break;
                case 2:
                    // 通知を連続して表示
                    break;
                case 3:
                    // メッセージボックスを表示
                    // System.Windows.Forms.MessageBox.Show(_alertMessage, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
            }
        }

        public string ValueString { get => _valueString; set => _valueString = value; }
        public int Order { get => _order; set => _order = value; }
        public string Label { get => _label; set => _label = value; }
        public string Format { get => _format; set => _format = value; }
        public string Unit { get => _unit; set => _unit = value; }
        public int AlertLevel { get => _alertLevel; set => _alertLevel = value; }
        public int? AlertThreshold { get => _alertThreshold; set => _alertThreshold = value; }
        public string AlertComparison { get => _alertComparison; set => _alertComparison = value; }
        public string AlertMessage { get => _alertMessage; set => _alertMessage = value; }
    }
}
