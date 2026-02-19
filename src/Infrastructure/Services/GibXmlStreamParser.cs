using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using MERSEL.Services.GibUserList.Application.Models;

namespace MERSEL.Services.GibUserList.Infrastructure.Services;

/// <summary>
/// GIB kullanıcı listesi dosyaları için akış tabanlı XML ayrıştırıcı.
/// Bellek kullanımını minimize etmek için kullanıcıları teker teker işler.
/// Toplam hata oranı %5'i aşarsa kritik uyarı verir.
/// </summary>
public sealed class GibXmlStreamParser(ILogger<GibXmlStreamParser> logger)
{
    private const double FailureThresholdPercent = 5.0;
    private readonly XmlSerializer _serializer = new(typeof(GibXmlUser));

    /// <summary>
    /// XML dosyasından akış kullanarak GibXmlUser nesnelerini teker teker döndürür.
    /// </summary>
    public IEnumerable<GibXmlUser> ParseUsers(string xmlFilePath)
    {
        using var fileStream = File.OpenRead(xmlFilePath);
        using var reader = XmlReader.Create(fileStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            Async = false
        });

        var successCount = 0;
        var failureCount = 0;

        while (reader.ReadToFollowing("User"))
        {
            GibXmlUser? user = null;
            try
            {
                using var subReader = reader.ReadSubtree();
                user = (GibXmlUser?)_serializer.Deserialize(subReader);
            }
            catch (Exception ex)
            {
                failureCount++;
                logger.LogWarning(ex, "Failed to parse user at position {Position} from {File}.",
                    successCount + failureCount, Path.GetFileName(xmlFilePath));
            }

            if (user is not null)
            {
                successCount++;
                yield return user;
            }
        }

        var totalAttempts = successCount + failureCount;
        if (failureCount > 0 && totalAttempts > 0)
        {
            var failurePercent = (double)failureCount / totalAttempts * 100;
            if (failurePercent >= FailureThresholdPercent)
            {
                logger.LogCritical(
                    "XML parse failure rate {Rate:F1}% exceeds threshold ({Threshold}%): " +
                    "{Failures}/{Total} entries failed in {File}. Data quality may be compromised.",
                    failurePercent, FailureThresholdPercent,
                    failureCount, totalAttempts, Path.GetFileName(xmlFilePath));
            }
        }

        logger.LogInformation("Parsed {SuccessCount} users ({FailureCount} failures) from {File}",
            successCount, failureCount, Path.GetFileName(xmlFilePath));
    }
}
