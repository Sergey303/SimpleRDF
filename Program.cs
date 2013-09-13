using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;

namespace SimpleRDF
{


    internal class Program
    {
        private static Query query;
        private static void Main(string[] args)
        {
            Stopwatch timer = new Stopwatch();

            Graph gr;
            var directoryInfo = new DirectoryInfo(Environment.CurrentDirectory);
            if (directoryInfo.Parent == null || directoryInfo.Parent.Parent == null) return;
            string dataPath = directoryInfo.Parent.Parent.FullName +
                              @"\data\";
            Console.WriteLine("Hello!");

            string path = args.Length > 0 ? args[0] :dataPath;
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
            {
                try
                {
                    dir.Create();
                }
                catch
                {
                    path = Path.GetTempPath();
                    Console.WriteLine("Path - " + path + " is wrong. Application will be use temp path");
                }
            }
             gr = new Graph(path);
            gr.Load(new[] {dataPath + "0001.xml"});
            TValue.ItemCtor = id => new Item(gr.GetEntryById(id), gr);

            timer.Restart();
            query = new Query(@"..\..\query.txt", gr);
            timer.Stop();
            using (var f = new StreamWriter(@"..\..\Output.txt", true))
                f.WriteLine("read query time {0}ms {1}ticks, memory {2}"
       , timer.ElapsedMilliseconds, timer.ElapsedTicks / 10000L,
       GC.GetTotalMemory(true) / (1024L * 1024L));

            timer.Restart();
         query.Run();
            timer.Stop();
            using (var f = new StreamWriter(@"..\..\Output.txt", true))
                f.WriteLine("run query time {0}ms {1}ticks, memory {2}"
      , timer.ElapsedMilliseconds, timer.ElapsedTicks / 10000L,
      GC.GetTotalMemory(true) / (1024L * 1024L));

            if (query.SelectParameters.Count == 0)
                query.OutputParamsAll(@"..\..\Output.txt");
            else
                query.OutputParamsBySelect(@"..\..\Output.txt");
        }


    }
}