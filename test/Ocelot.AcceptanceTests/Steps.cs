﻿using CacheManager.Core;
using IdentityServer4.AccessTokenValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Ocelot.AcceptanceTests.Caching;
using Ocelot.Cache.CacheManager;
using Ocelot.Configuration;
using Ocelot.Configuration.ChangeTracking;
using Ocelot.Configuration.Creator;
using Ocelot.Configuration.File;
using Ocelot.Configuration.Repository;
using Ocelot.DependencyInjection;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Logging;
using Ocelot.Middleware;
using Ocelot.Multiplexer;
using Ocelot.Provider.Consul;
using Ocelot.Provider.Eureka;
using Ocelot.Provider.Polly;
using Ocelot.Requester;
using Ocelot.ServiceDiscovery.Providers;
using Ocelot.Tracing.Butterfly;
using Ocelot.Tracing.OpenTracing;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using static Ocelot.AcceptanceTests.HttpDelegatingHandlersTests;
using ConfigurationBuilder = Microsoft.Extensions.Configuration.ConfigurationBuilder;
using CookieHeaderValue = Microsoft.Net.Http.Headers.CookieHeaderValue;
using MediaTypeHeaderValue = System.Net.Http.Headers.MediaTypeHeaderValue;

namespace Ocelot.AcceptanceTests
{
    public class Steps : IDisposable
    {
        private TestServer _ocelotServer;
        private HttpClient _ocelotClient;
        private HttpResponseMessage _response;
        private HttpContent _postContent;
        private BearerToken _token;
        public string RequestIdKey = "OcRequestId";
        private readonly Random _random;
        private readonly string _ocelotConfigFileName;
        private IWebHostBuilder _webHostBuilder;
        private WebHostBuilder _ocelotBuilder;
        private IWebHost _ocelotHost;
        private IOcelotConfigurationChangeTokenSource _changeToken;

        public Steps()
        {
            _random = new();
            _ocelotConfigFileName = $"{Guid.NewGuid():N}-ocelot.json";
        }

        public async Task ThenConfigShouldBe(FileConfiguration fileConfig)
        {
            var internalConfigCreator = _ocelotServer.Host.Services.GetService<IInternalConfigurationCreator>();
            var internalConfigRepo = _ocelotServer.Host.Services.GetService<IInternalConfigurationRepository>();

            var internalConfig = internalConfigRepo.Get();
            var config = await internalConfigCreator.Create(fileConfig);

            internalConfig.Data.RequestId.ShouldBe(config.Data.RequestId);
        }

        public async Task ThenConfigShouldBeWithTimeout(FileConfiguration fileConfig, int timeoutMs)
        {
            var result = await Wait.WaitFor(timeoutMs).Until(async () =>
            {
                var internalConfigCreator = _ocelotServer.Host.Services.GetService<IInternalConfigurationCreator>();
                var internalConfigRepo = _ocelotServer.Host.Services.GetService<IInternalConfigurationRepository>();

                var internalConfig = internalConfigRepo.Get();
                var config = await internalConfigCreator.Create(fileConfig);

                return internalConfig.Data.RequestId == config.Data.RequestId;
            });

            result.ShouldBe(true);
        }

