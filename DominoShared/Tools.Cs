using System;
using System.Collections.Generic;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;

namespace DominoShared
{
	class Tools
	{
		/// <summary>
		/// Send a mail by SMTP
		/// </summary>
		/// <param name="email"></param>
		/// <param name="subject"></param>
		/// <param name="text"></param>
		public void SendMail(string email, string subject, string text)
		{
			var message = new MimeMessage();
			string mailSender = BaseManager.Configuration["Main:MailSender"];
			string mailServer = BaseManager.Configuration["Main:MailServer"];
			string mailAccount = BaseManager.Configuration["Main:MailAccount"];
			string mailPwd = BaseManager.Configuration["Main:MailPassword"];
			message.From.Add(new MailboxAddress("Domino", mailSender));
			message.To.Add(new MailboxAddress(email));
			message.Subject = subject;

			TextPart body = new TextPart("plain");
			body.Text = text;
			message.Body = body;

			using (var client = new SmtpClient())
			{
				client.ServerCertificateValidationCallback = (s, c, h, e) => true;

				client.Connect(mailServer, 143, false);

				// Note: since we don't have an OAuth2 token, disable
				// the XOAUTH2 authentication mechanism.
				client.AuthenticationMechanisms.Remove("XOAUTH2");

				client.Authenticate(mailAccount, mailPwd);

				client.Send(message);
				client.Disconnect(true);
			}
		}
	}
}
