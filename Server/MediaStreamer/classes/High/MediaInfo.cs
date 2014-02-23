﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FatAttitude.MediaStreamer;
using WasFatAttitude;
using System.Threading;

namespace FatAttitude
{
    public class MediaInfoGrabber
    {

        public MediaInfo Info;
        string PathToToolkit;
        string ProbeResultsFolderPath;

        public MediaInfoGrabber(string pathToToolkit, string probeResultsFolderPath, string fileName)
        {
            Info = new MediaInfo(fileName);
            PathToToolkit = pathToToolkit;
            ProbeResultsFolderPath = probeResultsFolderPath;
        }
        public void GetInfo(string ffmpegexe, string workdir)
        {
            // File exists?
            if (!File.Exists(Info.FileName))
            {
                Info.ErrorText = "File not found.";
                return;
            }

            // OK let's try
            SendDebugMessage("MediaInfoGrabber: Setting up prober...");
            FFMPGProber prober = new FFMPGProber();

            string strTempDirName = "probe_results";
            string OutputBasePath = Path.Combine(ProbeResultsFolderPath, strTempDirName);
            prober.DebugMessage += new EventHandler<GenericEventArgs<string>>(prober_DebugMessage);
            bool result = prober.Probe(ffmpegexe, workdir, PathToToolkit, Info.FileName, OutputBasePath);
            prober.DebugMessage -= new EventHandler<GenericEventArgs<string>>(prober_DebugMessage);
            Info.NewLiveTVPartDuration = prober.duration;
            if (!result)
            {
                Info.ErrorText = "FFProber Failed";
                return;
            }

            Info.Streams = prober.AVAudioAndVideoStreams;
            Info.Success = true;
        }
        public void GetInfo2(string ffmpegexe, string workdir)
        {
            // File exists?
            while (!File.Exists(Info.FileName))
            {
            }


            // OK let's try
            bool result = false;
            SendDebugMessage("MediaInfoGrabber: Setting up prober...");
            FFMPGProber prober = new FFMPGProber();
            DateTime start = DateTime.Now;
            do
            {
                string strTempDirName = "probe_results";
                string OutputBasePath = Path.Combine(ProbeResultsFolderPath, strTempDirName);
                prober.DebugMessage += new EventHandler<GenericEventArgs<string>>(prober_DebugMessage);
                result = prober.Probe(ffmpegexe, workdir, PathToToolkit, Info.FileName, OutputBasePath);
                prober.DebugMessage -= new EventHandler<GenericEventArgs<string>>(prober_DebugMessage);
                Info.NewLiveTVPartDuration = prober.duration;
                if (DateTime.Now.Ticks < (start.Ticks + 10000000 * 20))
                // May take a small while, timeout after 20 seconds
                {
                    //Debug.Print("Probing failed, trying again");
                }
                else
                {
                    break;
                }
            } while (!result);
            if (!result)
            {
                Info.ErrorText = "FFProber Failed";
                return;
            }

            Info.Streams = prober.AVAudioAndVideoStreams;
            Info.Success = true;
        }

        

        #region Debug / Status
        public event EventHandler<GenericEventArgs<string>> DebugMessage;
        void SendDebugMessage(string txtDebug)
        {
            // Add our ID
            //txtDebug = "[" + this.ID.ToString() + "]" + txtDebug;

            if (DebugMessage != null)
                DebugMessage(this, new GenericEventArgs<string>(txtDebug));
        }

        // INCOMING debug
        void prober_DebugMessage(object sender, GenericEventArgs<string> e)
        {
            SendDebugMessage("Prober: " + e.Value);
        }
        #endregion


        #region MediaInfo SubClass
        public class MediaInfo
        {
            public string FileName {get; set;}
            public bool Success {get; set;}
            public string ErrorText {get; set;}
            public Exception ErrorException {get; set;}
            public List<AVStream> Streams { get; set; }

            public double NewLiveTVPartDuration;

            public MediaInfo(string FN)
            {
                FileName = FN;
                Streams = new List<AVStream>();
            }

            // Calculated properties
            public double AVSyncDifference
            {
                get
                {
                    if (Streams == null) return 0.0;
                    if (Streams.Count < 1) return 0.0;

                    return Streams[0].VideoAudioSync;
                }
            }
            public TimeSpan Duration
            {
                get
                {
                    if (Streams == null) return new TimeSpan(0);
                    if (Streams.Count < 1) return new TimeSpan(0);

                    TimeSpan maxDuration = new TimeSpan(0);
                    foreach (AVStream str in Streams)
                    {
                        if (str.Duration > maxDuration)
                            maxDuration = str.Duration;
                    }

                    return maxDuration;
                }
            }
            public bool HasVideo
            {
                get
                {
                    return (AVVideoStreams.Count > 0);
                }
            }
            public bool HasAudio
            {
                get
                {
                    return (AVAudioStreams.Count > 0);
                }
            }
            #region Stream Filters
            // Helpers / Filters
            public List<AVStream> AVAudioAndVideoStreams
            {
                get
                {
                    List<AVStream> output = new List<AVStream>();

                    if (AVAudioStreams.Count > 0)
                        output.AddRange(AVAudioStreams);

                    if (AVVideoStreams.Count > 0)
                        output.AddRange(AVVideoStreams);

                    return output;
                }
            }
            public List<AVStream> AVAudioStreamsStereo
            {
                get
                {
                    List<AVStream> output = new List<AVStream>();

                    foreach (AVStream s in AVAudioStreams)
                    {
                        if (s.Channels > 1)
                            output.Add(s);
                    }

                    return output;
                }
            }
            public List<AVStream> AVAudioStreams
            {
                get
                {
                    return AVStreamsOfType(AVCodecType.Audio);
                }
            }
            public List<AVStream> AVVideoStreams
            {
                get
                {
                    return AVStreamsOfType(AVCodecType.Video);
                }
            }
            List<AVStream> AVStreamsOfType(AVCodecType ctype)
            {

                List<AVStream> output = new List<AVStream>();

                foreach (AVStream s in Streams)
                {
                    if (s.CodecType == ctype)
                        output.Add(s);
                }

                return output;

            }
            AVStream AVStreamByIndex(int i)
            {
                foreach (AVStream s in Streams)
                {
                    if (s.StreamIndex == i)
                        return s;
                }

                return null;

            }
            #endregion

        }
        #endregion

    }
}
