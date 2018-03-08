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
        private Dictionary<string, ContentModel> _patientDictionary = new Dictionary<string, ContentModel>();
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

                            #region 資料加工

                            if (jObj.Property("data") != null)
                            {
                                if (((JObject)jObj["data"]).Property("weight") != null)
                                {                                   
                                    var root = jObj.ToObject<WeightModel>();
                                    var t = root.data.created;
                                    var createTime = _unixTime.AddMilliseconds(t);

                                    Console.WriteLine("--------------------------------------------------");
                                    Console.WriteLine("### Message Received ###");
                                    Console.WriteLine($"Time : {DateTime.UtcNow}");
                                    Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
                                    Console.WriteLine($"+ DataTime = {createTime}");
                                    Console.WriteLine($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");
                                    Console.WriteLine("--------------------------------------------------");

                                    if (_connectionString != null)
                                    {
                                        //取的病人DB資料
                                        using (iMOTContext db = new iMOTContext(_connectionString))
                                        {

                                            var dbPatientsCard = db.PatientsCards.Include(p => p.Patients).Include(p => p.Patients.LongOrder).SingleOrDefault(p => p.CardNumber == root.patient.id);
                                            var dbPatient = dbPatientsCard.Patients;
                                            var dbLongOrder = dbPatient != null ? dbPatient.LongOrder.OrderByDescending(p => p.Date).FirstOrDefault() : null;

                                            if (dbLongOrder != null)
                                            {
                                                double weight = Math.Round(root.data.weight, 1);
                                                double dryWeight = dbLongOrder.DryWeight.HasValue ? Math.Round(dbLongOrder.DryWeight.Value, 1) : 0;
                                                double uftarget = dryWeight != 0 ? Math.Round(weight - dryWeight, 1) : 0;
                                                double bloodFlow = dbLongOrder.BloodFlow.HasValue ? Math.Round(dbLongOrder.BloodFlow.Value, 0) : 0;
                                                double startValue = dbLongOrder.StartValue.HasValue ? Math.Round(dbLongOrder.StartValue.Value, 0) : 0;
                                                double maintainValue = dbLongOrder.MaintainValue.HasValue ? Math.Round(dbLongOrder.MaintainValue.Value, 0) : 0;

                                                var IsAddPatient = _patientDictionary.TryAdd(root.patient.id, new ContentModel()
                                                {
                                                    PatientName = dbPatient.Name,
                                                    PatientId = dbPatient.Id,
                                                    Weight = weight,
                                                    DryWeight = dryWeight,
                                                    UFTarget = uftarget,
                                                    BloodFlow = bloodFlow,
                                                    StartValue = startValue,
                                                    MaintainValue = maintainValue,
                                                    CardId = dbPatient.PatientsCard.Select(p => p.CardNumber).ToList()
                                                });

                                                var patientContent = _patientDictionary.GetValueOrDefault(root.patient.id);

                                                //如果資料已存在，複寫資料內容
                                                if (IsAddPatient == false && patientContent != null)
                                                {
                                                    patientContent.Weight = weight;
                                                    patientContent.DryWeight = dryWeight;
                                                    patientContent.UFTarget = uftarget;
                                                    patientContent.BloodFlow = bloodFlow;
                                                    patientContent.StartValue = startValue;
                                                    patientContent.MaintainValue = maintainValue;
                                                    patientContent.CardId = dbPatient.PatientsCard.Select(p => p.CardNumber).ToList();

                                                }

                                                //如果體重測量的時間不為當天，則體重和脫水量為0
                                                if (createTime.Day - DateTime.UtcNow.Day != 0 && patientContent != null)
                                                {
                                                    patientContent.Weight = 0;
                                                    patientContent.UFTarget = 0;
                                                }

                                                //如果DB資料Date為null，則數值歸0
                                                if (!dbLongOrder.Date.HasValue && patientContent != null)
                                                {
                                                    patientContent.Weight = 0;
                                                    patientContent.DryWeight = 0;
                                                    patientContent.UFTarget = 0;
                                                    patientContent.BloodFlow = 0;
                                                    patientContent.StartValue = 0;
                                                    patientContent.MaintainValue = 0;
                                                }
                                            }
                                            else
                                            {
                                                var IsAddPatient = _patientDictionary.TryAdd(root.patient.id, new ContentModel()
                                                {
                                                    PatientId = dbPatient != null ? dbPatient.Id : 0,
                                                    PatientName = dbPatient != null ? dbPatient.Name : string.Empty,
                                                    Weight = 0,
                                                    DryWeight = 0,
                                                    UFTarget = 0,
                                                    BloodFlow = 0,
                                                    StartValue = 0,
                                                    MaintainValue = 0,
                                                    CardId = dbPatient != null ? dbPatient.PatientsCard.Select(p => p.CardNumber).ToList() : new List<string>()
                                                });
                                                Console.WriteLine("LongOrder no data");

                                            }
                                        }
                                    }
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

                                var pulishPatient = _patientDictionary.Values.Where(p => p.CardId.Contains(cardId)).Select(p => new
                                {
                                    p.PatientId,
                                    p.PatientName,
                                    p.Weight,
                                    p.DryWeight,
                                    p.UFTarget,
                                    p.BloodFlow,
                                    p.StartValue,
                                    p.MaintainValue,
                                    CardId = cardId
                                }).SingleOrDefault();

                                if (pulishPatient != null)
                                {
                                    Pulish(pulishPatient, $"{responseTopicArray[Num]}{cardId}");
                                }
                                //如果沒有量體重或_patientDictionary沒有資料的話
                                else if (pulishPatient == null && _connectionString != null)
                                {
                                    using (iMOTContext db = new iMOTContext(_connectionString))
                                    {
                                        var dbPatientsCard = db.PatientsCards.SingleOrDefault(p => p.CardNumber == cardId);

                                        if (dbPatientsCard != null)
                                        {
                                            var dbPatient = db.Patients.Include(p => p.LongOrder).Include(p => p.PatientsCard).SingleOrDefault(p => p.Id == dbPatientsCard.PatientId);
                                            var dbLongOrder = dbPatient != null ? dbPatient.LongOrder.OrderByDescending(p => p.Date).FirstOrDefault() : null;

                                            if (dbLongOrder != null)
                                            {
                                                double dryWeight = dbLongOrder.DryWeight.HasValue ? Math.Round(dbLongOrder.DryWeight.Value, 1) : 0;
                                                double bloodFlow = dbLongOrder.BloodFlow.HasValue ? Math.Round(dbLongOrder.BloodFlow.Value, 0) : 0;
                                                double startValue = dbLongOrder.StartValue.HasValue ? Math.Round(dbLongOrder.StartValue.Value, 0) : 0;
                                                double maintainValue = dbLongOrder.MaintainValue.HasValue ? Math.Round(dbLongOrder.MaintainValue.Value, 0) : 0;

                                                var pulishDBPatient = new
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

                                                Pulish(pulishDBPatient, $"{responseTopicArray[Num]}{cardId}");
                                            }
                                            else
                                            {
                                                var pulishDBPatient = new
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

                                                Pulish(pulishDBPatient, $"{responseTopicArray[Num]}{cardId}");
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
