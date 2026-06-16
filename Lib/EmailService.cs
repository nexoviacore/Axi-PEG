using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using AxPeg.Lib.Interfaces;
using Serilog;

namespace AxPeg.Lib
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendMailAsync(string toEmail, string subject, string body, bool isHtml = true)
        {
            try
            {
                var smtpHost = _config["Smtp:Host"] ?? "localhost";
                var smtpPort = int.Parse(_config["Smtp:Port"] ?? "25");
                var fromAddress = _config["Smtp:From"] ?? "noreply@axpert.com";
                var username = _config["Smtp:Username"];
                var password = _config["Smtp:Password"];
                var enableSsl = bool.Parse(_config["Smtp:EnableSsl"] ?? "false");

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = enableSsl,
                    UseDefaultCredentials = string.IsNullOrEmpty(username)
                };

                if (!string.IsNullOrEmpty(username))
                {
                    client.Credentials = new NetworkCredential(username, password);
                }

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromAddress),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml
                };
                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);
                Log.Information("Email sent successfully to {To}", toEmail);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send email to {To}", toEmail);
                // We do not throw to prevent blocking the transaction flow for mailing failures
            }
        }
    }
}