        public async Task StartFakeOcelotWithWebSockets()
        {
            _ocelotBuilder = new WebHostBuilder();
            _ocelotBuilder.ConfigureServices(s =>
            {
                s.AddSingleton(_ocelotBuilder);
                s.AddOcelot();
            });
            _ocelotBuilder.UseKestrel()
                .UseUrls("http://localhost:5000")
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                })
                .Configure(app =>
                {
                    app.UseWebSockets();
                    app.UseOcelot().Wait();
                })
                .UseIISIntegration();
            _ocelotHost = _ocelotBuilder.Build();
            await _ocelotHost.StartAsync();
        }

        public async Task StartFakeOcelotWithWebSocketsWithConsul()
        {
            _ocelotBuilder = new WebHostBuilder();
            _ocelotBuilder.ConfigureServices(s =>
            {
                s.AddSingleton(_ocelotBuilder);
                s.AddOcelot().AddConsul();
            });
            _ocelotBuilder.UseKestrel()
                .UseUrls("http://localhost:5000")
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                })
                .Configure(app =>
                {
                    app.UseWebSockets();
                    app.UseOcelot().Wait();
                })
                .UseIISIntegration();
            _ocelotHost = _ocelotBuilder.Build();
            await _ocelotHost.StartAsync();
        }

        public void GivenThereIsAConfiguration(FileConfiguration fileConfiguration)
        {
            var jsonConfiguration = JsonConvert.SerializeObject(fileConfiguration, Formatting.Indented);
            File.WriteAllText(_ocelotConfigFileName, jsonConfiguration);
        }

        private void DeleteOcelotConfig()
        {
            if (!File.Exists(_ocelotConfigFileName))
            {
                return;
            }

            try
            {
                File.Delete(_ocelotConfigFileName);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public void ThenTheResponseBodyHeaderIs(string key, string value)
        {
            var header = _response.Content.Headers.GetValues(key);
            header.First().ShouldBe(value);
        }

        public void GivenOcelotIsRunningReloadingConfig(bool shouldReload)
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: false, reloadOnChange: shouldReload);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenIHaveAChangeToken()
        {
            _changeToken = _ocelotServer.Host.Services.GetRequiredService<IOcelotConfigurationChangeTokenSource>();
        }

        /// <summary>
        /// This is annoying cos it should be in the constructor but we need to set up the file before calling startup so its a step.
        /// </summary>
        public void GivenOcelotIsRunning()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        /// <summary>
        /// This is annoying cos it should be in the constructor but we need to set up the file before calling startup so its a step.
        /// </summary>
        /// <typeparam name="T">The <see cref="ILoadBalancer"/> type.</typeparam>
        /// <param name="loadBalancerFactoryFunc">The delegate object to load balancer factory.</param>
        public void GivenOcelotIsRunningWithCustomLoadBalancer<T>(Func<IServiceProvider, DownstreamRoute, IServiceDiscoveryProvider, T> loadBalancerFactoryFunc)
            where T : ILoadBalancer
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot()
                        .AddCustomLoadBalancer(loadBalancerFactoryFunc);
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithConsul()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot().AddConsul();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void ThenTheTraceHeaderIsSet(string key)
        {
            var header = _response.Headers.GetValues(key);
            header.First().ShouldNotBeNullOrEmpty();
        }

        internal void GivenOcelotIsRunningUsingButterfly(string butterflyUrl)
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot()
                    .AddButterfly(option =>
                    {
                        //this is the url that the butterfly collector server is running on...
                        option.CollectorUrl = butterflyUrl;
                        option.Service = "Ocelot";
                    });
                })
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        await next.Invoke();
                    });
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningUsingConsulToStoreConfigAndJsonSerializedCache()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot()
                        .AddCacheManager((x) =>
                        {
                            x.WithMicrosoftLogging(log =>
                                {
                                    //log.AddConsole(LogLevel.Debug);
                                })
                                .WithJsonSerializer()
                                .WithHandle(typeof(InMemoryJsonHandle<>));
                        })
                        .AddConsul()
                        .AddConfigStoredInConsul();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningUsingConsulToStoreConfig()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot().AddConsul().AddConfigStoredInConsul();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
            Thread.Sleep(1000);
        }

        public void WhenIGetUrlOnTheApiGatewayWaitingForTheResponseToBeOk(string url)
        {
            var result = Wait.WaitFor(2000).Until(() =>
            {
                try
                {
                    _response = _ocelotClient.GetAsync(url).Result;
                    _response.EnsureSuccessStatusCode();
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });

            result.ShouldBeTrue();
        }

        public void GivenOcelotIsRunningUsingJsonSerializedCache()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot()
                        .AddCacheManager((x) =>
                        {
                            x.WithMicrosoftLogging(log =>
                                {
                                    //log.AddConsole(LogLevel.Debug);
                                })
                                .WithJsonSerializer()
                                .WithHandle(typeof(InMemoryJsonHandle<>));
                        });
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithFakeHttpClientCache(IHttpClientCache cache)
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(cache);
                    s.AddOcelot();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        internal void GivenIWait(int wait)
        {
            Thread.Sleep(wait);
        }

        public void GivenOcelotIsRunningWithMiddleareBeforePipeline<T>(Func<object, Task> callback)
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot();
                })
                .Configure(app =>
                {
                    app.UseMiddleware<T>(callback);
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithSpecficHandlersRegisteredInDi<TOne, TWo>()
            where TOne : DelegatingHandler
            where TWo : DelegatingHandler
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(_webHostBuilder);
                    s.AddOcelot()
                        .AddDelegatingHandler<TOne>()
                        .AddDelegatingHandler<TWo>();
                })
                .Configure(a =>
                {
                    a.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithSpecficAggregatorsRegisteredInDi<TAggregator, TDepedency>()
            where TAggregator : class, IDefinedAggregator
            where TDepedency : class
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(_webHostBuilder);
                    s.AddSingleton<TDepedency>();
                    s.AddOcelot()
                        .AddSingletonDefinedAggregator<TAggregator>();
                })
                .Configure(a =>
                {
                    a.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithGlobalHandlersRegisteredInDi<TOne, TWo>()
            where TOne : DelegatingHandler
            where TWo : DelegatingHandler
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(_webHostBuilder);
                    s.AddOcelot()
                        .AddDelegatingHandler<TOne>(true)
                        .AddDelegatingHandler<TWo>(true);
                })
                .Configure(a =>
                {
                    a.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithGlobalHandlerRegisteredInDi<TOne>()
            where TOne : DelegatingHandler
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(_webHostBuilder);
                    s.AddOcelot()
                        .AddDelegatingHandler<TOne>(true);
                })
                .Configure(a =>
                {
                    a.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithGlobalHandlersRegisteredInDi<TOne>(FakeDependency dependency)
            where TOne : DelegatingHandler
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(_webHostBuilder);
                    s.AddSingleton(dependency);
                    s.AddOcelot()
                        .AddDelegatingHandler<TOne>(true);
                })
                .Configure(a =>
                {
                    a.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        internal void GivenIAddCookieToMyRequest(string cookie)
        {
            _ocelotClient.DefaultRequestHeaders.Add("Set-Cookie", cookie);
        }

        /// <summary>
        /// This is annoying cos it should be in the constructor but we need to set up the file before calling startup so its a step.
        /// </summary>
        public void GivenOcelotIsRunning(Action<IdentityServerAuthenticationOptions> options, string authenticationProviderKey)
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot();
                    s.AddAuthentication()
                        .AddIdentityServerAuthentication(authenticationProviderKey, options);
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void ThenTheResponseHeaderIs(string key, string value)
        {
            var header = _response.Headers.GetValues(key);
            header.First().ShouldBe(value);
        }

        public void ThenTheReasonPhraseIs(string expected)
        {
            _response.ReasonPhrase.ShouldBe(expected);
        }

        /// <summary>
        /// This is annoying cos it should be in the constructor but we need to set up the file before calling startup so its a step.
        /// </summary>
        public void GivenOcelotIsRunning(OcelotPipelineConfiguration ocelotPipelineConfig)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile(_ocelotConfigFileName, false, false)
                .AddEnvironmentVariables();

            var configuration = builder.Build();
            _webHostBuilder = new WebHostBuilder();
            _webHostBuilder.ConfigureServices(s =>
            {
                s.AddSingleton(_webHostBuilder);
            });

            _ocelotServer = new TestServer(_webHostBuilder
                .UseConfiguration(configuration)
                .ConfigureServices(s =>
                {
                    s.AddOcelot(configuration);
                })
                .ConfigureLogging(l =>
                {
                    l.AddConsole();
                    l.AddDebug();
                })
                .Configure(a =>
                {
                    a.UseOcelot(ocelotPipelineConfig).Wait();
                }));

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenIHaveAddedATokenToMyRequest()
        {
            _ocelotClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token.AccessToken);
        }

        public void GivenIHaveAToken(string url)
        {
            var tokenUrl = $"{url}/connect/token";
            var formData = new List<KeyValuePair<string, string>>
            {
                new("client_id", "client"),
                new("client_secret", "secret"),
                new("scope", "api"),
                new("username", "test"),
                new("password", "test"),
                new("grant_type", "password"),
            };
            var content = new FormUrlEncodedContent(formData);

            using (var httpClient = new HttpClient())
            {
                var response = httpClient.PostAsync(tokenUrl, content).Result;
                var responseContent = response.Content.ReadAsStringAsync().Result;
                response.EnsureSuccessStatusCode();
                _token = JsonConvert.DeserializeObject<BearerToken>(responseContent);
            }
        }

        public void GivenIHaveATokenForApiReadOnlyScope(string url)
        {
            var tokenUrl = $"{url}/connect/token";
            var formData = new List<KeyValuePair<string, string>>
            {
                new("client_id", "client"),
                new("client_secret", "secret"),
                new("scope", "api.readOnly"),
                new("username", "test"),
                new("password", "test"),
                new("grant_type", "password"),
            };
            var content = new FormUrlEncodedContent(formData);

            using (var httpClient = new HttpClient())
            {
                var response = httpClient.PostAsync(tokenUrl, content).Result;
                var responseContent = response.Content.ReadAsStringAsync().Result;
                response.EnsureSuccessStatusCode();
                _token = JsonConvert.DeserializeObject<BearerToken>(responseContent);
            }
        }

        public void GivenIHaveATokenForApi2(string url)
        {
            var tokenUrl = $"{url}/connect/token";
            var formData = new List<KeyValuePair<string, string>>
            {
                new("client_id", "client"),
                new("client_secret", "secret"),
                new("scope", "api2"),
                new("username", "test"),
                new("password", "test"),
                new("grant_type", "password"),
            };
            var content = new FormUrlEncodedContent(formData);

            using (var httpClient = new HttpClient())
            {
                var response = httpClient.PostAsync(tokenUrl, content).Result;
                var responseContent = response.Content.ReadAsStringAsync().Result;
                response.EnsureSuccessStatusCode();
                _token = JsonConvert.DeserializeObject<BearerToken>(responseContent);
            }
        }

        public void VerifyIdentiryServerStarted(string url)
        {
            using (var httpClient = new HttpClient())
            {
                var response = httpClient.GetAsync($"{url}/.well-known/openid-configuration").GetAwaiter().GetResult();
                var content = response.Content.ReadAsStringAsync().GetAwaiter();
                response.EnsureSuccessStatusCode();
            }
        }

        public void GivenOcelotIsRunningWithEureka()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot()
                        .AddEureka();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void GivenOcelotIsRunningWithPolly()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot()
                        .AddPolly();
                })
                .Configure(app =>
                {
                    app.UseOcelot()
                        .Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void WhenIGetUrlOnTheApiGateway(string url)
        {
            _response = _ocelotClient.GetAsync(url).Result;
        }

        public void WhenIGetUrlOnTheApiGatewayAndDontWait(string url)
        {
            _ocelotClient.GetAsync(url);
        }

        public void WhenICancelTheRequest()
        {
            _ocelotClient.CancelPendingRequests();
        }

        public void WhenIGetUrlOnTheApiGateway(string url, HttpContent content)
        {
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, url) { Content = content };
            _response = _ocelotClient.SendAsync(httpRequestMessage).Result;
        }

        public void WhenIPostUrlOnTheApiGateway(string url, HttpContent content)
        {
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            _response = _ocelotClient.SendAsync(httpRequestMessage).Result;
        }

        public void WhenIGetUrlOnTheApiGateway(string url, string cookie, string value)
        {
            var request = _ocelotServer.CreateRequest(url);
            request.And(x => { x.Headers.Add("Cookie", new CookieHeaderValue(cookie, value).ToString()); });
            var response = request.GetAsync().Result;
            _response = response;
        }

        public void GivenIAddAHeader(string key, string value)
        {
            _ocelotClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }

        public void WhenIGetUrlOnTheApiGatewayMultipleTimes(string url, int times)
        {
            var tasks = new Task[times];

            for (var i = 0; i < times; i++)
            {
                var urlCopy = url;
                tasks[i] = GetForServiceDiscoveryTest(urlCopy);
                Thread.Sleep(_random.Next(40, 60));
            }

            Task.WaitAll(tasks);
        }

        public void WhenIGetUrlOnTheApiGatewayMultipleTimes(string url, int times, string cookie, string value)
        {
            var tasks = new Task[times];

            for (var i = 0; i < times; i++)
            {
                var urlCopy = url;
                tasks[i] = GetForServiceDiscoveryTest(urlCopy, cookie, value);
                Thread.Sleep(_random.Next(40, 60));
            }

            Task.WaitAll(tasks);
        }

        private async Task GetForServiceDiscoveryTest(string url, string cookie, string value)
        {
            var request = _ocelotServer.CreateRequest(url);
            request.And(x => { x.Headers.Add("Cookie", new CookieHeaderValue(cookie, value).ToString()); });
            var response = await request.GetAsync();
            var content = await response.Content.ReadAsStringAsync();
            var count = int.Parse(content);
            count.ShouldBeGreaterThan(0);
        }

        private async Task GetForServiceDiscoveryTest(string url)
        {
            var response = await _ocelotClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            var count = int.Parse(content);
            count.ShouldBeGreaterThan(0);
        }

        public void WhenIGetUrlOnTheApiGatewayMultipleTimesForRateLimit(string url, int times)
        {
            for (var i = 0; i < times; i++)
            {
                var clientId = "ocelotclient1";
                var request = new HttpRequestMessage(new HttpMethod("GET"), url);
                request.Headers.Add("ClientId", clientId);
                _response = _ocelotClient.SendAsync(request).Result;
            }
        }

        public void WhenIGetUrlOnTheApiGateway(string url, string requestId)
        {
            _ocelotClient.DefaultRequestHeaders.TryAddWithoutValidation(RequestIdKey, requestId);

            _response = _ocelotClient.GetAsync(url).Result;
        }

        public void WhenIPostUrlOnTheApiGateway(string url)
        {
            _response = _ocelotClient.PostAsync(url, _postContent).Result;
        }

        public void GivenThePostHasContent(string postcontent)
        {
            _postContent = new StringContent(postcontent);
        }

        public void GivenThePostHasContentType(string postcontent)
        {
            _postContent.Headers.ContentType = new MediaTypeHeaderValue(postcontent);
        }

        public void GivenThePostHasGzipContent(object input)
        {
            var json = JsonConvert.SerializeObject(input);
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var ms = new MemoryStream();
            using (var gzip = new GZipStream(ms, CompressionMode.Compress, true))
            {
                gzip.Write(jsonBytes, 0, jsonBytes.Length);
            }

            ms.Position = 0;
            var content = new StreamContent(ms);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            _postContent = content;
        }

        public void ThenTheResponseBodyShouldBe(string expectedBody)
        {
            _response.Content.ReadAsStringAsync().Result.ShouldBe(expectedBody);
        }

        public void ThenTheContentLengthIs(int expected)
        {
            _response.Content.Headers.ContentLength.ShouldBe(expected);
        }

        public void ThenTheStatusCodeShouldBe(HttpStatusCode expectedHttpStatusCode)
        {
            _response.StatusCode.ShouldBe(expectedHttpStatusCode);
        }

        public void ThenTheStatusCodeShouldBe(int expectedHttpStatusCode)
        {
            var responseStatusCode = (int)_response.StatusCode;
            responseStatusCode.ShouldBe(expectedHttpStatusCode);
        }

        public void ThenTheRequestIdIsReturned()
        {
            _response.Headers.GetValues(RequestIdKey).First().ShouldNotBeNullOrEmpty();
        }

        public void ThenTheRequestIdIsReturned(string expected)
        {
            _response.Headers.GetValues(RequestIdKey).First().ShouldBe(expected);
        }

        public void WhenIMakeLotsOfDifferentRequestsToTheApiGateway()
        {
            var numberOfRequests = 100;
            var aggregateUrl = "/";
            var aggregateExpected = "{\"Laura\":{Hello from Laura},\"Tom\":{Hello from Tom}}";
            var tomUrl = "/tom";
            var tomExpected = "{Hello from Tom}";
            var lauraUrl = "/laura";
            var lauraExpected = "{Hello from Laura}";
            var random = new Random();

            var aggregateTasks = new Task[numberOfRequests];

            for (var i = 0; i < numberOfRequests; i++)
            {
                aggregateTasks[i] = Fire(aggregateUrl, aggregateExpected, random);
            }

            var tomTasks = new Task[numberOfRequests];

            for (var i = 0; i < numberOfRequests; i++)
            {
                tomTasks[i] = Fire(tomUrl, tomExpected, random);
            }

            var lauraTasks = new Task[numberOfRequests];

            for (var i = 0; i < numberOfRequests; i++)
            {
                lauraTasks[i] = Fire(lauraUrl, lauraExpected, random);
            }

            Task.WaitAll(lauraTasks);
            Task.WaitAll(tomTasks);
            Task.WaitAll(aggregateTasks);
        }

        private async Task Fire(string url, string expectedBody, Random random)
        {
            var request = new HttpRequestMessage(new HttpMethod("GET"), url);
            await Task.Delay(random.Next(0, 2));
            var response = await _ocelotClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            content.ShouldBe(expectedBody);
        }

        public void GivenOcelotIsRunningWithBlowingUpDiskRepo(IFileConfigurationRepository fake)
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton(fake);
                    s.AddOcelot();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void TheChangeTokenShouldBeActive(bool itShouldBeActive)
        {
            _changeToken.ChangeToken.HasChanged.ShouldBe(itShouldBeActive);
        }

        public void GivenOcelotIsRunningWithLogger()
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, false, false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot();
                    s.AddSingleton<IOcelotLoggerFactory, MockLoggerFactory>();
                })
                .Configure(app =>
                {
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        internal void GivenOcelotIsRunningUsingOpenTracing(OpenTracing.ITracer fakeTracer)
        {
            _webHostBuilder = new WebHostBuilder();

            _webHostBuilder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false);
                    config.AddJsonFile(_ocelotConfigFileName, optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(s =>
                {
                    s.AddOcelot()
                        .AddOpenTracing();

                    s.AddSingleton(fakeTracer);
                })
                .Configure(app =>
                {
                    app.Use(async (_, next) =>
                    {
                        await next.Invoke();
                    });
                    app.UseOcelot().Wait();
                });

            _ocelotServer = new TestServer(_webHostBuilder);

            _ocelotClient = _ocelotServer.CreateClient();
        }

        public void ThenWarningShouldBeLogged()
        {
            var loggerFactory = (MockLoggerFactory)_ocelotServer.Host.Services.GetService<IOcelotLoggerFactory>();
            loggerFactory.Verify();
        }

        internal class MockLoggerFactory : IOcelotLoggerFactory
        {
            private Mock<IOcelotLogger> _logger;

            public IOcelotLogger CreateLogger<T>()
            {
                if (_logger == null)
                {
                    _logger = new Mock<IOcelotLogger>();
                    _logger.Setup(x => x.LogWarning(It.IsAny<string>())).Verifiable();
                }

                return _logger.Object;
            }

            public void Verify()
            {
                _logger.Verify(x => x.LogWarning(It.IsAny<string>()), Times.Once);
            }
        }

        /// <summary>
        /// Public implementation of Dispose pattern callable by consumers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool _disposedValue;

        /// <summary>
        /// Protected implementation of Dispose pattern.
        /// </summary>
        /// <param name="disposing">Flag to trigger actual disposing operation.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
            {
                return;
            }

            if (disposing)
            {
                _ocelotClient?.Dispose();
                _ocelotServer?.Dispose();
                _ocelotHost?.Dispose();
                DeleteOcelotConfig();
            }

            _disposedValue = true;
        }
    }
}
