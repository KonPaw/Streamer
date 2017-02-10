using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading;
using Microsoft.Win32;
using MulticastLibrary;

namespace Streamer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MulticastDatabase MulticastDatabase { get; set; }
        public static string version { get; set; }
        private uint currentExecutionState = NativeMethods.ES_CONTINUOUS;


        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            MulticastDatabase = new MulticastDatabase();
            
            ///////Error if reading settings file fails and work
            
            if (!MulticastDatabase.GetLocalIpAddresses())
            {
                MessageBox.Show("No IPv4 network interfaces found. Program will now close", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
            try
            {
                MulticastDatabase.ReadSettingsFile();
                if(MulticastDatabase.FirstOrDefault(m => m.Streaming) != null)
                    BlockSleep(true);

            }
            catch (Exception)
            {
                MessageBox.Show("There was an error trying to read settings file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        private void start_Click(object sender, RoutedEventArgs e)
        {
            
            foreach (MulticastDbEntry multicast in dataGrid.SelectedItems)
            {
                if (multicast == null || multicast.Streaming) continue;

                startStreaming(multicast);
            }
            MulticastDatabase.SaveSettingsToFile();

        }

        private void dataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (MulticastDbEntry multicast in dataGrid.SelectedItems)
            {
                if (multicast == null || !multicast.Streaming) continue;
                multicast.Stop();
                multicast.Streaming = false;
            }
            MulticastDatabase.SaveSettingsToFile();
            BlockSleep(false);
        }

        private void startAllButton_Click(object sender, RoutedEventArgs e)
        {
            var multicasts = dataGrid.Items;
            foreach (MulticastDbEntry multicast in multicasts)
            {
                if (multicast == null || multicast.Streaming) continue;



                startStreaming(multicast);
            }
            MulticastDatabase.SaveSettingsToFile();
        }

        private void startStreaming(MulticastDbEntry multicast)
        {
            if (!System.IO.File.Exists(multicast.FilePath))
            {
                MessageBox.Show("File doesn't exist!", "Error");
                return;
            }
            if (multicast.StreamingThread == null || multicast.StreamingThread.IsAlive == false)
                multicast.StreamingThread = new Thread(multicast.Start);
            if (multicast.StreamingThread.IsAlive) return;

            var correctLocalIp = false;
            foreach (var ip in MulticastDatabase.LocalAddresses)
            {
                if (multicast.OutputLocalAddress.Equals(ip)) correctLocalIp = true;
            }
            if (!correctLocalIp)
            {
                MessageBox.Show("Local address mismatch");
                return;
            }

            multicast.StreamingThread.IsBackground = true;            
            multicast.StreamingThread.Start();
            multicast.Streaming = true;
            BlockSleep(true);
           

        }

        private void stopAllButton_Click(object sender, RoutedEventArgs e)
        {
            var multicasts = dataGrid.Items;
            foreach (MulticastDbEntry multicast in multicasts)
            {
                if (multicast == null || !multicast.Streaming) continue;
                multicast.Stop();
                multicast.Streaming = false;
            }
            MulticastDatabase.SaveSettingsToFile();
            BlockSleep(false);
        }
        

        private void addButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog {Filter = "MPEG-TS (*.ts)|*.ts"};

            bool? result = dlg.ShowDialog();

            if (result == true)
            {
               ShowCreateEditWindow(dlg.FileName);
                
            }

        }

        private void ShowCreateEditWindow(string filePath)
        {
            var bitrate = MpegTsOperations.CalculateBitrate(filePath);
            if (bitrate == 0)
            {
                MessageBox.Show("Bitrate calculation error. Please input the correct bitrate.");
                bitrate = 3000000;
            }

            
            var win2 = new CreateEditWindow();
           
            win2.DataContext = this;
            win2.CreateEdit.Title = "Adding entry";
            win2.fileTextBlock.Text = filePath;
            win2.bitrateTextBox.Text = bitrate.ToString();
            
            if (win2.ShowDialog() == true)
            {
                MulticastDatabase.Add(win2.MulticastEntry);
            }
            else return;

            MulticastDatabase.SaveSettingsToFile();
        }

        private void edit_Click(object sender, RoutedEventArgs e)
        {
            var multicast = dataGrid.SelectedItem as MulticastDbEntry;
            if (multicast == null) return;
                
            var editWin = new CreateEditWindow(multicast);
            editWin.DataContext = this;
            editWin.CreateEdit.Title = "Edit entry";
            if (editWin.ShowDialog() == true)
            {
                if (multicast.Streaming) multicast.Stop();
                dataGrid.SelectedItem = editWin.MulticastEntry;
                var multicastIndex = MulticastDatabase.IndexOf(MulticastDatabase.First(m => m == multicast));
                if (multicastIndex != -1)
                {
                    
                    MulticastDatabase[multicastIndex] = editWin.MulticastEntry;
                    if(multicast.Streaming) startStreaming(MulticastDatabase[multicastIndex]);
                }
                else MessageBox.Show("Selection error");
            }
            MulticastDatabase.SaveSettingsToFile();

        }

        private void copyButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (MulticastDbEntry multicast in dataGrid.SelectedItems)
            {
                if (multicast == null) continue;
                MulticastDatabase.Add(new MulticastDbEntry(multicast.Name, multicast.IpAddress, multicast.Port, multicast.OutputLocalAddress, multicast.Bitrate, multicast.Loop, multicast.FilePath));
                
            }
            MulticastDatabase.SaveSettingsToFile();
            
        }

        private void deleteButton_Click(object sender, RoutedEventArgs e)
        {
            var multicastList = new List<MulticastDbEntry>();
            foreach (MulticastDbEntry multicast in dataGrid.SelectedItems)
            {
                if (multicast == null) continue;
                multicastList.Add(multicast);
                
            }
            foreach(var multicast in multicastList)
                MulticastDatabase.DeleteMulticast(multicast);
            MulticastDatabase.SaveSettingsToFile();
            BlockSleep(false);
        }

        private void importSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var success = false;
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Configuration file (*.cfg)|*.cfg" };

            bool? result = dlg.ShowDialog();

            if (result == true)
            {
                if (MulticastDatabase.TryToReadSettings(dlg.FileName)) success = true;

            }
            else return;
             
            if (!success)
                MessageBox.Show("Błędny plik settings na wejściu.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            MulticastDatabase.SaveSettingsToFile();

           
        }

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                
                
                
                foreach (var file in files)
                {
                    switch(MulticastDatabase.DragDropCheck(file))
                    {
                        case "settings":
                            {
                                MulticastDatabase.SaveSettingsToFile();
                                
                                continue;
                            }
                        case "ts":
                            {
                                ShowCreateEditWindow(file);
                                continue;
                            }
                        default:
                            {
                                MessageBox.Show("Bad input file.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }
                    }                    
                
                
            }
        }


        private void DataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            edit_Click(null, null);
        }

        private void ExitButton_OnClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void UIElement_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                FullScreenButton_OnClick(null, null);
            }
            else
                DragMove();
        }

        private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void FullScreenButton_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void AboutButton_OnClick(object sender, RoutedEventArgs e)
        {
            var win2 = new AboutWindow();
            win2.ShowDialog();
        }

        private void BlockSleep(bool noSleep)
        {
            if (noSleep)
            {
                if (currentExecutionState != (NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED | NativeMethods.ES_AWAYMODE_REQUIRED) || currentExecutionState != (NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED))
                {
                    if (NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS |
                                                              NativeMethods.ES_SYSTEM_REQUIRED |
                                                              NativeMethods.ES_AWAYMODE_REQUIRED) == 0)
                    {
                        if (NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS |
                                                                  NativeMethods.ES_SYSTEM_REQUIRED) == 0)
                            MessageBox.Show(
                                "There was an error setting ThreadExecutionState. Please send info to pawel@konieczny.org.pl with as many details as possible.");
                        else
                            currentExecutionState = NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED;
                    }
                    else
                        currentExecutionState = NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED |
                                                NativeMethods.ES_AWAYMODE_REQUIRED;
                }
            }
            else
            {
                if (currentExecutionState != NativeMethods.ES_CONTINUOUS)
                {
                    var streamingMulticast = MulticastDatabase.FirstOrDefault(m => m.Streaming);
                    if (streamingMulticast == null)
                    {
                        if (NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS) == 0)
                            MessageBox.Show(
                                "There was an error setting ThreadExecutionState. Please send info to pawel@konieczny.org.pl with as many details as possible.");
                        else
                        {
                            currentExecutionState = NativeMethods.ES_CONTINUOUS;
                        }
                    }
                }
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint esFlags);
        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_AWAYMODE_REQUIRED = 0x00000040;
    }
}
