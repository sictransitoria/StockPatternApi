using System.Net.Mail;
using System.Text.Json;

namespace StockPatternApi.Services
{
    public class EmailService
    {
        public void SendEmail(object tickerData)
        {
            string fromEmail = "michael.oria@icloud.com";
            string toEmail = "michael.oria@icertis.com";
            string subject = "JSON Output";
            string body = JsonSerializer.Serialize(tickerData, new JsonSerializerOptions { WriteIndented = true });

            using var smtpClient = new SmtpClient("smtp.mail.me.com", 587)
            {
                Credentials = new System.Net.NetworkCredential(fromEmail, "APP_SPECIFIC_PASSWORD"),
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