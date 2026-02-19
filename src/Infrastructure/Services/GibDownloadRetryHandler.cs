using System.Net;
using Microsoft.Extensions.Logging;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// GIB HTTP indirmeleri için yeniden deneme işleyicisi.
/// Geçici hatalarda (5xx, 408, ağ hataları) üstel geri çekilme ile en fazla 3 kez yeniden dener.
/// </summary>
public sealed class GibDownloadRetryHandler(ILogger<GibDownloadRetryHandler> logger)
    : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);

    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode || attempt == MaxRetries)
                    return response;

                if (!RetryableStatusCodes.Contains(response.StatusCode))
                    return response;

                logger.LogWarning(
                    "GIB download attempt {Attempt}/{MaxRetries} returned {StatusCode}. Retrying...",
                    attempt + 1, MaxRetries, response.StatusCode);

                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                logger.LogWarning(ex,
                    "GIB download attempt {Attempt}/{MaxRetries} failed with network error. Retrying...",
                    attempt + 1, MaxRetries);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetries)
            {
                logger.LogWarning(ex,
                    "GIB download attempt {Attempt}/{MaxRetries} timed out. Retrying...",
                    attempt + 1, MaxRetries);
            }

            var delay = InitialDelay * Math.Pow(2, attempt);
            await Task.Delay(delay, cancellationToken);
        }

        // Buraya ulaşılmamalı, ancak derleyiciyi tatmin etmek için
        return await base.SendAsync(request, cancellationToken);
    }
}
