using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WatercoolingMonitor
{
    using System;
    using System.ComponentModel;
    using System.Windows;
    using System.IO.Ports;
    using System.Management;
    using System.Windows.Forms;
    using System.Threading;
    using System.Collections.ObjectModel;
    using Reactive.Bindings;

    public partial class NotifyIconWrapper : Component
    {
        // コントローラに接続しているかどうか
        private bool _serialEstablished;

        // 使用率計測用
        private int[] _cpuUsageStore;
        private int[] _gpuUsageStore;
        private int _usageCount;

        // CPU使用率取得用のパフォーマンスカウンター
        private PerformanceCounter _counterCpuUsage = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);

        // GPU使用率・温度取得用のnvidia-smiコマンド
        private ProcessStartInfo _psiGpuUsage = new ProcessStartInfo
        {
            FileName = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
            Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits -l 1",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true
        };

        // nvidia-smiプロセス
        private Process _nvsmi;
        public Process Nvsmi { get => _nvsmi; set => _nvsmi = value; }

        // 接続チェックタイマー
        private System.Timers.Timer _timerConnectionCheck;

        // データ受信タイムアウト検知タイマー
        private System.Timers.Timer _timerReceiveTimeout;

        // メインウィンドウ（モニタリング）
        private MainWindow _windowMain = new MainWindow();

        // 設定オブジェクト
        private Settings _config = new Settings();

        /// <summary>
        /// 初期化
        /// </summary>
        public NotifyIconWrapper()
        {
            // コンポーネントの初期化
            InitializeComponent();

            // ポートスキャンを実行
            PortScan();

            // 設定ファイルの読込
            ConfigLoad();

            // 使用率算出用配列初期化
            _cpuUsageStore = new int[_config.AvgSeconds];
            _gpuUsageStore = new int[_config.AvgSeconds];

            // ViewModelにデバイス設定を反映
            for (int i = 1; i <= _config.Devices.Count; i++)
            {
                _windowMain.ViewModel.Params.Add(new ReactiveProperty<string>());
                // var foundItem = list.Find(item => item.Name == textBox1.Text);
                var dd = _config.Devices.Find(x => x.Order == i);
                if(dd == null)
                {
                    _windowMain.ViewModel.Labels.Add("N/A");
                }
                else
                {
                    _windowMain.ViewModel.Labels.Add(dd.Label);
                }
            }

            // nvidia-smiのパスを設定ファイルから読込

            if (System.IO.File.Exists(_config.NvSmiPath))
            {
                _psiGpuUsage.FileName = _config.NvSmiPath;
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("nvidia-smiが見つかりません。アプリケーションを終了します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Windows.Application.Current.Shutdown();
            }


            // nvidia-smiを起動→標準出力受信時イベント定義→監視開始
            _nvsmi = Process.Start(_psiGpuUsage);
            _nvsmi.OutputDataReceived += NvSmiDataReceived;
            _nvsmi.BeginOutputReadLine();

            // データ受信検知タイマーを初期化
            _timerReceiveTimeout = new System.Timers.Timer
            {
                AutoReset = false,
                Interval = _config.ReceiveTimeout
            };
            _timerReceiveTimeout.Elapsed += SerialPortReceiveTimeout;

            // ポートを設定ファイルから読込
            if (_config.ComPort != null)
            {
                if (toolStripMenuItem_Port.DropDownItems.Find(_config.ComPort, true).Length > 0)
                {
                    // 設定と同じポートがあれば接続する
                    SerialPortOpen(_config.ComPort);

                    // 選択状態を復元
                    foreach (ToolStripMenuItem item in toolStripMenuItem_Port.DropDownItems)
                    {
                        if (item.Name == _config.ComPort)
                        {
                            item.CheckState = CheckState.Indeterminate;
                            break;
                        }
                    }
                }
                else
                {
                    // なければクリア
                    _config.ComPort = null;
                }
            }

            // 切断検知タイマーを開始（5秒毎）
            _timerConnectionCheck = new System.Timers.Timer
            {
                Interval = _config.CheckInterval
            };
            _timerConnectionCheck.Elapsed += SerialPortConnectionCheck;
            _timerConnectionCheck.Enabled = true;

            // コンテキストメニューのイベントを設定
            toolStripMenuItem_Open.Click += ToolStripMenuItemOpenClick;
            toolStripMenuItem_Scan.Click += ToolStripMenuItemScanClick;
            toolStripMenuItem_Close.Click += ToolStripMenuItemCloseClick;
            toolStripMenuItem_Test.Click += ToolStripMenuItemTestClick;
            toolStripMenuItem_Exit.Click += ToolStripMenuItemExitClick;

            // 受信時のイベントを設定
            serialPort1.DataReceived += new System.IO.Ports.SerialDataReceivedEventHandler(SerialPortDataReceived);
        }

        /// <summary>
        /// コンテナ を指定して NotifyIconWrapper クラス を生成、初期化します。
        /// </summary>
        public NotifyIconWrapper(IContainer container)
        {
            container.Add(this);

            InitializeComponent();
        }

        /// <summary>
        /// "モニター表示"
        /// </summary>
        private void ToolStripMenuItemOpenClick(object sender, EventArgs e)
        {
            // MainWindow表示
            _windowMain.Show();
        }

        /// <summary>
        /// "ポート選択" サブメニュー
        /// </summary>
        private void ToolStripMenuItem_Port_SubMenuClick(object sender, EventArgs e)
        {
            // 切断処理
            ToolStripMenuItemCloseClick(null, null);

            // サブメニュー項目を取得
            ToolStripMenuItem item = (ToolStripMenuItem)sender;

            // 選択されたサブメニュー項目にチェックを入れる
            item.CheckState = CheckState.Indeterminate;

            // 選択されたポートに接続する
            _config.ComPort = item.Name;
            SerialPortOpen(_config.ComPort);

        }

        /// <summary>
        /// "ポートスキャン"
        /// </summary>
        private void ToolStripMenuItemScanClick(object sender, EventArgs e)
        {
            // 切断
            ToolStripMenuItemCloseClick(null, null);

            // スキャン
            PortScan();
        }

        /// <summary>
        /// 接続
        /// </summary>
        private void SerialPortOpen(string port, int rate = 9600)
        {
            // ポートが指定されていなければアイコンを(?)にして抜ける
            if (port == null)
            {
                notifyIcon1.Icon = Properties.Resources.WcMonError;
                return;
            }

            // 既に接続済なら切断する
            if (serialPort1.IsOpen == true)
            {
                serialPort1.Close();
            }

            // ボーレートとポートを設定
            serialPort1.BaudRate = rate;
            serialPort1.PortName = port;

            // 接続開始
            try
            {
                serialPort1.Open();

                // 成功したらフラグをtrueにしてアイコンを正常に変更
                notifyIcon1.Icon = Properties.Resources.WcMonUnknown;

                // データ受信検知タイマーを開始
                _timerReceiveTimeout.Enabled = true;
            }
            catch (Exception ex)
            {
                // 接続に失敗したらアイコンを(x)に変更
                notifyIcon1.Icon = Properties.Resources.WcMonError;
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// ポートスキャン本体
        /// </summary>
        private void PortScan()
        {
            // ポート選択サブメニュー初期化
            toolStripMenuItem_Port.DropDownItems.Clear();

            // シリアルポート取得
            string[] ports = SerialPort.GetPortNames();

            // 取得したシリアルポートをサブメニューに追加
            if (ports != null)
            {
                foreach (string port in ports)
                {
                    toolStripMenuItem_Port.DropDownItems.Add(new ToolStripMenuItem(port, null, ToolStripMenuItem_Port_SubMenuClick) { Name = port });
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("利用可能なシリアルポートがありません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        /// <summary>
        /// 接続状態チェック（5秒毎）
        /// </summary>
        private void SerialPortConnectionCheck(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (serialPort1.IsOpen == true)
            {

            }
            else
            {
                // 接続確立済だった場合は切断されたと判断
                if (_serialEstablished == true)
                {
                    notifyIcon1.BalloonTipTitle = "Watercooling Monitor";
                    notifyIcon1.BalloonTipText = "シリアル通信が切断されました";
                    notifyIcon1.BalloonTipIcon = ToolTipIcon.Error;
                    notifyIcon1.ShowBalloonTip(10000);

                    // 接続フラグをオフにしてアイコンを(x)に
                    _serialEstablished = false;
                    notifyIcon1.Icon = Properties.Resources.WcMonError;

                    // 再接続を試みる
                    SerialPortOpen(_config.ComPort);
                }
            }
        }

        /// <summary>
        /// データ受信タイムアウト処理
        /// </summary>
        private void SerialPortReceiveTimeout(object sender, System.Timers.ElapsedEventArgs e)
        {
            _timerReceiveTimeout.Enabled = false;
            notifyIcon1.BalloonTipTitle = "Watercooling Monitor";
            notifyIcon1.BalloonTipText = "データが受信できません。接続先が間違っているか、デバイスが正常に動作していない可能性があります。";
            notifyIcon1.BalloonTipIcon = ToolTipIcon.Info;
            notifyIcon1.ShowBalloonTip(0);
            notifyIcon1.Icon = Properties.Resources.WcMonUnknown;

            // 再接続を試みる
            SerialPortOpen(_config.ComPort);
        }

        /// <summary>
        /// 設定の保存
        /// </summary>
        private void ConfigSave()
        {
            System.IO.StreamWriter sw = new System.IO.StreamWriter("config.xml", false, new System.Text.UTF8Encoding(false));
            new System.Xml.Serialization.XmlSerializer(typeof(Settings)).Serialize(sw, _config);
            sw.Close();
        }

        /// <summary>
        /// 設定の読込
        /// </summary>
        private void ConfigLoad()
        {
            try
            {
                System.IO.StreamReader sr = new System.IO.StreamReader("config.xml", new System.Text.UTF8Encoding(false));
                _config = (Settings)new System.Xml.Serialization.XmlSerializer(typeof(Settings)).Deserialize(sr);
                sr.Close();
            }
            catch (Exception ex)
            {
                // ひとまずエラー時はなにもしない
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// "テストモード"
        /// </summary>
        private void ToolStripMenuItemTestClick(object sender, EventArgs e)
        {
            // フラグとメニューのアイコンを切り替える
            if (_config.TestMode == true)
            {
                _config.TestMode = false;
                toolStripMenuItem_Test.CheckState = CheckState.Unchecked;
            }
            else
            {
                _config.TestMode = true;
                toolStripMenuItem_Test.CheckState = CheckState.Checked;
            }
        }

        /// <summary>
        /// "切断"
        /// </summary>
        private void ToolStripMenuItemCloseClick(object sender, EventArgs e)
        {
            // 接続を切り接続フラグと接続ポートをリセット
            if (serialPort1.IsOpen == true)
            {
                serialPort1.Close();
            }
            _config.ComPort = null;
            _serialEstablished = false;

            // 各種タイマーを停止
            _timerReceiveTimeout.Enabled = false;
            _timerConnectionCheck.Enabled = false;

            // アイコンを(?)に
            notifyIcon1.Icon = Properties.Resources.WcMonStop;

            // 全ての項目のチェックを外す
            foreach (ToolStripMenuItem menu_item in toolStripMenuItem_Port.DropDownItems)
            {
                menu_item.CheckState = CheckState.Unchecked;
            }
        }

        /// <summary>
        /// "終了"
        /// </summary>
        private void ToolStripMenuItemExitClick(object sender, EventArgs e)
        {
            ConfigSave();
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// notifyIcon1バルーンチップ
        /// </summary>
        public void ShowBaloon(string title, string text, ToolTipIcon icon, int duration)
        {
            notifyIcon1.BalloonTipTitle = title;
            notifyIcon1.BalloonTipText = text;
            notifyIcon1.BalloonTipIcon = icon;
            notifyIcon1.ShowBalloonTip(duration);
        }

        /// <summary>
        /// データ受信時イベント
        /// </summary>
        private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // 受信
            string data = serialPort1.ReadLine();
            try
            {
                serialPort1.DiscardInBuffer();
            }
            catch (Exception ex)
            {
                // ひとまずエラー時はなにもしない
                Console.WriteLine(ex.Message);
            }

            // 「data:」から始まる回転数/温度/Duty比のデータが使用率の送信要求を兼ねる
            if (data.StartsWith("data:"))
            {
                // データ受信検知タイマーをリセット
                _timerReceiveTimeout.Enabled = false;

                // 接続確立
                if(_serialEstablished == false) {
                    _serialEstablished = true;
                    notifyIcon1.Icon = Properties.Resources.WcMonRun;
                }

                // ※先に送信する

                // 使用率は設定された秒数の平均値
                int cpuUsage = Convert.ToInt32(_cpuUsageStore.Average());
                int gpuUsage = Convert.ToInt32(_gpuUsageStore.Average());

                // カンマで連結し終端のセミコロンをつけて送信
                try
                {
                    serialPort1.Write("usage:" + cpuUsage + "," + gpuUsage + ";");
                }
                catch (Exception ex)
                {
                    // ひとまずエラー時はなにもしない
                    Console.WriteLine(ex.Message);
                }

                // UIが表示されていればデータを処理する
                if (_windowMain.IsVisible)
                {
                    // 受信したデータをパースする
                    data = data.Substring(5).TrimEnd();
                    string[] splitted = data.Split(new char[] { ',' });

                    for (int i = 0; i < splitted.Length; i++)
                    {
                        // デバイスリストに入れる
                        _config.Devices[i].SetValue(Convert.ToDouble(splitted[i]));
                        // デバイスリストからモニターに反映する
                        _windowMain.ViewModel.Params[_config.Devices[i].Order - 1].Value = _config.Devices[i].ValueString;
                    }
                }

                // データ受信検知タイマーを開始
                _timerReceiveTimeout.Enabled = true;
            }
            else
            {
                Console.WriteLine(data);
            }
        }

        /// <summary>
        /// nvidia-smiの標準出力を受信（1秒毎）
        /// </summary>
        private void NvSmiDataReceived(object sender, DataReceivedEventArgs e)
        {
            // ついでにCPU使用率も取得して格納
            _cpuUsageStore[_usageCount] = Convert.ToInt32(Math.Round(_counterCpuUsage.NextValue(), MidpointRounding.AwayFromZero));

            // GPU使用率を取得して格納
            _gpuUsageStore[_usageCount] = Convert.ToInt32(e.Data.TrimEnd());

            _usageCount++;
            if (_usageCount == _config.AvgSeconds)
            {
                _usageCount = 0;
            }
        }
    }
}
