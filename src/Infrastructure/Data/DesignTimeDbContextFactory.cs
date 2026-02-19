using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MERSEL.Services.GibUserList.Infrastructure.Data;

/// <summary>
/// EF Core migration araçları için tasarım-zamanı DbContext fabrikası.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GibUserListDbContext>
{
    public GibUserListDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GibUserListDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=gib_user_list_design")
                      .UseSnakeCaseNamingConvention();

        return new GibUserListDbContext(optionsBuilder.Options);
    }
}
