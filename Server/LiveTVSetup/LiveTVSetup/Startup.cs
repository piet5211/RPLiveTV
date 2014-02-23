using SettingsProviderNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RemotePotatoServer.Properties;

namespace LiveTVSetup
{
    public class SetupLiveTVPorts
    {
        readonly SettingsProviderNet.SettingsProvider settingsRetreiver;
        readonly SettingsProviderNet.SettingsProvider settingsSaver;

        public SetupLiveTVPorts()
        {
            var store = new SettingsProviderNet.Settings.TestStorage();
            settingsRetreiver = new SettingsProviderNet.SettingsProvider(store);
            settingsSaver = new SettingsProviderNet.SettingsProvider(store);
        }

        public void start()
        {
            var settings = settingsRetreiver.GetSettings<SettingsProviderNet.Settings.LiveTVSettings>();
            
            if (RemotePotatoServer.Properties.Settings.Default.IsNewInstallOfRPLiveTV)
            {
                RemotePotatoServer.Properties.Settings.Default.IsNewInstallOfRPLiveTV = false;
                if (RemotePotatoServer.Properties.Settings.Default.Port != 9999999) // Install over old style RP (without Live TV Front-End)
                {
                    if (RemotePotatoServer.Properties.Settings.Default.Port==19909)
                    {
                        MessageBox.Show("Sorry can't use port 19909 anymore, as it is used internally betweeen the Live TV Front End and Remote Potato, pleasse contact support to solve this issue, see http://myfrem.nl/forum");
                        Application.Exit();
                    }
                    RemotePotatoServer.Properties.Settings.Default.RPPortWithLiveTV=RemotePotatoServer.Properties.Settings.Default.Port;
                    settings.LiveTVPort=(int)RemotePotatoServer.Properties.Settings.Default.Port;//from now on we don't use Settings.Default.Port anymore
                    RemotePotatoServer.Properties.Settings.Default.RPPort=19909;
                    settings.RPPort=19909;
                    settingsSaver.SaveSettings(settings);
                    //firewall does not have to be set
                    //But security for for RPPort Front-End, how do I do that?                  done later on in the program (for this program)
                    //Security for 19909 port:                                                  done later on in the program

                    RemotePotatoServer.Properties.Settings.Default.Port = 9999999; //Trick to indicate that now nnew style RP with Live TV Front End
                }
                else //Install new (never installed any RP before)
                {
                    settings.LiveTVPort=(int)RemotePotatoServer.Properties.Settings.Default.RPPortWithLiveTV;
                    settings.RPPort=19909;
                    settingsSaver.SaveSettings(settings);
                }
            }
            else //Install over an existing RP with Live TV Front End
            {

            }

            
        }

        //private string ChangeRPSetting(string xml, string key, string value, out string oldvalue)
        //{
        //    string newxml = "";
        //    oldvalue = "9080";
        //            int pos = xml.IndexOf("<" + key + ">");
        //            int pos2 = xml.IndexOf("</" + key + ">");
        //            oldvalue = xml.Substring(pos + key.Length + 2, pos2 - pos - key.Length - 2);
        //            newxml = xml.Substring(0, pos + key.Length + 2) + value + xml.Substring(pos2);
        //    return newxml;
        //}


    }
}
