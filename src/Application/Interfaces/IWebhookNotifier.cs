using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Application.Interfaces;

/// <summary>
/// Kritik olayları yapılandırılmış webhook kanallarına (Slack, HTTP) ileten bildirim servisi.
/// </summary>
public interface IWebhookNotifier
{
    Task NotifyAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default);
}
