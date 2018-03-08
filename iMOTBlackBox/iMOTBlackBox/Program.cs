using iMOTBlackBox.MqttClient;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;

namespace iMOTBlackBox
{
    class Program
    {
        static IConfiguration Configuration;

        public static ManualResetEvent _Shutdown = new ManualResetEvent(false);
        public static ManualResetEventSlim _Complete = new ManualResetEventSlim();

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Running iMOT black box");

                string environmentPath = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                string envRabbitMQIP = Environment.GetEnvironmentVariable("MqttConnectionIP");
                string envDBIP = Environment.GetEnvironmentVariable("DBConnectionIP");
                string envDBUser = Environment.GetEnvironmentVariable("DBConnectionUser");
                string envDBPassword = Environment.GetEnvironmentVariable("DBConnectionPWD");

                if (environmentPath != string.Empty && environmentPath != null)
                {
                    environmentPath = $".{environmentPath}";
                }

                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile($"appsettings{environmentPath}.json");

                Configuration = builder.Build();

                string rabbitMQIP = Configuration["MqttConnectionIP"];

                if (envRabbitMQIP != string.Empty && envRabbitMQIP != null)
                {
                    rabbitMQIP = envRabbitMQIP;
                }

                string DBConnectionString;

                #region 資料庫字串處理

                if (envDBIP != string.Empty && envDBIP != null)
                {
                    DBConnectionString = $"Data Source={envDBIP};Initial Catalog=iHD.tu;user id=";
                }
                else
                {
                    DBConnectionString = $"Data Source={Configuration["DBConnectionIP"]};Initial Catalog=iHD.tu;user id=";
                }
                if (envDBUser != string.Empty && envDBUser != null)
                {
                    DBConnectionString = $"{DBConnectionString}{envDBUser};password=";
                }
                else
                {
                    DBConnectionString = $"{DBConnectionString}{Configuration["DBConnectionUser"]};password=";
                }
                if (envDBPassword != string.Empty && envDBPassword != null)
                {
                    DBConnectionString = $"{DBConnectionString}{envDBPassword}";
                }
                else
                {
                    DBConnectionString = $"{DBConnectionString}{Configuration["DBConnectionPWD"]}";
                }

                #endregion

                iMOTMqttClient iMOTClient = new iMOTMqttClient(DBConnectionString, rabbitMQIP);

                iMOTClient.SetMqttClientEvent(ConfigurationJsonValueArray("ListenTopicArray"),
                    ConfigurationJsonValueArray("RequestTopicArray"),
                    ConfigurationJsonValueArray("ResponseTopicArray"));

                iMOTClient.SetMqttClient();

                AssemblyLoadContext.Default.Unloading += Default_Unloading;

                _Shutdown.WaitOne();
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }
            finally
            {
                Console.WriteLine("Cleaning up resources");
            }

            Console.WriteLine("Exiting...");
            _Complete.Set();
        }

        private static string[] ConfigurationJsonValueArray(string key)
        {
            var stringValueArray = Configuration.GetSection(key).AsEnumerable()
                .Where(x => x.Value != null)
                .Select(x => x.Value).ToArray();

            return stringValueArray;
        }

        private static void Default_Unloading(AssemblyLoadContext obj)
        {
            Console.Write($"Shutting down in response to SIGTERM.");
            _Shutdown.Set();
            _Complete.Wait();
        }
    }
}
