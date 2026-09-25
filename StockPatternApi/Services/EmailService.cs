using StockPatternApi.Models;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace StockPatternApi.Services
{
    public class EmailService
    {
        /// <summary>
        /// Backward-compatible sender: serializes the payload as indented JSON in a plain-text/pre body.
        /// Prefer <see cref="SendSetupsDigest"/> for setup digest emails.
        /// </summary>
        public void SendEmail(object tickerData)
        {
            DateTime currentDate = DateTime.Now;
            string formattedDate = currentDate.ToString("M/d/yyyy");
            string bodyJson = JsonSerializer.Serialize(tickerData, new JsonSerializerOptions
            {
                WriteIndented = true,
                MaxDepth = 10
            });

            string htmlBody =
                "<!DOCTYPE html><html><body style=\"margin:0;padding:16px;background:#f5f5f5;font-family:Segoe UI,Arial,sans-serif;\">" +
                "<pre style=\"white-space:pre-wrap;word-wrap:break-word;background:#fff;border:1px solid #ddd;padding:16px;border-radius:4px;font-size:13px;color:#222;\">" +
                WebUtility.HtmlEncode(bodyJson) +
                "</pre></body></html>";

            SendHtmlMail(
                subject: "Set Ups for " + formattedDate,
                htmlBody: htmlBody);
        }

        /// <summary>
        /// Sends a visually styled HTML digest of published stock setups.
        /// </summary>
        public void SendSetupsDigest(IEnumerable<StockSetups> setups, DateTime generatedAt, string? subtitle = null)
        {
            var list = setups?.ToList() ?? new List<StockSetups>();
            int count = list.Count;
            string datePart = generatedAt.ToString("M/d/yyyy");
            string subject = $"Surrender Flow — Setups for {datePart}";
            if (count > 0)
                subject = $"StockPatternAPIBot Setups — {datePart} ({count})";

            string effectiveSubtitle = string.IsNullOrWhiteSpace(subtitle)
                ? "washout-reclaim quality falling wedge"
                : subtitle.Trim();

            string htmlBody = BuildSetupsDigestHtml(list, generatedAt, effectiveSubtitle);
            SendHtmlMail(subject, htmlBody);
        }

        private static string BuildSetupsDigestHtml(List<StockSetups> setups, DateTime generatedAt, string subtitle)
        {
            var sb = new StringBuilder();
            int count = setups.Count;
            string generatedLocal = generatedAt.ToString("M/d/yyyy h:mm:ss tt");

            sb.Append("<!DOCTYPE html>");
            sb.Append("<html lang=\"en\"><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
            sb.Append("<title>Surrender Flow Strategy — Stock Setups</title></head>");
            sb.Append("<body style=\"margin:0;padding:0;background:#e9ecef;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#212529;\">");

            // Outer wrapper
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#e9ecef;padding:24px 12px;\">");
            sb.Append("<tr><td align=\"center\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:920px;background:#ffffff;border-radius:8px;overflow:hidden;border:1px solid #dee2e6;\">");

            // Green accent stripe
            sb.Append("<tr><td style=\"height:4px;background:#198754;font-size:0;line-height:0;\">&nbsp;</td></tr>");

            // Dark charcoal header
            sb.Append("<tr><td style=\"background:#2b2b2b;padding:20px 24px;\">");
            sb.Append("<div style=\"color:#ffffff;font-size:22px;font-weight:700;letter-spacing:0.3px;\">Surrender Flow Strategy</div>");
            sb.Append("<div style=\"color:#d0d0d0;font-size:14px;margin-top:4px;\">Stock Setups</div>");
            sb.Append("<div style=\"margin-top:12px;\">");
            sb.Append("<span style=\"display:inline-block;background:#198754;color:#ffffff;font-size:12px;font-weight:600;padding:4px 10px;border-radius:12px;\">");
            sb.Append(WebUtility.HtmlEncode(subtitle));
            sb.Append("</span></div>");
            sb.Append("</td></tr>");

            // Subtitle meta row
            sb.Append("<tr><td style=\"padding:16px 24px 8px 24px;background:#f8f9fa;border-bottom:1px solid #dee2e6;\">");
            sb.Append("<div style=\"font-size:13px;color:#495057;\">");
            sb.Append("Generated <strong>").Append(WebUtility.HtmlEncode(generatedLocal)).Append("</strong>");
            sb.Append(" &nbsp;·&nbsp; ");
            sb.Append("<strong>").Append(count).Append("</strong> setup").Append(count == 1 ? "" : "s");
            sb.Append(" &nbsp;·&nbsp; ");
            sb.Append(WebUtility.HtmlEncode(subtitle));
            sb.Append("</div></td></tr>");

            // Body
            sb.Append("<tr><td style=\"padding:20px 24px 8px 24px;\">");

            if (count == 0)
            {
                sb.Append("<div style=\"text-align:center;padding:40px 16px;color:#6c757d;\">");
                sb.Append("<div style=\"font-size:18px;font-weight:600;color:#343a40;margin-bottom:8px;\">No setups published</div>");
                sb.Append("<div style=\"font-size:14px;line-height:1.5;\">The scan completed successfully, but no washout-reclaim quality falling wedge setups met the publish criteria this run. Check back after the next market session.</div>");
                sb.Append("</div>");
            }
            else
            {
                sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid #ddd;font-size:13px;\">");

                // Table header
                sb.Append("<thead><tr style=\"background:#2b2b2b;color:#ffffff;\">");
                AppendTh(sb, "Ticker");
                AppendTh(sb, "Date");
                AppendTh(sb, "Risk/Share", alignRight: true);
                AppendTh(sb, "Reward/Share", alignRight: true);
                AppendTh(sb, "R:R", alignRight: true);
                AppendTh(sb, "Take Profit", alignRight: true);
                AppendTh(sb, "Stop Loss", alignRight: true);
                AppendTh(sb, "Signal");
                sb.Append("</tr></thead><tbody>");

                for (int i = 0; i < setups.Count; i++)
                {
                    var s = setups[i];
                    string rowBg = (i % 2 == 0) ? "#ffffff" : "#fafafa";
                    sb.Append("<tr style=\"background:").Append(rowBg).Append(";\">");
                    AppendTd(sb, WebUtility.HtmlEncode(s.Ticker ?? ""), bold: true);
                    AppendTd(sb, WebUtility.HtmlEncode(s.Date.ToString("M/d/yyyy h:mm:ss tt")));
                    AppendTd(sb, s.RiskPerShare.ToString("F2"), alignRight: true);
                    AppendTd(sb, s.RewardPerShare.ToString("F2"), alignRight: true);
                    AppendTd(sb, s.RewardToRisk.ToString("F2"), alignRight: true, bold: true);
                    AppendTd(sb, s.TakeProfit.ToString("F2"), alignRight: true);
                    AppendTd(sb, s.StopLoss.ToString("F2"), alignRight: true);
                    AppendTd(sb, WebUtility.HtmlEncode(s.Signal ?? ""));
                    sb.Append("</tr>");
                }

                sb.Append("</tbody></table>");
            }

            sb.Append("</td></tr>");

            // Footer
            sb.Append("<tr><td style=\"padding:20px 24px 24px 24px;text-align:center;color:#6c757d;font-size:12px;border-top:1px solid #dee2e6;\">");
            sb.Append("© 2026 Surrender Flow Strategy");
            sb.Append("</td></tr>");

            sb.Append("</table></td></tr></table>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void AppendTh(StringBuilder sb, string label, bool alignRight = false)
        {
            string align = alignRight ? "right" : "left";
            sb.Append("<th style=\"padding:10px 12px;text-align:").Append(align)
              .Append(";border:1px solid #2b2b2b;font-weight:600;font-size:12px;white-space:nowrap;\">")
              .Append(label).Append("</th>");
        }

        private static void AppendTd(StringBuilder sb, string content, bool alignRight = false, bool bold = false)
        {
            string align = alignRight ? "right" : "left";
            string weight = bold ? "700" : "400";
            sb.Append("<td style=\"padding:9px 12px;text-align:").Append(align)
              .Append(";border:1px solid #ddd;font-weight:").Append(weight)
              .Append(";color:#212529;white-space:nowrap;\">")
              .Append(content).Append("</td>");
        }

        private void SendHtmlMail(string subject, string htmlBody)
        {
            string fromEmail = Keys.EMAIL_FROM;
            string toEmail = Keys.EMAIL_TO;

            using var smtpClient = new SmtpClient(Keys.SMTP_SERVER, Keys.Port)
            {
                Credentials = new NetworkCredential(fromEmail, Keys.EMAIL_PASSWORD),
                EnableSsl = true
            };

            using var mail = new MailMessage(fromEmail, toEmail, subject, htmlBody)
            {
                IsBodyHtml = true
            };

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
