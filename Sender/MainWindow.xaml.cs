using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Sender
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public MainWindow()
        {
            DataContext = this;
            OutputFolder = ConfigurationManager.AppSettings.Get("outputFolder");
            InitializeComponent();
        }

        protected void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }

        protected void OnPropertyChanged(string propertyName)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        }
        public event PropertyChangedEventHandler PropertyChanged;

        private List<string> _fileNames;
        public List<string> FileNames
        {
            get => _fileNames;
            set
            {
                _fileNames = value;
                OnPropertyChanged("FileNames");
            }
        }

        private string _outputFolder;
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                _outputFolder = value;
                OnPropertyChanged("OutputFolder");
            }
        }

        private bool _isHost;
        public bool IsHost
        {
            get => _isHost;
            set
            {
                _isHost = value;
                OnPropertyChanged("IsHost");
            }
        }

        private bool _isReceiving;
        public bool IsReceiving
        {
            get => _isReceiving;
            set
            {
                _isReceiving = value;
                OnPropertyChanged("IsReceiving");
                OnPropertyChanged("IsNotReceiving");
            }
        }
        public bool IsNotReceiving
        {
            get => !_isReceiving;
        }

        private int _port = 25565;
        public int Port
        {
            get => _port;
            set
            {
                _port = value;
                OnPropertyChanged("Port");
            }
        }

        private bool _awaiting;
        public bool Awaiting
        {
            get => _awaiting;
            set
            {
                _awaiting = value;
                OnPropertyChanged("Awaiting");
            }
        }

        private double _progression;
        public double Progression
        {
            get => _progression;
            set
            {
                _progression = value;
                OnPropertyChanged("Progression");
            }
        }

        private bool _working;
        public bool Working
        {
            get => _working;
            set
            {
                _working = value;
                OnPropertyChanged("Working");
                OnPropertyChanged("NotWorking");
            }
        }
        public bool NotWorking
        {
            get => !_working;
        }

        private ObservableCollection<FileTransfer> _fileTransfers = new ObservableCollection<FileTransfer>();
        public ObservableCollection<FileTransfer> FileTransfers
        {
            get => _fileTransfers;
            set
            {
                _fileTransfers = value;
                OnPropertyChanged("FileTransfers");
            }
        }

        private void ExploreButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsReceiving)
            {
                var dialog = new CommonOpenFileDialog("Select file or folder")
                {
                    AllowNonFileSystemItems = true,
                    Multiselect = true,
                };

                if (dialog.ShowDialog(this) == CommonFileDialogResult.Ok)
                {
                    var filenames = dialog.FileNames;
                    FileNames = new List<string>(filenames);
                }
            }
            else
            {
                var dialog = new CommonOpenFileDialog("Select file or folder")
                {
                    AllowNonFileSystemItems = true,
                    IsFolderPicker = true,
                    Multiselect = false,
                };

                if (dialog.ShowDialog(this) == CommonFileDialogResult.Ok)
                {
                    OutputFolder = dialog.FileName;
                    SaveOutputFolder();
                }
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (Working)
            {
                return;
            }
            Awaiting = true;
            Progression = 0;
            Working = true;
            var isHost = IsHost;
            var isReceiving = IsReceiving;
            var port = Port;
            var address = IpBox.Text;
            var entries = FileNames?.ToList();

            Task.Run(() =>
            {
                TcpListener server = null;
                TcpClient client;
                NetworkStream stream;
                if (isHost)
                {
                    try
                    {
                        server = new TcpListener(new IPEndPoint(IPAddress.Parse("0.0.0.0"), port));
                        server.Start();
                        client = server.AcceptTcpClient();
                        stream = client.GetStream();
                    }
                    catch
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            Awaiting = false;
                        });
                        return;
                    }
                }
                else
                {
                    try
                    {
                        client = new TcpClient();
                        client.Connect(address, port);
                        stream = client.GetStream();
                    }
                    catch
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            Awaiting = false;
                        });
                        return;
                    }
                }

                Dispatcher.InvokeAsync(() =>
                {
                    Awaiting = false;
                });

                if (isReceiving)
                {
                    var reader = new BinaryReader(stream);
                    var fileCount = reader.ReadInt32();
                    var fileNames = new List<string>();
                    var fileSizes = new List<ulong>();
                    var fileTransfers = new List<FileTransfer>();
                    for (var i = 0; i < fileCount; i++)
                    {
                        var fileName = reader.ReadString();
                        var fileSize = reader.ReadUInt64();
                        fileNames.Add(fileName);
                        fileSizes.Add(fileSize);
                        fileTransfers.Add(new FileTransfer
                        {
                            Name = fileName,
                            Progression = 0,
                        });
                    }

                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach(var fileTransfer in fileTransfers)
                        {
                            FileTransfers.Add(fileTransfer);
                        }
                    });

                    for (var i = 0; i < fileCount; i++)
                    {
                        var iCpy = i;
                        var fileName = fileNames[iCpy];
                        var fileSize = fileSizes[iCpy];

                        var path = System.IO.Path.Combine(OutputFolder ?? "./", fileName);
                        var dir = System.IO.Path.GetDirectoryName(path);
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        using (var fileStream = File.OpenWrite(path))
                        {
                            ulong offset = 0;
                            while (offset < fileSize)
                            {
                                var buffer = reader.ReadBytes((int)Math.Min(fileSize - offset, 300000));
                                offset += (ulong)buffer.Length;

                                Dispatcher.InvokeAsync(() =>
                                {
                                    fileTransfers[iCpy].Progression = offset / (double)fileSize * 100;
                                    Progression = offset / (double)fileSize * 100;
                                });

                                fileStream.Write(buffer, 0, buffer.Length);
                            }
                            fileStream.Flush();
                        }
                    }
                }
                else
                {
                    var files = GetFiles(entries);
                    var writer = new BinaryWriter(stream);
                    int filesCount = files.Count;
                    writer.Write(filesCount);
                    var fileInfos = new List<FileInfo>();
                    var fileTransfers = new List<FileTransfer>();
                    foreach (var file in files)
                    {
                        var fileName = System.IO.Path.GetFileName(file);
                        var fileInfo = new FileInfo(file);
                        var fileSize = fileInfo.Length;

                        writer.Write(fileName);
                        writer.Write((ulong)fileSize);
                        fileInfos.Add(fileInfo);
                        fileTransfers.Add(new FileTransfer
                        {
                            Name = fileName,
                            Progression = 0,
                        });
                    }

                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var fileTransfer in fileTransfers)
                        {
                            FileTransfers.Add(fileTransfer);
                        }
                    });

                    for (int i = 0; i < fileInfos.Count; i++)
                    {
                        var iCpy = i;
                        FileInfo fileInfo = fileInfos[iCpy];
                        var fileSize = fileInfo.Length;

                        using (var fileStream = fileInfo.OpenRead())
                        {
                            var buffer = new byte[300000];
                            int read = 0;
                            int currentOffset = 0;
                            while ((read = fileStream.Read(buffer, 0, 300000)) > 0)
                            {
                                writer.Write(buffer.Take(read).ToArray());
                                currentOffset += read;
                                Dispatcher.InvokeAsync(() =>
                                {
                                    fileTransfers[iCpy].Progression = currentOffset / (double)fileSize * 100;
                                    Progression = currentOffset / (double)fileSize * 100;
                                });
                            }
                        }
                    }
                }

                client.Close();
                if (isHost)
                {
                    try
                    {
                        server.Stop();
                    }
                    catch
                    {
                        return;
                    }
                }
            }).ContinueWith((task) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    Working = false;
                });
            });
        }

        private List<string> GetFiles(List<string> entries)
        {
            var files = new List<string>();

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    files.AddRange(Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories));
                }
                else if (File.Exists(entry))
                {
                    files.Add(entry);
                }
            }
            
            return files.Distinct().ToList();
        }

        private void OutputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveOutputFolder();
        }

        private void SaveOutputFolder()
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings.Remove("outputFolder");
            config.AppSettings.Settings.Add("outputFolder", OutputFolder);
            config.Save(ConfigurationSaveMode.Minimal);
        }
    }

    public class ListToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is List<string> ? ((List<string>)value).Aggregate((a, b) => a + " ; " + b) : "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value == true ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IntStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is int ? value.ToString() : "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string)
            {
                if (int.TryParse((string)value, out int result))
                {
                    return result;
                }
            }
            return 0;
        }
    }

    public class FileTransfer : INotifyPropertyChanged
    {
        protected void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }

        protected void OnPropertyChanged(string propertyName)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
        }
        public event PropertyChangedEventHandler PropertyChanged;

        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        private double _progression;
        public double Progression
        {
            get => _progression;
            set
            {
                _progression = value;
                OnPropertyChanged("Progression");
            }
        }
    }
}
