// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Builder;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.PlatformAbstractions;

namespace Microsoft.AspNetCore.Hosting
{
    /// <summary>
    /// A builder for <see cref="IWebHost"/>
    /// </summary>
    public class WebHostBuilder : IWebHostBuilder
    {
        protected readonly IHostingEnvironment _hostingEnvironment;
        protected readonly ILoggerFactory _loggerFactory;

        protected IConfiguration _config;
        protected WebHostOptions _options;

        protected Action<IServiceCollection> _configureServices;

        // Only one of these should be set
        protected StartupMethods _startup;
        protected Type _startupType;

        // Only one of these should be set
        protected IServerFactory _serverFactory;

        public IDictionary<string, string> _settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public WebHostBuilder()
        {
            _hostingEnvironment = new HostingEnvironment();
            _loggerFactory = new LoggerFactory();
        }

        /// <summary>
        /// Gets the raw settings to be used by the web host. Values specified here will override 
        /// the configuration set by <see cref="UseConfiguration(IConfiguration)"/>.
        /// </summary>
        public IDictionary<string, string> Settings { get { return _settings; } }

        /// <summary>
        /// Add or replace a setting in <see cref="Settings"/>.
        /// </summary>
        /// <param name="key">The key of the setting to add or replace.</param>
        /// <param name="value">The value of the setting to add or replace.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public virtual IWebHostBuilder UseSetting(string key, string value)
        {
            _settings[key] = value;
            return this;
        }

        /// <summary>
        /// Specify the <see cref="IConfiguration"/> to be used by the web host. If no configuration is
        /// provided to the builder, the default configuration will be used. 
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> to be used.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public virtual IWebHostBuilder UseConfiguration(IConfiguration configuration)
        {
            _config = configuration;
            return this;
        }

        /// <summary>
        /// Specify the <see cref="IServerFactory"/> to be used by the web host.
        /// </summary>
        /// <param name="factory">The <see cref="IServerFactory"/> to be used.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public virtual IWebHostBuilder UseServer(IServerFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            _serverFactory = factory;
            return this;
        }

        /// <summary>
        /// Specify the startup type to be used by the web host. 
        /// </summary>
        /// <param name="startupType">The <see cref="Type"/> to be used.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public virtual IWebHostBuilder UseStartup(Type startupType)
        {
            if (startupType == null)
            {
                throw new ArgumentNullException(nameof(startupType));
            }

            _startupType = startupType;
            return this;
        }

        /// <summary>
        /// Specify the delegate that is used to configure the services of the web application.
        /// </summary>
        /// <param name="configureServices">The delegate that configures the <see cref="IServiceCollection"/>.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public virtual IWebHostBuilder ConfigureServices(Action<IServiceCollection> configureServices)
        {
            _configureServices = configureServices;
            return this;
        }

        /// <summary>
        /// Specify the startup method to be used to configure the web application. 
        /// </summary>
        /// <param name="configureApplication">The delegate that configures the <see cref="IApplicationBuilder"/>.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public virtual IWebHostBuilder Configure(Action<IApplicationBuilder> configureApp)
        {
            if (configureApp == null)
            {
                throw new ArgumentNullException(nameof(configureApp));
            }

            _startup = new StartupMethods(configureApp);
            return this;
        }

        /// <summary>
        /// Configure the provided <see cref="ILoggerFactory"/> which will be available as a hosting service. 
        /// </summary>
        /// <param name="configureLogging">The delegate that configures the <see cref="ILoggerFactory"/>.</param>
        /// <returns>The <see cref="IWebHostBuilder"/>.</returns>
        public virtual IWebHostBuilder ConfigureLogging(Action<ILoggerFactory> configureLogging)
        {
            configureLogging(_loggerFactory);
            return this;
        }

