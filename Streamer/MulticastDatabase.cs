using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using MulticastLibrary;
using System.Xml;
using System.Xml.Linq; 


namespace Streamer
{
    public class MulticastDatabase : ObservableCollection<MulticastDbEntry>
    {
        public static ObservableCollection<IPAddress> LocalAddresses { get; set; }
        public static int MulticastId { get; set; }

        public MulticastDatabase()
        {
            LocalAddresses = new ObservableCollection<IPAddress>();
            MulticastId = 1;
        }

        public void AddMulticast(string name, IPAddress ipAddress, int port, IPAddress localIpAddress, int bitrate,
            string filePath)
        {
            Add(new MulticastDbEntry(name, ipAddress, port, localIpAddress, bitrate, false, filePath));
        }

        public void DeleteMulticast(MulticastDbEntry multicast)
        {
            multicast.Stop();
            Remove(multicast);
        }


        public void SaveSettingsToFile(string filePath = "settings.cfg")
        {
            var settingsFile = new XDocument();
            settingsFile.Add(new XElement("settings"));

            foreach(var multicast in this)
            {
                var multicastEntry = new XElement("multicastEntry",
                    new XElement("name", multicast.Name),
                    new XElement("ipAddress", multicast.IpAddress.ToString()),
                    new XElement("port", multicast.Port),
                    new XElement("localAddress", multicast.OutputLocalAddress),
                    new XElement("bitrate", multicast.Bitrate),
                    new XElement("loop", multicast.Loop.ToString()),
                    new XElement("file", multicast.FilePath),
                    new XElement("streaming", multicast.Loop ? multicast.Streaming.ToString() : "False"));
                settingsFile.Root.Add(multicastEntry);

            }

            using (var writer = new XmlTextWriter(filePath, new UTF8Encoding(false)))
            {
                writer.Formatting = Formatting.Indented;
                settingsFile.Save(writer);
                               
            }
        }

        public bool TryToReadSettings(string filePath)
        {
           
            try
            {
                ReadSettingsFile(filePath);
            }
            catch (Exception)
            {
                return false;
            }
            return true;

           

        }

        public void ReadSettingsFile(string filePath = "settings.cfg")
        {
            if (!File.Exists(filePath)) return;
            var settings = new XDocument();
            var inputFile = File.ReadAllText(filePath);

            settings = XDocument.Parse(inputFile);

            if (settings.Root != null)
                foreach(var entry in settings.Root.Elements("multicastEntry"))
                {
                    var name = entry.Element("name").Value;
                    var ipAddress = IPAddress.Parse(entry.Element("ipAddress").Value);
                    var port = Int32.Parse(entry.Element("port").Value);
                    var localAddress = IPAddress.Parse(entry.Element("localAddress").Value);
                    var bitrate = Int32.Parse(entry.Element("bitrate").Value);
                    var loop = Boolean.Parse(entry.Element("loop").Value);
                    var file = entry.Element("file").Value;
                    var streaming = Boolean.Parse(entry.Element("streaming").Value);
                    if (streaming && !LocalAddresses.Contains(localAddress)) streaming = false;
                    this.Add(new MulticastDbEntry(name, ipAddress, port, localAddress, bitrate, loop, file, streaming));
                }
        }

        public string DragDropCheck(string filePath)
        {
            if (!File.Exists(filePath) || (Path.GetExtension(filePath) != ".cfg" && Path.GetExtension(filePath) != ".ts")) return "error";
            if (Path.GetExtension(filePath) == ".cfg") return TryToReadSettings(filePath) ? "settings" : "error";
            else return "ts";
        }

