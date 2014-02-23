using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text;

namespace SettingsProviderNet
{
    public class Settings
    {

        public class TestStorage : JsonSettingsStoreBase
        {
            private readonly Dictionary<string, string> files = new Dictionary<string, string>();
//            const IsolatedStorageScope Scope = IsolatedStorageScope.Assembly | IsolatedStorageScope.User | IsolatedStorageScope.Roaming;

            public Dictionary<string, string> Files
            {
                get { return files; }
            }

            //protected override void WriteTextFile(string filename, string fileContents)
            //{
            //    if (!Files.ContainsKey(filename))
            //        Files.Add(filename, fileContents);
            //    else
            //        Files[filename] = fileContents;
            //}

            //protected override string ReadTextFile(string filename)
            //{
            //    return Files.ContainsKey(filename) ? Files[filename] : null;
            //}



            protected override void WriteTextFile(string filename, string fileContents)
            {
                    using (var stream = new StreamWriter(filename,false))
                        stream.Write(fileContents);
            }

            protected override string ReadTextFile(string filename)
            {
                    if (File.Exists(filename))
                    {
                        using (var stream = new StreamReader(filename))
                            return stream.ReadToEnd();
                    }

                return null;
            }

        }
        public class LiveTVSettings
        {
            //[DefaultValue(true)]
            //public bool FirstStart { get; set; }

            [DefaultValue(19909)]
            public int RPPort { get; set; }

            [DefaultValue(9080)]
            public int LiveTVPort { get; set; }

            [DefaultValue(false)]
            public bool EfficientPiping { get; set; }

            [DefaultValue(false)]
            public bool GPUtranscode { get; set; }

            [DefaultValue(false)]
            public bool UseLastMapping { get; set; }

            [DefaultValue(0)]
            public int NumberOfCoresX264 { get; set; }

            [DefaultValue(1)]
            public double SpeedFactorLiveTV { get; set; }
            
            [DefaultValue(false)]
            public bool ConsoleWtv2Wtvs { get; set; }

            [DefaultValue(false)]
            public bool Console2Pipe { get; set; }

            

//            Some examples:
            [DefaultValue("foo")]
            public string TestProp1 { get; set; }

            public MyEnum SomeEnum { get; set; }

            public int TestProp2 { get; set; }

            public DateTime? FirstRun { get; set; }

            public ComplexType Complex { get { return new ComplexType(); } }

            public List<string> List { get; set; }

            public List<int> List2 { get; set; }

            public IList<Guid> IdList { get; set; }

            public class ComplexType
            {
            }
        }

        public enum MyEnum
        {
            Value1,
            Value2
        }
    }
}
