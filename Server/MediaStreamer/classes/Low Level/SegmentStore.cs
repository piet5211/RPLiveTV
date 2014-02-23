using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Text;
using FatAttitude.Collections;

namespace FatAttitude.MediaStreamer.HLS
{

    internal class SegmentStore
    {
        private string workingFolderPath;
        private bool newffmpeg;
        private bool NewLiveTV;
        public string ID;

        public SegmentStore(string ID, MediaStreamingRequest request)
        {
            this.ID = ID;
            this.newffmpeg = (request.UseNewerFFMPEG  && !request.LiveTV);
            NewLiveTV = request.NewLiveTV;
            workingFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
            workingFolderPath = Path.Combine(workingFolderPath + "\\static\\mediastreams\\", ID);
            if (!Directory.Exists(workingFolderPath)) Directory.CreateDirectory(workingFolderPath);
        }

        #region Top-Level Public
        object syncLock = new object();
        List<string> segmentsWaiting = new List<string>();
        public bool TryGetSegmentByNumber(int index, int SegNumber, ref Segment seg, bool doTranscode)
        {


            if (doTranscode)
            {


                // TODO this has been added to make the latest ffmpeg work: Mayeb this should be rermoved
                if (newffmpeg && !NewLiveTV && SegNumber != 99999)
                {
                    while (!DoesFileExistForSegmentNumber(0, SegNumber))
                    {
                    }
                }




                lock (syncLock)
                {
                    bool stopWaiting = false;
                    if (!DoesFileExistForSegmentNumber(index, SegNumber))
                    {
                        segmentsWaiting.Add("" + SegNumber);

                        do
                        {
                            Monitor.Wait(syncLock);

                            stopWaiting = (!segmentsWaiting.Contains("" + SegNumber));
                        }
                        while (
                        (!DoesFileExistForSegmentNumber(index, SegNumber)) &&
                        (!stopWaiting)
                        );
                    }

                    if (stopWaiting)
                    {
                        if (SegNumber == 99999)  // the ffmpeg runner is started now (for NewLiveTV and latest newffmpeg)
                        {
                            string workingFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RemotePotato");
                            workingFolderPath = Path.Combine(workingFolderPath + "\\static\\mediastreams\\", ID.ToString());
                            if (!Directory.Exists(workingFolderPath)) Directory.CreateDirectory(workingFolderPath);

                            File.Delete(workingFolderPath + "\\seg-99999.ts");
                            File.Delete(workingFolderPath + "\\segment-99999.ts");
                            string toolkitfolder = Functions2.ToolkitFolder;
                            File.Copy(toolkitfolder + "\\pleasewait.ts", workingFolderPath + "\\seg-99999.ts");
                            if (newffmpeg && !NewLiveTV)
                            {
                                File.Copy(toolkitfolder + "\\pleasewait.ts", workingFolderPath + "\\segment-99999.ts");
                                while (!DoesFileExistForSegmentNumber(0, 99999))
                                {
                                }
                            }
                        }
                        return false;
                    }

                    // It's arrived!  Remove segments waiting flag 
                    segmentsWaiting.Remove("" + SegNumber);

                }

            }

            seg = _GetSegment(index, SegNumber, doTranscode);

            return true;
        }

        public override string ToString()
        {
            return workingFolderPath;
        }


        public void CancelWaitingSegments()
        {
            lock (syncLock)
            {
                segmentsWaiting.Clear();
                Monitor.PulseAll(syncLock);
            }
        }
        public void StoreSegment(Segment s)
        {
            lock (syncLock)
            {
                _StoreSegment(s);  

                Monitor.PulseAll(syncLock);
            }
        }
        public bool HasSegment(int index, int SegNumber)
        {
            lock (syncLock)
            {
                return DoesFileExistForSegmentNumber(index,SegNumber);
            }
        }
        public void DeleteAllStoredSegmentsFromDisk()
        {
            lock (syncLock)
            {
                if (workingFolderPath == null) return;

                Directory.Delete(workingFolderPath, true);
            }
        }
        #endregion

