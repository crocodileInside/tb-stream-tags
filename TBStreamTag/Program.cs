using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using HtmlAgilityPack;
using Fizzler;
using Fizzler.Systems.HtmlAgilityPack;
using Fizzler.Systems.XmlNodeQuery;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace TBStreamTag
{
    class Program
    {

        static void Main(string[] args)
        {
            UseUnsafeHeaderParsing();

            Console.WriteLine("+---------------------------------------------------+");
            Console.WriteLine("|   Radio.fx StreamTag Emulator for TechnoBase.FM   |");
            Console.WriteLine("+---------------------------------------------------+");
            Console.WriteLine("|        written 11/2012 by crocodileInside         |");
            Console.WriteLine("+---------------------------------------------------+");
            Console.WriteLine("|         using proxy code by matt-dot-net          |");
            Console.WriteLine("+---------------------------------------------------+");
            Console.WriteLine("");

            HTTPProxyServer.ProxyServer.Server.Start();

            Console.ReadKey();

        }

        public static bool UseUnsafeHeaderParsing()
        {
            Assembly assembly = Assembly.GetAssembly(typeof(System.Net.Configuration.SettingsSection));
            if (null == assembly)
            {
                Debug.WriteLine("Could not access Assembly");
                return false;
            }

            Type type = assembly.GetType("System.Net.Configuration.SettingsSectionInternal");
            if (null == type)
            {
                Debug.WriteLine("Could not access internal settings");
                return false;
            }

            object obj = type.InvokeMember("Section",
               BindingFlags.Static | BindingFlags.GetProperty | BindingFlags.NonPublic,
                null, null, new object[] { });

            if (null == obj)
            {
                Debug.WriteLine("Could not invoke Section member");
                return false;
            }

            // If it's not already set, set it.
            FieldInfo fi = type.GetField("useUnsafeHeaderParsing", BindingFlags.NonPublic | BindingFlags.Instance);
            if (null == fi)
            {
                Debug.WriteLine("Could not access useUnsafeHeaderParsing field");
                return false;
            }

            if (!Convert.ToBoolean(fi.GetValue(obj)))
            {
                fi.SetValue(obj, true);
            }

            return true;
        }

    }


}