        public bool GetLocalIpAddresses()
        {
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return false;
            var host = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (var ip in host)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    LocalAddresses.Add(ip);
                }
            }
            return LocalAddresses.Count != 0;
        }
        
    }


    public class MulticastDbEntry : INotifyPropertyChanged
    {
        private int id;
        private string name;
        private IPAddress ipAddress;
        private int port;
        private IPAddress outputLocalAddress;
        private int bitrate;
        private bool loop;
        private string filePath;
        private bool streaming;
        private MulticastServer _multicastServer;

        public Thread StreamingThread { get; set; }
        public string Name
        {
            get
            {
                return name;
            }

            set
            {
                name = value;
                OnPropertyChanged("Name");
            }
        }
        public IPAddress IpAddress
        {
            get
            {
                return ipAddress;
            }

            set
            {
                ipAddress = value;
                OnPropertyChanged("IpAddress");
            }
        }
        public int Port
        {
            get
            {
                return port;
            }

            set
            {
                port = value;
                OnPropertyChanged("Port");
            }
        }
        public IPAddress OutputLocalAddress
        {
            get
            {
                return outputLocalAddress;
            }

            set
            {
                outputLocalAddress = value;
                OnPropertyChanged("OutputLocalAddress");
            }
        }
        public int Bitrate
        {
            get
            {
                return bitrate;
            }

            set
            {
                bitrate = value;
                OnPropertyChanged("Bitrate");
            }
        }
        public bool Loop
        {
            get
            {
                return loop;
            }

            set
            {
                loop = value;
                OnPropertyChanged("Loop");
            }
        }
        public string FilePath
        {
            get
            {
                return filePath;
            }

            set
            {
                filePath = value;
                OnPropertyChanged("FilePath");
                OnPropertyChanged("FileName");
            }
        }
        public bool Streaming
        {
            get
            {
                return streaming;
            }

            set
            {
                streaming = value;
                OnPropertyChanged("Streaming");
            }
        }
        public string FileName => Path.GetFileName(filePath);


        public MulticastDbEntry(string name, IPAddress ipAddress, int port, IPAddress outputLocalAddress, int bitrate,
            bool loop, string filePath)
        {
            this.name = name;
            this.ipAddress = ipAddress;
            this.port = port;
            this.outputLocalAddress = outputLocalAddress;
            this.bitrate = bitrate;
            this.loop = loop;
            this.filePath = filePath;
            streaming = false;
            id = MulticastDatabase.MulticastId;
            MulticastDatabase.MulticastId++;
            
        }
        public MulticastDbEntry(string name, IPAddress ipAddress, int port, IPAddress outputLocalAddress, int bitrate,
            bool loop, string filePath, bool streaming)
        {
            this.name = name;
            this.ipAddress = ipAddress;
            this.port = port;
            this.outputLocalAddress = outputLocalAddress;
            this.bitrate = bitrate;
            this.loop = loop;
            this.filePath = filePath;
            this.streaming = streaming;
            if (streaming)
            {
                this.StreamingThread = new Thread(new ThreadStart(this.Start));
                this.StreamingThread.IsBackground = true;
                this.StreamingThread.Start();
            }
            id = MulticastDatabase.MulticastId;
            MulticastDatabase.MulticastId++;
            
        }


        public void Start()
        {
            _multicastServer?.Dispose();
            using (_multicastServer = new MulticastServer(ipAddress, port, outputLocalAddress))
            {
                if(loop) _multicastServer.SendMpegTsLoop(filePath, bitrate);
                else _multicastServer.SendMpegTs(filePath, bitrate); 
            }
            Streaming = false;
        }

        public void Stop()
        {
            _multicastServer?.Dispose();
        }



        

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }

    public class IPAddressConverter : IValueConverter
    {
        public object Convert(object value, Type targeType, object parameter, CultureInfo culture)
        {
            var ipAddress = value as IPAddress;
            if (ipAddress != null) return ipAddress.ToString();
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targeType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            IPAddress ipAddress;

            if(text!=null && IPAddress.TryParse(text, out ipAddress)) return ipAddress;
            return DependencyProperty.UnsetValue;
        }
    }
    

}