        #region Disk Store / Retrieve - Not Thread Safe
        Segment _GetSegment(int index, int segNumber, bool doTranscode)
        {
            Segment s = new Segment();
            s.Number = segNumber;
            s.FileIndex = index;

            string FN = FileNameForSegmentNumber(s.Number, doTranscode);

            s.Data = FileToByteArray(FN);

            if (s.Data == null) // retry cuz could be NewLiveTV
            {
                string FN2 = FileNameForNewLiveTVSegmentNumber(s.Number);

                s.Data = FileToByteArray(FN2);
            }

            return s;
        }
        /// <summary>
        /// Function to get byte array from a file
        /// </summary>
        /// <param name="_FileName">File name to get byte array</param>
        /// <returns>Byte Array</returns>
        byte[] FileToByteArray(string _FileName)
        {
            byte[] bytes = null;

            FileStream fs = null;
            BinaryReader br = null;

            try
            {
                // Open file for reading
                fs = new FileStream(_FileName, FileMode.Open, FileAccess.Read);

                // attach filestream to binary reader
                br = new BinaryReader(fs);

                // get total byte length of the file
                long _TotalBytes = new System.IO.FileInfo(_FileName).Length;

                // read entire file into buffer
                bytes = br.ReadBytes((Int32)_TotalBytes);
            }
            catch (Exception _Exception)
            {
                // Error
                Console.WriteLine("Exception caught: {0}", _Exception.Message);
            }
            finally
            {
                try
                {
                    if (fs != null) fs.Close();
                    if (fs != null) fs.Dispose();
                    if (br != null) br.Close();
                }
                catch { }
            }

            return bytes;
        }
        void _StoreSegment(Segment s)
        {
            try
            {
                string FN = FileNameForSegmentNumber(s.Number, true);//FileIndex not interesting since only used for newlivetv, and that is generated by the new segmenter in livetvparts.cs
                FileStream fs = File.Create(FN, 1000);
                BinaryWriter bw = new BinaryWriter(fs);
                bw.Write(s.Data);
                bw.Flush();
                bw.Close();
            }
            catch { } // e.g. directory structure now erased, cannot store
        }

        public bool DoesFileExistForSegmentNumber(int index, int n)
        {
            if (File.Exists(FileNameForSegmentNumber(n, true)))
            {
                if (newffmpeg)
                {
                    //trick to wait until file is fullly being written to:
                    for (int i = 0; i < 1200; i++)//make a counter (2 minutes) in case of disk crash and such might occur
                    {
                        try
                        {
                            System.IO.File.Open(FileNameForSegmentNumber(n, true), FileMode.Open, FileAccess.Read,
                                FileShare.Read);
                            break;
                        }
                        catch (Exception e)
                        {
                            Thread.Sleep(100); //don't knock too often
                        }
                    }
                }
                return true;
            }
            if (File.Exists(FileNameForNewLiveTVSegmentNumber(n)))
            {
                if (newffmpeg)
                {
                    //trick to wait until file is fullly being written to:
                    for (int i = 0; i < 1200; i++)
                        //make a counter (2 minutes) in case of disk crash and such might occur
                    {
                        try
                        {
                            System.IO.File.Open(FileNameForNewLiveTVSegmentNumber(n), FileMode.Open, FileAccess.Read,
                                FileShare.Read);
                            break;
                        }
                        catch (Exception e)
                        {
                            Thread.Sleep(100);
                        }
                    }
                }
                return true;
            }
            return false;
        }
        string FileNameForSegmentNumber(int n, bool doTranscode)
        {
            if (doTranscode)
            {
                return Path.Combine(workingFolderPath, "segment-" + n.ToString() + ".ts");
            }
            else
            {
                return Path.Combine(workingFolderPath.Replace("mediastreams", "BackgroundTranscodedMediastreams"), "segment-" + n.ToString() + ".ts");
            }
        }
        string FileNameForNewLiveTVSegmentNumber(int n)
        {
//            return Path.Combine(workingFolderPath, "seg-" + n.ToString("D9") + ".ts");
            try
            {
                return Path.Combine(workingFolderPath, "liveseg-" +  n + ".ts");
            }
            catch (Exception)
            {
                return "s:\\deze\\file\\bestaat\\nooit\\nevernooitnie.hehe";
            }
        }
        #endregion


    }

    internal class SegmentWaiting
    {
        public int SegmentNumber { get; set; }
        public int FileIndex { get; set; }
        public SegmentWaiting(int FileIndex, int SegmentNumber)
        {
            this.SegmentNumber = SegmentNumber;
            this.FileIndex = FileIndex;
        }
    }

    internal class SegmentStoredEventArgs : EventArgs
    {
        public int SegmentNumber {get; set;}

        internal SegmentStoredEventArgs(int _Number)
        {
            SegmentNumber = _Number;
        }
    }
    
}