        /// <summary>
        /// Builds the required services and an <see cref="IWebHost"/> which hosts a web application.
        /// </summary>
        public virtual IWebHost Build()
        {
            var hostingServices = BuildHostingServices();

            var hostingContainer = hostingServices.BuildServiceProvider();

            var appEnvironment = hostingContainer.GetRequiredService<IApplicationEnvironment>();
            var startupLoader = hostingContainer.GetRequiredService<IStartupLoader>();

            // Initialize the hosting environment
            _hostingEnvironment.Initialize(appEnvironment.ApplicationBasePath, _options, _config);

            var host = BuildWebHost(hostingServices, startupLoader);

            return host;
        }

        protected IServiceCollection BuildHostingServices()
        {
            InitializeHostConfiguration();

            var services = new ServiceCollection();

            InitializeHostingServices(services);
            InitializePlatformServices(services);

            this._configureServices?.Invoke(services);

            return services;
        }

        protected IWebHost BuildWebHost(IServiceCollection services, IStartupLoader startupLoader)
        {
            if( services == null ) throw new ArgumentNullException( nameof( services ) );
            if( startupLoader == null ) throw new ArgumentNullException( nameof( startupLoader ) );

            var host = new WebHost(services, startupLoader, _options, _config);

            // Only one of these should be set, but they are used in priority
            host.ServerFactory = _serverFactory;
            host.ServerFactoryLocation = _options.ServerFactoryLocation;

            // Only one of these should be set, but they are used in priority
            host.Startup = _startup;
            host.StartupType = _startupType;
            host.StartupAssemblyName = _options.Application;

            host.Initialize();

            return host;
        }

        protected void InitializeHostConfiguration()
        {
            // Apply the configuration settings
            var configuration = _config ?? WebHostConfiguration.GetDefault();

            var mergedConfiguration = new ConfigurationBuilder().Add(new IncludedConfigurationProvider(configuration)).AddInMemoryCollection(_settings).Build();

            _config = mergedConfiguration;
            _options = new WebHostOptions(_config);
        }

        protected void InitializeHostingServices(IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddSingleton(_hostingEnvironment);
            services.AddSingleton(_loggerFactory);

            services.AddTransient<IStartupLoader, StartupLoader>();
            services.AddTransient<IServerLoader, ServerLoader>();

            services.AddTransient<IApplicationBuilderFactory, ApplicationBuilderFactory>();
            services.AddTransient<IHttpContextFactory, HttpContextFactory>();

            services.AddLogging();
            services.AddOptions();

            var diagnosticSource = new DiagnosticListener("Microsoft.AspNetCore");
            services.AddSingleton<DiagnosticSource>(diagnosticSource);
            services.AddSingleton<DiagnosticListener>(diagnosticSource);

            // Conjure up a RequestServices
            services.AddTransient<IStartupFilter, AutoRequestServicesStartupFilter>();
        }

        protected void InitializePlatformServices(IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            var defaultPlatformServices = PlatformServices.Default;
            if (defaultPlatformServices == null) return;

            if (defaultPlatformServices.Application != null)
            {
                var appEnv = defaultPlatformServices.Application;
                if (!string.IsNullOrEmpty(this._options.ApplicationBasePath))
                {
                    appEnv = new WrappedApplicationEnvironment(this._options.ApplicationBasePath, appEnv);
                }

                services.TryAddSingleton(appEnv);
            }

            if (defaultPlatformServices.Runtime != null)
            {
                services.TryAddSingleton(defaultPlatformServices.Runtime);
            }
        }

        private class WrappedApplicationEnvironment : IApplicationEnvironment
        {
            public WrappedApplicationEnvironment(string applicationBasePath, IApplicationEnvironment env)
            {
                ApplicationBasePath = applicationBasePath;
                ApplicationName = env.ApplicationName;
                ApplicationVersion = env.ApplicationVersion;
                RuntimeFramework = env.RuntimeFramework;
            }

            public string ApplicationBasePath { get; }

            public string ApplicationName { get; }

            public string ApplicationVersion { get; }

            public FrameworkName RuntimeFramework { get; }
        }
    }
}
