using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MulticastLibrary;

namespace Streamer
{
    /// <summary>
    /// Interaction logic for CreateEditWindow.xaml
    /// </summary>
    public partial class CreateEditWindow : Window
    {
        public MulticastDbEntry MulticastEntry { get; set; }

        public CreateEditWindow()
        {
            InitializeComponent();
            
        }

        public CreateEditWindow(MulticastDbEntry multicastEntry)
        {
            InitializeComponent();
            this.MulticastEntry = multicastEntry;
            if (MulticastEntry != null)
            {
                nameTextBox.Text = MulticastEntry.Name;
                firstOctetTextBox.Text = MulticastEntry.IpAddress.GetAddressBytes()[0].ToString();
                secondOctetTextBox.Text = MulticastEntry.IpAddress.GetAddressBytes()[1].ToString();
                thirdOctetTextBox.Text = MulticastEntry.IpAddress.GetAddressBytes()[2].ToString();
                fourthOctetTextBox.Text = MulticastEntry.IpAddress.GetAddressBytes()[3].ToString();
                portTextBox.Text = MulticastEntry.Port.ToString();
                bitrateTextBox.Text = MulticastEntry.Bitrate.ToString();
                loopCheckBox.IsChecked = MulticastEntry.Loop;
                
                fileTextBlock.Text = MulticastEntry.FilePath;
                var localAddressIndex = MulticastDatabase.LocalAddresses.FirstOrDefault(f => f.ToString() == MulticastEntry.OutputLocalAddress.ToString());
                outputComboBox.SelectedIndex = localAddressIndex == null ? 0 : MulticastDatabase.LocalAddresses.IndexOf(localAddressIndex);
            }
        }

        private void TextBoxIpAddress_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            int a;
            var senderTextBox = sender as TextBox;
            if(senderTextBox == null || senderTextBox.Text == "") return;
            if (!int.TryParse(senderTextBox.Text, out a)) senderTextBox.Text = "255";
            if (0 > a || a > 255) senderTextBox.Text = "255";
            if (senderTextBox.Text.Length == 3)
                ((UIElement) sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void BitrateTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            long a;
            var senderTextBox = sender as TextBox;
            if (senderTextBox == null || senderTextBox.Text == "") return;
            if (!long.TryParse(senderTextBox.Text, out a)) senderTextBox.Text = "1000000";
            if (0 > a || a > 500000000) senderTextBox.Text = "500000000";
        }

        private void PortTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            int a;
            var senderTextBox = sender as TextBox;
            if (senderTextBox == null || senderTextBox.Text == "") return;
            if (!int.TryParse(senderTextBox.Text, out a)) senderTextBox.Text = "255";
            if (0 > a || a > 65535) senderTextBox.Text = "65535";
        }

        private void okButton_Click(object sender, RoutedEventArgs e)
        {
            if (inputFormGrid.Children.OfType<TextBox>().Any(t => string.IsNullOrEmpty(t.Text)) && inputFormGrid.Children.OfType<ComboBox>().Any(t => string.IsNullOrEmpty(t.Text)))
            {
                MessageBox.Show("Please fill the form.", "Error");

            }
            else
            {
                var ipAddress = IPAddress.Parse(firstOctetTextBox.Text + "." + secondOctetTextBox.Text + "." + thirdOctetTextBox.Text + "." + fourthOctetTextBox.Text);
                var name = nameTextBox.Text;
                var port = Int32.Parse(portTextBox.Text);
                var bitrate = Int32.Parse(bitrateTextBox.Text);
                var loop = loopCheckBox.IsChecked ?? false;
                var filePath = fileTextBlock.Text as string;
                var outputLocalAddress = outputComboBox.SelectedItem as IPAddress;
                MulticastEntry = new MulticastDbEntry(name, ipAddress, port, outputLocalAddress, bitrate, loop, filePath);
                this.DialogResult = true;
                this.Close();
            }
        }

        private void fileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();

            //dlg.DefaultExt = ".ts";
            dlg.Filter = "MPEG-TS (*.ts)|*.ts";

            bool? result = dlg.ShowDialog();

            if (result == true)
            {
                var bitrate = MpegTsOperations.CalculateBitrate(dlg.FileName);
                if (bitrate == 0) MessageBox.Show("Bitrate calculation error. Please input the correct bitrate.");
                else bitrateTextBox.Text = bitrate.ToString();
                fileTextBlock.Text = dlg.FileName;
            }
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
