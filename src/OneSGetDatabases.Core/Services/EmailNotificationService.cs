using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using OneSGetDatabases.Core.Interfaces;
using OneSGetDatabases.Core.Models;

namespace OneSGetDatabases.Core.Services;

public class EmailNotificationService : INotificationService
{
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly EmailConfig _config;

    public EmailNotificationService(IOptions<EmailConfig> config, ILogger<EmailNotificationService> logger)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task SendErrorAlertAsync(string subject, IReadOnlyList<string> errors, CancellationToken cancellationToken = default)
    {
        if (!_config.EnableAlerts || string.IsNullOrEmpty(_config.SmtpServer) || string.IsNullOrEmpty(_config.Recipient))
        {
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("OneSGetDatabases Monitor", _config.Sender));
            message.To.Add(new MailboxAddress("", _config.Recipient));
            message.Subject = subject;

            var rowsBuilder = new StringBuilder();
            foreach (var err in errors)
            {
                rowsBuilder.Append($"<tr><td style='padding:8px;border-bottom:1px solid #eee;color:#888;'>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</td><td style='padding:8px;border-bottom:1px solid #eee;color:#d9534f;font-weight:bold;'>ERROR</td><td style='padding:8px;border-bottom:1px solid #eee;'>{System.Net.WebUtility.HtmlEncode(err)}</td></tr>");
            }

            string htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; color: #333; }}
        .container {{ max-width: 800px; background: #fff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.08); border-top: 4px solid #d9534f; }}
        .header {{ background: #2d323e; color: #fff; padding: 20px 25px; }}
        .header h2 {{ margin: 0; font-size: 18px; }}
        .content {{ padding: 25px; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 15px; font-size: 13px; }}
        th {{ background: #f8f9fa; text-align: left; padding: 10px 8px; border-bottom: 2px solid #dee2e6; color: #495057; }}
        .footer {{ background: #f8f9fa; padding: 12px 25px; font-size: 12px; color: #868e96; text-align: center; border-top: 1px solid #dee2e6; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>⚠️ Отчет об ошибках OneSGetDatabases ({errors.Count})</h2>
        </div>
        <div class='content'>
            <p>При выполнении автоматического сбора баз данных 1С или синхронизации с Confluence были зафиксированы следующие ошибки:</p>
            <table>
                <thead>
                    <tr>
                        <th style='width:160px;'>Время</th>
                        <th style='width:80px;'>Тип</th>
                        <th>Описание ошибки</th>
                    </tr>
                </thead>
                <tbody>
                    {rowsBuilder}
                </tbody>
            </table>
        </div>
        <div class='footer'>
            Автоматическое системное уведомление службы OneSGetDatabases.
        </div>
    </div>
</body>
</html>";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_config.SmtpServer, _config.SmtpPort, SecureSocketOptions.Auto, cancellationToken);

            if (!string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.Password))
            {
                await client.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Successfully sent error notification email to {Recipient}", _config.Recipient);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email alert: {Message}", ex.Message);
        }
    }
}
