using iMOTBlackBox.EF;
using iMOTBlackBox.Model;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace iMOTBlackBox.MqttClient
{
    public class iMOTMqttClient
    {
        private IMqttClient _mqttClient = new MqttFactory().CreateMqttClient();
        private object _messageReceivedLock = new object();
        private DateTime _unixTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private Dictionary<string, WeightModel> _patientDictionary = new Dictionary<string, WeightModel>();
        private string _connectionString;
        private string _mqttConnectionIP;
        private string _userName;
        private string _password;
        private int _port;

        public iMOTMqttClient(string DBConnectionString, string mqttConnectionIP, string userName = "ihd", string password = "3edc$RFV", int port = 1883)
        {
            _connectionString = DBConnectionString;
            _mqttConnectionIP = mqttConnectionIP;
            _userName = userName;
            _password = password;
            _port = port;

            Console.WriteLine($"DB conntection IP : {DBConnectionString.Split(';').ToList()[0].Replace("Data Source=", "")}");
            Console.WriteLine($"Mqtt conntection IP : {_mqttConnectionIP}");
        }

        /// <summary>
        /// 設定MqttClient連線屬性
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public IMqttClient SetMqttClient()
        {
            try
            {
                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(_mqttConnectionIP, _port)
                    .WithClientId(Guid.NewGuid().ToString())
                    .WithCredentials(_userName, _password)
                    .WithCleanSession(true)
                    .Build();

                _mqttClient.ConnectAsync(options);

                return _mqttClient;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SetMqttClient error : {ex}");
                return null;
            }
        }

        /// <summary>
        /// 設定連線事件，連線成功、連線斷掉、訊息接收
        /// </summary>
        /// <param name="listenTopicArray"></param>
        /// <param name="reConnectionSeconds"></param>
        public void SetMqttClientEvent(string[] listenTopicArray, string[] requsetTopicArray, string[] responseTopicArray, int reConnectionSeconds = 1)
        {

            //連線成功事件
            _mqttClient.Connected += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine($"Time : {DateTime.UtcNow}");
                Console.WriteLine("Connected with server");

                foreach (var topic in listenTopicArray)
                {
                    _mqttClient.SubscribeAsync(new TopicFilterBuilder()
                        .WithTopic(topic)
                        .Build());

                    Console.WriteLine($"SubscribeAsync add {topic}");
                }
            };

            //連線斷掉事件
            _mqttClient.Disconnected += async (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine($"Time : {DateTime.UtcNow}");
                Console.WriteLine("Disconnected with server");

                await Task.Delay(TimeSpan.FromSeconds(reConnectionSeconds));

                SetMqttClient();
            };

            //訊息接收
            _mqttClient.ApplicationMessageReceived += (s, e) =>
            {
                try
                {
                    lock (_messageReceivedLock)
                    {
                        var messageReceived = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

                        if (messageReceived != null && messageReceived != string.Empty)
                        {
                            var jObj = JObject.Parse(messageReceived);

                            #region 紀錄病人測量體重

                            if (jObj.Property("data") != null)
                            {
                                if (((JObject)jObj["data"]).Property("weight") != null)
                                {
                                    var root = jObj.ToObject<WeightModel>();
                                    var createTime = _unixTime.AddMilliseconds(root.data.created);
                                    double weight = Math.Floor(root.data.weight * 10) / 10;

                                    //queue病人的體重資料
                                    var IsAddPatient = _patientDictionary.TryAdd(root.patient.id, new WeightModel()
                                    {
                                        data = new Data()
                                        {
                                            created = root.data.created,
                                            weight = weight
                                        },
                                        patient = new Patient() { id = root.patient.id }
                                    });

                                    if (IsAddPatient == false)
                                    {
                                        var patientWeight = _patientDictionary.GetValueOrDefault(root.patient.id);

                                        //如果病人以測量過體重，再度測量覆寫
                                        if (patientWeight != null)
                                        {
                                            patientWeight.data.weight = weight;
                                            patientWeight.data.created = root.data.created;

                                            Console.WriteLine($"{root.patient.id} 體重資料覆寫");
                                        }
                                    }

                                    Console.WriteLine("--------------------------------------------------");
                                    Console.WriteLine("### Message Received ###");
                                    Console.WriteLine($"Time : {DateTime.UtcNow}");
                                    Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
                                    Console.WriteLine($"+ DataTime = {createTime}");
                                    Console.WriteLine($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");
                                    Console.WriteLine("--------------------------------------------------");
                                }
                            }

                            #endregion
                        }


                        #region 正則判斷Topic，並推送出去

                        //requsetTopic陣列與responseTopic陣列是一個對一個，requsetTopic[0]送出去只有responseTopic[0]
                        for (int Num = 0; Num < requsetTopicArray.Count(); Num++)
                        {
                            if (Regex.IsMatch(e.ApplicationMessage.Topic, $"{requsetTopicArray[Num]}+"))
                            {
                                string cardId = e.ApplicationMessage.Topic.Replace($"{requsetTopicArray[Num]}", "");

                                //取得病人的體重資料
                                var patientWeight = _patientDictionary.GetValueOrDefault(cardId);

                                ContentModel pulishDBPatient;

                                //取的病人DB資料
                                if (patientWeight != null && _connectionString != null)
                                {
                                    using (iMOTContext db = new iMOTContext(_connectionString))
                                    {

                                        var dbPatientsCard = db.PatientsCards.Include(p => p.Patients).Include(p => p.Patients.LongOrder).SingleOrDefault(p => p.CardNumber == patientWeight.patient.id);
                                        var dbPatient = dbPatientsCard != null ? dbPatientsCard.Patients : null;
                                        var dbLongOrder = dbPatient != null ? dbPatient.LongOrder.OrderByDescending(p => p.Date).FirstOrDefault() : null;

                                        if (dbLongOrder != null)
                                        {
                                            double weight = patientWeight.data.weight;
                                            double dryWeight = dbLongOrder.DryWeight.HasValue ? Math.Floor(dbLongOrder.DryWeight.Value * 10) / 10 : 0;
                                            double uftarget = dryWeight != 0 ? Math.Floor((weight - dryWeight) * 10) / 10 : 0;
                                            double bloodFlow = dbLongOrder.BloodFlow.HasValue ? Math.Floor(dbLongOrder.BloodFlow.Value) : 0;
                                            double startValue = dbLongOrder.StartValue.HasValue ? Math.Floor(dbLongOrder.StartValue.Value) : 0;
                                            double maintainValue = dbLongOrder.MaintainValue.HasValue ? Math.Floor(dbLongOrder.MaintainValue.Value) : 0;

                                            pulishDBPatient = new ContentModel()
                                            {
                                                PatientId = dbPatient.Id,
                                                PatientName = dbPatient.Name,
                                                Weight = weight,
                                                DryWeight = dryWeight,
                                                UFTarget = uftarget,
                                                BloodFlow = bloodFlow,
                                                StartValue = startValue,
                                                MaintainValue = maintainValue,
                                                CardId = cardId
                                            };

                                            var createTime = _unixTime.AddMilliseconds(patientWeight.data.created);

                                            //如果體重測量的時間不為當天，則體重和脫水量為0
                                            if (createTime.Day - DateTime.UtcNow.Day != 0)
                                            {
                                                pulishDBPatient.Weight = 0;
                                                pulishDBPatient.UFTarget = 0;

                                                Console.WriteLine($"{cardId} 體重測量時間: {createTime} ，測量不為當天");
                                            }

                                            //如果DB資料Date為null，則數值歸0
                                            if (!dbLongOrder.Date.HasValue)
                                            {
                                                pulishDBPatient.Weight = 0;
                                                pulishDBPatient.DryWeight = 0;
                                                pulishDBPatient.UFTarget = 0;
                                                pulishDBPatient.BloodFlow = 0;
                                                pulishDBPatient.StartValue = 0;
                                                pulishDBPatient.MaintainValue = 0;

                                                Console.WriteLine($"{cardId} 資料庫LongOrder的欄位Date為null");
                                            }
                                        }
                                        else
                                        {
                                            pulishDBPatient = new ContentModel()
                                            {
                                                PatientId = dbPatient != null ? dbPatient.Id : 0,
                                                PatientName = dbPatient != null ? dbPatient.Name : string.Empty,
                                                Weight = 0,
                                                DryWeight = 0,
                                                UFTarget = 0,
                                                BloodFlow = 0,
                                                StartValue = 0,
                                                MaintainValue = 0,
                                                CardId = cardId
                                            };

                                            Console.WriteLine($"{cardId} LongOrder no data");
                                        }
                                    }

                                    if (pulishDBPatient != null)
                                    {
                                        Pulish(pulishDBPatient, $"{responseTopicArray[Num]}{cardId}");
                                    }
                                    else
                                    {
                                        Console.WriteLine("有測量體重，pulishDBPatient is null");
                                    }
                                }

                                //如果沒有量體重或_patientDictionary沒有資料的話
                                else if (patientWeight == null && _connectionString != null)
                                {
                                    using (iMOTContext db = new iMOTContext(_connectionString))
                                    {
                                        var dbPatientsCard = db.PatientsCards.Include(p => p.Patients).Include(p => p.Patients.LongOrder).SingleOrDefault(p => p.CardNumber == cardId);

                                        if (dbPatientsCard != null)
                                        {
                                            var dbPatient = dbPatientsCard != null ? dbPatientsCard.Patients : null;
                                            var dbLongOrder = dbPatient != null ? dbPatient.LongOrder.OrderByDescending(p => p.Date).FirstOrDefault() : null;

                                            if (dbLongOrder != null)
                                            {
                                                double dryWeight = dbLongOrder.DryWeight.HasValue ? Math.Floor(dbLongOrder.DryWeight.Value * 10) / 10 : 0;
                                                double bloodFlow = dbLongOrder.BloodFlow.HasValue ? Math.Floor(dbLongOrder.BloodFlow.Value) : 0;
                                                double startValue = dbLongOrder.StartValue.HasValue ? Math.Floor(dbLongOrder.StartValue.Value) : 0;
                                                double maintainValue = dbLongOrder.MaintainValue.HasValue ? Math.Floor(dbLongOrder.MaintainValue.Value) : 0;

                                                pulishDBPatient = new ContentModel()
                                                {
                                                    PatientId = dbPatient.Id,
                                                    PatientName = dbPatient.Name,
                                                    Weight = 0,
                                                    DryWeight = dryWeight,
                                                    UFTarget = 0,
                                                    BloodFlow = bloodFlow,
                                                    StartValue = startValue,
                                                    MaintainValue = maintainValue,
                                                    CardId = cardId
                                                };

                                                Console.WriteLine($"{cardId} 沒有測量體重");
                                            }
                                            else
                                            {
                                                pulishDBPatient = new ContentModel()
                                                {
                                                    PatientId = dbPatient != null ? dbPatient.Id : 0,
                                                    PatientName = dbPatient != null ? dbPatient.Name : string.Empty,
                                                    Weight = 0,
                                                    DryWeight = 0,
                                                    UFTarget = 0,
                                                    BloodFlow = 0,
                                                    StartValue = 0,
                                                    MaintainValue = 0,
                                                    CardId = cardId
                                                };

                                                Console.WriteLine($"{cardId} 沒測量體重，資料庫也沒資料");
                                            }
                                            if (pulishDBPatient != null)
                                            {
                                                Pulish(pulishDBPatient, $"{responseTopicArray[Num]}{cardId}");
                                            }
                                            else
                                            {
                                                Console.WriteLine("沒測量體重，pulishDBPatient is null");
                                            }
                                        }
                                    }

                                }
                                break;
                            }
                        }

                        //接收responseTopic的訊息
                        for (int Num = 0; Num < responseTopicArray.Count(); Num++)
                        {
                            if (Regex.IsMatch(e.ApplicationMessage.Topic, $"{responseTopicArray[Num]}+"))
                            {
                                Console.WriteLine("--------------------------------------------------");
                                Console.WriteLine("### Message Received ###");
                                Console.WriteLine($"Time : {DateTime.UtcNow}");
                                Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
                                Console.WriteLine($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");
                                Console.WriteLine("--------------------------------------------------");
                                break;
                            }
                        }

                        #endregion
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine($"Time : {DateTime.UtcNow}");
                    Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
                    Console.WriteLine($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");
                    Console.WriteLine($"Received error : {ex}");
                    Console.WriteLine("--------------------------------------------------");
                }
            };

        }

        /// <summary>
        /// 推送訊息
        /// </summary>
        /// <param name="message"></param>
        /// <param name="topic"></param>
        public void Pulish(string message, string topic)
        {
            var publishObject = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(message)
                .WithAtMostOnceQoS()
                .WithRetainFlag(false)
                .Build();

            _mqttClient.PublishAsync(publishObject);

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("### Message Pulish ###");
            Console.WriteLine($"Time : {DateTime.UtcNow}");
            Console.WriteLine($"+ Topic = {topic}");
            Console.WriteLine($"+ Payload = {message}");
            Console.WriteLine("--------------------------------------------------");
        }

        /// <summary>
        /// 推送json序列化後的Model訊息
        /// </summary>
        /// <param name="message"></param>
        /// <param name="topic"></param>
        public void Pulish(object message, string topic)
        {
            if (message != null)
            {
                var jsonMessage = JsonConvert.SerializeObject(message);
                Pulish(jsonMessage, topic);
            }
            else
            {
                Pulish((string)message, topic);
            }
        }
    }
}
