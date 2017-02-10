using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace MulticastLibrary
{
    public class MulticastServer : IDisposable
    {
        private readonly IPAddress ipAddress;
        private readonly IPAddress localAddress;
        private readonly int port;
        private readonly UdpClient udpClient;
        private IPEndPoint endPoint;
        private byte[] mpegTsChunk;
        private int mpegPacketsPerIpPacket = 7;
        private bool disposed = false;
        bool streaming = true;

        public MulticastServer(IPAddress ipAddress, int port, IPAddress localAddress)
        {
            this.ipAddress = ipAddress;
            this.port = port;
            this.localAddress = localAddress;
            udpClient = new UdpClient(new IPEndPoint(localAddress, 0));
            udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            endPoint = new IPEndPoint(ipAddress, port);
            
            mpegTsChunk = new byte[188*mpegPacketsPerIpPacket];
        }

        public bool SendMpegTs(string filePath, int bitRate = 0)
        {
            if (bitRate == 0)
            {
                bitRate = MpegTsOperations.CalculateBitrate(filePath);
                bitRate = bitRate == 0 ? 1000000 : bitRate;
            }
            using (Stream stream = File.OpenRead(filePath))
                SendMpegTsStream(stream, bitRate);
            return true;
        }

        public void SendMpegTsLoop(string filePath, int bitRate = 0)
        {
            if (bitRate == 0)
            {
                bitRate = MpegTsOperations.CalculateBitrate(filePath);
                bitRate = bitRate == 0 ? 1000000 : bitRate;
            }

            while (streaming)
            {
                using (Stream stream = File.OpenRead(filePath))
                    SendMpegTsStream(stream, bitRate);
            }
        }

        private void SendMpegTsStream(Stream fileStream, int bitRate)
        {
            int index = 0, packetCounter = 0;
            var maxPacketPerSecond = (float) bitRate/(mpegTsChunk.Length*8) < 1
                ? (float) bitRate/(mpegTsChunk.Length*8)
                : bitRate/(mpegTsChunk.Length*8);

            //To do: adapt to pc
            int timeToSleep = (float) bitRate/(mpegTsChunk.Length*8) < 1 ? 100 : bitRate > 10000000 ? 1 : 10;

            var stopWatch = new Stopwatch();

            stopWatch.Start();
            while (streaming)
            {
                while (index < mpegTsChunk.Length)
                {
                    int bytesRead = fileStream.Read(mpegTsChunk, index, mpegTsChunk.Length - index);
                    if (bytesRead == 0) return;
                    index += bytesRead;
                }
                if (index != 0) udpClient.Send(mpegTsChunk, mpegTsChunk.Length, endPoint);
                if (index != mpegTsChunk.Length) return;
                ++packetCounter;
                index = 0;

                while (streaming && packetCounter > stopWatch.ElapsedMilliseconds*maxPacketPerSecond/1000)
                {
                    Thread.Sleep(timeToSleep);
                    if (packetCounter > 2000000000)
                    {
                        packetCounter = 0;
                        stopWatch.Restart();
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    streaming = false;
                    Thread.Sleep(10);
                    
                    udpClient.Close();
                }
                disposed = true;
            }
        }

        ~MulticastServer()
        {
            Dispose(false);
        }
    }

    public static class MpegTsOperations
    {
        public static int CalculateBitrate(string filePath)
        {
            int index = 0;
            byte[] mpegTsChunk = new byte[188];
            var pidsList = new List<int>();
            var pcrDictionary = new Dictionary<int, PcrCouple>();
            bool loop = true;
            
            if (!File.Exists(filePath)) return 0;
            using (Stream stream = File.OpenRead(filePath))
            {
                while (index < mpegTsChunk.Length && loop)
                {
                    while (index < mpegTsChunk.Length && loop)
                    {
                        int bytesRead = stream.Read(mpegTsChunk, index, mpegTsChunk.Length - index);
                        if (bytesRead == 0)
                        {
                            loop = false;
                        }
                        index += bytesRead;
                    }
                    if (index != 0 && CheckIfMpegTsPacket(mpegTsChunk))
                    {
                        int pid = GetPidOfPacket(mpegTsChunk);
                        if (!pidsList.Contains(pid))
                        {
                            pidsList.Add(pid);
                            pcrDictionary.Add(pid, null);
                        }
                        if (CheckForAdaptionFieldControl(mpegTsChunk) &&
                            CheckForPcrFlag(mpegTsChunk))
                        {
                            var pcr = GetMsPcr(mpegTsChunk);
                            if (pcrDictionary[pid] == null)
                            {
                                pcrDictionary[pid] = new PcrCouple(pcr, pcr);
                            }
                            else
                            {
                                
                                if (pcrDictionary[pid].End > 0x1F0000000 && pcrDictionary[pid].End > pcr && pcr < 1000)
                                   pcrDictionary[pid].BaseEnd += pcrDictionary[pid].End;
                                pcrDictionary[pid].End = pcr;
                               
                            }
                        }
                    }
                    index = 0;
                }
            }

            foreach (var pid in pidsList)
            {
                if (pcrDictionary[pid] == null || pcrDictionary[pid].End == pcrDictionary[pid].Start) continue;
                if (pcrDictionary[pid].BaseEnd != 0) pcrDictionary[pid].End += pcrDictionary[pid].BaseEnd;

                var milisec = pcrDictionary[pid].End - pcrDictionary[pid].Start;
                var tsInfo = new FileInfo(filePath);
                return (int) (tsInfo.Length*8000/milisec);
            }

            return 0;
        }

        public static int GetPidOfPacket(byte[] packet)
        {
            int pid = (packet[1] & 0x1f) << 8 | packet[2];
            return pid;
        }

        public static bool CheckIfMpegTsPacket(byte[] packet)
        {
            if (packet.Length != 188) return false;
            return packet[0] == 0x47;
        }

        public static bool CheckForAdaptionFieldControl(byte[] packet)
        {
            var afc = (packet[3] & 0x30) >> 4;
            return afc == 3 || afc == 2;
        }

        public static bool CheckForPcrFlag(byte[] packet)
        {
            return (packet[5] & 0x10) == 0x10;
        }

        public static long GetMsPcr(byte[] packet)
        {
            //Only one bitwise operator need casting, rest I'm 100% sure won't overflow, so it would be unnecessary to cast all of them
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
            long basePcr = (long) packet[6] << 25 | packet[7] << 17 | packet[8] << 9 | packet[9] << 1 |
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand
                           (packet[10] & 0x80) >> 7;
            long extension = (packet[10] & 0x01) << 8 | packet[11];
            return (basePcr*300 + extension)/27000;
        }

        class PcrCouple
        {
            public PcrCouple(long start, long end)
            {
                Start = start;
                End = end;
                BaseEnd = 0;
            }

            public long Start { get; set; }
            public long End { get; set; }
            public long BaseEnd { get; set; }
        }
    }
}
