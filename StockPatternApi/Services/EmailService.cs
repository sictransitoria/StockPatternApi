using StockPatternApi.Models;
using System.Net.Mail;
using System.Text.Json;

namespace StockPatternApi.Services
{
    public class EmailService
    {
        public void SendEmail(object tickerData)
        {
            DateTime currentDate = DateTime.Now;
            string formattedDate = currentDate.ToString("M/d/yyyy");
            string fromEmail = Keys.EMAIL_FROM;
            string toEmail = Keys.EMAIL_TO;
            string subject = "Set Ups for " + formattedDate;
            string body = JsonSerializer.Serialize(tickerData, new JsonSerializerOptions {
                WriteIndented = true,
                MaxDepth = 10
             });

            using var smtpClient = new SmtpClient(Keys.SMTP_SERVER, Keys.Port)
            {
                Credentials = new System.Net.NetworkCredential(fromEmail, Keys.EMAIL_PASSWORD),
                EnableSsl = true
            };

            using var mail = new MailMessage(fromEmail, toEmail, subject, body);
            try
            {
                smtpClient.Send(mail);
            }
            catch (SmtpException ex)
            {
                throw new Exception("Failed to send email: " + ex.Message);
            }
        }
    }
}