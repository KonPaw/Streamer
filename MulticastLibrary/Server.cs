using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace MulticastLibrary 
{
    public class Server : IDisposable
    {
        private readonly IPAddress ipAddress;
        private readonly int port;
        private readonly UdpClient udpClient;
        private IPEndPoint endPoint;
        private byte[] mpegTsChunk;
        private bool disposed = false;
        
        public Server(IPAddress ipAddress, int port, IPAddress localAddress)
        {
            this.ipAddress = ipAddress;
            this.port = port;
            udpClient = new UdpClient();
            udpClient.JoinMulticastGroup(ipAddress);
            endPoint = new IPEndPoint(ipAddress, port);
            mpegTsChunk = new byte[188];
            //buffer = new byte[4 * 1024]
        }

        public bool SendMessage(string message)
        {
            try
            {
                byte[] buffer = Encoding.Unicode.GetBytes(message);
                udpClient.Send(buffer, buffer.Length, endPoint);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public void SendMpegTs(string filePath, int bitRate = 0)
        {
            //////////IF bitrate=0, nalezy obliczyc bitrate - odczyt długości pliku
            if (bitRate == 0) bitRate = 1000000;
            using (Stream stream = File.OpenRead(filePath))
                SendMpegTsStream(stream, bitRate);
           
        }

        private void SendMpegTsStream(Stream fileStream, int bitRate)
        {
            int index = 0, packetCounter = 0;
            int maxPacketPerSecond = bitRate/(mpegTsChunk.Length*8);
            var onePercentPacketPerSecond = maxPacketPerSecond / 100;
            int timeTillNextPackets;
            //int bytesToRead = mpegTsChunk.Length;
            Stopwatch stopWatch = new Stopwatch();
            var spin = new SpinWait();
            
            stopWatch.Start();
            while (true)
            {                
                while (index < mpegTsChunk.Length)
                { 
                    int bytesRead = fileStream.Read(mpegTsChunk, index, mpegTsChunk.Length - index );
                    if (bytesRead == 0) return; 
                    index += bytesRead;
                }

                if (index != 0) udpClient.Send(mpegTsChunk, mpegTsChunk.Length, endPoint);
                var elapsedMilliseconds = stopWatch.ElapsedMilliseconds;

                if (index != mpegTsChunk.Length) return;
                ++packetCounter;

                if (packetCounter >= maxPacketPerSecond)
                {
                    /////To fix
                    while (stopWatch.ElapsedMilliseconds < 1000) { spin.SpinOnce(); }

                    packetCounter = 0;
                    stopWatch.Restart();
                }
                else if (packetCounter % onePercentPacketPerSecond == 0)
                {
                    timeTillNextPackets = 1000 * packetCounter / maxPacketPerSecond;
                    while (stopWatch.ElapsedMilliseconds < timeTillNextPackets) { spin.SpinOnce(); }
                }
                
                        
                index = 0;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            //Do sprawdzenia plusy/minusy
            //GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    udpClient.DropMulticastGroup(ipAddress);
                    udpClient.Close();
                }             
                disposed = true;
            }   
        }

        ~Server()
        {
            Dispose(false);
        }
    }
}
