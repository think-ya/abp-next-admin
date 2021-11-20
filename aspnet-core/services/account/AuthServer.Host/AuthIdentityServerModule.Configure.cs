﻿using AuthServer.IdentityResources;
using DotNetCore.CAP;
using LINGYUN.Abp.IdentityServer.IdentityResources;
using LINGYUN.Abp.Serilog.Enrichers.Application;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using LINGYUN.AuthServer;
using Microsoft.OpenApi.Models;
using Volo.Abp.Account.Localization;
using Volo.Abp.Auditing;
using Volo.Abp.Caching;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.IdentityServer;
using Volo.Abp.Json.SystemTextJson;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;

namespace AuthServer.Host
{
    public partial class AuthIdentityServerModule
    {
        private void PreConfigureApp()
        {
            AbpSerilogEnrichersConsts.ApplicationName = "Identity-Server-STS";
        }

        private void PreConfigureCAP(IConfiguration configuration)
        {
            PreConfigure<CapOptions>(options =>
            {
                options
                .UseMySql(mySqlOptions =>
                {
                    configuration.GetSection("CAP:MySql").Bind(mySqlOptions);
                })
                .UseRabbitMQ(rabbitMQOptions =>
                {
                    configuration.GetSection("CAP:RabbitMQ").Bind(rabbitMQOptions);
                })
                .UseDashboard();
            });
        }

        private void PreConfigureCertificate(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var cerConfig = configuration.GetSection("Certificates");
            if (environment.IsProduction() &&
                cerConfig.Exists())
            {
                // 开发环境下存在证书配置
                // 且证书文件存在则使用自定义的证书文件来启动Ids服务器
                var cerPath = Path.Combine(environment.ContentRootPath, cerConfig["CerPath"]);
                if (File.Exists(cerPath))
                {
                    PreConfigure<AbpIdentityServerBuilderOptions>(options =>
                    {
                        options.AddDeveloperSigningCredential = false;
                    });

                    var cer = new X509Certificate2(cerPath, cerConfig["Password"]);

                    PreConfigure<IIdentityServerBuilder>(builder =>
                    {
                        builder.AddSigningCredential(cer);
                    });
                }
            }
        }

        private void ConfigureDbContext()
        {
            Configure<AbpDbContextOptions>(options =>
            {
                options.UseMySQL();
            });
        }

        private void ConfigureDataSeeder()
        {
            Configure<CustomIdentityResourceDataSeederOptions>(options =>
            {
                options.Resources.Add(new CustomIdentityResources.AvatarUrl());
            });
        }

        private void ConfigureJsonSerializer()
        {
            // 中文序列化的编码问题
            Configure<AbpSystemTextJsonSerializerOptions>(options =>
            {
                options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
            });
        }
        private void ConfigureCaching(IConfiguration configuration)
        {
            Configure<AbpDistributedCacheOptions>(options =>
            {
                // 最好统一命名,不然某个缓存变动其他应用服务有例外发生
                options.KeyPrefix = "LINGYUN.Abp.Application";
                // 滑动过期30天
                options.GlobalCacheEntryOptions.SlidingExpiration = TimeSpan.FromDays(30d);
                // 绝对过期60天
                options.GlobalCacheEntryOptions.AbsoluteExpiration = DateTimeOffset.Now.AddDays(60d);
            });

            Configure<RedisCacheOptions>(options =>
            {
                var redisConfig = ConfigurationOptions.Parse(options.Configuration);
                options.ConfigurationOptions = redisConfig;
                options.InstanceName = configuration["Redis:InstanceName"];
            });
        }
        private void ConfigureIdentity(IConfiguration configuration)
        {
            // 增加配置文件定义,在新建租户时需要
            Configure<IdentityOptions>(options =>
            {
                var identityConfiguration = configuration.GetSection("Identity");
                if (identityConfiguration.Exists())
                {
                    identityConfiguration.Bind(options);
                }
            });
        }
        private void ConfigureVirtualFileSystem()
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<AuthIdentityServerModule>("AuthServer");
            });
        }
        private void ConfigureLocalization()
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options.Languages.Add(new LanguageInfo("en", "en", "English"));
                options.Languages.Add(new LanguageInfo("zh-Hans", "zh-Hans", "简体中文"));

                options.Resources
                    .Get<AccountResource>()
                    .AddVirtualJson("/Localization/Resources");
            });
        }
        private void ConfigureAuditing()
        {
            Configure<AbpAuditingOptions>(options =>
            {
                // options.IsEnabledForGetRequests = true;
                options.ApplicationName = "Identity-Server-STS";
            });
        }

        private void ConfigureSwagger(IServiceCollection services)
        {
            // Swagger
            services.AddSwaggerGen(
                options =>
                {
                    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Auth Identity API", Version = "v1" });
                    options.DocInclusionPredicate((docName, description) => true);
                    options.CustomSchemaIds(type => type.FullName);
                    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                    {
                        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                        Name = "Authorization",
                        In = ParameterLocation.Header,
                        Scheme = "bearer",
                        Type = SecuritySchemeType.Http,
                        BearerFormat = "JWT"
                    });
                    options.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                            },
                            new string[] { }
                        }
                    });
                    options.OperationFilter<TenantHeaderParamter>();
                });
        }

        private void ConfigureUrls(IConfiguration configuration)
        {
            Configure<AppUrlOptions>(options =>
            {
                options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
                // 邮件登录地址
                options.Applications["MVC"].Urls["EmailVerifyLogin"] = "Account/VerifyCode";
            });
        }
        private void ConfigureSecurity(IServiceCollection services, IConfiguration configuration, bool isDevelopment = false)
        {
            services.AddAuthentication()
                    .AddJwtBearer(options =>
                    {
                        options.Authority = configuration["AuthServer:Authority"];
                        options.RequireHttpsMetadata = false;
                        options.Audience = configuration["AuthServer:ApiName"];
                    });

            if (!isDevelopment)
            {
                var redis = ConnectionMultiplexer.Connect(configuration["Redis:Configuration"]);
                services
                    .AddDataProtection()
                    .SetApplicationName("LINGYUN.Abp.Application")
                    .PersistKeysToStackExchangeRedis(redis, "LINGYUN.Abp.Application:DataProtection:Protection-Keys");
            }

            services.AddSameSiteCookiePolicy();
        }
        private void ConfigureMultiTenancy(IConfiguration configuration)
        {
            // 多租户
            Configure<AbpMultiTenancyOptions>(options =>
            {
                options.IsEnabled = true;
            });

            var tenantResolveCfg = configuration.GetSection("App:Domains");
            if (tenantResolveCfg.Exists())
            {
                Configure<AbpTenantResolveOptions>(options =>
                {
                    var domains = tenantResolveCfg.Get<string[]>();
                    foreach (var domain in domains)
                    {
                        options.AddDomainTenantResolver(domain);
                    }
                });
            }
        }
        private void ConfigureCors(IServiceCollection services, IConfiguration configuration)
        {
            services.AddCors(options =>
            {
                options.AddPolicy(DefaultCorsPolicyName, builder =>
                {
                    builder
                        .WithOrigins(
                            configuration["App:CorsOrigins"]
                                .Split(",", StringSplitOptions.RemoveEmptyEntries)
                                .Select(o => o.RemovePostFix("/"))
                                .ToArray()
                        )
                        .WithAbpExposedHeaders()
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });
        }
    }
}
