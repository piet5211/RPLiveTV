﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using WasFatAttitude;

namespace FatAttitude.MediaStreamer
{
    public class FFMPGProber
    {
        string PathToTools;
        string InputFile;
        string WorkingDirectory;
        string TempFile;
        int PreferredAudioStreamIndex;
        bool UsePreferredAudioStreamIndex;

        #region Probing / Detecting Mappings

        EventWaitHandle probeHandle;
        public bool ProbeSuccess;
        public bool ProbeFinished;
        public string ProbeReport;
        private CommandArguments probeArguments;

        public double duration;
        public List<AVStream> AVStreams;
        public bool Probe(string ffmpegexe, string workdir, string _pathToTools, string _inputFile, string _workingDirectory)
        {
            return Probe(ffmpegexe, workdir, _pathToTools, _inputFile, _workingDirectory, -1);
        }
        public bool Probe(string ffmpegexe, string workdir, string _pathToTools, string _inputFile, string _workingDirectory, int _preferredAudioStreamIndex)
        {
            // Wait for probing
            PathToTools = _pathToTools;
            InputFile = _inputFile;
            WorkingDirectory = _workingDirectory;
            PreferredAudioStreamIndex = _preferredAudioStreamIndex;
            UsePreferredAudioStreamIndex = (PreferredAudioStreamIndex >= 0);

            string fileStub = Path.GetFileNameWithoutExtension(_inputFile);

            bool randomFileAlreadyExists = false;
            do
            {
                Random r = new Random();
                // Construct temp file
                string bestand = fileStub + "_probe" + r.Next(0, int.MaxValue);
                TempFile = Path.Combine(WorkingDirectory, bestand + ".txt");
                // ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(TempFile));

                string[] filePaths=null;
                try
                {
                    SendDebugMessage("Checking whether " + bestand + "*.txt exists in " + WorkingDirectory);
                    filePaths = Directory.GetFiles(WorkingDirectory, bestand + "*.txt");
                }
                catch (Exception e)
                {
                    SendDebugMessage("Probe exception"+e.Message);
                }
                try
                {
                    SendDebugMessage("FilePaths.length=" + filePaths.Length);
                    if (filePaths.Length > 0)
                    {
                        randomFileAlreadyExists = true;
                    }
                }
                catch (Exception e)
                {
                    SendDebugMessage("Probe exception, filepaths" + e.Message);
                }
            } while (randomFileAlreadyExists);


            probeHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            StartProbeAsync(ffmpegexe, workdir);
            SendDebugMessage( "waitin max 8 seconds for probeasync to completee...");
            probeHandle.WaitOne(8000);  // 8 second time out
            SendDebugMessage("probeasync completed, csucces=" + ProbeSuccess);

            return (ProbeSuccess);
        }
        void StartProbeAsync(string ffmpegexe, string workdir)
        {

            // Set up objects
            AVStreams = new List<AVStream>();
            probeArguments = new CommandArguments();
            ConstructProbeArguments();


            // From parameters
            ProcessStartInfo psi = new ProcessStartInfo();

//            if (ffmpegexe.Equals("ffmpeglatest.exe")) ffmpegexe = "ffmpeg.exe"; // prevent issues like 5.1(side) audio not recognized


            string theFileName = "\"" + Path.Combine(PathToTools, ffmpegexe) + "\"";
            string cmdArguments = "/C ";
            psi.Arguments = cmdArguments + "\"" + theFileName + " " + probeArguments.ToString() + "\"";
            SendDebugMessage("probe command: " + psi.Arguments);
            psi.FileName = "cmd";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            if (ffmpegexe.Equals("ffmpeglatest.exe")) psi.WorkingDirectory = workdir;  // the m3u8 index files refers to .ts files in workdir

            // ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(TempFile));

            Process p = Process.Start(psi);
            p.WaitForExit(50000);


            if (!File.Exists(TempFile))
            {
                // Failed
                ProbeFinished = false;
                ProbeSuccess = false;
                ProbeReport = "No output file was created.";
                EndProbe();
                return;
            }

            // Probe has finished - we waited for exit.. ..let's analyse
            ProbeFinished = true;
            processOutputFile(); // Must do this first!
            AnalyseStreams();

        }
        void ConstructProbeArguments()
        {
            //probeArguments.AddArg("-show_streams");

            // Input file - use short name to avoid UTF-8 issues
            string sInputFile = Functions.FileWriter.GetShortPathName(InputFile);
            string quotedInputFile = "\"" + sInputFile + "\"";
            probeArguments.AddArgCouple("-i", quotedInputFile);

            // Redirect to temp file
            string quotedTempFile = "\"" + TempFile + "\"";
            probeArguments.AddArgCouple(">", quotedTempFile);

            probeArguments.AddArg("2>&1"); //http://www.microsoft.com/resources/documentation/windows/xp/all/proddocs/en-us/redirection.mspx?mfr=true
        }
        public CommandArguments mapArguments;
        void AddStreamToMap(AVStream strm)
        {
            mapArguments.AddArg("-map 0:" + strm.StreamIndex.ToString());
        }
        void AnalyseStreams()
        {
            SendDebugMessage("Analysing streams.");
            mapArguments = new CommandArguments();

            // Okay, let's look at what we got...
            // NOT: There must be at least one audio stream AND one video stream for us to add mapping parameters to ffmpeg
            if ((AVStreams == null) ||
                ((AVAudioStreams.Count < 1) &&
                (AVVideoStreams.Count < 1))
                )
            {
                ProbeSuccess = false;
                ProbeReport = "Not enough audio or video streams detected to add mappings.";
                EndProbe();
                return;
            }


            // We already know there's at least one video stream
            // Use the first video stream
            if (AVVideoStreams.Count > 0)
            {
                AVStream CurrentAvs=null;
                foreach (AVStream avs in AVVideoStreams)
                {
                    CurrentAvs = avs;
                    if (avs.CodecName.Equals("mjpeg"))
                    {
                        //skip
                    }
                    else
                    {
                        break;
                    }
                }
                SendDebugMessage("MediaProbe: Adding first non MJPEG video stream 0:" + CurrentAvs.StreamIndex.ToString() + " (" + CurrentAvs.CodecTag + ")");
                AddStreamToMap(CurrentAvs);
            }

            // Do we have a preferred audio index?
            if ((UsePreferredAudioStreamIndex) && (AVStreamByIndex(PreferredAudioStreamIndex) != null))
            {
                AVStream ast = AVStreamByIndex(PreferredAudioStreamIndex);
                SendDebugMessage("MediaProbe: Adding requested multiple track audio stream 0:" + ast.StreamIndex.ToString() + " (" + ast.CodecTag + ")");
                AddStreamToMap(ast);
            }
            else
            {
                // TODO: For MP2, different behavioru with AC3?
                // We already know there's at least one audio stream
                if (AVAudioStreams.Count == 1) // If there's just one audio stream, use it.
                    AddStreamToMap(AVAudioStreams[0]);
                else if (AVAudioStreamsStereo.Count > 0) // If there are some stereo streams
                {
                    if (AVAudioStreamsStereo.Count == 1) // If there's just one stereo audio stream, use it
                    {
                        AVStream ast = AVAudioStreamsStereo[0];
                        SendDebugMessage("MediaProbe: Adding only multiple track audio stream 0:" + ast.StreamIndex.ToString() + " (" + ast.CodecTag + ")");
                        AddStreamToMap(ast);
                    }
                    else
                    {
                        // There are multiple stereo streams: add the LAST stereo stream
                        AVStream ast = AVAudioStreamsStereo[AVAudioStreamsStereo.Count - 1];
                        SendDebugMessage("MediaProbe: Adding last  multiple track audio stream 0:" + ast.StreamIndex.ToString() + " (" + ast.CodecTag + ")");
                        AddStreamToMap(ast);
                    }
                }
                else
                {
                    // There are no stereo streams: just add the first audio stream
                    if (AVAudioStreams.Count > 0)
                    {
                        AVStream ast = AVAudioStreams[0];
                        SendDebugMessage("MediaProbe: No  multiple track audio streams found, adding first audio stream 0:" + ast.StreamIndex.ToString() + " (" + ast.CodecTag + ")");
                        AddStreamToMap(ast);
                    }
                }
            }

            // Use the first subtitle   TODO for testing purposes
            //if (AVSubtitleStreams.Count > 0)
            //{
            //    SendDebugMessage("MediaProbe: Adding second subtitle stream 0:" + AVSubtitleStreams[0].StreamIndex.ToString() + " (" + AVSubtitleStreams[0].CodecTag + ")");
            //    AddStreamToMap(AVSubtitleStreams[0]);
            //}

            SendDebugMessage("MediaProbe: Analysis complete.  Mappings are: " + mapArguments.ToString());


            // It's a success!
            ProbeReport = "Probe finished OK.";
            ProbeSuccess = true;
            ProbeFinished = true;

            EndProbe();
        }

