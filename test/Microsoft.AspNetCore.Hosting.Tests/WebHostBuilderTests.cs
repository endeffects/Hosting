// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Fakes;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Xunit;

namespace Microsoft.AspNetCore.Hosting
{
    public class WebHostBuilderTests
    {
        [Fact]
        public void Build_honors_UseStartup_with_string()
        {
            var builder = CreateWebHostBuilder().UseServer(new TestServer());

            var host = (WebHost)builder.UseStartup("MyStartupAssembly").Build();

            Assert.Equal("MyStartupAssembly", host.StartupAssemblyName);
        }

        [Fact]
        public async Task StartupMissing_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup("MissingStartupAssembly").Build();
            using (host)
            {
                host.Start();
                await AssertResponseContains(server.RequestDelegate, "MissingStartupAssembly");
            }
        }

        [Fact]
        public async Task StartupStaticCtorThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupStaticCtorThrows>().Build();
            using (host)
            {
                host.Start();
                await AssertResponseContains(server.RequestDelegate, "Exception from static constructor");
            }
        }

        [Fact]
        public async Task StartupCtorThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupCtorThrows>().Build();
            using (host)
            {
                host.Start();
                await AssertResponseContains(server.RequestDelegate, "Exception from constructor");
            }
        }

        [Fact]
        public async Task StartupCtorThrows_TypeLoadException()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupThrowTypeLoadException>().Build();
            using (host)
            {
                host.Start();
                await AssertResponseContains(server.RequestDelegate, "Message from the LoaderException</span>");
            }
        }

        [Fact]
        public async Task IApplicationLifetimeRegisteredEvenWhenStartupCtorThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupCtorThrows>().Build();
            using (host)
            {
                host.Start();
                var service = host.Services.GetServices<IApplicationLifetime>();
                Assert.NotNull(service);
                await AssertResponseContains(server.RequestDelegate, "Exception from constructor");
            }
        }

        [Fact]
        public async Task StartupConfigureServicesThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupConfigureServicesThrows>().Build();
            using (host)
            {
                host.Start();
                await AssertResponseContains(server.RequestDelegate, "Exception from ConfigureServices");
            }
        }

        [Fact]
        public async Task StartupConfigureThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupConfigureServicesThrows>().Build();
            using (host)
            {
                host.Start();
                await AssertResponseContains(server.RequestDelegate, "Exception from Configure");
            }
        }

        [Fact]
        public void CaptureStartupErrorsByDefault()
        {
            var hostBuilder = new WebHostBuilder()
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            // This should not throw
            hostBuilder.Build();
        }

        [Fact]
        public void UseCaptureStartupErrorsHonored()
        {
            var hostBuilder = new WebHostBuilder()
                .UseCaptureStartupErrors(false)
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            var exception = Assert.Throws<InvalidOperationException>(() => hostBuilder.Build());
            Assert.Equal("A public method named 'ConfigureProduction' or 'Configure' could not be found in the 'Microsoft.AspNetCore.Hosting.Fakes.StartupBoom' type.", exception.Message);
        }

        [Fact]
        public void CodeBasedSettingsCodeBasedOverride()
        {
            var hostBuilder = new WebHostBuilder()
                .UseCaptureStartupErrors(false)
                .UseCaptureStartupErrors(true)
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            // This should not throw
            hostBuilder.Build();
        }

        [Fact]
        public void CodeBasedSettingsConfigBasedOverride()
        {
            var settings = new Dictionary<string, string>
            {
                { WebHostDefaults.CaptureStartupErrorsKey, "true" }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var hostBuilder = new WebHostBuilder()
                .UseCaptureStartupErrors(false)
                .UseConfiguration(config)
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            // This should not throw
            hostBuilder.Build();
        }

        [Fact]
        public void ConfigBasedSettingsCodeBasedOverride()
        {
            var settings = new Dictionary<string, string>
            {
                { WebHostDefaults.CaptureStartupErrorsKey, "false" }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var hostBuilder = new WebHostBuilder()
                .UseConfiguration(config)
                .UseCaptureStartupErrors(true)
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            // This should not throw
            hostBuilder.Build();
        }

        [Fact]
        public void ConfigBasedSettingsConfigBasedOverride()
        {
            var settings = new Dictionary<string, string>
            {
                { WebHostDefaults.CaptureStartupErrorsKey, "false" }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var overrideSettings = new Dictionary<string, string>
            {
                { WebHostDefaults.CaptureStartupErrorsKey, "true" }
            };

            var overrideConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(overrideSettings)
                .Build();

            var hostBuilder = new WebHostBuilder()
                .UseConfiguration(config)
                .UseConfiguration(overrideConfig)
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            // This should not throw
            hostBuilder.Build();
        }

        [Fact]
        public void UseEnvironmentIsNotOverriden()
        {
            var vals = new Dictionary<string, string>
            {
                { "ENV", "Dev" },
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();

            var expected = "MY_TEST_ENVIRONMENT";
            var host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseEnvironment(expected)
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            Assert.Equal(expected, host.Services.GetService<IHostingEnvironment>().EnvironmentName);
        }

        [Fact]
        public void BuildAndDispose()
        {
            var vals = new Dictionary<string, string>
            {
                { "ENV", "Dev" },
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();

            var expected = "MY_TEST_ENVIRONMENT";
            var host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseEnvironment(expected)
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            host.Dispose();
        }

        [Fact]
        public void UseBasePathConfiguresBasePath()
        {
            var vals = new Dictionary<string, string>
            {
                { "ENV", "Dev" },
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();

            var host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseApplicationBasePath("/foo/bar")
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            Assert.Equal("/foo/bar", host.Services.GetService<IApplicationEnvironment>().ApplicationBasePath);
        }

        private IWebHostBuilder CreateWebHostBuilder()
        {
            var vals = new Dictionary<string, string>
            {
                { "DetailedErrors", "true" },
                { "captureStartupErrors", "true" }
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();
            return new WebHostBuilder().UseConfiguration(config);
        }

        private async Task AssertResponseContains(RequestDelegate app, string expectedText)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();
            await app(httpContext);
            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            var bodyText = new StreamReader(httpContext.Response.Body).ReadToEnd();
            Assert.Contains(expectedText, bodyText);
        }

        private class TestServer : IServer
        {
            IFeatureCollection IServer.Features { get; }
            public RequestDelegate RequestDelegate { get; private set; }

            public void Dispose()
            {

            }

            public void Start<TContext>(IHttpApplication<TContext> application)
            {
                RequestDelegate = async ctx =>
                {
                    var httpContext = application.CreateContext(ctx.Features);
                    try
                    {
                        await application.ProcessRequestAsync(httpContext);
                    }
                    catch (Exception ex)
                    {
                        application.DisposeContext(httpContext, ex);
                        throw;
                    }
                    application.DisposeContext(httpContext, null);
                };
            }
        }
    }
}
