using MERSEL.Services.GibUserList.Domain.Entities;

namespace MERSEL.Services.GibUserList.Infrastructure.Data.Configurations;

/// <summary>
/// EDespatchGibUser varlığı için yapılandırma.
/// mv_e_despatch_gib_users materialized view'ına eşlenir.
/// </summary>
public sealed class EDespatchGibUserConfiguration : GibUserConfigurationBase<EDespatchGibUser>
{
    protected override string ViewName => "mv_e_despatch_gib_users";
}
