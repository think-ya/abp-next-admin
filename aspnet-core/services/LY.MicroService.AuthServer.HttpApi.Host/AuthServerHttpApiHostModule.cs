using DotNetCore.CAP;
using LINGYUN.Abp.Account;
using LINGYUN.Abp.AspNetCore.HttpOverrides;
using LINGYUN.Abp.AspNetCore.Mvc.Localization;
using LINGYUN.Abp.AuditLogging.Elasticsearch;
using LINGYUN.Abp.Authorization.OrganizationUnits;
using LINGYUN.Abp.EventBus.CAP;
using LINGYUN.Abp.ExceptionHandling.Emailing;
using LINGYUN.Abp.Identity;
using LINGYUN.Abp.Identity.EntityFrameworkCore;
using LINGYUN.Abp.Localization.CultureMap;
using LINGYUN.Abp.LocalizationManagement.EntityFrameworkCore;
using LINGYUN.Abp.OpenIddict;
using LINGYUN.Abp.Saas.EntityFrameworkCore;
using LINGYUN.Abp.Serilog.Enrichers.Application;
using LINGYUN.Abp.Serilog.Enrichers.UniqueId;
using LINGYUN.Abp.Sms.Aliyun;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp;
using Volo.Abp.AspNetCore.Authentication.JwtBearer;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.EntityFrameworkCore.MySQL;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;

namespace LY.MicroService.AuthServer;

[DependsOn(
    typeof(AbpSerilogEnrichersApplicationModule),
    typeof(AbpSerilogEnrichersUniqueIdModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(AbpAspNetCoreMvcLocalizationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpOpenIddictApplicationModule),
    typeof(AbpOpenIddictHttpApiModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreMySQLModule),
    typeof(AbpSaasEntityFrameworkCoreModule),
    typeof(AbpSettingManagementEntityFrameworkCoreModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpLocalizationManagementEntityFrameworkCoreModule),
    typeof(AbpAspNetCoreAuthenticationJwtBearerModule),
    typeof(AbpAuthorizationOrganizationUnitsModule),
    typeof(AbpAuditLoggingElasticsearchModule),
    typeof(AbpEmailingExceptionHandlingModule),
    typeof(AbpCAPEventBusModule),
    typeof(AbpAliyunSmsModule),
    typeof(AbpCachingStackExchangeRedisModule),
    typeof(AbpAspNetCoreHttpOverridesModule),
    typeof(AbpLocalizationCultureMapModule),
    typeof(AbpAutofacModule)
    )]
public partial class AuthServerHttpApiHostModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        PreConfigureApp();
        PreConfigureFeature();
        PreConfigureCAP(configuration);
        PreConfigureIdentity();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        ConfigureDbContext();
        ConfigureLocalization();
        ConfigureJsonSerializer();
        ConfigreExceptionHandling();
        ConfigureVirtualFileSystem();
        ConfigurePermissionManagement();
        ConfigureUrls(configuration);
        ConfigureCaching(configuration);
        ConfigureAuditing(configuration);
        ConfigureSwagger(context.Services);
        ConfigureMultiTenancy(configuration);
        ConfigureCors(context.Services, configuration);
        ConfigureSecurity(context.Services, configuration, hostingEnvironment.IsDevelopment());
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        // http调用链
        app.UseCorrelationId();
        // 虚拟文件系统
        app.UseStaticFiles();
        // 路由
        app.UseRouting();
        // 跨域
        app.UseCors(DefaultCorsPolicyName);
        // 认证
        app.UseAuthentication();
        app.UseAbpClaimsMap();
        // jwt
        app.UseJwtTokenMiddleware();
        // 多租户
        app.UseMultiTenancy();
        // 本地化
        app.UseMapRequestLocalization();
        // 授权
        app.UseAuthorization();
        // Cap Dashboard
        app.UseCapDashboard();
        // Swagger
        app.UseSwagger();
        // Swagger可视化界面
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Support IdentityServer API");
        });
        // 审计日志
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        // 路由
        app.UseConfiguredEndpoints();
    }
}