        const string kBeginStreamLine = @"stream";

        /*
 * [STREAM]
index=2
codec_name=mp2
codec_long_name=MP2 (MPEG audio layer 2)
codec_type=audio
codec_time_base=0/1
codec_tag_string=P[0][0][0]
codec_tag=0x0050
sample_rate=48000.000000
channels=1
bits_per_sample=0
r_frame_rate=0/0
avg_frame_rate=125/3
time_base=1/10000000
start_time=1.364370
duration=4.191059
TAG:language=eng
[/STREAM]
         
         [STREAM]
index=0
codec_name=mpeg4
codec_long_name=MPEG-4 part 2
codec_type=video
codec_time_base=1/25
codec_tag_string=FMP4
codec_tag=0x34504d46
width=320
height=240
has_b_frames=0
sample_aspect_ratio=1:1
display_aspect_ratio=4:3
pix_fmt=yuv420p
r_frame_rate=25/1
avg_frame_rate=0/0
time_base=1/25
start_time=0.000000
duration=264.920000
nb_frames=6623
[/STREAM]
         
         */
        void processOutputFile()
        {
            try
            {
                string str = File.ReadAllText(TempFile);
                //string[] outputBuffer = str.Split(Environment.NewLine.ToCharArray());
                List<string> outputBuffer = splitByDelimiter(str, Environment.NewLine);
                TimeSpan tsDuration = TimeSpan.FromSeconds(0);
                double Start = 0.0;

                // First get the duration
                foreach (string s in outputBuffer)
                {
                    string txtOutput = s.ToLowerInvariant();
                    if (txtOutput.Length < 3) continue;


                    if (txtOutput.StartsWith("duration"))
                    {
                        ProcessDurationLine(txtOutput, out tsDuration);
                        duration = tsDuration.TotalSeconds;
                        //ProcessStartInLine(txtOutput, out Start);
                    }
                }

                // Now loop back through and capture the streams
                foreach (string s in outputBuffer)
                {
                    string txtOutput = s.ToLowerInvariant();

                    if (txtOutput.Length < 3) continue;

                    if (
                        (txtOutput.StartsWith(kBeginStreamLine))
                        )
                    {
                        AVStream newStream;
                        if (getStreamByParsingLine(txtOutput, out newStream))
                        {
                            newStream.DurationSeconds = tsDuration.TotalSeconds;
                            newStream.VideoAudioSync = Start;
                            AVStreams.Add(newStream);
                        }
                    }

                }
            }
            catch (Exception e)
            {
                SendDebugMessage("Cannot read locked probe file (output of ffmpeg) to get AV metadata");
            }
        }
        bool getStreamByParsingLine(string strText, out AVStream s)
        {
            s = new AVStream();

            try
            {
                if ((strText.Contains("audio:")))
                {
                    s.CodecType = AVCodecType.Audio;

                    if ((strText.Contains("hearing impaired")))
                        s.AudioCodecSubType = AudioStreamTypes.Commentary;
                }
                else if (strText.Contains("video:"))
                    s.CodecType = AVCodecType.Video;
                else if (strText.Contains("subtitle:"))
                    s.CodecType = AVCodecType.Subtitle;
                else
                    s.CodecType = AVCodecType.Unknown;

                //To make new ffmpeglatest compatible with old ffmpeg:
                var regex = new Regex(Regex.Escape(":"));
                strText = regex.Replace(strText, ".", 1);


                // For all streams, get the stream index number,  e.g. #0.1  =>  1
                List<string> mainParts = splitByDelimiter(strText, ":");
                if (mainParts.Count < 3) return false;
                if (!ProcessStreamIndexLine(ref s, mainParts[0])) return false;

                if (s.CodecType == AVCodecType.Subtitle)
                {
                    // Strip away everything before the av tag
                    int endAudioTag = strText.IndexOf("subtitle:");
                    strText = strText.Substring(endAudioTag + 9);

                    // Finally split into comma-delimited strings
                    List<string> subParts = splitByDelimiter(strText, ",");
                    if (subParts.Count < 1) return false;
                    // The first subpart is always the codec type
                    s.CodecName = subParts[0];

                }
                if (s.CodecType == AVCodecType.Audio)
                {
                    // Strip away everything before the av tag
                    int endAudioTag = strText.IndexOf("audio:");
                    strText = strText.Substring(endAudioTag + 6);

                    // Look for number of channels
                    string strChannels;
                    if (findSuffixAndNumberInString(strText, "channels", out strChannels))
                    {
                        int iChannels;
                        if (int.TryParse(strChannels, out iChannels))
                            s.Channels = iChannels;
                    }
                    else
                    {
                        if (strText.Contains("stereo")) s.Channels = 2;
                        else if (strText.Contains("mono")) s.Channels = 1;
                        else if (strText.Contains("5.1")) s.Channels = 6;
                        else if (strText.Contains("7.1")) s.Channels = 8;
                    }

                    // Finally split into comma-delimited strings
                    List<string> subParts = splitByDelimiter(strText, ",");
                    if (subParts.Count < 1) return false;
                    // The first subpart is always the codec type
                    s.CodecName = subParts[0];

                }

                if (s.CodecType == AVCodecType.Video)
                {
                    // Strip away everything before the av tag
                    int endVideoTag = strText.IndexOf("video:");
                    strText = strText.Substring(endVideoTag + 6);

                    // Find a SAR&DAR if one exists
                    if (strText.Contains("mjpeg")) // skip the thumbnail
                    {
                        
                    }
                    else
                    {
                        string strSARValue; string strDARValue;
                        if (findPrefixAndRatioInString(strText, "sar", out strSARValue))
                            s.SampleAspectRatio = strSARValue;
                        if (findPrefixAndRatioInString(strText, "dar", out strDARValue))
                            s.DisplayAspectRatio = strDARValue;
                    }

                    //Determine Framerate
                    string frameRate;
                    if (findFrameRate(strText, "tbr", out frameRate))
                        s.frameRate = frameRate;
                    
                    // Now split into comma-delimited strings
                    List<string> subParts = splitByDelimiter(strText, ",");
                    if (subParts.Count < 1) return false;
                    // The first subpart is always the codec type
                    s.CodecName = subParts[0];

                    // Find a size in the remaining strings
                    //bool found;
                    int Width; int Height;
                    foreach (string subpart in subParts)
                    {
                        if (findSizeInString(subpart, out Width, out Height))
                        {
                            s.Width = Width;
                            s.Height = Height;
                            //found = true;
                            continue;
                        }
                    }
                    // if (!found) return false;  // Un-comment if we ever REQUIRE a video size
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        bool findFrameRate(string strText, string strNumberSuffix, out string Number)
        {
            Number = null;

            Regex exp = new Regex(@"([-+]?[0-9]*\.?[0-9]+)\s(" + strNumberSuffix + @")");
            Match m = exp.Match(strText);
            if (!m.Success) return false;

            Group gNumber = m.Groups[1];
            if (!gNumber.Success) return false;
            Number = gNumber.Value;
            return true;
        }
        
        bool findSuffixAndNumberInString(string strText, string strNumberSuffix, out string Number)
        {
            Number = null;

            Regex exp = new Regex(@"(\d+)\s(" + strNumberSuffix + @")");
            Match m = exp.Match(strText);
            if (!m.Success) return false;

            Group gNumber = m.Groups[1];
            if (!gNumber.Success) return false;
            Number = gNumber.Value;
            return true;
        }
        bool findPrefixAndRatioInString(string strText, string strRatioPrefix, out string Ratiovalue)
        {
            Ratiovalue = null;

            Regex exp = new Regex(@"(" + strRatioPrefix + @"\s)(\d+:\d+)");
            Match m = exp.Match(strText);
            if (!m.Success) return false;

            Group gRatio = m.Groups[2];
            if (!gRatio.Success) return false;
            Ratiovalue = gRatio.Value;
            return true;
        }
        bool findSizeInString(string strText, out int Width, out int Height)
        {
            Width = 0; Height = 0;

            Regex exp = new Regex(@"(\d+)x(\d+)");
            Match m = exp.Match(strText);
            if (!m.Success) return false;

            Group g1 = m.Groups[1];
            if (!g1.Success) return false;
            if (!int.TryParse(g1.Value, out Width)) return false;

            Group g2 = m.Groups[2];
            if (!g2.Success) return false;
            if (!int.TryParse(g2.Value, out Height)) return false;

            return true;
        }
        bool ProcessStreamIndexLine(ref AVStream s, string strText)
        {
            strText = strText.Replace("stream", "").Trim();

            // Strip any Language tag
            if (strText.Contains("("))
            {
                int firstBracket = strText.IndexOf("(");
                int secondBracket = strText.IndexOf(")");
                if ((firstBracket != -1) && (secondBracket != -1))
                {
                    s.Language = strText.Substring(firstBracket + 1, (secondBracket - firstBracket - 1));
                    strText = strText.Remove(firstBracket, (secondBracket - firstBracket) + 1);
                }
            }

            // Strip any Codec tag
            if (strText.Contains("["))
            {
                int firstBracket = strText.IndexOf("[");
                int secondBracket = strText.IndexOf("]");
                if ((firstBracket != -1) && (secondBracket != -1))
                {
                    s.CodecTag = strText.Substring(firstBracket + 1, (secondBracket - firstBracket - 1));
                    strText = strText.Remove(firstBracket, (secondBracket - firstBracket) + 1);
                }
            }

            // Extra trim
            strText = strText.Trim();

            int dotIndex = strText.IndexOf(".");
            if (dotIndex == -1) return false;
            if (dotIndex == (strText.Length - 1)) return false; // it's at the last character

            string strIndex = strText.Substring(dotIndex + 1);
            int iIndex;
            if (!(int.TryParse(strIndex, out iIndex)))
                return false;

            s.StreamIndex = iIndex;
            return true;
        }
        //void ProcessStartInLine(string strText, out double Start){
        //    List<string> parts = splitByDelimiter(strText, ",");
        //    if (parts.Count < 2)
        //    {
        //        Start = 0.0;
        //        return;
        //    }
        //    parts[1] = parts[1].Replace("start:", "");
        //    double.TryParse(parts[1], System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out Start);//extract the time
        //}

        void ProcessDurationLine(string strText, out TimeSpan tsDuration)
        {
            strText = strText.Replace("duration:", "");
            List<string> parts = splitByDelimiter(strText, ",");

            if (parts.Count < 1)
            {
                tsDuration = TimeSpan.FromSeconds(0);
                return;
            }

            tsDuration = timeSpanFromTimeString(parts[0]);

            // Can also get 'start' parameter and bitrate, fwiw...
        }
        TimeSpan timeSpanFromTimeString(string strTimeString)
        {
            TimeSpan tsResult;
            if (!TimeSpan.TryParse(strTimeString, System.Globalization.CultureInfo.InvariantCulture, out tsResult))
                return TimeSpan.FromSeconds(0);
            return tsResult;

        }
        /// <summary>
        /// Split a string by a delimiter, remove blank lines and trim remaining lines
        /// </summary>
        /// <param name="strText"></param>
        /// <param name="strDelimiter"></param>
        /// <returns></returns>
        List<String> splitByDelimiter(string strText, string strDelimiter)
        {
            List<string> strList = strText.Split(strDelimiter.ToCharArray()).ToList();
            List<string> strOutput = new List<string>();
            foreach (string s in strList)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                strOutput.Add(s.Trim());
            }

            return strOutput;
        }
        void EndProbe()
        {
            // Signal that we're done
            probeHandle.Set();
        }

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

                if (AVSubtitleStreams.Count > 0)
                    output.AddRange(AVSubtitleStreams);

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
        public List<AVStream> AVSubtitleStreams
        {
            get
            {
                return AVStreamsOfType(AVCodecType.Subtitle);
            }
        }
        List<AVStream> AVStreamsOfType(AVCodecType ctype)
        {

            List<AVStream> output = new List<AVStream>();

            foreach (AVStream s in AVStreams)
            {
                if (s.CodecType == ctype)
                    output.Add(s);
            }

            return output;

        }
        AVStream AVStreamByIndex(int i)
        {
            foreach (AVStream s in AVStreams)
            {
                if (s.StreamIndex == i)
                    return s;
            }

            return null;

        }
        #endregion

        #region Debug
        public event EventHandler<GenericEventArgs<string>> DebugMessage;
        void SendDebugMessage(string txtDebug)
        {
            if (DebugMessage != null)
                DebugMessage(this, new GenericEventArgs<string>(txtDebug));
        }
        #endregion

    }
}
