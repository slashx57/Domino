using DominoShared;
using DominoShared.Data;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit.Net.Smtp;
using NLog;

namespace DominoShared
{
    public class HWManager
    {
        //protected readonly IHostingEnvironment env;
        protected readonly IConfiguration config;
        //protected ILogger<HWManager> logger;
        protected static Logger logger = LogManager.GetCurrentClassLogger();
        protected static object _lockObject = new object();
        protected DominoDbContext globalDBcontext;
        public static Dictionary<string, int> readCounter = new Dictionary<string, int>();
        public static Dictionary<string, int> writeCounter = new Dictionary<string, int>();

        public HWManager(/*IHostingEnvironment env, DominoDbContext context, ILogger<HWManager> logger, */IConfiguration config)
        {
            //this.logger = logger;
            //this.env = env;
            //this.globalDBcontext = context;
            this.config = config;
        }

        public int GetHWEnabled(string name)
        {
            //lock (_lockObject)
            {
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {
                    var hw = localDbContext.Hardware.FirstOrDefault(x => x.name == name);
                    return hw.enabled;
                }
            }
        }

        /*public bool GetHWConnected(string name)
        {
            lock (_lockObject)
            {
                var hw = dbContext.Hardware.FirstOrDefault(x => x.name == name);
                return hw.connected==1?true:false;
            }
        }

        public void SetHWConnected(string name, bool connected)
        {
            lock (_lockObject)
            {
                var hw = dbContext.Hardware.FirstOrDefault(x => x.name == name);
                hw.connected = connected?1:0;
                dbContext.SaveChanges();
            }
        }*/

        public int GetHWStatus(string name)
        {
            //lock (_lockObject)
            {
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {
                    var hw = localDbContext.Hardware.FirstOrDefault(x => x.name == name);
                    return hw.status;
                }
            }
        }

        public void SetHWStatus(string name, int status)
        {
            //lock (_lockObject)
            {
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {
                    var hw = localDbContext.Hardware.FirstOrDefault(x => x.name == name);
                    if (hw == null)
                    {
                        hw = new HardwareTable();
                        hw.name = name;
                        hw.enabled = 1;
                    }
                    hw.status = status;
                    localDbContext.SaveChanges();
                }
            }
        }

        public string GetHWParameters(string name)
        {
            //lock (_lockObject)
            {
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {

                    var hw = localDbContext.Hardware.FirstOrDefault(x => x.name == name);
                    return hw.parameters;
                }
            }
        }

        protected List<MailboxTable> DataRequested(object sender, RequestDataEventArgs args)
        {
            //lock (_lockObject)
            {
                if (!readCounter.ContainsKey(args.Category))
                    readCounter.Add(args.Category,0);
                readCounter[args.Category]++;
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {
                    return localDbContext.PeekMailbox(args.Category, args.Direction);
                }
            }
        }

        protected MailboxTable DataSubmitted(object sender, SubmitDataEventArgs args)
        {
            //lock (_lockObject)
            {
                if (!writeCounter.ContainsKey(args.Category))   //ajoute au dictionnaire des compteurs d'écriture
                    writeCounter.Add(args.Category,0);
                writeCounter[args.Category]++;
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {
                    return localDbContext.AddToMailbox(DateTime.Now, args.Category, args.Direction, args.Message);
                }
            }
        }

        protected void DataProcessed(object sender, ProcessedDataEventArgs args)
        {
            //lock (_lockObject)
            {
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {
                    localDbContext.ArchiveMailboxMsg(args.Mbx, args.Comment);
                }
            }
        }


        public void SaveHisto(long modId, string signalName, string value, DateTime dt)
        {
            try
            {
                lock (_lockObject)
                {
                    using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                    {
                        HistoriansTable signalRecord = new HistoriansTable();
                        if (signalRecord == null) return;

                        signalRecord.configID = modId;
                        signalRecord.dt_update = dt;
                        signalRecord.signalName = signalName;
                        signalRecord.value = value;
                        localDbContext.Histo.Add(signalRecord);
                        localDbContext.SaveChanges();
                        logger.Debug("insert histo " + value + " for mod" + modId);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "", null);
                throw;
            }
        }

        public void SaveHisto(long modId, string signalName, string value)
        {
            SaveHisto(modId, signalName, value, DateTime.Now);
        }

        public void SaveValue(long modId, string signalName, string value)
        {
            try
            {
                lock (_lockObject)
                {
                    using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                    {
                        bool bCreated = false;
                        List<ValuesTable> signalRecords = localDbContext.Values.Where(m => m.configID == modId && m.signalName == signalName).ToList();
                        ValuesTable signalRecord;
                        if (signalRecords.Count == 0)    //ligne non trouvée
                        {
                            signalRecord = new ValuesTable();
                            signalRecord.configID = modId;
                            signalRecord.signalName = signalName;
                            bCreated = true;
                        }
                        else
                        {
                            signalRecord = signalRecords.FirstOrDefault();
                        }
                        signalRecord.dt_update = DateTime.Now;
                        if (signalRecord.value != value)
                            signalRecord.dt_changed = DateTime.Now;
                        signalRecord.value = value;
                        if (bCreated)
                            localDbContext.Values.Add(signalRecord);
                        else
                            localDbContext.Values.Update(signalRecord);
                        localDbContext.SaveChanges();
                        logger.Debug("update value " + value + " for mod" + modId);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "", null);
                throw;
            }
        }

        public string GetValue(long modId, string signalName)
        {
            lock (_lockObject)
            {
                using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
                {
                    ValuesTable signalRecord = localDbContext.Values.FirstOrDefault(m => m.configID == modId && m.signalName == signalName);
                    if (signalRecord == null) return null;
                    return signalRecord.value;
                }
            }
        }

        public void SendMail(string email, string subject, string text)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Domino", "domotique@boiselet.net"));
            message.To.Add(new MailboxAddress(email));
            message.Subject = subject;

            TextPart body = new TextPart("plain");
            body.Text = text;
            message.Body = body;

            using (var client = new SmtpClient())
            {
                // For demo-purposes, accept all SSL certificates (in case the server supports STARTTLS)
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;

                client.Connect("192.168.1.2", 143, false);

                // Note: since we don't have an OAuth2 token, disable
                // the XOAUTH2 authentication mechanism.
                client.AuthenticationMechanisms.Remove("XOAUTH2");

                // Note: only needed if the SMTP server requires authentication
                client.Authenticate("domotique", "domotique");

                client.Send(message);
                client.Disconnect(true);
            }
        }

        public void RaiseEvent(long exprId, string eventmsg)
        {
            using (DominoDbContext localDbContext = new DominoDbContext(config.GetConnectionString("DefaultConnection")))
            {
                var nots = localDbContext.Notifiers.Where(m => m.exprID == exprId).Include(u => u.userId);
                foreach (NotifiersTable not in nots)
                {
                    SendMail(not.user.Email, eventmsg.Substring(0, 15) + "...", eventmsg);
                }
            }
        }
    }
}
