using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace FatAttitude.Functions
{
    public static class FileWriter
    {


        public static bool WriteTextFileToDisk(string filePath, string txtContent, Encoding encoding)
        {
            /*

            byte[] contents = Encoding.UTF8.GetBytes(txtContent);
            byte[] newContents;
            if (encoding != Encoding.UTF8)
                newContents = Encoding.Convert(Encoding.UTF8, encoding, contents);
            else
                newContents = contents;
            */

            try
            {
                // Delete if exists
                if (File.Exists(filePath))
                    File.Delete(filePath);

                /*using (BinaryWriter bw = new BinaryWriter(File.Open(filePath, FileMode.Create)))
                {
                    bw.Write(newContents);
                }*/

                

                //TextWriter tw = new StreamWriter(filePath, false, encoding );
                TextWriter tw = new StreamWriter(filePath);
                tw.Write(txtContent);
                tw.Close();

                return true;
            }
            catch 
            {
                return false;
            }
        }


        //[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        //static extern int GetShortPathName(
        //         [MarshalAs(UnmanagedType.LPTStr)]
        //           string path,
        //         [MarshalAs(UnmanagedType.LPTStr)]
        //           StringBuilder shortPath,
        //         int shortPathLength
        //         );

        //public static string GetShortPathName(string fileName)
        //{
        //    StringBuilder shortPath = new StringBuilder(255);
        //    GetShortPathName(fileName, shortPath, (uint) shortPath.Capacity);
        //    return shortPath.ToString();
        //}
        /// <summary>
        /// The ToLongPathNameToShortPathName function retrieves the short path form of a specified long input path
        /// </summary>
        /// <param name="longName">The long name path</param>
        /// <returns>A short name path string</returns>
        public static string GetShortPathName(string sLongFileName)
        {
            var buffer = new StringBuilder(259);
            sLongFileName=WebUtility.HtmlDecode(sLongFileName);
            int len = GetShortPathName(sLongFileName, buffer, buffer.Capacity);
            if (len == 0) throw new System.ComponentModel.Win32Exception();
            return buffer.ToString();
        }

        [DllImport("kernel32", EntryPoint = "GetShortPathName", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufSize);
    }
}
